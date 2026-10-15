using System.Collections.Concurrent;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Core.History;
using TorrentCore.Core.Torrents;
using TorrentCore.Persistence.Sqlite.History;
using TorrentCore.Service.Application;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Infrastructure;
using TorrentCore.Contracts.Torrents;

namespace TorrentCore.Service.Engine;

public sealed record PayloadRejectionReply(bool Confirmed, bool SeasonExcluded = false, string Message = "");
public interface ITorrentPayloadRejectionEffects
{
    Task<PayloadRejectionReply> ReportAsync(TorrentPayloadRejection rejection, bool dataDeleted, CancellationToken token);
    Task DeleteAsync(TorrentPayloadRejection rejection, CancellationToken token);
}

// Progress belongs to retained history, not a separate queue or permanent blacklist.
public sealed class TorrentPayloadRejectionCoordinator
{
    private readonly ITorrentPayloadRejectionStore _store;
    private readonly Func<Guid, CancellationToken, Task<bool>> _exists;
    private readonly Func<TorrentPayloadRejection, CancellationToken, Task> _markRemoved;
    private readonly ITorrentPayloadRejectionEffects _effects;
    private readonly Func<Guid?, string, string, bool, Task> _log;
    private readonly ConcurrentDictionary<Guid, byte> _blocked = new();
    private readonly ConcurrentDictionary<Guid, byte> _excluded = new();
    private readonly ConcurrentDictionary<Guid, Task<PayloadRejectionReply>> _eligibilityRequests = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public TorrentPayloadRejectionCoordinator(ITorrentPayloadRejectionStore store, ITorrentStateStore states,
        ITorrentHistoryStore history, ITorrentPayloadRejectionEffects effects, IActivityLogService logs,
        ServiceInstanceContext instance) : this(store,
        async (id, token) => await states.GetAsync(id, token) is not null,
        async (rejection, token) =>
        {
            var row = await history.GetAsync(rejection.TorrentId, token)
                ?? throw new InvalidOperationException("Rejection history was removed; cleanup cannot be confirmed.");
            if (!string.Equals(row.InfoHash, rejection.InfoHash, StringComparison.OrdinalIgnoreCase))
            { throw new InvalidOperationException("History hash differs from the recorded cleanup identity."); }
            row.RemovedAtUtc ??= DateTimeOffset.UtcNow;
            row.LatestTorrentState = "Removed";
            row.RemovalReason = SqliteTorrentPayloadRejectionStore.RemovalReason;
            row.RemovalKind = null;
            row.DataDeleted = false;
            row.LastUpdatedAtUtc = row.RemovedAtUtc.Value;
            await history.UpdateAsync(row, token);
        }, effects, async (id, activity, message, failed) =>
        {
            try
            {
                await logs.TryWriteActivityLogAsync(new()
                {
                    Level = failed ? ActivityLogLevel.Error : ActivityLogLevel.Information,
                    Category = "torrent", EventType = "torrent.payload_rejection." + activity,
                    Message = message, TorrentId = id, ServiceInstanceId = instance.ServiceInstanceId,
                }, CancellationToken.None);
            }
            catch (Exception) { /* Reporting must not reopen payload admission. */ }
        }) { }

    internal TorrentPayloadRejectionCoordinator(ITorrentPayloadRejectionStore store,
        Func<Guid, CancellationToken, Task<bool>> exists,
        Func<TorrentPayloadRejection, CancellationToken, Task> markRemoved,
        ITorrentPayloadRejectionEffects effects, Func<Guid?, string, string, bool, Task> log)
    { _store = store; _exists = exists; _markRemoved = markRemoved; _effects = effects; _log = log; }

    public bool IsBlocked(Guid id) => _blocked.ContainsKey(id);
    internal static bool IsMediaCategory(string? key) => key is not null &&
        (key.Equals("TV", StringComparison.OrdinalIgnoreCase) || key.Equals("Movie", StringComparison.OrdinalIgnoreCase));
    internal static string[] Executables(IEnumerable<string> paths) => paths
        .Where(path => Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.Ordinal).ToArray();

    public async Task EnsureAdmissionAllowedAsync(string hash, CancellationToken token)
    {
        try
        {
            await LogAsync(null, "history_check", $"Checking retained executable rejection history for hash {hash}.");
            if (await _store.IsRejectedAsync(hash, token))
            {
                await LogAsync(null, "admission_rejected", $"Magnet {hash} rejected: executable payload rejection remains in history.", true);
                throw new ServiceOperationException("executable_payload_rejected", "This info hash was rejected for an executable payload and is still present in retained history.", 409, "MagnetUri");
            }
            await LogAsync(null, "history_checked", $"No retained executable rejection found for hash {hash}.");
        }
        catch (ServiceOperationException) { throw; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            await LogAsync(null, "history_check_failed", "Retained rejection history could not be checked: " + ex.Message, true);
            throw new ServiceOperationException("rejection_history_unavailable", "Retained rejection history is unavailable; magnet was not accepted.", 503, "MagnetUri");
        }
    }

