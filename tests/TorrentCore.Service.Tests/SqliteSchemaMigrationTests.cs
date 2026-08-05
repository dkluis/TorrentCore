using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using TorrentCore.Persistence.Sqlite.Schema;
using TorrentCore.Service.Configuration;

namespace TorrentCore.Service.Tests;

public sealed class SqliteSchemaMigrationTests
{
    [Fact]
    public async Task Migration17_BackfillsColdDownloadAbandonmentKind()
    {
        var rootPath = CreateTempRootPath("torrentcore-history-removal-kind");
        var databaseFilePath = Path.Combine(rootPath, "torrentcore.db");
        var submittedAtUtc = DateTimeOffset.UtcNow.AddDays(-4);
        var removedAtUtc = DateTimeOffset.UtcNow;

        var initialMigrator = new SqliteSchemaMigrator(databaseFilePath);
        await initialMigrator.ApplyMigrationsAsync(CancellationToken.None);

        await using (var connection = new SqliteConnection($"Data Source={databaseFilePath}"))
        {
            await connection.OpenAsync();
            var insertCommand = connection.CreateCommand();
            insertCommand.CommandText = """
                                        INSERT INTO torrent_history (
                                            torrent_id, name, magnet_uri, latest_torrent_state,
                                            latest_progress_percent, latest_downloaded_bytes, latest_uploaded_bytes,
                                            latest_download_rate_bytes_per_second, latest_upload_rate_bytes_per_second,
                                            latest_tracker_count, latest_connected_peer_count, submitted_at_utc,
                                            last_updated_at_utc, removed_at_utc, invoke_completion_callback,
                                            data_deleted, removal_reason, removed_by_cleanup_policy, removal_kind
                                        )
                                        VALUES (
                                            '17171717-1717-1717-1717-171717171717', 'Abandoned Download',
                                            'magnet:?xt=urn:btih:1717171717171717171717171717171717171717',
                                            'Downloading', 91, 910, 0, 0, 0, 1, 0, $submitted_at_utc,
                                            $removed_at_utc, $removed_at_utc, 0, 1,
                                            'Download abandoned after 72 hours without peer or transfer activity.',
                                            1, NULL
                                        );

                                        DELETE FROM schema_migrations WHERE version = 17;
                                        """;
            insertCommand.Parameters.AddWithValue("$submitted_at_utc", submittedAtUtc.ToString("O"));
            insertCommand.Parameters.AddWithValue("$removed_at_utc", removedAtUtc.ToString("O"));
            await insertCommand.ExecuteNonQueryAsync();
        }

        var backfillMigrator = new SqliteSchemaMigrator(databaseFilePath);
        await backfillMigrator.ApplyMigrationsAsync(CancellationToken.None);

        await using var verifyConnection = new SqliteConnection($"Data Source={databaseFilePath}");
        await verifyConnection.OpenAsync();
        var verifyCommand = verifyConnection.CreateCommand();
        verifyCommand.CommandText = """
                                    SELECT removal_kind
                                    FROM torrent_history
                                    WHERE torrent_id = '17171717-1717-1717-1717-171717171717';
                                    """;

        Assert.Equal("ColdDownloadAbandonment", await verifyCommand.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Migration15_ClearsMetadataTimestampForStillUnresolvedLiveMagnet()
    {
        var rootPath = CreateTempRootPath("torrentcore-history-metadata-repair");
        var databaseFilePath = Path.Combine(rootPath, "torrentcore.db");
        var submittedAtUtc = DateTimeOffset.UtcNow.AddHours(-12);

        var initialMigrator = new SqliteSchemaMigrator(databaseFilePath);
        await initialMigrator.ApplyMigrationsAsync(CancellationToken.None);

        await using (var connection = new SqliteConnection($"Data Source={databaseFilePath}"))
        {
            await connection.OpenAsync();
            var insertCommand = connection.CreateCommand();
            insertCommand.CommandText = """
                                        INSERT INTO torrents (
                                            torrent_id, name, state, magnet_uri, info_hash, save_path,
                                            progress_percent, downloaded_bytes, uploaded_bytes, total_bytes,
                                            download_rate_bytes_per_second, upload_rate_bytes_per_second,
                                            tracker_count, connected_peer_count, added_at_utc
                                        )
                                        VALUES (
                                            '15151515-1515-1515-1515-151515151515', 'Unresolved Magnet',
                                            'ResolvingMetadata',
                                            'magnet:?xt=urn:btih:1515151515151515151515151515151515151515',
                                            '1515151515151515151515151515151515151515', '/tmp/unresolved',
                                            0, 0, 0, NULL, 0, 0, 1, 0, $submitted_at_utc
                                        );

                                        INSERT INTO torrent_history (
                                            torrent_id, name, magnet_uri, latest_torrent_state,
                                            latest_progress_percent, latest_downloaded_bytes, latest_uploaded_bytes,
                                            latest_download_rate_bytes_per_second, latest_upload_rate_bytes_per_second,
                                            latest_tracker_count, latest_connected_peer_count, submitted_at_utc,
                                            metadata_resolved_at_utc, last_updated_at_utc, invoke_completion_callback,
                                            data_deleted, removed_by_cleanup_policy
                                        )
                                        VALUES (
                                            '15151515-1515-1515-1515-151515151515', 'Unresolved Magnet',
                                            'magnet:?xt=urn:btih:1515151515151515151515151515151515151515',
                                            'Queued', 0, 0, 0, 0, 0, 1, 0, $submitted_at_utc,
                                            $metadata_resolved_at_utc, $metadata_resolved_at_utc, 0, 0, 0
                                        );

                                        DELETE FROM schema_migrations WHERE version = 15;
                                        """;
            insertCommand.Parameters.AddWithValue("$submitted_at_utc", submittedAtUtc.ToString("O"));
            insertCommand.Parameters.AddWithValue(
                "$metadata_resolved_at_utc", submittedAtUtc.AddMinutes(1).ToString("O"));
            await insertCommand.ExecuteNonQueryAsync();
        }

        var repairMigrator = new SqliteSchemaMigrator(databaseFilePath);
        await repairMigrator.ApplyMigrationsAsync(CancellationToken.None);

        await using var verifyConnection = new SqliteConnection($"Data Source={databaseFilePath}");
        await verifyConnection.OpenAsync();
        var verifyCommand = verifyConnection.CreateCommand();
        verifyCommand.CommandText = """
                                    SELECT metadata_resolved_at_utc
                                    FROM torrent_history
                                    WHERE torrent_id = '15151515-1515-1515-1515-151515151515';
                                    """;

        Assert.Equal(DBNull.Value, await verifyCommand.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Migration14_RepairsCompletionTimestampThatPredatesDownloadStart()
    {
        var rootPath = CreateTempRootPath("torrentcore-history-completion-repair");
        var databaseFilePath = Path.Combine(rootPath, "torrentcore.db");
        var submittedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10);
        var downloadStartedAtUtc = submittedAtUtc.AddMinutes(1);
        var seedingStartedAtUtc = submittedAtUtc.AddMinutes(5);

        var initialMigrator = new SqliteSchemaMigrator(databaseFilePath);
        await initialMigrator.ApplyMigrationsAsync(CancellationToken.None);

        await using (var connection = new SqliteConnection($"Data Source={databaseFilePath}"))
        {
            await connection.OpenAsync();
            var insertCommand = connection.CreateCommand();
            insertCommand.CommandText = """
                                        INSERT INTO torrent_history (
                                            torrent_id,
                                            name,
                                            magnet_uri,
                                            latest_torrent_state,
                                            latest_progress_percent,
                                            latest_downloaded_bytes,
                                            latest_uploaded_bytes,
                                            latest_download_rate_bytes_per_second,
                                            latest_upload_rate_bytes_per_second,
                                            latest_tracker_count,
                                            latest_connected_peer_count,
                                            submitted_at_utc,
                                            download_started_at_utc,
                                            download_completed_at_utc,
                                            seeding_started_at_utc,
                                            last_updated_at_utc,
                                            invoke_completion_callback,
                                            data_deleted,
                                            removed_by_cleanup_policy
                                        )
                                        VALUES (
                                            '11111111-1111-1111-1111-111111111111',
                                            'Premature Completion',
                                            'magnet:?xt=urn:btih:1111111111111111111111111111111111111111',
                                            'Completed',
                                            100,
                                            100,
                                            0,
                                            0,
                                            0,
                                            1,
                                            0,
                                            $submitted_at_utc,
                                            $download_started_at_utc,
                                            $download_completed_at_utc,
                                            $seeding_started_at_utc,
                                            $last_updated_at_utc,
                                            1,
                                            0,
                                            0
                                        );

                                        DELETE FROM schema_migrations WHERE version = 14;
                                        """;
            insertCommand.Parameters.AddWithValue("$submitted_at_utc", submittedAtUtc.ToString("O"));
            insertCommand.Parameters.AddWithValue("$download_started_at_utc", downloadStartedAtUtc.ToString("O"));
            insertCommand.Parameters.AddWithValue("$download_completed_at_utc", submittedAtUtc.ToString("O"));
            insertCommand.Parameters.AddWithValue("$seeding_started_at_utc", seedingStartedAtUtc.ToString("O"));
            insertCommand.Parameters.AddWithValue("$last_updated_at_utc", seedingStartedAtUtc.ToString("O"));
            await insertCommand.ExecuteNonQueryAsync();
        }

        var repairMigrator = new SqliteSchemaMigrator(databaseFilePath);
        await repairMigrator.ApplyMigrationsAsync(CancellationToken.None);

        await using var verifyConnection = new SqliteConnection($"Data Source={databaseFilePath}");
        await verifyConnection.OpenAsync();
        var verifyCommand = verifyConnection.CreateCommand();
        verifyCommand.CommandText = """
                                    SELECT download_completed_at_utc
                                    FROM torrent_history
                                    WHERE torrent_id = '11111111-1111-1111-1111-111111111111';
                                    """;
        var repairedValue = (string?) await verifyCommand.ExecuteScalarAsync();

        Assert.Equal(seedingStartedAtUtc, DateTimeOffset.Parse(repairedValue!));
    }

    [Fact]
    public async Task SqliteNativeLibraryVersion_IsNotVulnerableToCve20256965()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version();";

        var version = Assert.IsType<string>(await command.ExecuteScalarAsync());

        Assert.True(
            Version.Parse(version) >= new Version(3, 50, 2),
            $"SQLite native library version '{version}' must be 3.50.2 or newer."
        );
    }

    [Fact]
    public async Task Startup_CreatesSchemaMigrationsTable_AndRecordsAppliedVersions()
    {
        var rootPath = CreateTempRootPath("torrentcore-migrations");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var databaseFilePath = Path.Combine(storagePath, "torrentcore.db");

        await using var factory = CreateFactory(downloadPath, storagePath);
        using var httpClient = factory.CreateClient();

        var response = await httpClient.GetAsync("api/host/status");
        response.EnsureSuccessStatusCode();

        await using var connection = new SqliteConnection($"Data Source={databaseFilePath}");
        await connection.OpenAsync();

        var journalModeCommand = connection.CreateCommand();
        journalModeCommand.CommandText = "PRAGMA journal_mode;";
        Assert.Equal("wal", Assert.IsType<string>(await journalModeCommand.ExecuteScalarAsync()));

        var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM schema_migrations ORDER BY version;";

        var versions = new List<int>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            versions.Add(reader.GetInt32(0));
        }

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19], versions);

