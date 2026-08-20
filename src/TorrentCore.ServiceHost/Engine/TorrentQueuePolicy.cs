using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Torrents;

namespace TorrentCore.Service.Engine;

internal enum TorrentQueueWorkKind
{
    Metadata,
    Download,
}

internal sealed record TorrentQueuePolicyItem(
    TorrentSnapshot Snapshot,
    TorrentQueueWorkKind WorkKind,
    bool IsActive,
    bool IsComplete = false);

internal sealed class TorrentQueuePolicyResult
{
    public required IReadOnlyDictionary<Guid, TorrentQueueDiagnostic> Diagnostics { get; init; }
    public required IReadOnlyList<Guid> AdmissionOrder { get; init; }
    public required IReadOnlySet<Guid> StopActiveTorrentIds { get; init; }
    public required Guid? PriorityMetadataDisplacementTorrentId { get; init; }
    public required IReadOnlyList<Guid> HeldReleaseOrder { get; init; }
}

internal static class TorrentQueuePolicy
{
    public static TorrentQueueCapabilities GetCapabilities(TorrentSnapshot snapshot)
    {
        var queuedIncomplete = snapshot.DesiredState == TorrentDesiredState.Runnable &&
                               snapshot.State == TorrentState.Queued &&
                               snapshot.ProgressPercent < 100;
        var pausedIncomplete = snapshot.DesiredState == TorrentDesiredState.Paused &&
                               snapshot.State == TorrentState.Paused &&
                               snapshot.ProgressPercent < 100;

        return new TorrentQueueCapabilities(
            CanMakeNext: queuedIncomplete && snapshot.PriorityQueueOrder is null,
            CanHold: queuedIncomplete && !snapshot.IsQueueHeld,
            CanReleaseHold: queuedIncomplete && snapshot.IsQueueHeld,
            CanResumeNext: pausedIncomplete,
            CanResumeOnHold: pausedIncomplete
        );
    }

    public static TorrentQueuePolicyResult EvaluateSnapshots(
        IReadOnlyList<TorrentSnapshot> snapshots,
        int maxActiveMetadataResolutions,
        int maxActiveDownloads)
    {
        return Evaluate(
            snapshots.Select(snapshot => new TorrentQueuePolicyItem(
                snapshot,
                snapshot.TotalBytes is null ? TorrentQueueWorkKind.Metadata : TorrentQueueWorkKind.Download,
                snapshot.State is TorrentState.ResolvingMetadata or TorrentState.Downloading
            )).ToArray(),
            maxActiveMetadataResolutions,
            maxActiveDownloads
        );
    }

