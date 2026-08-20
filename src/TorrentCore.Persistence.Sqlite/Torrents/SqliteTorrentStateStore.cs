#region

using System.Globalization;
using Microsoft.Data.Sqlite;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Torrents;

#endregion

namespace TorrentCore.Persistence.Sqlite.Torrents;

public sealed class SqliteTorrentStateStore(string databaseFilePath) : ITorrentStateStore
{
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

    public async Task<int> CountAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = await SqliteConnectionFactory.OpenAsync(databaseFilePath, cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM torrents;";
        var count = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(count, CultureInfo.InvariantCulture);
    }

    public async Task<bool> ExistsByInfoHashAsync(string infoHash, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = await SqliteConnectionFactory.OpenAsync(databaseFilePath, cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM torrents WHERE info_hash = $info_hash);";
        command.Parameters.AddWithValue("$info_hash", infoHash);
        var exists = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(exists, CultureInfo.InvariantCulture) == 1;
    }

    public async Task<IReadOnlyList<TorrentSnapshot>> ListAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = await SqliteConnectionFactory.OpenAsync(databaseFilePath, cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
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
                                  completion_callback_feedback_received_at_utc,
                                  completion_callback_feedback_json,
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
                                  download_cold_since_utc,
                                  last_activity_at_utc,
                                  error_message,
                                  metadata_resolution_attempt_started_at_utc,
                                  metadata_resolution_last_yielded_at_utc,
                                  seeding_policy_applied_at_utc,
                                  ordinary_queue_order,
                                  priority_queue_order,
                                  priority_metadata_attempts_remaining,
                                  is_queue_held
                              FROM torrents
                              ORDER BY added_at_utc DESC, torrent_id DESC;
                              """;

        return await ReadSnapshotsAsync(command, cancellationToken);
    }

    public async Task<TorrentSnapshot?> GetAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var connection = await SqliteConnectionFactory.OpenAsync(databaseFilePath, cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
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
                                  completion_callback_feedback_received_at_utc,
                                  completion_callback_feedback_json,
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
                                  download_cold_since_utc,
                                  last_activity_at_utc,
                                  error_message,
                                  metadata_resolution_attempt_started_at_utc,
                                  metadata_resolution_last_yielded_at_utc,
                                  seeding_policy_applied_at_utc,
                                  ordinary_queue_order,
                                  priority_queue_order,
                                  priority_metadata_attempts_remaining,
                                  is_queue_held
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
        TorrentQueueIntentTransitions.Normalize(torrent);

        await using var writeLease = await SqliteWriteCoordinator.AcquireAsync(databaseFilePath, cancellationToken);
        await using var connection = await SqliteConnectionFactory.OpenAsync(databaseFilePath, cancellationToken);
        await using var transaction =
                (SqliteTransaction) await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var command = CreateInsertCommand(connection, transaction, torrent);
            var ordinaryQueueOrder = Convert.ToInt64(
                await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture
            );
            TorrentQueueIntentTransitions.AssignOrdinaryOrder(torrent, ordinaryQueueOrder);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task UpdateAsync(TorrentSnapshot torrent, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        TorrentQueueIntentTransitions.Normalize(torrent);

        await using var writeLease = await SqliteWriteCoordinator.AcquireAsync(databaseFilePath, cancellationToken);
        await using var connection = await SqliteConnectionFactory.OpenAsync(databaseFilePath, cancellationToken);
        var command = CreateUpdateCommand(connection, torrent);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<long?> AssignNextOrdinaryQueueOrderAsync(Guid torrentId,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        return await AssignNextQueueOrderAsync(
            torrentId,
            "ordinary_queue_order",
            clearHeldIntent: false,
            requiresRunnable: false,
            cancellationToken
        );
    }

    public async Task<long?> AssignNextPriorityQueueOrderAsync(Guid torrentId,
        int priorityMetadataAttempts, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(priorityMetadataAttempts);
        await EnsureInitializedAsync(cancellationToken);

        await using var writeLease = await SqliteWriteCoordinator.AcquireAsync(databaseFilePath, cancellationToken);
        await using var connection = await SqliteConnectionFactory.OpenAsync(databaseFilePath, cancellationToken);
        await using var transaction =
                (SqliteTransaction) await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                                  UPDATE torrents
                                  SET
                                      priority_queue_order = (
                                          SELECT COALESCE(MAX(priority_queue_order), 0) + 1
                                          FROM torrents
                                      ),
                                      priority_metadata_attempts_remaining = $priority_metadata_attempts,
                                      is_queue_held = 0
                                  WHERE torrent_id = $torrent_id
                                    AND state = 'Queued'
                                    AND desired_state = 'Runnable'
                                  RETURNING priority_queue_order;
                                  """;
            command.Parameters.AddWithValue("$torrent_id", torrentId.ToString());
            command.Parameters.AddWithValue("$priority_metadata_attempts", priorityMetadataAttempts);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result is null || result == DBNull.Value
                ? null
                : Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> YieldPriorityMetadataAttemptAsync(Guid torrentId, int remainingAttempts,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(remainingAttempts);
        await EnsureInitializedAsync(cancellationToken);

        await using var writeLease = await SqliteWriteCoordinator.AcquireAsync(databaseFilePath, cancellationToken);
        await using var connection = await SqliteConnectionFactory.OpenAsync(databaseFilePath, cancellationToken);
        await using var transaction =
                (SqliteTransaction) await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                                  UPDATE torrents
                                  SET
                                      ordinary_queue_order = (
                                          SELECT COALESCE(MAX(ordinary_queue_order), 0) + 1
                                          FROM torrents
                                      ),
                                      priority_queue_order = CASE
                                          WHEN $remaining_attempts > 0 THEN (
                                              SELECT COALESCE(MAX(priority_queue_order), 0) + 1
                                              FROM torrents
                                          )
                                          ELSE NULL
                                      END,
                                      priority_metadata_attempts_remaining = CASE
                                          WHEN $remaining_attempts > 0 THEN $remaining_attempts
                                          ELSE NULL
                                      END,
                                      is_queue_held = 0
                                  WHERE torrent_id = $torrent_id
                                    AND state = 'Queued'
                                    AND desired_state = 'Runnable'
                                    AND priority_queue_order IS NOT NULL;
                                  """;
            command.Parameters.AddWithValue("$torrent_id", torrentId.ToString());
            command.Parameters.AddWithValue("$remaining_attempts", remainingAttempts);
            var changed = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
            await transaction.CommitAsync(cancellationToken);
            return changed;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> SetQueueHeldAsync(Guid torrentId, bool isHeld,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var writeLease = await SqliteWriteCoordinator.AcquireAsync(databaseFilePath, cancellationToken);
        await using var connection = await SqliteConnectionFactory.OpenAsync(databaseFilePath, cancellationToken);
        await using var transaction =
                (SqliteTransaction) await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                                  UPDATE torrents
                                  SET
                                      is_queue_held = $is_queue_held,
                                      priority_queue_order = CASE
                                          WHEN $is_queue_held = 1 THEN NULL
                                          ELSE priority_queue_order
                                      END,
                                      priority_metadata_attempts_remaining = CASE
                                          WHEN $is_queue_held = 1 THEN NULL
                                          ELSE priority_metadata_attempts_remaining
                                      END
                                  WHERE torrent_id = $torrent_id
                                    AND state = 'Queued'
                                    AND desired_state = 'Runnable';
                                  """;
            command.Parameters.AddWithValue("$torrent_id", torrentId.ToString());
            command.Parameters.AddWithValue("$is_queue_held", isHeld ? 1 : 0);
            var changed = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
            await transaction.CommitAsync(cancellationToken);
            return changed;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> ClearPriorityQueueOrderAsync(Guid torrentId,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var writeLease = await SqliteWriteCoordinator.AcquireAsync(databaseFilePath, cancellationToken);
        await using var connection = await SqliteConnectionFactory.OpenAsync(databaseFilePath, cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
                              UPDATE torrents
                              SET
                                  priority_queue_order = NULL,
                                  priority_metadata_attempts_remaining = NULL
                              WHERE torrent_id = $torrent_id
                                AND priority_queue_order IS NOT NULL;
                              """;
        command.Parameters.AddWithValue("$torrent_id", torrentId.ToString());
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<int> ReleaseQueueHoldsAsync(IReadOnlyList<Guid> torrentIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(torrentIds);
        if (torrentIds.Count == 0)
        {
            return 0;
        }

        await EnsureInitializedAsync(cancellationToken);
        await using var writeLease = await SqliteWriteCoordinator.AcquireAsync(databaseFilePath, cancellationToken);
        await using var connection = await SqliteConnectionFactory.OpenAsync(databaseFilePath, cancellationToken);
        await using var transaction =
                (SqliteTransaction) await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var releasedCount = 0;
            foreach (var torrentId in torrentIds.Distinct())
            {
                var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                                      UPDATE torrents
                                      SET is_queue_held = 0
                                      WHERE torrent_id = $torrent_id
                                        AND state = 'Queued'
                                        AND desired_state = 'Runnable'
                                        AND is_queue_held = 1;
                                      """;
                command.Parameters.AddWithValue("$torrent_id", torrentId.ToString());
                releasedCount += await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return releasedCount;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<TorrentSnapshot?> ResumeWithQueueIntentAsync(Guid torrentId, TorrentQueueResumeMode mode,
        DateTimeOffset resumedAtUtc, int priorityMetadataAttempts, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(priorityMetadataAttempts);
        await EnsureInitializedAsync(cancellationToken);
        var priorityExpression = mode == TorrentQueueResumeMode.Priority
            ? "(SELECT COALESCE(MAX(priority_queue_order), 0) + 1 FROM torrents)"
            : "NULL";
        var heldValue = mode == TorrentQueueResumeMode.Hold ? 1 : 0;
        var eligibleStateSql = mode == TorrentQueueResumeMode.Normal
            ? "state IN ('Paused', 'Error')"
            : "state = 'Paused'";

        await using var writeLease = await SqliteWriteCoordinator.AcquireAsync(databaseFilePath, cancellationToken);
        await using var connection = await SqliteConnectionFactory.OpenAsync(databaseFilePath, cancellationToken);
        await using var transaction =
                (SqliteTransaction) await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                                   UPDATE torrents
                                   SET
                                       desired_state = 'Runnable',
                                       state = 'Queued',
                                       ordinary_queue_order = (
                                           SELECT COALESCE(MAX(ordinary_queue_order), 0) + 1
                                           FROM torrents
                                       ),
                                       priority_queue_order = {priorityExpression},
                                       priority_metadata_attempts_remaining = CASE
                                           WHEN $has_priority = 1 THEN $priority_metadata_attempts
                                           ELSE NULL
                                       END,
                                       is_queue_held = $is_queue_held,
                                       connected_peer_count = 0,
                                       download_rate_bytes_per_second = 0,
                                       upload_rate_bytes_per_second = 0,
                                       metadata_resolution_attempt_started_at_utc = NULL,
                                       last_activity_at_utc = $last_activity_at_utc,
                                       error_message = NULL
                                   WHERE torrent_id = $torrent_id
                                     AND {eligibleStateSql}
                                     AND progress_percent < 100;
                                   """;
            command.Parameters.AddWithValue("$torrent_id", torrentId.ToString());
            command.Parameters.AddWithValue("$is_queue_held", heldValue);
            command.Parameters.AddWithValue("$has_priority", mode == TorrentQueueResumeMode.Priority ? 1 : 0);
            command.Parameters.AddWithValue("$priority_metadata_attempts", priorityMetadataAttempts);
            command.Parameters.AddWithValue(
                "$last_activity_at_utc", resumedAtUtc.ToString("O", CultureInfo.InvariantCulture));
            var changed = await command.ExecuteNonQueryAsync(cancellationToken) == 1;
            await transaction.CommitAsync(cancellationToken);
            return changed ? await GetAsync(torrentId, cancellationToken) : null;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task DeleteAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);

        await using var writeLease = await SqliteWriteCoordinator.AcquireAsync(databaseFilePath, cancellationToken);
        await using var connection = await SqliteConnectionFactory.OpenAsync(databaseFilePath, cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM torrents WHERE torrent_id = $torrent_id;";
        command.Parameters.AddWithValue("$torrent_id", torrentId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<TorrentSnapshot>> ReadSnapshotsAsync(SqliteCommand command,
        CancellationToken                                                                      cancellationToken)
    {
        var             results = new List<TorrentSnapshot>();
        await using var reader  = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(
                new TorrentSnapshot
                {
                    TorrentId                = Guid.Parse(reader.GetString(0)),
                    Name                     = reader.GetString(1),
                    CategoryKey              = reader.IsDBNull(2) ? null : reader.GetString(2),
                    CompletionCallbackLabel  = reader.IsDBNull(3) ? null : reader.GetString(3),
                    InvokeCompletionCallback = reader.GetInt64(4) != 0,
                    CompletionCallbackState = reader.IsDBNull(5) ? null :
                            Enum.Parse<TorrentCompletionCallbackState>(reader.GetString(5), true),
                    CompletionCallbackPendingSinceUtc = reader.IsDBNull(6) ? null : DateTimeOffset.Parse(
                        reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind
                    ),
                    CompletionCallbackInvokedAtUtc = reader.IsDBNull(7) ? null : DateTimeOffset.Parse(
                        reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind
                    ),
                    CompletionCallbackLastError = reader.IsDBNull(8) ? null : reader.GetString(8),
                    CompletionCallbackFeedbackReceivedAtUtc = reader.IsDBNull(9) ? null : DateTimeOffset.Parse(
                        reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind
                    ),
                    CompletionCallbackFeedbackJson = reader.IsDBNull(10) ? null : reader.GetString(10),
                    State                       = Enum.Parse<TorrentState>(reader.GetString(11), true),
                    DesiredState                = Enum.Parse<TorrentDesiredState>(reader.GetString(12), true),
                    MagnetUri                   = reader.GetString(13),
                    InfoHash                    = reader.IsDBNull(14) ? null : reader.GetString(14),
                    DownloadRootPath            = reader.IsDBNull(15) ? null : reader.GetString(15),
                    SavePath                    = reader.GetString(16),
                    ProgressPercent             = reader.GetDouble(17),
                    DownloadedBytes             = reader.GetInt64(18),
                    UploadedBytes               = reader.GetInt64(19),
                    TotalBytes                  = reader.IsDBNull(20) ? null : reader.GetInt64(20),
                    DownloadRateBytesPerSecond  = reader.GetInt64(21),
                    UploadRateBytesPerSecond    = reader.GetInt64(22),
                    TrackerCount                = reader.GetInt32(23),
                    ConnectedPeerCount          = reader.GetInt32(24),
                    AddedAtUtc = DateTimeOffset.Parse(
                        reader.GetString(25), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind
                    ),
                    CompletedAtUtc = reader.IsDBNull(26) ? null : DateTimeOffset.Parse(
                        reader.GetString(26), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind
                    ),
                    SeedingStartedAtUtc = reader.IsDBNull(27) ? null : DateTimeOffset.Parse(
                        reader.GetString(27), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind
                    ),
                    DownloadColdSinceUtc = reader.IsDBNull(28) ? null : DateTimeOffset.Parse(
                        reader.GetString(28), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind
                    ),
                    LastActivityAtUtc = reader.IsDBNull(29) ? null : DateTimeOffset.Parse(
                        reader.GetString(29), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind
                    ),
                    ErrorMessage = reader.IsDBNull(30) ? null : reader.GetString(30),
                    MetadataResolutionAttemptStartedAtUtc = reader.IsDBNull(31) ? null : DateTimeOffset.Parse(
                        reader.GetString(31), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind
                    ),
                    MetadataResolutionLastYieldedAtUtc = reader.IsDBNull(32) ? null : DateTimeOffset.Parse(
                        reader.GetString(32), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind
                    ),
                    SeedingPolicyAppliedAtUtc = reader.IsDBNull(33) ? null : DateTimeOffset.Parse(
                        reader.GetString(33), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind
                    ),
                    OrdinaryQueueOrder = reader.IsDBNull(34) ? null : reader.GetInt64(34),
                    PriorityQueueOrder = reader.IsDBNull(35) ? null : reader.GetInt64(35),
                    PriorityMetadataAttemptsRemaining = reader.IsDBNull(36) ? null : reader.GetInt32(36),
                    IsQueueHeld = reader.GetInt64(37) != 0,
                }
            );
        }

        return results;
    }

    private static SqliteCommand CreateInsertCommand(SqliteConnection connection, SqliteTransaction transaction,
        TorrentSnapshot torrent)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
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
                                  completion_callback_feedback_received_at_utc,
                                  completion_callback_feedback_json,
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
                                  download_cold_since_utc,
                                  last_activity_at_utc,
                                  error_message,
                                  metadata_resolution_attempt_started_at_utc,
                                  metadata_resolution_last_yielded_at_utc,
                                  seeding_policy_applied_at_utc,
                                  ordinary_queue_order,
                                  priority_queue_order,
                                  priority_metadata_attempts_remaining,
                                  is_queue_held
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
                                  $completion_callback_feedback_received_at_utc,
                                  $completion_callback_feedback_json,
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
                                  $download_cold_since_utc,
                                  $last_activity_at_utc,
                                  $error_message,
                                  $metadata_resolution_attempt_started_at_utc,
                                  $metadata_resolution_last_yielded_at_utc,
                                  $seeding_policy_applied_at_utc,
                                  COALESCE(
                                      $ordinary_queue_order,
                                      (SELECT COALESCE(MAX(ordinary_queue_order), 0) + 1 FROM torrents)
                                  ),
                                  $priority_queue_order,
                                  $priority_metadata_attempts_remaining,
                                  $is_queue_held
                              )
                              RETURNING ordinary_queue_order;
                              """;

        AddSnapshotParameters(command, torrent);
        return command;
    }

    private static SqliteCommand CreateUpdateCommand(SqliteConnection connection, TorrentSnapshot torrent)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
                              UPDATE torrents
                              SET
                                  name = $name,
                                  category_key = $category_key,
                                  completion_callback_label = $completion_callback_label,
                                  invoke_completion_callback = $invoke_completion_callback,
                                  completion_callback_state = CASE
                                      WHEN (
                                          (completion_callback_feedback_json IS NOT NULL AND completion_callback_feedback_json <> '' AND ($completion_callback_feedback_json IS NULL OR $completion_callback_feedback_json = ''))
                                          OR
                                          (completion_callback_state = 'Invoked' AND ($completion_callback_state IS NULL OR $completion_callback_state <> 'Invoked'))
                                      )
                                      THEN completion_callback_state
                                      ELSE $completion_callback_state
                                  END,
                                  completion_callback_pending_since_utc = CASE
                                      WHEN (
                                          (completion_callback_feedback_json IS NOT NULL AND completion_callback_feedback_json <> '' AND ($completion_callback_feedback_json IS NULL OR $completion_callback_feedback_json = ''))
                                          OR
                                          (completion_callback_state = 'Invoked' AND ($completion_callback_state IS NULL OR $completion_callback_state <> 'Invoked'))
                                      )
                                      THEN completion_callback_pending_since_utc
                                      ELSE $completion_callback_pending_since_utc
                                  END,
                                  completion_callback_invoked_at_utc = CASE
                                      WHEN (
                                          (completion_callback_feedback_json IS NOT NULL AND completion_callback_feedback_json <> '' AND ($completion_callback_feedback_json IS NULL OR $completion_callback_feedback_json = ''))
                                          OR
                                          (completion_callback_state = 'Invoked' AND ($completion_callback_state IS NULL OR $completion_callback_state <> 'Invoked'))
                                      )
                                      THEN completion_callback_invoked_at_utc
                                      ELSE $completion_callback_invoked_at_utc
                                  END,
                                  completion_callback_last_error = CASE
                                      WHEN (
                                          (completion_callback_feedback_json IS NOT NULL AND completion_callback_feedback_json <> '' AND ($completion_callback_feedback_json IS NULL OR $completion_callback_feedback_json = ''))
                                          OR
                                          (completion_callback_state = 'Invoked' AND ($completion_callback_state IS NULL OR $completion_callback_state <> 'Invoked'))
                                      )
                                      THEN completion_callback_last_error
                                      ELSE $completion_callback_last_error
                                  END,
                                  completion_callback_feedback_received_at_utc = CASE
                                      WHEN (
                                          (completion_callback_feedback_json IS NOT NULL AND completion_callback_feedback_json <> '' AND ($completion_callback_feedback_json IS NULL OR $completion_callback_feedback_json = ''))
                                          OR
                                          (completion_callback_state = 'Invoked' AND ($completion_callback_state IS NULL OR $completion_callback_state <> 'Invoked'))
                                      )
                                      THEN completion_callback_feedback_received_at_utc
                                      ELSE $completion_callback_feedback_received_at_utc
                                  END,
                                  completion_callback_feedback_json = CASE
                                      WHEN (
                                          (completion_callback_feedback_json IS NOT NULL AND completion_callback_feedback_json <> '' AND ($completion_callback_feedback_json IS NULL OR $completion_callback_feedback_json = ''))
                                          OR
                                          (completion_callback_state = 'Invoked' AND ($completion_callback_state IS NULL OR $completion_callback_state <> 'Invoked'))
                                      )
                                      THEN completion_callback_feedback_json
                                      ELSE $completion_callback_feedback_json
                                  END,
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
                                  download_cold_since_utc = $download_cold_since_utc,
                                  last_activity_at_utc = $last_activity_at_utc,
                                  error_message = $error_message,
                                  metadata_resolution_attempt_started_at_utc = $metadata_resolution_attempt_started_at_utc,
                                  metadata_resolution_last_yielded_at_utc = $metadata_resolution_last_yielded_at_utc,
                                  priority_queue_order = CASE
                                      WHEN $state = 'Paused' OR $desired_state = 'Paused' THEN NULL
                                      ELSE priority_queue_order
                                  END,
                                  priority_metadata_attempts_remaining = CASE
                                      WHEN $state = 'Paused' OR $desired_state = 'Paused' THEN NULL
                                      ELSE priority_metadata_attempts_remaining
                                  END,
                                  is_queue_held = CASE
                                      WHEN $state = 'Paused' OR $desired_state = 'Paused' THEN 0
                                      ELSE is_queue_held
                                  END,
                                  seeding_policy_applied_at_utc = COALESCE(
                                      seeding_policy_applied_at_utc,
                                      $seeding_policy_applied_at_utc
                                  )
                              WHERE torrent_id = $torrent_id;
                              """;

        AddSnapshotParameters(command, torrent);
        return command;
    }

    private static void AddSnapshotParameters(SqliteCommand command, TorrentSnapshot torrent)
    {
        command.Parameters.AddWithValue("$torrent_id",   torrent.TorrentId.ToString());
        command.Parameters.AddWithValue("$name",         torrent.Name);
        command.Parameters.AddWithValue("$category_key", torrent.CategoryKey ?? (object) DBNull.Value);
        command.Parameters.AddWithValue(
            "$completion_callback_label", torrent.CompletionCallbackLabel ?? (object) DBNull.Value
        );
        command.Parameters.AddWithValue("$invoke_completion_callback", torrent.InvokeCompletionCallback ? 1 : 0);
        command.Parameters.AddWithValue(
            "$completion_callback_state", torrent.CompletionCallbackState?.ToString() ?? (object) DBNull.Value
        );
        command.Parameters.AddWithValue(
            "$completion_callback_pending_since_utc",
            torrent.CompletionCallbackPendingSinceUtc?.ToString("O", CultureInfo.InvariantCulture) ??
            (object) DBNull.Value
        );
        command.Parameters.AddWithValue(
            "$completion_callback_invoked_at_utc",
            torrent.CompletionCallbackInvokedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? (object) DBNull.Value
        );
        command.Parameters.AddWithValue(
            "$completion_callback_last_error", torrent.CompletionCallbackLastError ?? (object) DBNull.Value
        );
        command.Parameters.AddWithValue(
            "$completion_callback_feedback_received_at_utc",
            torrent.CompletionCallbackFeedbackReceivedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ??
            (object)DBNull.Value
        );
        command.Parameters.AddWithValue(
            "$completion_callback_feedback_json",
            torrent.CompletionCallbackFeedbackJson ?? (object)DBNull.Value
        );
        command.Parameters.AddWithValue("$state", torrent.State.ToString());
        command.Parameters.AddWithValue("$desired_state", torrent.DesiredState.ToString());
        command.Parameters.AddWithValue("$magnet_uri", torrent.MagnetUri);
        command.Parameters.AddWithValue("$info_hash", torrent.InfoHash ?? (object) DBNull.Value);
        command.Parameters.AddWithValue("$download_root_path", torrent.DownloadRootPath ?? (object) DBNull.Value);
        command.Parameters.AddWithValue("$save_path", torrent.SavePath);
        command.Parameters.AddWithValue("$progress_percent", torrent.ProgressPercent);
        command.Parameters.AddWithValue("$downloaded_bytes", torrent.DownloadedBytes);
        command.Parameters.AddWithValue("$uploaded_bytes", torrent.UploadedBytes);
        command.Parameters.AddWithValue("$total_bytes", torrent.TotalBytes ?? (object) DBNull.Value);
        command.Parameters.AddWithValue("$download_rate_bytes_per_second", torrent.DownloadRateBytesPerSecond);
        command.Parameters.AddWithValue("$upload_rate_bytes_per_second", torrent.UploadRateBytesPerSecond);
        command.Parameters.AddWithValue("$tracker_count", torrent.TrackerCount);
        command.Parameters.AddWithValue("$connected_peer_count", torrent.ConnectedPeerCount);
        command.Parameters.AddWithValue(
            "$added_at_utc", torrent.AddedAtUtc.ToString("O", CultureInfo.InvariantCulture)
        );
        command.Parameters.AddWithValue(
            "$completed_at_utc",
            torrent.CompletedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? (object) DBNull.Value
        );
        command.Parameters.AddWithValue(
            "$seeding_started_at_utc",
            torrent.SeedingStartedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? (object) DBNull.Value
        );
        command.Parameters.AddWithValue(
            "$download_cold_since_utc",
            torrent.DownloadColdSinceUtc?.ToString("O", CultureInfo.InvariantCulture) ?? (object) DBNull.Value
        );
        command.Parameters.AddWithValue(
            "$last_activity_at_utc",
            torrent.LastActivityAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? (object) DBNull.Value
        );
        command.Parameters.AddWithValue("$error_message", torrent.ErrorMessage ?? (object) DBNull.Value);
        command.Parameters.AddWithValue(
            "$metadata_resolution_attempt_started_at_utc",
            torrent.MetadataResolutionAttemptStartedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ??
            (object) DBNull.Value
        );
        command.Parameters.AddWithValue(
            "$metadata_resolution_last_yielded_at_utc",
            torrent.MetadataResolutionLastYieldedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ??
            (object) DBNull.Value
        );
        command.Parameters.AddWithValue(
            "$seeding_policy_applied_at_utc",
            torrent.SeedingPolicyAppliedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ??
            (object) DBNull.Value
        );
        command.Parameters.AddWithValue(
            "$ordinary_queue_order", torrent.OrdinaryQueueOrder ?? (object) DBNull.Value
        );
        command.Parameters.AddWithValue(
            "$priority_queue_order", torrent.PriorityQueueOrder ?? (object) DBNull.Value
        );
        command.Parameters.AddWithValue(
            "$priority_metadata_attempts_remaining",
            torrent.PriorityMetadataAttemptsRemaining ?? (object) DBNull.Value
        );
        command.Parameters.AddWithValue("$is_queue_held", torrent.IsQueueHeld ? 1 : 0);
    }

    private async Task<long?> AssignNextQueueOrderAsync(Guid torrentId, string columnName, bool clearHeldIntent,
        bool requiresRunnable, CancellationToken cancellationToken)
    {
        await using var writeLease = await SqliteWriteCoordinator.AcquireAsync(databaseFilePath, cancellationToken);
        await using var connection = await SqliteConnectionFactory.OpenAsync(databaseFilePath, cancellationToken);
        await using var transaction =
                (SqliteTransaction) await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"""
                                   UPDATE torrents
                                   SET
                                       {columnName} = (
                                           SELECT COALESCE(MAX({columnName}), 0) + 1
                                           FROM torrents
                                       )
                                       {(clearHeldIntent ? ", is_queue_held = 0" : string.Empty)}
                                   WHERE torrent_id = $torrent_id
                                     {(requiresRunnable ? "AND state = 'Queued' AND desired_state = 'Runnable'" : string.Empty)}
                                   RETURNING {columnName};
                                   """;
            command.Parameters.AddWithValue("$torrent_id", torrentId.ToString());
            var result = await command.ExecuteScalarAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result is null || result == DBNull.Value
                ? null
                : Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

}