        var torrentColumnsCommand = connection.CreateCommand();
        torrentColumnsCommand.CommandText = "PRAGMA table_info(torrents);";
        var torrentColumns = new List<string>();
        await using (var torrentColumnsReader = await torrentColumnsCommand.ExecuteReaderAsync())
        {
            while (await torrentColumnsReader.ReadAsync())
            {
                torrentColumns.Add(torrentColumnsReader.GetString(1));
            }
        }
        Assert.Contains("metadata_resolution_attempt_started_at_utc", torrentColumns);
        Assert.Contains("metadata_resolution_last_yielded_at_utc", torrentColumns);
        Assert.Contains("seeding_policy_applied_at_utc", torrentColumns);

        var historyColumnsCommand = connection.CreateCommand();
        historyColumnsCommand.CommandText = "PRAGMA table_info(torrent_history);";

        var historyColumns = new List<string>();
        await using var historyReader = await historyColumnsCommand.ExecuteReaderAsync();
        while (await historyReader.ReadAsync())
        {
            historyColumns.Add(historyReader.GetString(1));
        }

        Assert.Contains("download_root_path", historyColumns);
        Assert.Contains("latest_completion_callback_feedback_received_at_utc", historyColumns);
        Assert.Contains("latest_completion_callback_feedback_json", historyColumns);
        Assert.Contains("removal_kind", historyColumns);
        Assert.DoesNotContain("save_path", historyColumns);
    }

    [Fact]
    public async Task Startup_UpgradesLegacyActivityLogsSchema()
    {
        var rootPath = CreateTempRootPath("torrentcore-legacy-migrations");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var databaseFilePath = Path.Combine(storagePath, "torrentcore.db");
        Directory.CreateDirectory(storagePath);

        await using (var connection = new SqliteConnection($"Data Source={databaseFilePath}"))
        {
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE activity_logs (
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
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using var factory = CreateFactory(downloadPath, storagePath);
        using var httpClient = factory.CreateClient();

        var response = await httpClient.GetAsync("api/host/status");
        response.EnsureSuccessStatusCode();

        await using var verifyConnection = new SqliteConnection($"Data Source={databaseFilePath}");
        await verifyConnection.OpenAsync();

        var pragmaCommand = verifyConnection.CreateCommand();
        pragmaCommand.CommandText = "PRAGMA table_info(activity_logs);";

        var columns = new List<string>();
        await using var reader = await pragmaCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        Assert.Contains("service_instance_id", columns);
    }

    [Fact]
    public async Task Startup_UpgradesLegacyTorrentsSchema()
    {
        var rootPath = CreateTempRootPath("torrentcore-legacy-torrent-migrations");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var databaseFilePath = Path.Combine(storagePath, "torrentcore.db");
        Directory.CreateDirectory(storagePath);

        await using (var connection = new SqliteConnection($"Data Source={databaseFilePath}"))
        {
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE torrents (
                    torrent_id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    state TEXT NOT NULL,
                    magnet_uri TEXT NOT NULL,
                    info_hash TEXT NULL,
                    save_path TEXT NOT NULL,
                    progress_percent REAL NOT NULL,
                    downloaded_bytes INTEGER NOT NULL,
                    total_bytes INTEGER NULL,
                    download_rate_bytes_per_second INTEGER NOT NULL,
                    upload_rate_bytes_per_second INTEGER NOT NULL,
                    tracker_count INTEGER NOT NULL,
                    connected_peer_count INTEGER NOT NULL,
                    added_at_utc TEXT NOT NULL,
                    completed_at_utc TEXT NULL,
                    last_activity_at_utc TEXT NULL,
                    error_message TEXT NULL
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using var factory = CreateFactory(downloadPath, storagePath);
        using var httpClient = factory.CreateClient();

        var response = await httpClient.GetAsync("api/host/status");
        response.EnsureSuccessStatusCode();

        await using var verifyConnection = new SqliteConnection($"Data Source={databaseFilePath}");
        await verifyConnection.OpenAsync();

        var pragmaCommand = verifyConnection.CreateCommand();
        pragmaCommand.CommandText = "PRAGMA table_info(torrents);";

        var columns = new List<string>();
        await using var reader = await pragmaCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        Assert.Contains("download_root_path", columns);
        Assert.Contains("uploaded_bytes", columns);
        Assert.Contains("seeding_started_at_utc", columns);
        Assert.Contains("desired_state", columns);
        Assert.Contains("category_key", columns);
        Assert.Contains("completion_callback_label", columns);
        Assert.Contains("invoke_completion_callback", columns);
        Assert.Contains("completion_callback_state", columns);
        Assert.Contains("completion_callback_pending_since_utc", columns);
        Assert.Contains("completion_callback_invoked_at_utc", columns);
        Assert.Contains("completion_callback_last_error", columns);
    }

    [Fact]
    public async Task Startup_CreatesRuntimeSettingsTable()
    {
        var rootPath = CreateTempRootPath("torrentcore-runtime-settings");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var databaseFilePath = Path.Combine(storagePath, "torrentcore.db");

        await using var factory = CreateFactory(downloadPath, storagePath);
        using var httpClient = factory.CreateClient();

        var response = await httpClient.GetAsync("api/host/status");
        response.EnsureSuccessStatusCode();

        await using var verifyConnection = new SqliteConnection($"Data Source={databaseFilePath}");
        await verifyConnection.OpenAsync();

        var command = verifyConnection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'runtime_settings';";
        var tableName = await command.ExecuteScalarAsync();

        Assert.Equal("runtime_settings", tableName);
    }

    [Fact]
    public async Task Startup_CreatesTorrentCategoriesTable()
    {
        var rootPath = CreateTempRootPath("torrentcore-category-settings");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var databaseFilePath = Path.Combine(storagePath, "torrentcore.db");

        await using var factory = CreateFactory(downloadPath, storagePath);
        using var httpClient = factory.CreateClient();

        var response = await httpClient.GetAsync("api/categories");
        response.EnsureSuccessStatusCode();

        await using var verifyConnection = new SqliteConnection($"Data Source={databaseFilePath}");
        await verifyConnection.OpenAsync();

        var command = verifyConnection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'torrent_categories';";
        var tableName = await command.ExecuteScalarAsync();

        Assert.Equal("torrent_categories", tableName);
    }

    [Fact]
    public async Task Startup_CreatesTorrentHistoryTable()
    {
        var rootPath = CreateTempRootPath("torrentcore-history");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var databaseFilePath = Path.Combine(storagePath, "torrentcore.db");

        await using var factory = CreateFactory(downloadPath, storagePath);
        using var httpClient = factory.CreateClient();

        var response = await httpClient.GetAsync("api/host/status");
        response.EnsureSuccessStatusCode();

        await using var verifyConnection = new SqliteConnection($"Data Source={databaseFilePath}");
        await verifyConnection.OpenAsync();

        var command = verifyConnection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'torrent_history';";
        var tableName = await command.ExecuteScalarAsync();

        Assert.Equal("torrent_history", tableName);
    }

    private static WebApplicationFactory<Program> CreateFactory(string downloadPath, string storagePath)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configurationBuilder) =>
                {
                    configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        [$"{TorrentCoreServiceOptions.SectionName}:DownloadRootPath"] = downloadPath,
                        [$"{TorrentCoreServiceOptions.SectionName}:StorageRootPath"] = storagePath,
                    });
                });
            });
    }

    private static string CreateTempRootPath(string prefix)
    {
        return Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
    }
}
