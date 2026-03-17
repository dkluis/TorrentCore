using System.Globalization;
using Microsoft.Data.Sqlite;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Torrents;

namespace TorrentCore.Persistence.Sqlite.Torrents;

public sealed class SqliteTorrentStateStore(string databaseFilePath) : ITorrentStateStore
{
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private volatile bool _isInitialized;

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_isInitialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken);

        try
        {
            if (_isInitialized)
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
            _isInitialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM torrents;";
        var count = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(count, CultureInfo.InvariantCulture);
    }

    public async Task<bool> ExistsByInfoHashAsync(string infoHash, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM torrents WHERE info_hash = $info_hash);";
        command.Parameters.AddWithValue("$info_hash", infoHash);
        var exists = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(exists, CultureInfo.InvariantCulture) == 1;
    }

    public async Task<IReadOnlyList<TorrentSnapshot>> ListAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                torrent_id,
                name,
                category_key,
                completion_callback_label,
                invoke_completion_callback,
                completion_callback_state,
                completion_callback_pending_since_utc,
                completion_callback_invoked_at_utc,
                completion_callback_last_error,
                state,
                desired_state,
                magnet_uri,
                info_hash,
                download_root_path,
                save_path,
                progress_percent,
                downloaded_bytes,
                uploaded_bytes,
                total_bytes,
                download_rate_bytes_per_second,
                upload_rate_bytes_per_second,
                tracker_count,
                connected_peer_count,
                added_at_utc,
                completed_at_utc,
                seeding_started_at_utc,
                last_activity_at_utc,
                error_message
            FROM torrents
            ORDER BY added_at_utc DESC, torrent_id DESC;
            """;

        return await ReadSnapshotsAsync(command, cancellationToken);
    }

    public async Task<TorrentSnapshot?> GetAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                torrent_id,
                name,
                category_key,
                completion_callback_label,
                invoke_completion_callback,
                completion_callback_state,
                completion_callback_pending_since_utc,
                completion_callback_invoked_at_utc,
                completion_callback_last_error,
                state,
                desired_state,
                magnet_uri,
                info_hash,
                download_root_path,
                save_path,
                progress_percent,
                downloaded_bytes,
                uploaded_bytes,
                total_bytes,
                download_rate_bytes_per_second,
                upload_rate_bytes_per_second,
                tracker_count,
                connected_peer_count,
                added_at_utc,
                completed_at_utc,
                seeding_started_at_utc,
                last_activity_at_utc,
                error_message
            FROM torrents
            WHERE torrent_id = $torrent_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$torrent_id", torrentId.ToString());

        var results = await ReadSnapshotsAsync(command, cancellationToken);
        return results.SingleOrDefault();
    }

    public async Task InsertAsync(TorrentSnapshot torrent, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = CreateInsertCommand(connection, torrent);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateAsync(TorrentSnapshot torrent, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = CreateUpdateCommand(connection, torrent);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM torrents WHERE torrent_id = $torrent_id;";
        command.Parameters.AddWithValue("$torrent_id", torrentId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<TorrentSnapshot>> ReadSnapshotsAsync(SqliteCommand command, CancellationToken cancellationToken)
    {
        var results = new List<TorrentSnapshot>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new TorrentSnapshot
            {
                TorrentId = Guid.Parse(reader.GetString(0)),
                Name = reader.GetString(1),
                CategoryKey = reader.IsDBNull(2) ? null : reader.GetString(2),
                CompletionCallbackLabel = reader.IsDBNull(3) ? null : reader.GetString(3),
                InvokeCompletionCallback = reader.GetInt64(4) != 0,
                CompletionCallbackState = reader.IsDBNull(5) ? null : Enum.Parse<TorrentCompletionCallbackState>(reader.GetString(5), ignoreCase: true),
                CompletionCallbackPendingSinceUtc = reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                CompletionCallbackInvokedAtUtc = reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                CompletionCallbackLastError = reader.IsDBNull(8) ? null : reader.GetString(8),
                State = Enum.Parse<TorrentState>(reader.GetString(9), ignoreCase: true),
                DesiredState = Enum.Parse<TorrentDesiredState>(reader.GetString(10), ignoreCase: true),
                MagnetUri = reader.GetString(11),
                InfoHash = reader.IsDBNull(12) ? null : reader.GetString(12),
                DownloadRootPath = reader.IsDBNull(13) ? null : reader.GetString(13),
                SavePath = reader.GetString(14),
                ProgressPercent = reader.GetDouble(15),
                DownloadedBytes = reader.GetInt64(16),
                UploadedBytes = reader.GetInt64(17),
                TotalBytes = reader.IsDBNull(18) ? null : reader.GetInt64(18),
                DownloadRateBytesPerSecond = reader.GetInt64(19),
                UploadRateBytesPerSecond = reader.GetInt64(20),
                TrackerCount = reader.GetInt32(21),
                ConnectedPeerCount = reader.GetInt32(22),
                AddedAtUtc = DateTimeOffset.Parse(reader.GetString(23), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                CompletedAtUtc = reader.IsDBNull(24) ? null : DateTimeOffset.Parse(reader.GetString(24), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                SeedingStartedAtUtc = reader.IsDBNull(25) ? null : DateTimeOffset.Parse(reader.GetString(25), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                LastActivityAtUtc = reader.IsDBNull(26) ? null : DateTimeOffset.Parse(reader.GetString(26), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                ErrorMessage = reader.IsDBNull(27) ? null : reader.GetString(27),
            });
        }

        return results;
    }

    private static SqliteCommand CreateInsertCommand(SqliteConnection connection, TorrentSnapshot torrent)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO torrents (
                torrent_id,
                name,
                category_key,
                completion_callback_label,
                invoke_completion_callback,
                completion_callback_state,
                completion_callback_pending_since_utc,
                completion_callback_invoked_at_utc,
                completion_callback_last_error,
                state,
                desired_state,
                magnet_uri,
                info_hash,
                download_root_path,
                save_path,
                progress_percent,
                downloaded_bytes,
                uploaded_bytes,
                total_bytes,
                download_rate_bytes_per_second,
                upload_rate_bytes_per_second,
                tracker_count,
                connected_peer_count,
                added_at_utc,
                completed_at_utc,
                seeding_started_at_utc,
                last_activity_at_utc,
                error_message
            )
            VALUES (
                $torrent_id,
                $name,
                $category_key,
                $completion_callback_label,
                $invoke_completion_callback,
                $completion_callback_state,
                $completion_callback_pending_since_utc,
                $completion_callback_invoked_at_utc,
                $completion_callback_last_error,
                $state,
                $desired_state,
                $magnet_uri,
                $info_hash,
                $download_root_path,
                $save_path,
                $progress_percent,
                $downloaded_bytes,
                $uploaded_bytes,
                $total_bytes,
                $download_rate_bytes_per_second,
                $upload_rate_bytes_per_second,
                $tracker_count,
                $connected_peer_count,
                $added_at_utc,
                $completed_at_utc,
                $seeding_started_at_utc,
                $last_activity_at_utc,
                $error_message
            );
            """;

        AddSnapshotParameters(command, torrent);
        return command;
    }

    private static SqliteCommand CreateUpdateCommand(SqliteConnection connection, TorrentSnapshot torrent)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE torrents
            SET
                name = $name,
                category_key = $category_key,
                completion_callback_label = $completion_callback_label,
                invoke_completion_callback = $invoke_completion_callback,
                completion_callback_state = $completion_callback_state,
                completion_callback_pending_since_utc = $completion_callback_pending_since_utc,
                completion_callback_invoked_at_utc = $completion_callback_invoked_at_utc,
                completion_callback_last_error = $completion_callback_last_error,
                state = $state,
                desired_state = $desired_state,
                magnet_uri = $magnet_uri,
                info_hash = $info_hash,
                download_root_path = $download_root_path,
                save_path = $save_path,
                progress_percent = $progress_percent,
                downloaded_bytes = $downloaded_bytes,
                uploaded_bytes = $uploaded_bytes,
                total_bytes = $total_bytes,
                download_rate_bytes_per_second = $download_rate_bytes_per_second,
                upload_rate_bytes_per_second = $upload_rate_bytes_per_second,
                tracker_count = $tracker_count,
                connected_peer_count = $connected_peer_count,
                added_at_utc = $added_at_utc,
                completed_at_utc = $completed_at_utc,
                seeding_started_at_utc = $seeding_started_at_utc,
                last_activity_at_utc = $last_activity_at_utc,
                error_message = $error_message
            WHERE torrent_id = $torrent_id;
            """;

        AddSnapshotParameters(command, torrent);
        return command;
    }

    private static void AddSnapshotParameters(SqliteCommand command, TorrentSnapshot torrent)
    {
        command.Parameters.AddWithValue("$torrent_id", torrent.TorrentId.ToString());
        command.Parameters.AddWithValue("$name", torrent.Name);
        command.Parameters.AddWithValue("$category_key", torrent.CategoryKey ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$completion_callback_label", torrent.CompletionCallbackLabel ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$invoke_completion_callback", torrent.InvokeCompletionCallback ? 1 : 0);
        command.Parameters.AddWithValue("$completion_callback_state", torrent.CompletionCallbackState?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$completion_callback_pending_since_utc", torrent.CompletionCallbackPendingSinceUtc?.ToString("O", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$completion_callback_invoked_at_utc", torrent.CompletionCallbackInvokedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$completion_callback_last_error", torrent.CompletionCallbackLastError ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$state", torrent.State.ToString());
        command.Parameters.AddWithValue("$desired_state", torrent.DesiredState.ToString());
        command.Parameters.AddWithValue("$magnet_uri", torrent.MagnetUri);
        command.Parameters.AddWithValue("$info_hash", torrent.InfoHash ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$download_root_path", torrent.DownloadRootPath ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$save_path", torrent.SavePath);
        command.Parameters.AddWithValue("$progress_percent", torrent.ProgressPercent);
        command.Parameters.AddWithValue("$downloaded_bytes", torrent.DownloadedBytes);
        command.Parameters.AddWithValue("$uploaded_bytes", torrent.UploadedBytes);
        command.Parameters.AddWithValue("$total_bytes", torrent.TotalBytes ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$download_rate_bytes_per_second", torrent.DownloadRateBytesPerSecond);
        command.Parameters.AddWithValue("$upload_rate_bytes_per_second", torrent.UploadRateBytesPerSecond);
        command.Parameters.AddWithValue("$tracker_count", torrent.TrackerCount);
        command.Parameters.AddWithValue("$connected_peer_count", torrent.ConnectedPeerCount);
        command.Parameters.AddWithValue("$added_at_utc", torrent.AddedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$completed_at_utc", torrent.CompletedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$seeding_started_at_utc", torrent.SeedingStartedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$last_activity_at_utc", torrent.LastActivityAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$error_message", torrent.ErrorMessage ?? (object)DBNull.Value);
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
}
