using System.Globalization;
using Microsoft.Data.Sqlite;

namespace TorrentCore.Persistence.Sqlite.Schema;

public sealed class SqliteSchemaMigrator(string databaseFilePath)
{
    private readonly SemaphoreSlim _migrationLock = new(1, 1);
    private volatile bool _isMigrated;

    public async Task ApplyMigrationsAsync(CancellationToken cancellationToken)
    {
        if (_isMigrated)
        {
            return;
        }

        await _migrationLock.WaitAsync(cancellationToken);

        try
        {
            if (_isMigrated)
            {
                return;
            }

            var directoryPath = Path.GetDirectoryName(databaseFilePath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await EnsureSchemaMigrationsTableAsync(connection, cancellationToken);

            var migrations = GetMigrations();
            var appliedVersions = await GetAppliedVersionsAsync(connection, cancellationToken);

            foreach (var migration in migrations)
            {
                if (appliedVersions.Contains(migration.Version))
                {
                    continue;
                }

                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
                try
                {
                    await migration.ApplyAsync(connection, cancellationToken);

                    var recordCommand = connection.CreateCommand();
                    recordCommand.Transaction = transaction;
                    recordCommand.CommandText =
                        """
                        INSERT INTO schema_migrations (version, name, applied_at_utc)
                        VALUES ($version, $name, $applied_at_utc);
                        """;
                    recordCommand.Parameters.AddWithValue("$version", migration.Version);
                    recordCommand.Parameters.AddWithValue("$name", migration.Name);
                    recordCommand.Parameters.AddWithValue("$applied_at_utc", DateTimeOffset.UtcNow.ToString("O"));
                    await recordCommand.ExecuteNonQueryAsync(cancellationToken);

                    await transaction.CommitAsync(cancellationToken);
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    throw;
                }
            }

            _isMigrated = true;
        }
        finally
        {
            _migrationLock.Release();
        }
    }

    private static async Task EnsureSchemaMigrationsTableAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                applied_at_utc TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<HashSet<int>> GetAppliedVersionsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM schema_migrations;";

        var appliedVersions = new HashSet<int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            appliedVersions.Add(reader.GetInt32(0));
        }