    public static TorrentQueuePolicyResult Evaluate(
        IReadOnlyList<TorrentQueuePolicyItem> items,
        int maxActiveMetadataResolutions,
        int maxActiveDownloads)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxActiveMetadataResolutions);
        ArgumentOutOfRangeException.ThrowIfNegative(maxActiveDownloads);

        var diagnostics = items.ToDictionary(
            item => item.Snapshot.TorrentId,
            _ => new TorrentQueueDiagnostic(null, null, null, null, false)
        );
        var eligible = items.Where(IsRunnableIncomplete).ToArray();
        var queued = eligible.Where(item => !item.IsActive && item.Snapshot.State == TorrentState.Queued).ToArray();
        var held = queued.Where(item => item.Snapshot.IsQueueHeld)
                         .OrderBy(OrdinaryOrder)
                         .ThenBy(item => item.Snapshot.AddedAtUtc)
                         .ThenBy(item => item.Snapshot.TorrentId)
                         .ToArray();
        var nonHeld = queued.Where(item => !item.Snapshot.IsQueueHeld).ToArray();
        var priorityLane = eligible.Where(item => !item.Snapshot.IsQueueHeld &&
                                                  item.Snapshot.PriorityQueueOrder is not null)
                                   .OrderBy(item => item.Snapshot.PriorityQueueOrder)
                                   .ThenBy(OrdinaryOrder)
                                   .ThenBy(item => item.Snapshot.TorrentId)
                                   .ToArray();
        var queuedPriority = priorityLane.Where(item => !item.IsActive &&
                                                        item.Snapshot.State == TorrentState.Queued)
                                         .ToArray();
        var ordinaryNeverYielded = nonHeld
            .Where(item => item.Snapshot.PriorityQueueOrder is null && !item.Snapshot.IsDownloadYielded)
            .OrderBy(OrdinaryOrder)
            .ThenBy(item => item.Snapshot.AddedAtUtc)
            .ThenBy(item => item.Snapshot.TorrentId)
            .ToArray();
        var automaticallyYielded = nonHeld
            .Where(item => item.Snapshot.PriorityQueueOrder is null && item.Snapshot.IsDownloadYielded)
            .OrderBy(item => item.Snapshot.DownloadLastYieldedAtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(item => item.Snapshot.TorrentId)
            .ToArray();
        var ordinary = ordinaryNeverYielded.Concat(automaticallyYielded).ToArray();

        var ordinaryMetadataLane = nonHeld.Where(item => item.WorkKind == TorrentQueueWorkKind.Metadata)
                                          .OrderBy(OrdinaryOrder)
                                          .ThenBy(item => item.Snapshot.AddedAtUtc)
                                          .ThenBy(item => item.Snapshot.TorrentId)
                                          .ToArray();
        var ordinaryDownloadLane = nonHeld.Where(item => item.WorkKind == TorrentQueueWorkKind.Download)
                                          .OrderBy(OrdinaryOrder)
                                          .ThenBy(item => item.Snapshot.AddedAtUtc)
                                          .ThenBy(item => item.Snapshot.TorrentId)
                                          .ToArray();

        var admissionOrder = new List<Guid>();
        var admittedIds = new HashSet<Guid>();
        var stopIds = new HashSet<Guid>();
        var activeDownloads = eligible.Where(item => item.IsActive &&
                                                      item.WorkKind == TorrentQueueWorkKind.Download)
                                      .OrderBy(OrdinaryOrder)
                                      .ThenBy(item => item.Snapshot.AddedAtUtc)
                                      .ThenBy(item => item.Snapshot.TorrentId)
                                      .ToList();
        var activeMetadata = eligible.Where(item => item.IsActive &&
                                                     item.WorkKind == TorrentQueueWorkKind.Metadata)
                                     .OrderBy(item => item.Snapshot.MetadataResolutionAttemptStartedAtUtc ??
                                                      DateTimeOffset.MaxValue)
                                     .ThenBy(OrdinaryOrder)
                                     .ThenBy(item => item.Snapshot.TorrentId)
                                     .ToList();

        while (activeDownloads.Count > maxActiveDownloads)
        {
            var stopped = activeDownloads[^1];
            activeDownloads.RemoveAt(activeDownloads.Count - 1);
            stopIds.Add(stopped.Snapshot.TorrentId);
        }

        while (activeMetadata.Count > maxActiveMetadataResolutions ||
               activeDownloads.Count + activeMetadata.Count > maxActiveDownloads)
        {
            if (activeMetadata.Count == 0)
            {
                break;
            }

            var stopped = activeMetadata[^1];
            activeMetadata.RemoveAt(activeMetadata.Count - 1);
            stopIds.Add(stopped.Snapshot.TorrentId);
        }

        var displaceableMetadata = activeMetadata
                                    .Where(item => item.Snapshot.PriorityQueueOrder is null)
                                    .ToList();
        Guid? priorityDisplacementId = null;
        var priorityBlocked = false;
        foreach (var item in queuedPriority)
        {
            if (CanAdmit(item, activeDownloads.Count, activeMetadata.Count,
                    maxActiveMetadataResolutions, maxActiveDownloads))
            {
                Admit(item, activeDownloads, activeMetadata, admissionOrder, admittedIds);
                continue;
            }

            if (priorityDisplacementId is null && displaceableMetadata.Count > 0 &&
                (item.WorkKind == TorrentQueueWorkKind.Download || maxActiveMetadataResolutions > 0))
            {
                var displaced = displaceableMetadata[0];
                displaceableMetadata.RemoveAt(0);
                activeMetadata.Remove(displaced);
                stopIds.Add(displaced.Snapshot.TorrentId);
                priorityDisplacementId = displaced.Snapshot.TorrentId;

                if (CanAdmit(item, activeDownloads.Count, activeMetadata.Count,
                        maxActiveMetadataResolutions, maxActiveDownloads))
                {
                    Admit(item, activeDownloads, activeMetadata, admissionOrder, admittedIds);
                    continue;
                }
            }

            priorityBlocked = true;
            break;
        }

        if (!priorityBlocked)
        {
            foreach (var item in ordinaryNeverYielded.Where(item => item.WorkKind == TorrentQueueWorkKind.Download))
            {
                if (!CanAdmit(item, activeDownloads.Count, activeMetadata.Count,
                        maxActiveMetadataResolutions, maxActiveDownloads) && displaceableMetadata.Count > 0)
                {
                    var stopped = displaceableMetadata[^1];
                    displaceableMetadata.RemoveAt(displaceableMetadata.Count - 1);
                    activeMetadata.Remove(stopped);
                    stopIds.Add(stopped.Snapshot.TorrentId);
                }

                if (!CanAdmit(item, activeDownloads.Count, activeMetadata.Count,
                        maxActiveMetadataResolutions, maxActiveDownloads))
                {
                    break;
                }

                Admit(item, activeDownloads, activeMetadata, admissionOrder, admittedIds);
            }

            foreach (var item in ordinaryNeverYielded.Where(item => item.WorkKind == TorrentQueueWorkKind.Metadata))
            {
                if (!CanAdmit(item, activeDownloads.Count, activeMetadata.Count,
                        maxActiveMetadataResolutions, maxActiveDownloads))
                {
                    break;
                }

                Admit(item, activeDownloads, activeMetadata, admissionOrder, admittedIds);
            }

            foreach (var item in automaticallyYielded)
            {
                if (!CanAdmit(item, activeDownloads.Count, activeMetadata.Count,
                        maxActiveMetadataResolutions, maxActiveDownloads))
                {
                    break;
                }

                Admit(item, activeDownloads, activeMetadata, admissionOrder, admittedIds);
            }
        }

        for (var index = 0; index < ordinaryMetadataLane.Length; index++)
        {
            ApplyQueuedDiagnostic(diagnostics, ordinaryMetadataLane[index], index + 1, admittedIds);
        }

        for (var index = 0; index < ordinaryDownloadLane.Length; index++)
        {
            ApplyQueuedDiagnostic(diagnostics, ordinaryDownloadLane[index], index + 1, admittedIds);
        }

        for (var index = 0; index < priorityLane.Length; index++)
        {
            var current = diagnostics[priorityLane[index].Snapshot.TorrentId];
            diagnostics[priorityLane[index].Snapshot.TorrentId] = current with
            {
                PriorityQueuePosition = index + 1,
            };
        }

        for (var index = 0; index < held.Length; index++)
        {
            diagnostics[held[index].Snapshot.TorrentId] = new TorrentQueueDiagnostic(
                TorrentWaitReason.HeldByOperator,
                null,
                null,
                index + 1,
                true
            );
        }

        foreach (var paused in items.Where(item => item.Snapshot.State == TorrentState.Paused))
        {
            diagnostics[paused.Snapshot.TorrentId] = new TorrentQueueDiagnostic(
                TorrentWaitReason.PausedByOperator, null, null, null, false);
        }

        foreach (var waiting in items.Where(item => item.Snapshot.State == TorrentState.WaitingForFileCompletion))
        {
            diagnostics[waiting.Snapshot.TorrentId] = new TorrentQueueDiagnostic(
                TorrentWaitReason.WaitingForFileCompletion, null, null, null, false);
        }

        foreach (var error in items.Where(item => item.Snapshot.State == TorrentState.Error))
        {
            diagnostics[error.Snapshot.TorrentId] = new TorrentQueueDiagnostic(
                TorrentWaitReason.BlockedByError, null, null, null, false);
        }

        return new TorrentQueuePolicyResult
        {
            Diagnostics = diagnostics,
            AdmissionOrder = admissionOrder,
            StopActiveTorrentIds = stopIds,
            PriorityMetadataDisplacementTorrentId = priorityDisplacementId,
            HeldReleaseOrder = nonHeld.Length == 0
                ? held.Select(item => item.Snapshot.TorrentId).ToArray()
                : [],
        };
    }

    private static bool IsRunnableIncomplete(TorrentQueuePolicyItem item)
        => !item.IsComplete && item.Snapshot.DesiredState == TorrentDesiredState.Runnable &&
           item.Snapshot.State is not TorrentState.Completed and not TorrentState.Error and not TorrentState.Removed and
               not TorrentState.WaitingForFileCompletion and not TorrentState.Seeding;

    private static long OrdinaryOrder(TorrentQueuePolicyItem item)
        => item.Snapshot.OrdinaryQueueOrder ?? long.MaxValue;

    private static bool CanAdmit(TorrentQueuePolicyItem item, int activeDownloadCount, int activeMetadataCount,
        int maxActiveMetadataResolutions, int maxActiveDownloads)
    {
        if (activeDownloadCount + activeMetadataCount >= maxActiveDownloads)
        {
            return false;
        }

        return item.WorkKind == TorrentQueueWorkKind.Download ||
               activeMetadataCount < maxActiveMetadataResolutions;
    }

    private static void Admit(TorrentQueuePolicyItem item, ICollection<TorrentQueuePolicyItem> activeDownloads,
        ICollection<TorrentQueuePolicyItem> activeMetadata, ICollection<Guid> admissionOrder,
        ISet<Guid> admittedIds)
    {
        if (item.WorkKind == TorrentQueueWorkKind.Download)
        {
            activeDownloads.Add(item);
        }
        else
        {
            activeMetadata.Add(item);
        }

        admissionOrder.Add(item.Snapshot.TorrentId);
        admittedIds.Add(item.Snapshot.TorrentId);
    }

    private static void ApplyQueuedDiagnostic(IDictionary<Guid, TorrentQueueDiagnostic> diagnostics,
        TorrentQueuePolicyItem item, int queuePosition, IReadOnlySet<Guid> admittedIds)
    {
        var waitReason = item.WorkKind switch
        {
            TorrentQueueWorkKind.Metadata when admittedIds.Contains(item.Snapshot.TorrentId)
                => TorrentWaitReason.PendingMetadataDispatch,
            TorrentQueueWorkKind.Metadata => TorrentWaitReason.WaitingForMetadataSlot,
            TorrentQueueWorkKind.Download when admittedIds.Contains(item.Snapshot.TorrentId)
                => TorrentWaitReason.PendingDownloadDispatch,
            TorrentQueueWorkKind.Download when item.Snapshot.IsDownloadYielded
                => TorrentWaitReason.AutomaticallyYieldedDownload,
            _ => TorrentWaitReason.WaitingForDownloadSlot,
        };
        diagnostics[item.Snapshot.TorrentId] = new TorrentQueueDiagnostic(
            waitReason,
            queuePosition,
            diagnostics[item.Snapshot.TorrentId].PriorityQueuePosition,
            null,
            false
        );
    }
}

internal readonly record struct TorrentQueueCapabilities(
    bool CanMakeNext,
    bool CanHold,
    bool CanReleaseHold,
    bool CanResumeNext,
    bool CanResumeOnHold);
