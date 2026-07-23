#region

using System.Globalization;
using Microsoft.Data.Sqlite;
using TorrentCore.Contracts.History;
using TorrentCore.Core.History;

#endregion

namespace TorrentCore.Persistence.Sqlite.History;

public sealed class SqliteTorrentHistoryStore(string databaseFilePath) : ITorrentHistoryStore
{
    private const string SelectColumns = """
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
                                             latest_completion_callback_feedback_received_at_utc,
                                             latest_completion_callback_feedback_json,
                                             data_deleted,
                                             removal_reason,
                                             removed_by_cleanup_policy,
                                             final_payload_path,
                                             service_instance_id_last_seen,
                                             removal_kind
                                         FROM torrent_history
                                         """;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private volatile bool          _isInitialized;

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

            await using var connection = await SqliteConnectionFactory.OpenAsync(databaseFilePath, cancellationToken);
            _isInitialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task<TorrentHistoryRecord?> GetAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = await SqliteConnectionFactory.OpenAsync(databaseFilePath, cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = $"{SelectColumns}\nWHERE torrent_id = $torrent_id\nLIMIT 1;";
        command.Parameters.AddWithValue("$torrent_id", torrentId.ToString());

        return await ReadSingleAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<TorrentHistoryRecord>> ListAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = await SqliteConnectionFactory.OpenAsync(databaseFilePath, cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = $"{SelectColumns}\nORDER BY submitted_at_utc DESC, torrent_id DESC;";

        var results = new List<TorrentHistoryRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadRecord(reader));
        }