        return appliedVersions;
    }

    private static IReadOnlyList<SqliteMigrationDefinition> GetMigrations()
    {
        return
        [
            new SqliteMigrationDefinition(
                1,
                "create_activity_logs",
                async (connection, cancellationToken) =>
                {
                    var command = connection.CreateCommand();
                    command.CommandText =
                        """
                        CREATE TABLE IF NOT EXISTS activity_logs (
                            log_entry_id INTEGER PRIMARY KEY AUTOINCREMENT,
                            occurred_at_utc TEXT NOT NULL,
                            level TEXT NOT NULL,
                            category TEXT NOT NULL,
                            event_type TEXT NOT NULL,
                            message TEXT NOT NULL,
                            torrent_id TEXT NULL,
                            trace_id TEXT NULL,
                            details_json TEXT NULL
                        );

                        CREATE INDEX IF NOT EXISTS idx_activity_logs_occurred_at_utc
                            ON activity_logs (occurred_at_utc DESC);

                        CREATE INDEX IF NOT EXISTS idx_activity_logs_torrent_id
                            ON activity_logs (torrent_id);
                        """;
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }),
            new SqliteMigrationDefinition(
                2,
                "add_activity_logs_service_instance_id",
                async (connection, cancellationToken) =>
                {
                    if (!await ColumnExistsAsync(connection, "activity_logs", "service_instance_id", cancellationToken))
                    {
                        var alterCommand = connection.CreateCommand();
                        alterCommand.CommandText = "ALTER TABLE activity_logs ADD COLUMN service_instance_id TEXT NULL;";
                        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

                    var indexCommand = connection.CreateCommand();
                    indexCommand.CommandText =
                        """
                        CREATE INDEX IF NOT EXISTS idx_activity_logs_service_instance_id
                            ON activity_logs (service_instance_id);
                        """;
                    await indexCommand.ExecuteNonQueryAsync(cancellationToken);
                }),
            new SqliteMigrationDefinition(
                3,
                "create_torrents",
                async (connection, cancellationToken) =>
                {
                    var command = connection.CreateCommand();
                    command.CommandText =
                        """
                        CREATE TABLE IF NOT EXISTS torrents (
                            torrent_id TEXT PRIMARY KEY,
                            name TEXT NOT NULL,
                            state TEXT NOT NULL,
                            desired_state TEXT NOT NULL DEFAULT 'Runnable',
                            magnet_uri TEXT NOT NULL,
                            info_hash TEXT NULL,
                            download_root_path TEXT NULL,
                            save_path TEXT NOT NULL,
                            progress_percent REAL NOT NULL,
                            downloaded_bytes INTEGER NOT NULL,
                            uploaded_bytes INTEGER NOT NULL,
                            total_bytes INTEGER NULL,
                            download_rate_bytes_per_second INTEGER NOT NULL,
                            upload_rate_bytes_per_second INTEGER NOT NULL,
                            tracker_count INTEGER NOT NULL,
                            connected_peer_count INTEGER NOT NULL,
                            added_at_utc TEXT NOT NULL,
                            completed_at_utc TEXT NULL,
                            seeding_started_at_utc TEXT NULL,
                            last_activity_at_utc TEXT NULL,
                            error_message TEXT NULL
                        );

                        CREATE UNIQUE INDEX IF NOT EXISTS idx_torrents_info_hash
                            ON torrents (info_hash)
                            WHERE info_hash IS NOT NULL;

                        CREATE INDEX IF NOT EXISTS idx_torrents_added_at_utc
                            ON torrents (added_at_utc DESC);
                        """;
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }),
            new SqliteMigrationDefinition(
                4,
                "add_torrents_download_root_path",
                async (connection, cancellationToken) =>
                {
                    if (!await ColumnExistsAsync(connection, "torrents", "download_root_path", cancellationToken))
                    {
                        var alterCommand = connection.CreateCommand();
                        alterCommand.CommandText = "ALTER TABLE torrents ADD COLUMN download_root_path TEXT NULL;";
                        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
                    }
                }),
            new SqliteMigrationDefinition(
                5,
                "add_torrents_uploaded_and_seeding_fields",
                async (connection, cancellationToken) =>
                {
                    if (!await ColumnExistsAsync(connection, "torrents", "uploaded_bytes", cancellationToken))
                    {
                        var addUploadedBytesCommand = connection.CreateCommand();
                        addUploadedBytesCommand.CommandText = "ALTER TABLE torrents ADD COLUMN uploaded_bytes INTEGER NOT NULL DEFAULT 0;";
                        await addUploadedBytesCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

                    if (!await ColumnExistsAsync(connection, "torrents", "seeding_started_at_utc", cancellationToken))
                    {
                        var addSeedingStartedCommand = connection.CreateCommand();
                        addSeedingStartedCommand.CommandText = "ALTER TABLE torrents ADD COLUMN seeding_started_at_utc TEXT NULL;";
                        await addSeedingStartedCommand.ExecuteNonQueryAsync(cancellationToken);
                    }
                }),
            new SqliteMigrationDefinition(
                6,
                "create_runtime_settings",
                async (connection, cancellationToken) =>
                {
                    var command = connection.CreateCommand();
                    command.CommandText =
                        """
                        CREATE TABLE IF NOT EXISTS runtime_settings (
                            setting_key TEXT PRIMARY KEY,
                            setting_value TEXT NOT NULL,
                            updated_at_utc TEXT NOT NULL
                        );
                        """;
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }),
            new SqliteMigrationDefinition(
                7,
                "add_torrents_desired_state",
                async (connection, cancellationToken) =>
                {
                    if (!await ColumnExistsAsync(connection, "torrents", "desired_state", cancellationToken))
                    {
                        var alterCommand = connection.CreateCommand();
                        alterCommand.CommandText = "ALTER TABLE torrents ADD COLUMN desired_state TEXT NOT NULL DEFAULT 'Runnable';";
                        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

                    var normalizeCommand = connection.CreateCommand();
                    normalizeCommand.CommandText =
                        """
                        UPDATE torrents
                        SET desired_state = CASE
                            WHEN state = 'Paused' THEN 'Paused'
                            ELSE 'Runnable'
                        END
                        WHERE desired_state IS NULL OR desired_state = '' OR state = 'Paused';
                    """;
                    await normalizeCommand.ExecuteNonQueryAsync(cancellationToken);
                }),
            new SqliteMigrationDefinition(
                8,
                "create_torrent_categories_and_add_torrent_category_key",
                async (connection, cancellationToken) =>
                {
                    if (!await TableExistsAsync(connection, "torrent_categories", cancellationToken))
                    {
                        var createCategoriesCommand = connection.CreateCommand();
                        createCategoriesCommand.CommandText =
                            """
                            CREATE TABLE torrent_categories (
                                category_key TEXT PRIMARY KEY,
                                display_name TEXT NOT NULL,
                                callback_label TEXT NOT NULL,
                                download_root_path TEXT NOT NULL,
                                enabled INTEGER NOT NULL,
                                invoke_completion_callback INTEGER NOT NULL,
                                sort_order INTEGER NOT NULL,
                                updated_at_utc TEXT NOT NULL
                            );

                            CREATE INDEX IF NOT EXISTS idx_torrent_categories_sort_order
                                ON torrent_categories (sort_order ASC, category_key ASC);
                            """;
                        await createCategoriesCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

                    if (!await ColumnExistsAsync(connection, "torrents", "category_key", cancellationToken))
                    {
                        var alterTorrentsCommand = connection.CreateCommand();
                        alterTorrentsCommand.CommandText = "ALTER TABLE torrents ADD COLUMN category_key TEXT NULL;";
                        await alterTorrentsCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

                    var indexCommand = connection.CreateCommand();
                    indexCommand.CommandText =
                        """
                        CREATE INDEX IF NOT EXISTS idx_torrents_category_key
                            ON torrents (category_key);
                        """;
                    await indexCommand.ExecuteNonQueryAsync(cancellationToken);
                }),
            new SqliteMigrationDefinition(
                9,
                "add_torrent_callback_fields",
                async (connection, cancellationToken) =>
                {
                    if (!await ColumnExistsAsync(connection, "torrents", "completion_callback_label", cancellationToken))
                    {
                        var alterCommand = connection.CreateCommand();
                        alterCommand.CommandText = "ALTER TABLE torrents ADD COLUMN completion_callback_label TEXT NULL;";
                        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

                    if (!await ColumnExistsAsync(connection, "torrents", "invoke_completion_callback", cancellationToken))
                    {
                        var alterCommand = connection.CreateCommand();
                        alterCommand.CommandText = "ALTER TABLE torrents ADD COLUMN invoke_completion_callback INTEGER NOT NULL DEFAULT 0;";
                        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
                    }
                }),
        ];
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS(
                SELECT 1
                FROM sqlite_master
                WHERE type = 'table' AND name = $table_name
            );
            """;
        command.Parameters.AddWithValue("$table_name", tableName);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<bool> ColumnExistsAsync(SqliteConnection connection, string tableName, string columnName, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private SqliteConnection CreateConnection()
    {
        var connectionStringBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = databaseFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        };

        return new SqliteConnection(connectionStringBuilder.ToString());
    }

    private sealed record SqliteMigrationDefinition(
        int Version,
        string Name,
        Func<SqliteConnection, CancellationToken, Task> ApplyAsync);
}
