#region

using System.Text.Json;
using Microsoft.Extensions.Options;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Core.Torrents;
using TorrentCore.Service.Application;
using TorrentCore.Service.Callbacks;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Infrastructure;

#endregion

namespace TorrentCore.Service.Engine;

public sealed class FakeTorrentRuntimeService(ITorrentStateStore torrentStateStore,
    IActivityLogService activityLogService, ITorrentCompletionCallbackProcessor completionCallbackProcessor,
    ITorrentCompletionFinalizationChecker finalizationChecker,
    ITorrentHistoryService torrentHistoryService,
    ServiceInstanceContext serviceInstanceContext, IOptions<TorrentCoreServiceOptions> serviceOptions,
    IRuntimeSettingsService runtimeSettingsService, ILogger<FakeTorrentRuntimeService> logger) : BackgroundService
{
    private readonly TorrentCoreServiceOptions _serviceOptions = serviceOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_serviceOptions.EngineMode != TorrentEngineMode.Fake)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_serviceOptions.RuntimeTickIntervalMilliseconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Fake torrent runtime tick failed.");

                await activityLogService.TryWriteActivityLogAsync(
                    new ActivityLogWriteRequest
                    {
                        Level             = ActivityLogLevel.Error,
                        Category          = "runtime",
                        EventType         = "runtime.tick.failed",
                        Message           = exception.Message,
                        ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                        DetailsJson = JsonSerializer.Serialize(
                            new
                            {
                                exception.GetType().FullName,
                                exception.StackTrace,
                            }
                        ),
                    }, stoppingToken
                );
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }
    }

    private async Task ProcessTickAsync(CancellationToken cancellationToken)
    {
        var now             = DateTimeOffset.UtcNow;
        var runtimeSettings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
        var torrents        = await torrentStateStore.ListAsync(cancellationToken);

        await ReconcileQueuePolicyAsync(torrents, runtimeSettings, now, cancellationToken);

        torrents = await torrentStateStore.ListAsync(cancellationToken);
        await ResolveMetadataAsync(torrents, now, cancellationToken);

        torrents = await torrentStateStore.ListAsync(cancellationToken);
        await ReconcileQueuePolicyAsync(torrents, runtimeSettings, now, cancellationToken);

        torrents = await torrentStateStore.ListAsync(cancellationToken);

        await AdvanceDownloadsAsync(
            torrents.Where(torrent => torrent.State == TorrentState.Downloading).ToList(), runtimeSettings, now,
            cancellationToken
        );

        torrents = await torrentStateStore.ListAsync(cancellationToken);
        await AdvanceSeedingAsync(
            torrents.Where(torrent => torrent.State == TorrentState.Seeding).ToList(), runtimeSettings, now,
            cancellationToken
        );

        torrents = await torrentStateStore.ListAsync(cancellationToken);
        await AdvanceFileCompletionAsync(
            torrents.Where(torrent => torrent.State == TorrentState.WaitingForFileCompletion).ToList(),
            runtimeSettings,
            now,
            cancellationToken
        );

        torrents = await torrentStateStore.ListAsync(cancellationToken);
        await ProcessPendingCallbacksAsync(torrents, runtimeSettings, now, cancellationToken);
    }

    private async Task ReconcileQueuePolicyAsync(IReadOnlyList<TorrentSnapshot> torrents,
        RuntimeSettingsSnapshot runtimeSettings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var waitingMetadataCount = torrents.Count(torrent =>
            torrent.DesiredState == TorrentDesiredState.Runnable && !torrent.IsQueueHeld &&
            torrent.TotalBytes is null && torrent.State == TorrentState.Queued);
        var expiredMetadata = torrents
            .Where(torrent => torrent.State == TorrentState.ResolvingMetadata)
            .Where(torrent => torrent.MetadataResolutionAttemptStartedAtUtc is { } startedAt &&
                              now - startedAt >= TimeSpan.FromMinutes(
                                  runtimeSettings.MetadataResolutionTimeSliceMinutes))
            .OrderBy(torrent => torrent.MetadataResolutionAttemptStartedAtUtc)
            .ThenBy(torrent => torrent.OrdinaryQueueOrder)
            .Take(waitingMetadataCount)
            .ToArray();

        foreach (var torrent in expiredMetadata)
        {
            var startedAt = torrent.MetadataResolutionAttemptStartedAtUtc;
            var isProtectedPriorityAttempt = torrent.PriorityQueueOrder is not null;
            var remainingPriorityAttempts = isProtectedPriorityAttempt
                ? Math.Max(0, (torrent.PriorityMetadataAttemptsRemaining ?? 1) - 1)
                : 0;
            QueueSnapshot(torrent, now, yielded: true);
            await torrentStateStore.UpdateAsync(torrent, cancellationToken);
            if (isProtectedPriorityAttempt)
            {
                await torrentStateStore.YieldPriorityMetadataAttemptAsync(
                    torrent.TorrentId, remainingPriorityAttempts, cancellationToken);
                await LogTorrentEventAsync(
                    remainingPriorityAttempts > 0
                        ? "torrent.queue.priority_metadata_attempt_yielded"
                        : "torrent.queue.priority_metadata_attempts_expired",
                    remainingPriorityAttempts > 0
                        ? $"Priority metadata resolution for torrent '{torrent.Name}' exhausted an attempt and moved to the priority queue tail."
                        : $"Priority metadata resolution for torrent '{torrent.Name}' exhausted its final attempt and moved to the ordinary queue tail.",
                    torrent,
                    new { AttemptStartedAtUtc = startedAt, YieldedAtUtc = now,
                        TimeSliceMinutes = runtimeSettings.MetadataResolutionTimeSliceMinutes,
                        RemainingPriorityAttempts = remainingPriorityAttempts }, cancellationToken);
            }
            else
            {
                await torrentStateStore.AssignNextOrdinaryQueueOrderAsync(torrent.TorrentId, cancellationToken);
                await LogTorrentEventAsync(
                    "torrent.metadata.resolution_yielded",
                    $"Metadata resolution for torrent '{torrent.Name}' yielded its slot to queued work.",
                    torrent,
                    new { AttemptStartedAtUtc = startedAt, YieldedAtUtc = now,
                        TimeSliceMinutes = runtimeSettings.MetadataResolutionTimeSliceMinutes }, cancellationToken);
            }

            var persistedQueued = await torrentStateStore.GetAsync(torrent.TorrentId, cancellationToken) ?? torrent;
            await torrentHistoryService.ObserveSnapshotAsync(persistedQueued, cancellationToken);
        }

        torrents = await torrentStateStore.ListAsync(cancellationToken);
        var policy = TorrentQueuePolicy.EvaluateSnapshots(
            torrents, runtimeSettings.MaxActiveMetadataResolutions, runtimeSettings.MaxActiveDownloads);
        if (policy.HeldReleaseOrder.Count > 0)
        {
            await torrentStateStore.ReleaseQueueHoldsAsync(policy.HeldReleaseOrder, cancellationToken);
            foreach (var torrentId in policy.HeldReleaseOrder)
            {
                var released = torrents.First(torrent => torrent.TorrentId == torrentId);
                await LogTorrentEventAsync(
                    "torrent.queue.hold_auto_released",
                    $"Released queue hold for torrent '{released.Name}' after ordinary queued work was exhausted.",
                    released, new { released.OrdinaryQueueOrder }, cancellationToken);
            }
            torrents = await torrentStateStore.ListAsync(cancellationToken);
            policy = TorrentQueuePolicy.EvaluateSnapshots(
                torrents, runtimeSettings.MaxActiveMetadataResolutions, runtimeSettings.MaxActiveDownloads);
        }

        foreach (var torrentId in policy.StopActiveTorrentIds)
        {
            var torrent = torrents.First(torrent => torrent.TorrentId == torrentId);
            var priorityDisplacement = policy.PriorityMetadataDisplacementTorrentId == torrentId;
            QueueSnapshot(torrent, now, yielded: priorityDisplacement);
            await torrentStateStore.UpdateAsync(torrent, cancellationToken);
            if (priorityDisplacement)
            {
                await torrentStateStore.AssignNextOrdinaryQueueOrderAsync(torrentId, cancellationToken);
                await LogTorrentEventAsync(
                    "torrent.queue.priority_displaced_metadata",
                    $"Returned metadata resolution for torrent '{torrent.Name}' to the queue for priority work.",
                    torrent, new { DisplacedTorrentId = torrentId }, cancellationToken);
            }
            await torrentHistoryService.ObserveSnapshotAsync(torrent, cancellationToken);
        }

        torrents = await torrentStateStore.ListAsync(cancellationToken);
        await AdmitQueuedTorrentsAsync(torrents, policy.AdmissionOrder, now, cancellationToken);

        torrents = await torrentStateStore.ListAsync(cancellationToken);
        var rotationSelection = TorrentDownloadRotationPolicy.Evaluate(
            torrents.Select(torrent => new TorrentQueuePolicyItem(
                torrent,
                torrent.TotalBytes is null ? TorrentQueueWorkKind.Metadata : TorrentQueueWorkKind.Download,
                torrent.State is TorrentState.ResolvingMetadata or TorrentState.Downloading)).ToArray(),
            runtimeSettings.MaxActiveMetadataResolutions,
            runtimeSettings.MaxActiveDownloads,
            TimeSpan.FromMinutes(runtimeSettings.DownloadNoProgressTimeSliceMinutes),
            now);
        foreach (var torrentId in rotationSelection.YieldTorrentIds)
        {
            var torrent = torrents.First(candidate => candidate.TorrentId == torrentId);
            torrent.State = TorrentState.Queued;
            torrent.ConnectedPeerCount = 0;
            torrent.DownloadRateBytesPerSecond = 0;
            torrent.UploadRateBytesPerSecond = 0;
            torrent.DownloadNoProgressStartedAtUtc = null;
            torrent.DownloadLastYieldedAtUtc = now;
            torrent.IsDownloadYielded = true;
            torrent.PriorityQueueOrder = null;
            torrent.PriorityMetadataAttemptsRemaining = null;
            torrent.LastActivityAtUtc = now;
            await torrentStateStore.UpdateAsync(torrent, cancellationToken);
            await torrentHistoryService.ObserveSnapshotAsync(torrent, cancellationToken);
            await LogTorrentEventAsync(
                "torrent.download.rotation_yielded",
                $"Download '{torrent.Name}' yielded its slot after receiving no durable payload progress.",
                torrent,
                new
                {
                    torrent.DownloadedBytes,
                    YieldedAtUtc = now,
                    IntervalMinutes = runtimeSettings.DownloadNoProgressTimeSliceMinutes,
                    QueueDisposition = "automatically_yielded",
                },
                cancellationToken);
        }

        if (rotationSelection.YieldTorrentIds.Count > 0)
        {
            torrents = await torrentStateStore.ListAsync(cancellationToken);
            policy = TorrentQueuePolicy.EvaluateSnapshots(
                torrents, runtimeSettings.MaxActiveMetadataResolutions, runtimeSettings.MaxActiveDownloads);
            await AdmitQueuedTorrentsAsync(torrents, policy.AdmissionOrder, now, cancellationToken);
        }
    }

    private async Task AdmitQueuedTorrentsAsync(
        IReadOnlyList<TorrentSnapshot> torrents,
        IReadOnlyList<Guid> admissionOrder,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var torrentId in admissionOrder)
        {
            var torrent = torrents.First(torrent => torrent.TorrentId == torrentId);
            if (torrent.State != TorrentState.Queued || torrent.DesiredState != TorrentDesiredState.Runnable ||
                torrent.IsQueueHeld)
            {
                continue;
            }

            var progressClock = TorrentDownloadProgressClock.Evaluate(
                torrent,
                torrent.DownloadedBytes,
                torrent.TotalBytes is null
                    ? TorrentDownloadActivityState.Inactive
                    : TorrentDownloadActivityState.Active,
                now);
            torrent.State = torrent.TotalBytes is null ? TorrentState.ResolvingMetadata : TorrentState.Downloading;
            torrent.ConnectedPeerCount = torrent.TotalBytes is null ? 0 : CalculatePeerCount(torrent);
            torrent.DownloadRateBytesPerSecond = torrent.TotalBytes is null ? 0 : CalculateDownloadRate(torrent);
            torrent.UploadRateBytesPerSecond = torrent.TotalBytes is null ? 0 : CalculateUploadRate(torrent);
            torrent.MetadataResolutionAttemptStartedAtUtc = torrent.TotalBytes is null ? now : null;
            torrent.DownloadNoProgressStartedAtUtc = progressClock.NoProgressStartedAtUtc;
            torrent.IsDownloadYielded = progressClock.IsDownloadYielded;
            torrent.LastActivityAtUtc = now;
            torrent.ErrorMessage = null;
            await torrentStateStore.UpdateAsync(torrent, cancellationToken);
            if (torrent.PriorityQueueOrder is not null && torrent.TotalBytes is not null)
            {
                await torrentStateStore.ClearPriorityQueueOrderAsync(torrentId, cancellationToken);
            }
            await torrentHistoryService.ObserveSnapshotAsync(torrent, cancellationToken);
        }
    }

    private static void QueueSnapshot(TorrentSnapshot torrent, DateTimeOffset now, bool yielded)
    {
        torrent.State = TorrentState.Queued;
        torrent.ConnectedPeerCount = 0;
        torrent.DownloadRateBytesPerSecond = 0;
        torrent.UploadRateBytesPerSecond = 0;
        torrent.MetadataResolutionAttemptStartedAtUtc = null;
        torrent.MetadataResolutionLastYieldedAtUtc = yielded ? now : torrent.MetadataResolutionLastYieldedAtUtc;
        torrent.DownloadNoProgressStartedAtUtc = null;
        torrent.IsDownloadYielded = false;
        torrent.LastActivityAtUtc = now;
    }

    private async Task ReconcileMetadataResolutionQueueAsync(IReadOnlyList<TorrentSnapshot> torrents,
        RuntimeSettingsSnapshot runtimeSettings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var activeResolutions = 0;
        var resolvedDownloadDemand = torrents.Count(torrent =>
            torrent.DesiredState == TorrentDesiredState.Runnable &&
            torrent.TotalBytes is not null &&
            torrent.State is not TorrentState.Completed and not TorrentState.Error and not TorrentState.Removed);
        var effectiveMetadataResolutionLimit = TorrentDownloadAdmissionPolicy.CalculateMetadataResolutionLimit(
            runtimeSettings.MaxActiveMetadataResolutions,
            runtimeSettings.MaxActiveDownloads,
            resolvedDownloadDemand);
        var unresolvedTorrents = torrents.Where(torrent => torrent.DesiredState == TorrentDesiredState.Runnable)
                                         .Where(torrent
                                                  => torrent.TotalBytes is null &&
                                                  torrent.State is TorrentState.ResolvingMetadata or TorrentState.Queued
                                          )
                                         .ToList();

        var waitingCount = unresolvedTorrents.Count(torrent => torrent.State == TorrentState.Queued);
        var expiredActiveTorrents = unresolvedTorrents
                                   .Where(torrent => torrent.State == TorrentState.ResolvingMetadata)
                                   .Where(torrent => torrent.MetadataResolutionAttemptStartedAtUtc is { } startedAt &&
                                            now - startedAt >= TimeSpan.FromMinutes(
                                                runtimeSettings.MetadataResolutionTimeSliceMinutes))
                                   .OrderBy(torrent => torrent.MetadataResolutionAttemptStartedAtUtc)
                                   .ThenBy(torrent => torrent.AddedAtUtc)
                                   .ThenBy(torrent => torrent.TorrentId)
                                   .Take(waitingCount)
                                   .ToList();

        foreach (var torrent in expiredActiveTorrents)
        {
            var startedAt = torrent.MetadataResolutionAttemptStartedAtUtc!.Value;
            torrent.State = TorrentState.Queued;
            torrent.MetadataResolutionAttemptStartedAtUtc = null;
            torrent.MetadataResolutionLastYieldedAtUtc = now;
            torrent.LastActivityAtUtc = now;
            await torrentStateStore.UpdateAsync(torrent, cancellationToken);
            if (torrent.PriorityQueueOrder is not null)
            {
                await torrentStateStore.ClearPriorityQueueOrderAsync(torrent.TorrentId, cancellationToken);
            }
            await torrentHistoryService.ObserveSnapshotAsync(torrent, cancellationToken);
            await LogTorrentEventAsync(
                "torrent.metadata.resolution_yielded",
                $"Metadata resolution for torrent '{torrent.Name}' yielded its slot to queued work.",
                torrent,
                new
                {
                    AttemptStartedAtUtc = startedAt,
                    YieldedAtUtc = now,
                    TimeSliceMinutes = runtimeSettings.MetadataResolutionTimeSliceMinutes,
                },
                cancellationToken);
        }

        unresolvedTorrents = unresolvedTorrents
                            .OrderBy(torrent => torrent.State == TorrentState.ResolvingMetadata ? 0 :
                                torrent.MetadataResolutionLastYieldedAtUtc is null ? 1 : 2)
                            .ThenBy(torrent => torrent.State == TorrentState.ResolvingMetadata ?
                                torrent.MetadataResolutionAttemptStartedAtUtc ?? torrent.AddedAtUtc :
                                torrent.MetadataResolutionLastYieldedAtUtc ?? torrent.AddedAtUtc)
                            .ThenBy(torrent => torrent.AddedAtUtc)
                            .ThenBy(torrent => torrent.TorrentId)
                            .ToList();

        foreach (var torrent in unresolvedTorrents)
        {
            if (torrent.DesiredState == TorrentDesiredState.Paused)
            {
                continue;
            }

            if (activeResolutions < effectiveMetadataResolutionLimit)
            {
                activeResolutions++;

                if (torrent.State == TorrentState.ResolvingMetadata)
                {
                    if (torrent.MetadataResolutionAttemptStartedAtUtc is null)
                    {
                        torrent.MetadataResolutionAttemptStartedAtUtc = now;
                        await torrentStateStore.UpdateAsync(torrent, cancellationToken);
                    }
                    continue;
                }

                torrent.State                      = TorrentState.ResolvingMetadata;
                torrent.ConnectedPeerCount         = 0;
                torrent.DownloadRateBytesPerSecond = 0;
                torrent.UploadRateBytesPerSecond   = 0;
                torrent.LastActivityAtUtc          = now;
                torrent.ErrorMessage               = null;
                torrent.MetadataResolutionAttemptStartedAtUtc ??= now;
                await torrentStateStore.UpdateAsync(torrent, cancellationToken);
                await torrentHistoryService.ObserveSnapshotAsync(torrent, cancellationToken);
                continue;
            }

            if (torrent.State != TorrentState.ResolvingMetadata)
            {
                continue;
            }

            torrent.State                      = TorrentState.Queued;
            torrent.ConnectedPeerCount         = 0;
            torrent.DownloadRateBytesPerSecond = 0;
            torrent.UploadRateBytesPerSecond   = 0;
            torrent.LastActivityAtUtc          = now;
            torrent.MetadataResolutionAttemptStartedAtUtc = null;
            await torrentStateStore.UpdateAsync(torrent, cancellationToken);
            await torrentHistoryService.ObserveSnapshotAsync(torrent, cancellationToken);
        }
    }

    private async Task ResolveMetadataAsync(IReadOnlyList<TorrentSnapshot> torrents, DateTimeOffset now,
        CancellationToken                                                  cancellationToken)
    {
        foreach (var torrent in torrents.Where(torrent => torrent.State == TorrentState.ResolvingMetadata))
        {
            var lastRelevantTime = torrent.LastActivityAtUtc ?? torrent.AddedAtUtc;
            if ((now - lastRelevantTime).TotalMilliseconds < _serviceOptions.MetadataResolutionDelayMilliseconds)
            {
                continue;
            }

            torrent.TotalBytes                 ??= CalculateTotalBytes(torrent);
            torrent.TrackerCount               =   CalculateTrackerCount(torrent);
            torrent.ConnectedPeerCount         =   0;
            torrent.DownloadRateBytesPerSecond =   0;
            torrent.UploadRateBytesPerSecond   =   0;
            torrent.UploadedBytes              =   0;
            torrent.SeedingStartedAtUtc        =   null;
            torrent.State                      =   TorrentState.Queued;
            torrent.LastActivityAtUtc          =   now;
            torrent.ErrorMessage               =   null;
            torrent.MetadataResolutionAttemptStartedAtUtc = null;
            torrent.MetadataResolutionLastYieldedAtUtc = null;

            await torrentStateStore.UpdateAsync(torrent, cancellationToken);
            await torrentHistoryService.ObserveSnapshotAsync(torrent, cancellationToken);
            await LogTorrentEventAsync(
                "torrent.metadata.resolved", $"Resolved metadata for torrent '{torrent.Name}'.", torrent, new
                {
                    torrent.TotalBytes,
                    torrent.TrackerCount,
                }, cancellationToken
            );
        }
    }

    private async Task StartQueuedDownloadsAsync(IReadOnlyList<TorrentSnapshot> torrents, int activeDownloadCount,
        RuntimeSettingsSnapshot runtimeSettings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (activeDownloadCount >= runtimeSettings.MaxActiveDownloads)
        {
            return;
        }

        var availableSlots = runtimeSettings.MaxActiveDownloads - activeDownloadCount;
        var queuedTorrents = torrents.Where(torrent => torrent.DesiredState == TorrentDesiredState.Runnable)
                                     .Where(torrent => torrent.State        == TorrentState.Queued)
                                     .Where(torrent => torrent.TotalBytes is not null)
                                     .OrderBy(torrent => torrent.AddedAtUtc)
                                     .ThenBy(torrent => torrent.TorrentId)
                                     .Take(availableSlots)
                                     .ToList();

        foreach (var torrent in queuedTorrents)
        {
            torrent.TotalBytes                 ??= CalculateTotalBytes(torrent);
            torrent.TrackerCount               =   Math.Max(torrent.TrackerCount, CalculateTrackerCount(torrent));
            torrent.ConnectedPeerCount         =   CalculatePeerCount(torrent);
            torrent.DownloadRateBytesPerSecond =   CalculateDownloadRate(torrent);
            torrent.UploadRateBytesPerSecond   =   CalculateUploadRate(torrent);
            torrent.State                      =   TorrentState.Downloading;
            torrent.LastActivityAtUtc          =   now;
            torrent.ErrorMessage               =   null;

            await torrentStateStore.UpdateAsync(torrent, cancellationToken);
            await torrentHistoryService.ObserveSnapshotAsync(torrent, cancellationToken);
            await LogTorrentEventAsync(
                "torrent.download.started", $"Started download for torrent '{torrent.Name}'.", torrent, new
                {
                    torrent.TotalBytes,
                    torrent.DownloadRateBytesPerSecond,
                    torrent.UploadRateBytesPerSecond,
                }, cancellationToken
            );
        }
    }

    private async Task AdvanceDownloadsAsync(IReadOnlyList<TorrentSnapshot> torrents,
        RuntimeSettingsSnapshot runtimeSettings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var torrent in torrents.Where(torrent => torrent.DesiredState == TorrentDesiredState.Runnable))
        {
            var previousCompletedAtUtc = torrent.CompletedAtUtc;
            torrent.TotalBytes ??= CalculateTotalBytes(torrent);

            var nextProgress = Math.Min(100, torrent.ProgressPercent + _serviceOptions.DownloadProgressPercentPerTick);
            var nextDownloadedBytes = (long) Math.Round(
                torrent.TotalBytes.Value * (nextProgress / 100d), MidpointRounding.AwayFromZero
            );
            var progressClock = TorrentDownloadProgressClock.Evaluate(
                torrent, nextDownloadedBytes, TorrentDownloadActivityState.Active, now);
            torrent.ProgressPercent = nextProgress;
            torrent.DownloadedBytes = Math.Max(torrent.DownloadedBytes, nextDownloadedBytes);
            torrent.DownloadNoProgressStartedAtUtc = progressClock.NoProgressStartedAtUtc;
            torrent.IsDownloadYielded = progressClock.IsDownloadYielded;
            torrent.TrackerCount      = Math.Max(torrent.TrackerCount, CalculateTrackerCount(torrent));
            torrent.LastActivityAtUtc = now;

            if (nextProgress >= 100)
            {
                var finalizationResult = finalizationChecker.Check(torrent, runtimeSettings);
                if (!finalizationResult.IsReady)
                {
                    torrent.State = TorrentState.WaitingForFileCompletion;
                    torrent.DownloadNoProgressStartedAtUtc = null;
                    torrent.IsDownloadYielded = false;
                    torrent.CompletedAtUtc = null;
                    torrent.SeedingStartedAtUtc = null;
                    torrent.ConnectedPeerCount = 0;
                    torrent.DownloadRateBytesPerSecond = 0;
                    torrent.UploadRateBytesPerSecond = 0;

                    await torrentStateStore.UpdateAsync(torrent, cancellationToken);
                    await torrentHistoryService.ObserveSnapshotAsync(torrent, cancellationToken);
                    continue;
                }

                torrent.CompletedAtUtc      ??= now;
                torrent.SeedingStartedAtUtc ??= now;

                var seedingDecision = SeedingPolicyEvaluator.Evaluate(
                    runtimeSettings.SeedingStopMode, runtimeSettings.SeedingStopRatio,
                    runtimeSettings.SeedingStopMinutes, torrent.UploadedBytes, torrent.TotalBytes,
                    torrent.SeedingStartedAtUtc, now
                );

                if (seedingDecision.ShouldStop)
                {
                    var shouldRecordPolicyApplication = torrent.SeedingPolicyAppliedAtUtc is null;
                    torrent.State                      = TorrentState.Completed;
                    torrent.DownloadNoProgressStartedAtUtc = null;
                    torrent.IsDownloadYielded = false;
                    torrent.ConnectedPeerCount         = 0;
                    torrent.DownloadRateBytesPerSecond = 0;
                    torrent.UploadRateBytesPerSecond   = 0;
                    torrent.SeedingPolicyAppliedAtUtc ??= now;
                    await completionCallbackProcessor.MarkPendingIfTriggeredAsync(
                        previousCompletedAtUtc, torrent, runtimeSettings, now, cancellationToken
                    );

                    await torrentStateStore.UpdateAsync(torrent, cancellationToken);
                    await torrentHistoryService.ObserveSnapshotAsync(torrent, cancellationToken);
                    await LogTorrentEventAsync(
                        "torrent.download.completed", $"Completed download for torrent '{torrent.Name}'.", torrent, new
                        {
                            torrent.TotalBytes,
                            torrent.CompletedAtUtc,
                        }, cancellationToken
                    );

                    if (shouldRecordPolicyApplication)
                    {
                        await LogTorrentEventAsync(
                            "torrent.seeding.stopped_policy",
                            $"Applied the '{seedingDecision.Reason}' seeding stop policy to torrent '{torrent.Name}'.",
                            torrent, new
                            {
                                seedingDecision.Reason,
                                seedingDecision.CurrentRatio,
                                seedingDecision.CurrentSeedingMinutes,
                                torrent.SeedingPolicyAppliedAtUtc,
                            }, cancellationToken
                        );
                    }
                    continue;
                }

                torrent.State                      = TorrentState.Seeding;
                torrent.DownloadNoProgressStartedAtUtc = null;
                torrent.IsDownloadYielded = false;
                torrent.ConnectedPeerCount         = CalculatePeerCount(torrent);
                torrent.DownloadRateBytesPerSecond = 0;
                torrent.UploadRateBytesPerSecond   = CalculateUploadRate(torrent);
                await completionCallbackProcessor.MarkPendingIfTriggeredAsync(
                    previousCompletedAtUtc, torrent, runtimeSettings, now, cancellationToken
                );

                await torrentStateStore.UpdateAsync(torrent, cancellationToken);
                await torrentHistoryService.ObserveSnapshotAsync(torrent, cancellationToken);
                await LogTorrentEventAsync(
                    "torrent.download.completed", $"Completed download for torrent '{torrent.Name}'.", torrent, new
                    {
                        torrent.TotalBytes,
                        torrent.CompletedAtUtc,
                        torrent.SeedingStartedAtUtc,
                    }, cancellationToken
                );
                continue;
            }

            torrent.State                      = TorrentState.Downloading;
            torrent.ConnectedPeerCount         = CalculatePeerCount(torrent);
            torrent.DownloadRateBytesPerSecond = CalculateDownloadRate(torrent);
            torrent.UploadRateBytesPerSecond   = CalculateUploadRate(torrent);

            await torrentStateStore.UpdateAsync(torrent, cancellationToken);
            await torrentHistoryService.ObserveSnapshotAsync(torrent, cancellationToken);
        }
    }

    private async Task ProcessPendingCallbacksAsync(IReadOnlyList<TorrentSnapshot> torrents,
        RuntimeSettingsSnapshot runtimeSettings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var torrent in torrents.Where(torrent
                        => torrent.CompletionCallbackState is TorrentCompletionCallbackState.PendingFinalization or
                           TorrentCompletionCallbackState.WaitingForFeedback
                ))
        {
            if (!await completionCallbackProcessor.ProcessPendingAsync(
                        torrent, runtimeSettings, now, cancellationToken
                    ))
            {
                continue;
            }

            await torrentStateStore.UpdateAsync(torrent, cancellationToken);
            await torrentHistoryService.ObserveSnapshotAsync(torrent, cancellationToken);
        }
    }

    private async Task AdvanceSeedingAsync(IReadOnlyList<TorrentSnapshot> torrents,
        RuntimeSettingsSnapshot runtimeSettings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var torrent in torrents.Where(torrent => torrent.DesiredState == TorrentDesiredState.Runnable))
        {
            var finalizationResult = finalizationChecker.Check(torrent, runtimeSettings);
            if (!finalizationResult.IsReady)
            {
                torrent.State = TorrentState.WaitingForFileCompletion;
                torrent.CompletedAtUtc = null;
                torrent.SeedingStartedAtUtc = null;
                torrent.DownloadRateBytesPerSecond = 0;
                torrent.UploadRateBytesPerSecond = 0;
                torrent.ConnectedPeerCount = 0;
                torrent.LastActivityAtUtc = now;

                await torrentStateStore.UpdateAsync(torrent, cancellationToken);
                await torrentHistoryService.ObserveSnapshotAsync(torrent, cancellationToken);
                continue;
            }

            torrent.CompletedAtUtc             ??= now;
            torrent.SeedingStartedAtUtc        ??= torrent.CompletedAtUtc ?? now;
            torrent.DownloadRateBytesPerSecond =   0;
            torrent.UploadRateBytesPerSecond   =   CalculateUploadRate(torrent);
            torrent.ConnectedPeerCount         =   CalculatePeerCount(torrent);
            torrent.LastActivityAtUtc          =   now;
            torrent.UploadedBytes += Math.Max(
                0L, torrent.UploadRateBytesPerSecond * _serviceOptions.RuntimeTickIntervalMilliseconds / 1_000L
            );

            var seedingDecision = SeedingPolicyEvaluator.Evaluate(
                runtimeSettings.SeedingStopMode, runtimeSettings.SeedingStopRatio, runtimeSettings.SeedingStopMinutes,
                torrent.UploadedBytes, torrent.TotalBytes, torrent.SeedingStartedAtUtc, now
            );

            if (seedingDecision.ShouldStop)
            {
                var shouldRecordPolicyApplication = torrent.SeedingPolicyAppliedAtUtc is null;
                torrent.State                    = TorrentState.Completed;
                torrent.ConnectedPeerCount       = 0;
                torrent.UploadRateBytesPerSecond = 0;
                torrent.SeedingPolicyAppliedAtUtc ??= now;

                await torrentStateStore.UpdateAsync(torrent, cancellationToken);
                await torrentHistoryService.ObserveSnapshotAsync(torrent, cancellationToken);
                if (shouldRecordPolicyApplication)
                {
                    await LogTorrentEventAsync(
                        "torrent.seeding.stopped_policy",
                        $"Applied the '{seedingDecision.Reason}' seeding stop policy to torrent '{torrent.Name}'.",
                        torrent, new
                        {
                            seedingDecision.Reason,
                            seedingDecision.CurrentRatio,
                            seedingDecision.CurrentSeedingMinutes,
                            torrent.SeedingPolicyAppliedAtUtc,
                        }, cancellationToken
                    );
                }

                continue;
            }

            await torrentStateStore.UpdateAsync(torrent, cancellationToken);
            await torrentHistoryService.ObserveSnapshotAsync(torrent, cancellationToken);
        }
    }

    private async Task AdvanceFileCompletionAsync(IReadOnlyList<TorrentSnapshot> torrents,
        RuntimeSettingsSnapshot runtimeSettings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var torrent in torrents.Where(torrent => torrent.DesiredState == TorrentDesiredState.Runnable))
        {
            var finalizationResult = finalizationChecker.Check(torrent, runtimeSettings);
            if (!finalizationResult.IsReady)
            {
                continue;
            }

            var previousCompletedAtUtc = torrent.CompletedAtUtc;
            torrent.CompletedAtUtc ??= now;
            torrent.SeedingStartedAtUtc ??= now;

            var seedingDecision = SeedingPolicyEvaluator.Evaluate(
                runtimeSettings.SeedingStopMode, runtimeSettings.SeedingStopRatio, runtimeSettings.SeedingStopMinutes,
                torrent.UploadedBytes, torrent.TotalBytes, torrent.SeedingStartedAtUtc, now
            );

            if (seedingDecision.ShouldStop)
            {
                torrent.State = TorrentState.Completed;
                torrent.ConnectedPeerCount = 0;
                torrent.DownloadRateBytesPerSecond = 0;
                torrent.UploadRateBytesPerSecond = 0;
                await completionCallbackProcessor.MarkPendingIfTriggeredAsync(
                    previousCompletedAtUtc, torrent, runtimeSettings, now, cancellationToken
                );
            }
            else
            {
                torrent.State = TorrentState.Seeding;
                torrent.ConnectedPeerCount = CalculatePeerCount(torrent);
                torrent.DownloadRateBytesPerSecond = 0;
                torrent.UploadRateBytesPerSecond = CalculateUploadRate(torrent);
                await completionCallbackProcessor.MarkPendingIfTriggeredAsync(
                    previousCompletedAtUtc, torrent, runtimeSettings, now, cancellationToken
                );
            }

            torrent.LastActivityAtUtc = now;
            await torrentStateStore.UpdateAsync(torrent, cancellationToken);
            await torrentHistoryService.ObserveSnapshotAsync(torrent, cancellationToken);
        }
    }

    private async Task LogTorrentEventAsync(string eventType, string message, TorrentSnapshot torrent, object details,
        CancellationToken                          cancellationToken)
    {
        await activityLogService.TryWriteActivityLogAsync(
            new ActivityLogWriteRequest
            {
                Level             = ActivityLogLevel.Information,
                Category          = "torrent",
                EventType         = eventType,
                Message           = message,
                TorrentId         = torrent.TorrentId,
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson       = JsonSerializer.Serialize(details),
            }, cancellationToken
        );
    }

    private static long CalculateTotalBytes(TorrentSnapshot torrent)
    {
        var seed   = torrent.InfoHash ?? torrent.TorrentId.ToString("N");
        var bucket = Math.Abs(seed[..8].GetHashCode()) % 4;
        return bucket switch
        {
            0 => 512L  * 1024 * 1024,
            1 => 1024L * 1024 * 1024,
            2 => 1536L * 1024 * 1024,
            _ => 2048L * 1024 * 1024,
        };
    }

    private static int CalculateTrackerCount(TorrentSnapshot torrent)
    {
        var seed = torrent.InfoHash ?? torrent.TorrentId.ToString("N");
        return 2 + Math.Abs(seed[^6..].GetHashCode()) % 4;
    }

    private static int CalculatePeerCount(TorrentSnapshot torrent)
    {
        var seed = torrent.InfoHash ?? torrent.TorrentId.ToString("N");
        return 3 + Math.Abs(seed[4..10].GetHashCode()) % 8;
    }

    private static long CalculateDownloadRate(TorrentSnapshot torrent)
    {
        var seed = torrent.InfoHash ?? torrent.TorrentId.ToString("N");
        return 2_000_000L + Math.Abs(seed[..6].GetHashCode()) % 2_500_000;
    }

    private static long CalculateUploadRate(TorrentSnapshot torrent)
    {
        var seed = torrent.InfoHash ?? torrent.TorrentId.ToString("N");
        return 120_000L + Math.Abs(seed[^5..].GetHashCode()) % 400_000;
    }
}
