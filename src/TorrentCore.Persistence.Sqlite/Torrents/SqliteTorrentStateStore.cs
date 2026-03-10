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
                state,
                magnet_uri,
                info_hash,
                save_path,
                progress_percent,
                downloaded_bytes,
                total_bytes,
                download_rate_bytes_per_second,
                upload_rate_bytes_per_second,
                tracker_count,
                connected_peer_count,
                added_at_utc,
                completed_at_utc,
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
                state,
                magnet_uri,
                info_hash,
                save_path,
                progress_percent,
                downloaded_bytes,
                total_bytes,
                download_rate_bytes_per_second,
                upload_rate_bytes_per_second,
                tracker_count,
                connected_peer_count,
                added_at_utc,
                completed_at_utc,
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

        var command = CreateUpsertCommand(connection, torrent);
        command.CommandText = command.CommandText.Replace("INSERT OR REPLACE", "INSERT", StringComparison.Ordinal);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateAsync(TorrentSnapshot torrent, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var command = CreateUpsertCommand(connection, torrent);
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
                State = Enum.Parse<TorrentState>(reader.GetString(2), ignoreCase: true),
                MagnetUri = reader.GetString(3),
                InfoHash = reader.IsDBNull(4) ? null : reader.GetString(4),
                SavePath = reader.GetString(5),
                ProgressPercent = reader.GetDouble(6),
                DownloadedBytes = reader.GetInt64(7),
                TotalBytes = reader.IsDBNull(8) ? null : reader.GetInt64(8),
                DownloadRateBytesPerSecond = reader.GetInt64(9),
                UploadRateBytesPerSecond = reader.GetInt64(10),
                TrackerCount = reader.GetInt32(11),
                ConnectedPeerCount = reader.GetInt32(12),
                AddedAtUtc = DateTimeOffset.Parse(reader.GetString(13), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                CompletedAtUtc = reader.IsDBNull(14) ? null : DateTimeOffset.Parse(reader.GetString(14), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                LastActivityAtUtc = reader.IsDBNull(15) ? null : DateTimeOffset.Parse(reader.GetString(15), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                ErrorMessage = reader.IsDBNull(16) ? null : reader.GetString(16),
            });
        }

        return results;
    }

    private static SqliteCommand CreateUpsertCommand(SqliteConnection connection, TorrentSnapshot torrent)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT OR REPLACE INTO torrents (
                torrent_id,
                name,
                state,
                magnet_uri,
                info_hash,
                save_path,
                progress_percent,
                downloaded_bytes,
                total_bytes,
                download_rate_bytes_per_second,
                upload_rate_bytes_per_second,
                tracker_count,
                connected_peer_count,
                added_at_utc,
                completed_at_utc,
                last_activity_at_utc,
                error_message
            )
            VALUES (
                $torrent_id,
                $name,
                $state,
                $magnet_uri,
                $info_hash,
                $save_path,
                $progress_percent,
                $downloaded_bytes,
                $total_bytes,
                $download_rate_bytes_per_second,
                $upload_rate_bytes_per_second,
                $tracker_count,
                $connected_peer_count,
                $added_at_utc,
                $completed_at_utc,
                $last_activity_at_utc,
                $error_message
            );
            """;

        command.Parameters.AddWithValue("$torrent_id", torrent.TorrentId.ToString());
        command.Parameters.AddWithValue("$name", torrent.Name);
        command.Parameters.AddWithValue("$state", torrent.State.ToString());
        command.Parameters.AddWithValue("$magnet_uri", torrent.MagnetUri);
        command.Parameters.AddWithValue("$info_hash", torrent.InfoHash ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$save_path", torrent.SavePath);
        command.Parameters.AddWithValue("$progress_percent", torrent.ProgressPercent);
        command.Parameters.AddWithValue("$downloaded_bytes", torrent.DownloadedBytes);
        command.Parameters.AddWithValue("$total_bytes", torrent.TotalBytes ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$download_rate_bytes_per_second", torrent.DownloadRateBytesPerSecond);
        command.Parameters.AddWithValue("$upload_rate_bytes_per_second", torrent.UploadRateBytesPerSecond);
        command.Parameters.AddWithValue("$tracker_count", torrent.TrackerCount);
        command.Parameters.AddWithValue("$connected_peer_count", torrent.ConnectedPeerCount);
        command.Parameters.AddWithValue("$added_at_utc", torrent.AddedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$completed_at_utc", torrent.CompletedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$last_activity_at_utc", torrent.LastActivityAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$error_message", torrent.ErrorMessage ?? (object)DBNull.Value);
        return command;
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