    public async Task<bool> InspectAsync(TorrentSnapshot snapshot, IReadOnlyList<string> metadataFiles,
        IReadOnlyList<string> cleanupPaths, Func<Task> stop, Func<Task> remove, CancellationToken token)
    {
        if (!IsMediaCategory(snapshot.CategoryKey) || _excluded.ContainsKey(snapshot.TorrentId)) { return false; }
        var offending = Executables(metadataFiles);
        if (offending.Length == 0) { return false; }
        _blocked.TryAdd(snapshot.TorrentId, 0);
        await _gate.WaitAsync(token);
        try
        {
            await stop();
            var row = await _store.GetAsync(snapshot.TorrentId, token);
            if (row is not null && (row.TorrentId != snapshot.TorrentId ||
                !string.Equals(row.InfoHash, snapshot.InfoHash, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(row.DownloadRootPath, snapshot.DownloadRootPath, StringComparison.Ordinal)))
            { throw new InvalidOperationException("Recorded executable cleanup identity differs from the current torrent; review required."); }
            if (row?.Phase == "Excluded") { _excluded.TryAdd(snapshot.TorrentId, 0); _blocked.TryRemove(snapshot.TorrentId, out _); return false; }
            row ??= new(snapshot.TorrentId, snapshot.InfoHash ?? "", snapshot.DownloadRootPath ?? "",
                cleanupPaths.ToArray(), offending.Take(1000).ToArray());
            if (row.Phase is not ("EligibilityPending" or "Removing"))
            { throw new InvalidOperationException("Stored cleanup phase conflicts with a current torrent; review required."); }
            if (row.NextAttemptAtUtc > DateTimeOffset.UtcNow) { return true; }
            if (_eligibilityRequests.TryGetValue(row.TorrentId, out var inFlight) && !inFlight.IsCompleted) { return true; }
            await LogAsync(row.TorrentId, "inspect", $"Executable manifest path awaiting recorded-season verification. Hash={row.InfoHash}; files={string.Join(", ", row.OffendingFiles)}.");
            // Save before the first network call, including the event ID used for every retry.
            await SaveAsync(row, token);
            if (row.Phase == "EligibilityPending")
            {
                // Like the existing finalization probes, poll bounded I/O rather than hold engine synchronization waiting for HTTP.
                var pendingReply = _eligibilityRequests.GetOrAdd(row.TorrentId, _ => _effects.ReportAsync(row, false, token));
                if (!pendingReply.IsCompleted) { return true; }
                _eligibilityRequests.TryRemove(row.TorrentId, out _);
                var reply = await pendingReply;
                await LogAsync(row.TorrentId, "eligibility", reply.Message, !reply.Confirmed);
                if (!reply.Confirmed) { await SaveAsync(row with { NextAttemptAtUtc = DateTimeOffset.UtcNow.AddSeconds(30) }, token); return true; }
                if (reply.SeasonExcluded)
                {
                    await SaveAsync(row with { Phase = "Excluded", NextAttemptAtUtc = null }, token);
                    _excluded.TryAdd(row.TorrentId, 0);
                    _blocked.TryRemove(row.TorrentId, out _);
                    return false;
                }
                row = row with { Phase = "Removing", NextAttemptAtUtc = null };
                await LogAsync(row.TorrentId, "rejected", $"Executable payload rejected. Hash={row.InfoHash}; files={string.Join(", ", row.OffendingFiles)}.", true);
                await SaveAsync(row, token);
            }
            await LogAsync(row.TorrentId, "remove", "Started executable torrent removal; downstream callback remains blocked.");
            await remove();
            await _markRemoved(row, token);
            row = row with { Phase = "Removed" };
            await SaveAsync(row, token);
            // File deletion/notification run on reconciliation outside the engine synchronization gate.
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        { await LogAsync(snapshot.TorrentId, "pending", "Executable cleanup remains unconfirmed; no alternate/reset. " + ex.Message, true); return true; }
        finally { _gate.Release(); }
    }

    public async Task ReconcileRemovedAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            foreach (var row in await _store.ListPendingAsync(token))
            {
                if (row.NextAttemptAtUtc > DateTimeOffset.UtcNow || row.Phase == "EligibilityPending") { continue; }
                try
                {
                    if (await _exists(row.TorrentId, token)) { continue; }
                    var current = row;
                    if (current.Phase == "Removing")
                    {
                        await _markRemoved(current, token);
                        current = current with { Phase = "Removed" };
                        await SaveAsync(current, token);
                    }
                    await FinishAsync(current, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex) { await LogAsync(row.TorrentId, "resume_pending", ex.Message, true); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) { await LogAsync(null, "history_resume_failed", ex.Message, true); }
        finally { _gate.Release(); }
    }

    private async Task FinishAsync(TorrentPayloadRejection row, CancellationToken token)
    {
        // Persist the next attempt before I/O so exceptions/restarts do not cause a tight retry loop.
        row = row with { NextAttemptAtUtc = DateTimeOffset.UtcNow.AddSeconds(30) };
        await SaveAsync(row, token);
        if (row.Phase == "Removed")
        {
            await LogAsync(row.TorrentId, "delete", "Started deletion of the recorded rejected payload.");
            await _effects.DeleteAsync(row, token);
            row = row with { Phase = "Deleted" };
            await SaveAsync(row, token);
            await LogAsync(row.TorrentId, "deleted", "Executable payload data deletion confirmed.");
        }
        if (row.Phase != "Deleted") { return; }
        var reply = await _effects.ReportAsync(row, true, token);
        await LogAsync(row.TorrentId, "notification", reply.Message, !reply.Confirmed);
        await SaveAsync(row with { Phase = reply.Confirmed ? "Notified" : "Deleted",
            NextAttemptAtUtc = reply.Confirmed ? null : DateTimeOffset.UtcNow.AddSeconds(30) }, token);
    }

    private async Task SaveAsync(TorrentPayloadRejection row, CancellationToken token)
    {
        await LogAsync(row.TorrentId, "history_save", $"Saving executable rejection progress: {row.Phase}.");
        await _store.SaveAsync(row, token);
        await LogAsync(row.TorrentId, "history_saved", $"Executable rejection progress saved: {row.Phase}.");
    }
    private async Task LogAsync(Guid? id, string activity, string message, bool failed = false)
    { try { await _log(id, activity, message, failed); } catch (Exception) { } }
}