        return results;
    }

    public async Task InsertAsync(TorrentHistoryRecord record, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var writeLease = await SqliteWriteCoordinator.AcquireAsync(databaseFilePath, cancellationToken);
        await using var connection = await SqliteConnectionFactory.OpenAsync(databaseFilePath, cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
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
                                  latest_completion_callback_feedback_received_at_utc,
                                  latest_completion_callback_feedback_json,
                                  data_deleted,
                                  removal_reason,
                                  removed_by_cleanup_policy,
                                  final_payload_path,
                                  service_instance_id_last_seen,
                                  removal_kind
                              )
                              VALUES (
                                  $torrent_id,
                                  $name,
                                  $magnet_uri,
                                  $info_hash,
                                  $category_key,
                                  $download_root_path,
                                  $latest_torrent_state,
                                  $latest_wait_reason,
                                  $latest_error_message,
                                  $latest_progress_percent,
                                  $latest_downloaded_bytes,
                                  $latest_uploaded_bytes,
                                  $latest_total_bytes,
                                  $latest_download_rate_bytes_per_second,
                                  $latest_upload_rate_bytes_per_second,
                                  $latest_tracker_count,
                                  $latest_connected_peer_count,
                                  $submitted_at_utc,
                                  $metadata_resolved_at_utc,
                                  $download_started_at_utc,
                                  $download_completed_at_utc,
                                  $seeding_started_at_utc,
                                  $last_activity_at_utc,
                                  $last_updated_at_utc,
                                  $removed_at_utc,
                                  $invoke_completion_callback,
                                  $completion_callback_label,
                                  $latest_callback_status,
                                  $callback_started_at_utc,
                                  $callback_completed_at_utc,
                                  $callback_last_error,
                                  $latest_completion_callback_feedback_received_at_utc,
                                  $latest_completion_callback_feedback_json,
                                  $data_deleted,
                                  $removal_reason,
                                  $removed_by_cleanup_policy,
                                  $final_payload_path,
                                  $service_instance_id_last_seen,
                                  $removal_kind
                              );
                              """;

        BindRecord(command, record);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> TryInsertAsync(TorrentHistoryRecord record, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var writeLease = await SqliteWriteCoordinator.AcquireAsync(databaseFilePath, cancellationToken);
        await using var connection = await SqliteConnectionFactory.OpenAsync(databaseFilePath, cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
                              INSERT OR IGNORE INTO torrent_history (
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
                                  latest_completion_callback_feedback_received_at_utc,
                                  latest_completion_callback_feedback_json,
                                  data_deleted,
                                  removal_reason,
                                  removed_by_cleanup_policy,
                                  final_payload_path,
                                  service_instance_id_last_seen,
                                  removal_kind
                              )
                              VALUES (
                                  $torrent_id,
                                  $name,
                                  $magnet_uri,
                                  $info_hash,
                                  $category_key,
                                  $download_root_path,
                                  $latest_torrent_state,
                                  $latest_wait_reason,
                                  $latest_error_message,
                                  $latest_progress_percent,
                                  $latest_downloaded_bytes,
                                  $latest_uploaded_bytes,
                                  $latest_total_bytes,
                                  $latest_download_rate_bytes_per_second,
                                  $latest_upload_rate_bytes_per_second,
                                  $latest_tracker_count,
                                  $latest_connected_peer_count,
                                  $submitted_at_utc,
                                  $metadata_resolved_at_utc,
                                  $download_started_at_utc,
                                  $download_completed_at_utc,
                                  $seeding_started_at_utc,
                                  $last_activity_at_utc,
                                  $last_updated_at_utc,
                                  $removed_at_utc,
                                  $invoke_completion_callback,
                                  $completion_callback_label,
                                  $latest_callback_status,
                                  $callback_started_at_utc,
                                  $callback_completed_at_utc,
                                  $callback_last_error,
                                  $latest_completion_callback_feedback_received_at_utc,
                                  $latest_completion_callback_feedback_json,
                                  $data_deleted,
                                  $removal_reason,
                                  $removed_by_cleanup_policy,
                                  $final_payload_path,
                                  $service_instance_id_last_seen,
                                  $removal_kind
                              );
                              """;

        BindRecord(command, record);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task UpdateAsync(TorrentHistoryRecord record, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var writeLease = await SqliteWriteCoordinator.AcquireAsync(databaseFilePath, cancellationToken);
        await using var connection = await SqliteConnectionFactory.OpenAsync(databaseFilePath, cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
                              UPDATE torrent_history
                              SET
                                  name = $name,
                                  magnet_uri = $magnet_uri,
                                  info_hash = $info_hash,
                                  category_key = $category_key,
                                  download_root_path = $download_root_path,
                                  latest_torrent_state = $latest_torrent_state,
                                  latest_wait_reason = $latest_wait_reason,
                                  latest_error_message = $latest_error_message,
                                  latest_progress_percent = $latest_progress_percent,
                                  latest_downloaded_bytes = $latest_downloaded_bytes,
                                  latest_uploaded_bytes = $latest_uploaded_bytes,
                                  latest_total_bytes = $latest_total_bytes,
                                  latest_download_rate_bytes_per_second = $latest_download_rate_bytes_per_second,
                                  latest_upload_rate_bytes_per_second = $latest_upload_rate_bytes_per_second,
                                  latest_tracker_count = $latest_tracker_count,
                                  latest_connected_peer_count = $latest_connected_peer_count,
                                  submitted_at_utc = $submitted_at_utc,
                                  metadata_resolved_at_utc = $metadata_resolved_at_utc,
                                  download_started_at_utc = $download_started_at_utc,
                                  download_completed_at_utc = $download_completed_at_utc,
                                  seeding_started_at_utc = $seeding_started_at_utc,
                                  last_activity_at_utc = $last_activity_at_utc,
                                  last_updated_at_utc = $last_updated_at_utc,
                                  removed_at_utc = $removed_at_utc,
                                  invoke_completion_callback = $invoke_completion_callback,
                                  completion_callback_label = $completion_callback_label,
                                  latest_callback_status = $latest_callback_status,
                                  callback_started_at_utc = $callback_started_at_utc,
                                  callback_completed_at_utc = $callback_completed_at_utc,
                                  callback_last_error = $callback_last_error,
                                  latest_completion_callback_feedback_received_at_utc = $latest_completion_callback_feedback_received_at_utc,
                                  latest_completion_callback_feedback_json = $latest_completion_callback_feedback_json,
                                  data_deleted = $data_deleted,
                                  removal_reason = $removal_reason,
                                  removed_by_cleanup_policy = $removed_by_cleanup_policy,
                                  final_payload_path = $final_payload_path,
                                  service_instance_id_last_seen = $service_instance_id_last_seen,
                                  removal_kind = $removal_kind
                              WHERE torrent_id = $torrent_id;
                              """;

        BindRecord(command, record);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<TorrentHistoryRecord?> ReadSingleAsync(SqliteCommand command,
        CancellationToken                                                                    cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadRecord(reader);
    }

    private static TorrentHistoryRecord ReadRecord(SqliteDataReader reader)
    {
        return new TorrentHistoryRecord
        {
            TorrentId = Guid.Parse(reader.GetString(0)),
            Name = reader.GetString(1),
            MagnetUri = reader.GetString(2),
            InfoHash = reader.IsDBNull(3) ? null : reader.GetString(3),
            CategoryKey = reader.IsDBNull(4) ? null : reader.GetString(4),
            DownloadRootPath = reader.IsDBNull(5) ? null : reader.GetString(5),
            LatestTorrentState = reader.GetString(6),
            LatestWaitReason = reader.IsDBNull(7) ? null : reader.GetString(7),
            LatestErrorMessage = reader.IsDBNull(8) ? null : reader.GetString(8),
            LatestProgressPercent = reader.GetDouble(9),
            LatestDownloadedBytes = reader.GetInt64(10),
            LatestUploadedBytes = reader.GetInt64(11),
            LatestTotalBytes = reader.IsDBNull(12) ? null : reader.GetInt64(12),
            LatestDownloadRateBytesPerSecond = reader.GetInt64(13),
            LatestUploadRateBytesPerSecond = reader.GetInt64(14),
            LatestTrackerCount = reader.GetInt32(15),
            LatestConnectedPeerCount = reader.GetInt32(16),
            SubmittedAtUtc = ParseDateTime(reader.GetString(17)),
            MetadataResolvedAtUtc = reader.IsDBNull(18) ? null : ParseDateTime(reader.GetString(18)),
            DownloadStartedAtUtc = reader.IsDBNull(19) ? null : ParseDateTime(reader.GetString(19)),
            DownloadCompletedAtUtc = reader.IsDBNull(20) ? null : ParseDateTime(reader.GetString(20)),
            SeedingStartedAtUtc = reader.IsDBNull(21) ? null : ParseDateTime(reader.GetString(21)),
            LastActivityAtUtc = reader.IsDBNull(22) ? null : ParseDateTime(reader.GetString(22)),
            LastUpdatedAtUtc = ParseDateTime(reader.GetString(23)),
            RemovedAtUtc = reader.IsDBNull(24) ? null : ParseDateTime(reader.GetString(24)),
            InvokeCompletionCallback = reader.GetInt64(25) != 0,
            CompletionCallbackLabel = reader.IsDBNull(26) ? null : reader.GetString(26),
            LatestCallbackStatus = reader.IsDBNull(27) ? null : reader.GetString(27),
            CallbackStartedAtUtc = reader.IsDBNull(28) ? null : ParseDateTime(reader.GetString(28)),
            CallbackCompletedAtUtc = reader.IsDBNull(29) ? null : ParseDateTime(reader.GetString(29)),
            CallbackLastError = reader.IsDBNull(30) ? null : reader.GetString(30),
            LatestCompletionCallbackFeedbackReceivedAtUtc = reader.IsDBNull(31) ? null : ParseDateTime(reader.GetString(31)),
            LatestCompletionCallbackFeedbackJson = reader.IsDBNull(32) ? null : reader.GetString(32),
            DataDeleted = reader.GetInt64(33) != 0,
            RemovalReason = reader.IsDBNull(34) ? null : reader.GetString(34),
            RemovedByCleanupPolicy = reader.GetInt64(35) != 0,
            FinalPayloadPath = reader.IsDBNull(36) ? null : reader.GetString(36),
            ServiceInstanceIdLastSeen = reader.IsDBNull(37) ? null : Guid.Parse(reader.GetString(37)),
            RemovalKind = reader.IsDBNull(38) ? null : ParseRemovalKind(reader.GetString(38)),
        };
    }

    private static void BindRecord(SqliteCommand command, TorrentHistoryRecord record)
    {
        command.Parameters.AddWithValue("$torrent_id", record.TorrentId.ToString());
        command.Parameters.AddWithValue("$name", record.Name);
        command.Parameters.AddWithValue("$magnet_uri", record.MagnetUri);
        command.Parameters.AddWithValue("$info_hash", (object?)record.InfoHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$category_key", (object?)record.CategoryKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$download_root_path", (object?)record.DownloadRootPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$latest_torrent_state", record.LatestTorrentState);
        command.Parameters.AddWithValue("$latest_wait_reason", (object?)record.LatestWaitReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$latest_error_message", (object?)record.LatestErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$latest_progress_percent", record.LatestProgressPercent);
        command.Parameters.AddWithValue("$latest_downloaded_bytes", record.LatestDownloadedBytes);
        command.Parameters.AddWithValue("$latest_uploaded_bytes", record.LatestUploadedBytes);
        command.Parameters.AddWithValue("$latest_total_bytes", (object?)record.LatestTotalBytes ?? DBNull.Value);
        command.Parameters.AddWithValue("$latest_download_rate_bytes_per_second", record.LatestDownloadRateBytesPerSecond);
        command.Parameters.AddWithValue("$latest_upload_rate_bytes_per_second", record.LatestUploadRateBytesPerSecond);
        command.Parameters.AddWithValue("$latest_tracker_count", record.LatestTrackerCount);
        command.Parameters.AddWithValue("$latest_connected_peer_count", record.LatestConnectedPeerCount);
        command.Parameters.AddWithValue("$submitted_at_utc", record.SubmittedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$metadata_resolved_at_utc", ToDbValue(record.MetadataResolvedAtUtc));
        command.Parameters.AddWithValue("$download_started_at_utc", ToDbValue(record.DownloadStartedAtUtc));
        command.Parameters.AddWithValue("$download_completed_at_utc", ToDbValue(record.DownloadCompletedAtUtc));
        command.Parameters.AddWithValue("$seeding_started_at_utc", ToDbValue(record.SeedingStartedAtUtc));
        command.Parameters.AddWithValue("$last_activity_at_utc", ToDbValue(record.LastActivityAtUtc));
        command.Parameters.AddWithValue("$last_updated_at_utc", record.LastUpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$removed_at_utc", ToDbValue(record.RemovedAtUtc));
        command.Parameters.AddWithValue("$invoke_completion_callback", record.InvokeCompletionCallback ? 1 : 0);
        command.Parameters.AddWithValue("$completion_callback_label", (object?)record.CompletionCallbackLabel ?? DBNull.Value);
        command.Parameters.AddWithValue("$latest_callback_status", (object?)record.LatestCallbackStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("$callback_started_at_utc", ToDbValue(record.CallbackStartedAtUtc));
        command.Parameters.AddWithValue("$callback_completed_at_utc", ToDbValue(record.CallbackCompletedAtUtc));
        command.Parameters.AddWithValue("$callback_last_error", (object?)record.CallbackLastError ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$latest_completion_callback_feedback_received_at_utc",
            ToDbValue(record.LatestCompletionCallbackFeedbackReceivedAtUtc)
        );
        command.Parameters.AddWithValue(
            "$latest_completion_callback_feedback_json",
            (object?)record.LatestCompletionCallbackFeedbackJson ?? DBNull.Value
        );
        command.Parameters.AddWithValue("$data_deleted", record.DataDeleted ? 1 : 0);
        command.Parameters.AddWithValue("$removal_reason", (object?)record.RemovalReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$removed_by_cleanup_policy", record.RemovedByCleanupPolicy ? 1 : 0);
        command.Parameters.AddWithValue("$final_payload_path", (object?)record.FinalPayloadPath ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$service_instance_id_last_seen",
            record.ServiceInstanceIdLastSeen?.ToString() ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "$removal_kind",
            record.RemovalKind?.ToString() ?? (object)DBNull.Value);
    }

    private static object ToDbValue(DateTimeOffset? value)
    {
        return value?.ToString("O", CultureInfo.InvariantCulture) ?? (object)DBNull.Value;
    }

    private static DateTimeOffset ParseDateTime(string value)
    {
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static TorrentRemovalKind? ParseRemovalKind(string value)
    {
        return Enum.TryParse<TorrentRemovalKind>(value, ignoreCase: false, out var removalKind)
            ? removalKind
            : null;
    }

}
