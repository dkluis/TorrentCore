#region

using System.Globalization;
using Microsoft.Data.Sqlite;

#endregion

namespace TorrentCore.Persistence.Sqlite.Schema;

public sealed class SqliteSchemaMigrator(string databaseFilePath)
{
    private readonly SemaphoreSlim _migrationLock = new(1, 1);
    private volatile bool          _isMigrated;

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

            await using var writeLease = await SqliteWriteCoordinator.AcquireAsync(databaseFilePath, cancellationToken);
            await using var connection = await SqliteConnectionFactory.OpenAsync(databaseFilePath, cancellationToken);

            var journalModeCommand = connection.CreateCommand();
            journalModeCommand.CommandText = "PRAGMA journal_mode=WAL;";
            await journalModeCommand.ExecuteNonQueryAsync(cancellationToken);

            await EnsureSchemaMigrationsTableAsync(connection, cancellationToken);

            var migrations      = GetMigrations();
            var appliedVersions = await GetAppliedVersionsAsync(connection, cancellationToken);

            foreach (var migration in migrations)
            {
                if (appliedVersions.Contains(migration.Version))
                {
                    continue;
                }

                await using var transaction =
                        (SqliteTransaction) await connection.BeginTransactionAsync(cancellationToken);
                try
                {
                    await migration.ApplyAsync(connection, cancellationToken);

                    var recordCommand = connection.CreateCommand();
                    recordCommand.Transaction = transaction;
                    recordCommand.CommandText = """
                                                INSERT INTO schema_migrations (version, name, applied_at_utc)
                                                VALUES ($version, $name, $applied_at_utc);
                                                """;
                    recordCommand.Parameters.AddWithValue("$version",        migration.Version);
                    recordCommand.Parameters.AddWithValue("$name",           migration.Name);
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

    private static async Task EnsureSchemaMigrationsTableAsync(SqliteConnection connection,
        CancellationToken                                                       cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
                              CREATE TABLE IF NOT EXISTS schema_migrations (
                                  version INTEGER PRIMARY KEY,
                                  name TEXT NOT NULL,
                                  applied_at_utc TEXT NOT NULL
                              );
                              """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<HashSet<int>> GetAppliedVersionsAsync(SqliteConnection connection,
        CancellationToken                                                            cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM schema_migrations;";

        var             appliedVersions = new HashSet<int>();
        await using var reader          = await command.ExecuteReaderAsync(cancellationToken);
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
                1, "create_activity_logs", async (connection, cancellationToken) =>
                {
                    var command = connection.CreateCommand();
                    command.CommandText = """
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
                }
            ),
            new SqliteMigrationDefinition(
                2, "add_activity_logs_service_instance_id", async (connection, cancellationToken) =>
                {
                    if (!await ColumnExistsAsync(connection, "activity_logs", "service_instance_id", cancellationToken))
                    {
                        var alterCommand = connection.CreateCommand();
                        alterCommand.CommandText =
                                "ALTER TABLE activity_logs ADD COLUMN service_instance_id TEXT NULL;";
                        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

                    var indexCommand = connection.CreateCommand();
                    indexCommand.CommandText = """
                                               CREATE INDEX IF NOT EXISTS idx_activity_logs_service_instance_id
                                                   ON activity_logs (service_instance_id);
                                               """;
                    await indexCommand.ExecuteNonQueryAsync(cancellationToken);
                }
            ),
            new SqliteMigrationDefinition(
                3, "create_torrents", async (connection, cancellationToken) =>
                {
                    var command = connection.CreateCommand();
                    command.CommandText = """
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
                }
            ),
            new SqliteMigrationDefinition(
                4, "add_torrents_download_root_path", async (connection, cancellationToken) =>
                {
                    if (!await ColumnExistsAsync(connection, "torrents", "download_root_path", cancellationToken))
                    {
                        var alterCommand = connection.CreateCommand();
                        alterCommand.CommandText = "ALTER TABLE torrents ADD COLUMN download_root_path TEXT NULL;";
                        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
                    }
                }
            ),
            new SqliteMigrationDefinition(
                5, "add_torrents_uploaded_and_seeding_fields", async (connection, cancellationToken) =>
                {
                    if (!await ColumnExistsAsync(connection, "torrents", "uploaded_bytes", cancellationToken))
                    {
                        var addUploadedBytesCommand = connection.CreateCommand();
                        addUploadedBytesCommand.CommandText =
                                "ALTER TABLE torrents ADD COLUMN uploaded_bytes INTEGER NOT NULL DEFAULT 0;";
                        await addUploadedBytesCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

                    if (!await ColumnExistsAsync(connection, "torrents", "seeding_started_at_utc", cancellationToken))
                    {
                        var addSeedingStartedCommand = connection.CreateCommand();
                        addSeedingStartedCommand.CommandText =
                                "ALTER TABLE torrents ADD COLUMN seeding_started_at_utc TEXT NULL;";
                        await addSeedingStartedCommand.ExecuteNonQueryAsync(cancellationToken);
                    }
                }
            ),
            new SqliteMigrationDefinition(
                6, "create_runtime_settings", async (connection, cancellationToken) =>
                {
                    var command = connection.CreateCommand();
                    command.CommandText = """
                                          CREATE TABLE IF NOT EXISTS runtime_settings (
                                              setting_key TEXT PRIMARY KEY,
                                              setting_value TEXT NOT NULL,
                                              updated_at_utc TEXT NOT NULL
                                          );
                                          """;
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
            ),
            new SqliteMigrationDefinition(
                7, "add_torrents_desired_state", async (connection, cancellationToken) =>
                {
                    if (!await ColumnExistsAsync(connection, "torrents", "desired_state", cancellationToken))
                    {
                        var alterCommand = connection.CreateCommand();
                        alterCommand.CommandText =
                                "ALTER TABLE torrents ADD COLUMN desired_state TEXT NOT NULL DEFAULT 'Runnable';";
                        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

                    var normalizeCommand = connection.CreateCommand();
                    normalizeCommand.CommandText = """
                                                       UPDATE torrents
                                                       SET desired_state = CASE
                                                           WHEN state = 'Paused' THEN 'Paused'
                                                           ELSE 'Runnable'
                                                       END
                                                       WHERE desired_state IS NULL OR desired_state = '' OR state = 'Paused';
                                                   """;
                    await normalizeCommand.ExecuteNonQueryAsync(cancellationToken);
                }
            ),
            new SqliteMigrationDefinition(
                8, "create_torrent_categories_and_add_torrent_category_key", async (connection, cancellationToken) =>
                {
                    if (!await TableExistsAsync(connection, "torrent_categories", cancellationToken))
                    {
                        var createCategoriesCommand = connection.CreateCommand();
                        createCategoriesCommand.CommandText = """
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
                    indexCommand.CommandText = """
                                               CREATE INDEX IF NOT EXISTS idx_torrents_category_key
                                                   ON torrents (category_key);
                                               """;
                    await indexCommand.ExecuteNonQueryAsync(cancellationToken);
                }
            ),
            new SqliteMigrationDefinition(
                9, "add_torrent_callback_fields", async (connection, cancellationToken) =>
                {
                    if (!await ColumnExistsAsync(
                                connection, "torrents", "completion_callback_label", cancellationToken
                            ))
                    {
                        var alterCommand = connection.CreateCommand();
                        alterCommand.CommandText =
                                "ALTER TABLE torrents ADD COLUMN completion_callback_label TEXT NULL;";
                        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

                    if (!await ColumnExistsAsync(
                                connection, "torrents", "invoke_completion_callback", cancellationToken
                            ))
                    {
                        var alterCommand = connection.CreateCommand();
                        alterCommand.CommandText =
                                "ALTER TABLE torrents ADD COLUMN invoke_completion_callback INTEGER NOT NULL DEFAULT 0;";
                        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
                    }
                }
            ),
            new SqliteMigrationDefinition(
                10, "add_torrent_callback_lifecycle_fields", async (connection, cancellationToken) =>
                {
                    if (!await ColumnExistsAsync(
                                connection, "torrents", "completion_callback_state", cancellationToken
                            ))
                    {
                        var alterCommand = connection.CreateCommand();
                        alterCommand.CommandText =
                                "ALTER TABLE torrents ADD COLUMN completion_callback_state TEXT NULL;";
                        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

                    if (!await ColumnExistsAsync(
                                connection, "torrents", "completion_callback_pending_since_utc", cancellationToken
                            ))
                    {
                        var alterCommand = connection.CreateCommand();
                        alterCommand.CommandText =
                                "ALTER TABLE torrents ADD COLUMN completion_callback_pending_since_utc TEXT NULL;";
                        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

                    if (!await ColumnExistsAsync(
                                connection, "torrents", "completion_callback_invoked_at_utc", cancellationToken
                            ))
                    {
                        var alterCommand = connection.CreateCommand();
                        alterCommand.CommandText =
                                "ALTER TABLE torrents ADD COLUMN completion_callback_invoked_at_utc TEXT NULL;";
                        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

                    if (!await ColumnExistsAsync(
                                connection, "torrents", "completion_callback_last_error", cancellationToken
                            ))
                    {
                        var alterCommand = connection.CreateCommand();
                        alterCommand.CommandText =
                                "ALTER TABLE torrents ADD COLUMN completion_callback_last_error TEXT NULL;";
                        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
                    }
                }
            ),
            new SqliteMigrationDefinition(
                11, "create_torrent_history", async (connection, cancellationToken) =>
                {
                    if (!await TableExistsAsync(connection, "torrent_history", cancellationToken))
                    {
                        var command = connection.CreateCommand();
                        command.CommandText = """
                                              CREATE TABLE torrent_history (
                                                  torrent_id TEXT PRIMARY KEY,
                                                  name TEXT NOT NULL,
                                                  magnet_uri TEXT NOT NULL,
                                                  info_hash TEXT NULL,
                                                  category_key TEXT NULL,
                                                  download_root_path TEXT NULL,
                                                  latest_torrent_state TEXT NOT NULL,
                                                  latest_wait_reason TEXT NULL,
                                                  latest_error_message TEXT NULL,
                                                  latest_progress_percent REAL NOT NULL,
                                                  latest_downloaded_bytes INTEGER NOT NULL,
                                                  latest_uploaded_bytes INTEGER NOT NULL,
                                                  latest_total_bytes INTEGER NULL,
                                                  latest_download_rate_bytes_per_second INTEGER NOT NULL,
                                                  latest_upload_rate_bytes_per_second INTEGER NOT NULL,
                                                  latest_tracker_count INTEGER NOT NULL,
                                                  latest_connected_peer_count INTEGER NOT NULL,
                                                  submitted_at_utc TEXT NOT NULL,
                                                  metadata_resolved_at_utc TEXT NULL,
                                                  download_started_at_utc TEXT NULL,
                                                  download_completed_at_utc TEXT NULL,
                                                  seeding_started_at_utc TEXT NULL,
                                                  last_activity_at_utc TEXT NULL,
                                                  last_updated_at_utc TEXT NOT NULL,
                                                  removed_at_utc TEXT NULL,
                                                  invoke_completion_callback INTEGER NOT NULL,
                                                  completion_callback_label TEXT NULL,
                                                  latest_callback_status TEXT NULL,
                                                  callback_started_at_utc TEXT NULL,
                                                  callback_completed_at_utc TEXT NULL,
                                                  callback_last_error TEXT NULL,
                                                  data_deleted INTEGER NOT NULL DEFAULT 0,
                                                  removal_reason TEXT NULL,
                                                  removed_by_cleanup_policy INTEGER NOT NULL DEFAULT 0,
                                                  final_payload_path TEXT NULL,
                                                  service_instance_id_last_seen TEXT NULL
                                              );

                                              CREATE INDEX IF NOT EXISTS idx_torrent_history_submitted_at_utc
                                                  ON torrent_history (submitted_at_utc DESC, torrent_id DESC);

                                              CREATE INDEX IF NOT EXISTS idx_torrent_history_info_hash
                                                  ON torrent_history (info_hash)
                                                  WHERE info_hash IS NOT NULL;
                                              """;
                        await command.ExecuteNonQueryAsync(cancellationToken);
                    }
                }
            ),
            new SqliteMigrationDefinition(
                12, "remove_torrent_history_save_path", async (connection, cancellationToken) =>
                {
                    if (!await TableExistsAsync(connection, "torrent_history", cancellationToken) ||
                        !await ColumnExistsAsync(connection, "torrent_history", "save_path", cancellationToken))
                    {
                        return;
                    }

                    var command = connection.CreateCommand();
                    command.CommandText = """
                                          ALTER TABLE torrent_history RENAME TO torrent_history_legacy_save_path;

                                          CREATE TABLE torrent_history (
                                              torrent_id TEXT PRIMARY KEY,
                                              name TEXT NOT NULL,
                                              magnet_uri TEXT NOT NULL,
                                              info_hash TEXT NULL,
                                              category_key TEXT NULL,
                                              download_root_path TEXT NULL,
                                              latest_torrent_state TEXT NOT NULL,
                                              latest_wait_reason TEXT NULL,
                                              latest_error_message TEXT NULL,
                                              latest_progress_percent REAL NOT NULL,
                                              latest_downloaded_bytes INTEGER NOT NULL,
                                              latest_uploaded_bytes INTEGER NOT NULL,
                                              latest_total_bytes INTEGER NULL,
                                              latest_download_rate_bytes_per_second INTEGER NOT NULL,
                                              latest_upload_rate_bytes_per_second INTEGER NOT NULL,
                                              latest_tracker_count INTEGER NOT NULL,
                                              latest_connected_peer_count INTEGER NOT NULL,
                                              submitted_at_utc TEXT NOT NULL,
                                              metadata_resolved_at_utc TEXT NULL,
                                              download_started_at_utc TEXT NULL,
                                              download_completed_at_utc TEXT NULL,
                                              seeding_started_at_utc TEXT NULL,
                                              last_activity_at_utc TEXT NULL,
                                              last_updated_at_utc TEXT NOT NULL,
                                              removed_at_utc TEXT NULL,
                                              invoke_completion_callback INTEGER NOT NULL,
                                              completion_callback_label TEXT NULL,
                                              latest_callback_status TEXT NULL,
                                              callback_started_at_utc TEXT NULL,
                                              callback_completed_at_utc TEXT NULL,
                                              callback_last_error TEXT NULL,
                                              data_deleted INTEGER NOT NULL DEFAULT 0,
                                              removal_reason TEXT NULL,
                                              removed_by_cleanup_policy INTEGER NOT NULL DEFAULT 0,
                                              final_payload_path TEXT NULL,
                                              service_instance_id_last_seen TEXT NULL
                                          );

                                          INSERT INTO torrent_history (
                                              torrent_id,
                                              name,
                                              magnet_uri,
                                              info_hash,
                                              category_key,
                                              download_root_path,
                                              latest_torrent_state,
                                              latest_wait_reason,
                                              latest_error_message,
                                              latest_progress_percent,
                                              latest_downloaded_bytes,
                                              latest_uploaded_bytes,
                                              latest_total_bytes,
                                              latest_download_rate_bytes_per_second,
                                              latest_upload_rate_bytes_per_second,
                                              latest_tracker_count,
                                              latest_connected_peer_count,
                                              submitted_at_utc,
                                              metadata_resolved_at_utc,
                                              download_started_at_utc,
                                              download_completed_at_utc,
                                              seeding_started_at_utc,
                                              last_activity_at_utc,
                                              last_updated_at_utc,
                                              removed_at_utc,
                                              invoke_completion_callback,
                                              completion_callback_label,
                                              latest_callback_status,
                                              callback_started_at_utc,
                                              callback_completed_at_utc,
                                              callback_last_error,
                                              data_deleted,
                                              removal_reason,
                                              removed_by_cleanup_policy,
                                              final_payload_path,
                                              service_instance_id_last_seen
                                          )
                                          SELECT
                                              torrent_id,
                                              name,
                                              magnet_uri,
                                              info_hash,
                                              category_key,
                                              download_root_path,
                                              latest_torrent_state,
                                              latest_wait_reason,
                                              latest_error_message,
                                              latest_progress_percent,
                                              latest_downloaded_bytes,
                                              latest_uploaded_bytes,
                                              latest_total_bytes,
                                              latest_download_rate_bytes_per_second,
                                              latest_upload_rate_bytes_per_second,
                                              latest_tracker_count,
                                              latest_connected_peer_count,
                                              submitted_at_utc,
                                              metadata_resolved_at_utc,
                                              download_started_at_utc,
                                              download_completed_at_utc,
                                              seeding_started_at_utc,
                                              last_activity_at_utc,
                                              last_updated_at_utc,
                                              removed_at_utc,
                                              invoke_completion_callback,
                                              completion_callback_label,
                                              latest_callback_status,
                                              callback_started_at_utc,
                                              callback_completed_at_utc,
                                              callback_last_error,
                                              data_deleted,
                                              removal_reason,
                                              removed_by_cleanup_policy,
                                              final_payload_path,
                                              service_instance_id_last_seen
                                          FROM torrent_history_legacy_save_path;

                                          DROP TABLE torrent_history_legacy_save_path;

                                          CREATE INDEX IF NOT EXISTS idx_torrent_history_submitted_at_utc
                                              ON torrent_history (submitted_at_utc DESC, torrent_id DESC);

                                          CREATE INDEX IF NOT EXISTS idx_torrent_history_info_hash
                                              ON torrent_history (info_hash)
                                              WHERE info_hash IS NOT NULL;
                                          """;
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
            ),
            new SqliteMigrationDefinition(
                13, "add_callback_feedback_storage", async (connection, cancellationToken) =>
                {
                    if (!await ColumnExistsAsync(connection, "torrents", "completion_callback_feedback_received_at_utc", cancellationToken))
                    {
                        var alterCommand = connection.CreateCommand();
                        alterCommand.CommandText =
                            "ALTER TABLE torrents ADD COLUMN completion_callback_feedback_received_at_utc TEXT NULL;";
                        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

                    if (!await ColumnExistsAsync(connection, "torrents", "completion_callback_feedback_json", cancellationToken))
                    {
                        var alterCommand = connection.CreateCommand();
                        alterCommand.CommandText =
                            "ALTER TABLE torrents ADD COLUMN completion_callback_feedback_json TEXT NULL;";
                        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

                    if (!await ColumnExistsAsync(connection, "torrent_history", "latest_completion_callback_feedback_received_at_utc", cancellationToken))
                    {
                        var alterCommand = connection.CreateCommand();
                        alterCommand.CommandText =
                            "ALTER TABLE torrent_history ADD COLUMN latest_completion_callback_feedback_received_at_utc TEXT NULL;";
                        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

                    if (!await ColumnExistsAsync(connection, "torrent_history", "latest_completion_callback_feedback_json", cancellationToken))
                    {
                        var alterCommand = connection.CreateCommand();
                        alterCommand.CommandText =
                            "ALTER TABLE torrent_history ADD COLUMN latest_completion_callback_feedback_json TEXT NULL;";
                        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
                    }
                }
            ),
            new SqliteMigrationDefinition(
                14, "repair_premature_history_completion_timestamps", async (connection, cancellationToken) =>
                {
                    if (!await TableExistsAsync(connection, "torrent_history", cancellationToken))
                    {
                        return;
                    }

                    var command = connection.CreateCommand();
                    command.CommandText = """
                                          UPDATE torrent_history
                                          SET download_completed_at_utc = seeding_started_at_utc
                                          WHERE download_started_at_utc IS NOT NULL
                                            AND download_completed_at_utc IS NOT NULL
                                            AND seeding_started_at_utc IS NOT NULL
                                            AND download_completed_at_utc < download_started_at_utc
                                            AND seeding_started_at_utc >= download_started_at_utc;
                                          """;
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
            ),
            new SqliteMigrationDefinition(
                15, "repair_premature_history_metadata_timestamps", async (connection, cancellationToken) =>
                {
                    if (!await TableExistsAsync(connection, "torrents", cancellationToken) ||
                        !await TableExistsAsync(connection, "torrent_history", cancellationToken))
                    {
                        return;
                    }

                    var command = connection.CreateCommand();
                    command.CommandText = """
                                          UPDATE torrent_history
                                          SET metadata_resolved_at_utc = NULL
                                          WHERE metadata_resolved_at_utc IS NOT NULL
                                            AND download_started_at_utc IS NULL
                                            AND download_completed_at_utc IS NULL
                                            AND seeding_started_at_utc IS NULL
                                            AND EXISTS (
                                                SELECT 1
                                                FROM torrents
                                                WHERE torrents.torrent_id = torrent_history.torrent_id
                                                  AND torrents.state = 'ResolvingMetadata'
                                                  AND torrents.total_bytes IS NULL
                                            );
                                          """;
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
            ),
            new SqliteMigrationDefinition(
                16, "persist_download_cold_since_timestamp", async (connection, cancellationToken) =>
                {
                    if (!await ColumnExistsAsync(connection, "torrents", "download_cold_since_utc", cancellationToken))
                    {
                        var command = connection.CreateCommand();
                        command.CommandText = "ALTER TABLE torrents ADD COLUMN download_cold_since_utc TEXT NULL;";
                        await command.ExecuteNonQueryAsync(cancellationToken);
                    }
                }
            ),
            new SqliteMigrationDefinition(
                17, "add_structured_history_removal_kind", async (connection, cancellationToken) =>
                {
                    if (!await ColumnExistsAsync(connection, "torrent_history", "removal_kind", cancellationToken))
                    {
                        var alterCommand = connection.CreateCommand();
                        alterCommand.CommandText =
                                "ALTER TABLE torrent_history ADD COLUMN removal_kind TEXT NULL;";
                        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
                    }

                    var backfillCommand = connection.CreateCommand();
                    backfillCommand.CommandText = """
                                                  UPDATE torrent_history
                                                  SET removal_kind = CASE
                                                      WHEN removal_reason = 'manual_remove'
                                                          THEN 'ManualRemoval'
                                                      WHEN removal_reason = 'manual_remove_delete_data'
                                                          THEN 'ManualRemovalWithData'
                                                      WHEN removal_reason = 'automatic_cleanup'
                                                          THEN 'CompletedTorrentCleanup'
                                                      WHEN removed_by_cleanup_policy = 1
                                                           AND data_deleted = 1
                                                           AND removal_reason LIKE 'Download abandoned after %'
                                                          THEN 'ColdDownloadAbandonment'
                                                      ELSE removal_kind
                                                  END
                                                  WHERE removal_kind IS NULL
                                                    AND removed_at_utc IS NOT NULL;
                                                  """;
                    await backfillCommand.ExecuteNonQueryAsync(cancellationToken);
                }
            ),
            new SqliteMigrationDefinition(
                18, "persist_metadata_resolution_rotation", async (connection, cancellationToken) =>
                {
                    if (!await ColumnExistsAsync(
                            connection, "torrents", "metadata_resolution_attempt_started_at_utc", cancellationToken))
                    {
                        var command = connection.CreateCommand();
                        command.CommandText =
                                "ALTER TABLE torrents ADD COLUMN metadata_resolution_attempt_started_at_utc TEXT NULL;";
                        await command.ExecuteNonQueryAsync(cancellationToken);
                    }

                    if (!await ColumnExistsAsync(
                            connection, "torrents", "metadata_resolution_last_yielded_at_utc", cancellationToken))
                    {
                        var command = connection.CreateCommand();
                        command.CommandText =
                                "ALTER TABLE torrents ADD COLUMN metadata_resolution_last_yielded_at_utc TEXT NULL;";
                        await command.ExecuteNonQueryAsync(cancellationToken);
                    }
                }
            ),
            new SqliteMigrationDefinition(
                19, "persist_seeding_policy_application", async (connection, cancellationToken) =>
                {
                    if (!await ColumnExistsAsync(
                            connection, "torrents", "seeding_policy_applied_at_utc", cancellationToken))
                    {
                        var command = connection.CreateCommand();
                        command.CommandText =
                                "ALTER TABLE torrents ADD COLUMN seeding_policy_applied_at_utc TEXT NULL;";
                        await command.ExecuteNonQueryAsync(cancellationToken);
                    }
                }
            ),
            new SqliteMigrationDefinition(
                20, "persist_queue_intent", async (connection, cancellationToken) =>
                {
                    if (!await ColumnExistsAsync(
                            connection, "torrents", "ordinary_queue_order", cancellationToken))
                    {
                        var command = connection.CreateCommand();
                        command.CommandText =
                                "ALTER TABLE torrents ADD COLUMN ordinary_queue_order INTEGER NULL;";
                        await command.ExecuteNonQueryAsync(cancellationToken);
                    }

                    if (!await ColumnExistsAsync(
                            connection, "torrents", "priority_queue_order", cancellationToken))
                    {
                        var command = connection.CreateCommand();
                        command.CommandText =
                                "ALTER TABLE torrents ADD COLUMN priority_queue_order INTEGER NULL;";
                        await command.ExecuteNonQueryAsync(cancellationToken);
                    }

                    if (!await ColumnExistsAsync(
                            connection, "torrents", "is_queue_held", cancellationToken))
                    {
                        var command = connection.CreateCommand();
                        command.CommandText =
                                "ALTER TABLE torrents ADD COLUMN is_queue_held INTEGER NOT NULL DEFAULT 0;";
                        await command.ExecuteNonQueryAsync(cancellationToken);
                    }

                    var backfillCommand = connection.CreateCommand();
                    backfillCommand.CommandText = """
                                                  WITH ordered AS (
                                                      SELECT
                                                          torrent_id,
                                                          ROW_NUMBER() OVER (
                                                              ORDER BY julianday(added_at_utc), added_at_utc, torrent_id
                                                          ) AS relative_queue_order
                                                      FROM torrents
                                                      WHERE ordinary_queue_order IS NULL
                                                  ),
                                                  queue_tail AS (
                                                      SELECT COALESCE(MAX(ordinary_queue_order), 0) AS value
                                                      FROM torrents
                                                  )
                                                  UPDATE torrents
                                                  SET ordinary_queue_order = (
                                                      SELECT queue_tail.value + ordered.relative_queue_order
                                                      FROM ordered, queue_tail
                                                      WHERE ordered.torrent_id = torrents.torrent_id
                                                  )
                                                  WHERE ordinary_queue_order IS NULL;

                                                  CREATE UNIQUE INDEX IF NOT EXISTS idx_torrents_ordinary_queue_order
                                                      ON torrents (ordinary_queue_order)
                                                      WHERE ordinary_queue_order IS NOT NULL;

                                                  CREATE UNIQUE INDEX IF NOT EXISTS idx_torrents_priority_queue_order
                                                      ON torrents (priority_queue_order)
                                                      WHERE priority_queue_order IS NOT NULL;
                                                  """;
                    await backfillCommand.ExecuteNonQueryAsync(cancellationToken);
                }
            ),
            new SqliteMigrationDefinition(
                21, "persist_priority_metadata_attempt_budget", async (connection, cancellationToken) =>
                {
                    if (!await ColumnExistsAsync(
                            connection, "torrents", "priority_metadata_attempts_remaining", cancellationToken))
                    {
                        var command = connection.CreateCommand();
                        command.CommandText =
                                "ALTER TABLE torrents ADD COLUMN priority_metadata_attempts_remaining INTEGER NULL;";
                        await command.ExecuteNonQueryAsync(cancellationToken);
                    }

                    var normalizeCommand = connection.CreateCommand();
                    normalizeCommand.CommandText = """
                                                   UPDATE torrents
                                                   SET priority_metadata_attempts_remaining = CASE
                                                       WHEN priority_queue_order IS NOT NULL
                                                           THEN COALESCE(priority_metadata_attempts_remaining, 3)
                                                       ELSE NULL
                                                   END;
                                                   """;
                    await normalizeCommand.ExecuteNonQueryAsync(cancellationToken);
                }
            ),
        ];
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName,
        CancellationToken                                             cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
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

    private static async Task<bool> ColumnExistsAsync(SqliteConnection connection, string tableName, string columnName,
        CancellationToken                                              cancellationToken)
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

    private sealed record SqliteMigrationDefinition(int Version, string Name,
        Func<SqliteConnection, CancellationToken, Task> ApplyAsync);
}
