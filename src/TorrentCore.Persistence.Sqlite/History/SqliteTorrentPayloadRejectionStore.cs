using System.Text.Json;
using TorrentCore.Core.History;
using TorrentCore.Core.Diagnostics;

namespace TorrentCore.Persistence.Sqlite.History;

public sealed class SqliteTorrentPayloadRejectionStore(string databaseFilePath, IActivityLogService? logs = null) : ITorrentPayloadRejectionStore
{
    public const string RemovalReason = "executable_payload_rejected";

    public Task<TorrentPayloadRejection?> GetAsync(Guid torrentId, CancellationToken token) =>
        ActivityAsync("read", () => ReadCoreAsync(torrentId, token), token);
    public Task<IReadOnlyList<TorrentPayloadRejection>> ListPendingAsync(CancellationToken token) =>
        // Synchronization polls every tick. Actual reads/writes/actions and scan failures remain logged.
        ActivityAsync("list", () => ListCoreAsync(token), token, logSuccess: false);
    public Task<bool> IsRejectedAsync(string hash, CancellationToken token) =>
        ActivityAsync("check_hash", () => CheckCoreAsync(hash, token), token);
    public Task SaveAsync(TorrentPayloadRejection row, CancellationToken token) =>
        ActivityAsync("save", async () => { await SaveCoreAsync(row, token); return true; }, token);

    private async Task<TorrentPayloadRejection?> ReadCoreAsync(Guid torrentId, CancellationToken token)
    {
        await using var connection = await SqliteConnectionFactory.OpenAsync(databaseFilePath, token);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT executable_rejection_json FROM torrent_history WHERE torrent_id = $id;";
        command.Parameters.AddWithValue("$id", torrentId.ToString());
        var value = await command.ExecuteScalarAsync(token);
        return value is string json ? JsonSerializer.Deserialize<TorrentPayloadRejection>(json) : null;
    }

    private async Task<IReadOnlyList<TorrentPayloadRejection>> ListCoreAsync(CancellationToken token)
    {
        await using var connection = await SqliteConnectionFactory.OpenAsync(databaseFilePath, token);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT executable_rejection_json FROM torrent_history WHERE executable_rejection_json IS NOT NULL;";
        var result = new List<TorrentPayloadRejection>();
        await using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            try
            {
                var row = JsonSerializer.Deserialize<TorrentPayloadRejection>(reader.GetString(0))
                    ?? throw new InvalidOperationException("Executable rejection history is invalid.");
                if (row.Phase is not ("Excluded" or "Notified")) { result.Add(row); }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            { await LogAsync("invalid_record", "Stored executable rejection needs review: " + ex.Message, true); }
        }
        return result;
    }

    private async Task<bool> CheckCoreAsync(string infoHash, CancellationToken token)
    {
        await using var connection = await SqliteConnectionFactory.OpenAsync(databaseFilePath, token);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM torrent_history WHERE info_hash = $hash COLLATE NOCASE AND removal_reason = $reason);";
        command.Parameters.AddWithValue("$hash", infoHash);
        command.Parameters.AddWithValue("$reason", RemovalReason);
        return Convert.ToInt32(await command.ExecuteScalarAsync(token)) == 1;
    }

    private async Task SaveCoreAsync(TorrentPayloadRejection rejection, CancellationToken token)
    {
        await using var lease = await SqliteWriteCoordinator.AcquireAsync(databaseFilePath, token);
        await using var connection = await SqliteConnectionFactory.OpenAsync(databaseFilePath, token);
        using var command = connection.CreateCommand();
        // Do not refresh last_updated_at_utc merely for retry bookkeeping: normal retention stays authoritative.
        command.CommandText = """
            UPDATE torrent_history SET executable_rejection_json = $json,
                removal_reason = CASE WHEN $rejected THEN $reason ELSE removal_reason END,
                data_deleted = CASE WHEN $deleted THEN 1 ELSE data_deleted END
            WHERE torrent_id = $id AND info_hash = $hash COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$id", rejection.TorrentId.ToString());
        command.Parameters.AddWithValue("$hash", rejection.InfoHash);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(rejection));
        command.Parameters.AddWithValue("$reason", RemovalReason);
        command.Parameters.AddWithValue("$rejected", rejection.Phase is "Removing" or "Removed" or "Deleted" or "Notified");
        command.Parameters.AddWithValue("$deleted", rejection.Phase is "Deleted" or "Notified");
        if (await command.ExecuteNonQueryAsync(token) != 1)
        { throw new InvalidOperationException("Executable rejection history is missing or has changed; no cleanup acknowledgement saved."); }
    }

    private async Task<T> ActivityAsync<T>(string activity, Func<Task<T>> execute, CancellationToken token,
        bool logSuccess = true)
    {
        try
        {
            if (logSuccess) { await LogAsync(activity, "Started executable rejection history activity.", false); }
            var result = await execute();
            if (logSuccess) { await LogAsync(activity, "Completed executable rejection history activity.", false); }
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            await LogAsync(activity, "Executable rejection history activity failed: " + ex.Message, true);
            // The admission/inspection/reconciliation boundary contains this failure and blocks side effects.
            throw new InvalidOperationException("Executable rejection history " + activity + " failed.", ex);
        }
    }
    private async Task LogAsync(string activity, string message, bool failed)
    {
        if (logs is null) { return; }
        try { await logs.WriteAsync(new() { Category = "storage", EventType = "history.payload_rejection." + activity,
            Level = failed ? ActivityLogLevel.Error : ActivityLogLevel.Debug, Message = message }, CancellationToken.None); }
        catch (Exception) { /* A failed diagnostics channel cannot turn an unconfirmed write into success. */ }
    }
}
