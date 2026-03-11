using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Core.Torrents;
using TorrentCore.Service.Configuration;

namespace TorrentCore.Service.Engine;

public sealed class FakeTorrentRuntimeService(
    ITorrentStateStore torrentStateStore,
    IActivityLogService activityLogService,
    ServiceInstanceContext serviceInstanceContext,
    IOptions<TorrentCoreServiceOptions> serviceOptions,
    IRuntimeSettingsService runtimeSettingsService,
    ILogger<FakeTorrentRuntimeService> logger) : BackgroundService
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

                await activityLogService.WriteAsync(new ActivityLogWriteRequest
                {
                    Level = ActivityLogLevel.Error,
                    Category = "runtime",
                    EventType = "runtime.tick.failed",
                    Message = exception.Message,
                    ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        exception.GetType().FullName,
                        exception.StackTrace,
                    }),
                }, stoppingToken);
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }
    }

    private async Task ProcessTickAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var runtimeSettings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
        var torrents = await torrentStateStore.ListAsync(cancellationToken);

        await ReconcileMetadataResolutionQueueAsync(torrents, runtimeSettings, now, cancellationToken);

        torrents = await torrentStateStore.ListAsync(cancellationToken);
        await ResolveMetadataAsync(torrents, now, cancellationToken);

        torrents = await torrentStateStore.ListAsync(cancellationToken);

        var activeDownloads = torrents
            .Where(torrent => torrent.State == TorrentState.Downloading)
            .OrderBy(torrent => torrent.AddedAtUtc)
            .ThenBy(torrent => torrent.TorrentId)
            .ToList();

        await StartQueuedDownloadsAsync(torrents, activeDownloads.Count, runtimeSettings, now, cancellationToken);

        torrents = await torrentStateStore.ListAsync(cancellationToken);
        await AdvanceDownloadsAsync(torrents.Where(torrent => torrent.State == TorrentState.Downloading).ToList(), runtimeSettings, now, cancellationToken);

        torrents = await torrentStateStore.ListAsync(cancellationToken);
        await AdvanceSeedingAsync(torrents.Where(torrent => torrent.State == TorrentState.Seeding).ToList(), runtimeSettings, now, cancellationToken);
    }

    private async Task ReconcileMetadataResolutionQueueAsync(
        IReadOnlyList<TorrentSnapshot> torrents,
        RuntimeSettingsSnapshot runtimeSettings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var activeResolutions = 0;
        var unresolvedTorrents = torrents
            .Where(torrent => torrent.DesiredState == TorrentDesiredState.Runnable)
            .Where(torrent => torrent.TotalBytes is null && torrent.State is TorrentState.ResolvingMetadata or TorrentState.Queued)
            .OrderBy(torrent => torrent.AddedAtUtc)
            .ThenBy(torrent => torrent.TorrentId)
            .ToList();

        foreach (var torrent in unresolvedTorrents)
        {
            if (torrent.DesiredState == TorrentDesiredState.Paused)
            {
                continue;
            }

            if (activeResolutions < runtimeSettings.MaxActiveMetadataResolutions)
            {
                activeResolutions++;

                if (torrent.State == TorrentState.ResolvingMetadata)
                {
                    continue;
                }

                torrent.State = TorrentState.ResolvingMetadata;
                torrent.ConnectedPeerCount = 0;
                torrent.DownloadRateBytesPerSecond = 0;
                torrent.UploadRateBytesPerSecond = 0;
                torrent.LastActivityAtUtc = now;
                torrent.ErrorMessage = null;
                await torrentStateStore.UpdateAsync(torrent, cancellationToken);
                continue;
            }

            if (torrent.State != TorrentState.ResolvingMetadata)
            {
                continue;
            }

            torrent.State = TorrentState.Queued;
            torrent.ConnectedPeerCount = 0;
            torrent.DownloadRateBytesPerSecond = 0;
            torrent.UploadRateBytesPerSecond = 0;
            torrent.LastActivityAtUtc = now;
            await torrentStateStore.UpdateAsync(torrent, cancellationToken);
        }
    }

    private async Task ResolveMetadataAsync(IReadOnlyList<TorrentSnapshot> torrents, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var torrent in torrents.Where(torrent => torrent.State == TorrentState.ResolvingMetadata))
        {
            var lastRelevantTime = torrent.LastActivityAtUtc ?? torrent.AddedAtUtc;
            if ((now - lastRelevantTime).TotalMilliseconds < _serviceOptions.MetadataResolutionDelayMilliseconds)
            {
                continue;
            }

            torrent.TotalBytes ??= CalculateTotalBytes(torrent);
            torrent.TrackerCount = CalculateTrackerCount(torrent);
            torrent.ConnectedPeerCount = 0;
            torrent.DownloadRateBytesPerSecond = 0;
            torrent.UploadRateBytesPerSecond = 0;
            torrent.UploadedBytes = 0;
            torrent.SeedingStartedAtUtc = null;
            torrent.State = TorrentState.Queued;
            torrent.LastActivityAtUtc = now;
            torrent.ErrorMessage = null;

            await torrentStateStore.UpdateAsync(torrent, cancellationToken);
            await LogTorrentEventAsync(
                "torrent.metadata.resolved",
                $"Resolved metadata for torrent '{torrent.Name}'.",
                torrent,
                new
                {
                    torrent.TotalBytes,
                    torrent.TrackerCount,
                },
                cancellationToken);
        }
    }

    private async Task StartQueuedDownloadsAsync(
        IReadOnlyList<TorrentSnapshot> torrents,
        int activeDownloadCount,
        RuntimeSettingsSnapshot runtimeSettings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (activeDownloadCount >= runtimeSettings.MaxActiveDownloads)
        {
            return;
        }

        var availableSlots = runtimeSettings.MaxActiveDownloads - activeDownloadCount;
        var queuedTorrents = torrents
            .Where(torrent => torrent.DesiredState == TorrentDesiredState.Runnable)
            .Where(torrent => torrent.State == TorrentState.Queued)
            .Where(torrent => torrent.TotalBytes is not null)
            .OrderBy(torrent => torrent.AddedAtUtc)
            .ThenBy(torrent => torrent.TorrentId)
            .Take(availableSlots)
            .ToList();

        foreach (var torrent in queuedTorrents)
        {
            torrent.TotalBytes ??= CalculateTotalBytes(torrent);
            torrent.TrackerCount = Math.Max(torrent.TrackerCount, CalculateTrackerCount(torrent));
            torrent.ConnectedPeerCount = CalculatePeerCount(torrent);
            torrent.DownloadRateBytesPerSecond = CalculateDownloadRate(torrent);
            torrent.UploadRateBytesPerSecond = CalculateUploadRate(torrent);
            torrent.State = TorrentState.Downloading;
            torrent.LastActivityAtUtc = now;
            torrent.ErrorMessage = null;

            await torrentStateStore.UpdateAsync(torrent, cancellationToken);
            await LogTorrentEventAsync(
                "torrent.download.started",
                $"Started download for torrent '{torrent.Name}'.",
                torrent,
                new
                {
                    torrent.TotalBytes,
                    torrent.DownloadRateBytesPerSecond,
                    torrent.UploadRateBytesPerSecond,
                },
                cancellationToken);
        }
    }

    private async Task AdvanceDownloadsAsync(
        IReadOnlyList<TorrentSnapshot> torrents,
        RuntimeSettingsSnapshot runtimeSettings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var torrent in torrents.Where(torrent => torrent.DesiredState == TorrentDesiredState.Runnable))
        {
            torrent.TotalBytes ??= CalculateTotalBytes(torrent);

            var nextProgress = Math.Min(100, torrent.ProgressPercent + _serviceOptions.DownloadProgressPercentPerTick);
            torrent.ProgressPercent = nextProgress;
            torrent.DownloadedBytes = (long)Math.Round(torrent.TotalBytes.Value * (nextProgress / 100d), MidpointRounding.AwayFromZero);
            torrent.TrackerCount = Math.Max(torrent.TrackerCount, CalculateTrackerCount(torrent));
            torrent.LastActivityAtUtc = now;

            if (nextProgress >= 100)
            {
                torrent.CompletedAtUtc ??= now;
                torrent.SeedingStartedAtUtc ??= now;

                var seedingDecision = SeedingPolicyEvaluator.Evaluate(
                    runtimeSettings.SeedingStopMode,
                    runtimeSettings.SeedingStopRatio,
                    runtimeSettings.SeedingStopMinutes,
                    torrent.UploadedBytes,
                    torrent.TotalBytes,
                    torrent.SeedingStartedAtUtc,
                    now);

                if (seedingDecision.ShouldStop)
                {
                    torrent.State = TorrentState.Completed;
                    torrent.ConnectedPeerCount = 0;
                    torrent.DownloadRateBytesPerSecond = 0;
                    torrent.UploadRateBytesPerSecond = 0;

                    await torrentStateStore.UpdateAsync(torrent, cancellationToken);
                    await LogTorrentEventAsync(
                        "torrent.download.completed",
                        $"Completed download for torrent '{torrent.Name}'.",
                        torrent,
                        new
                        {
                            torrent.TotalBytes,
                            torrent.CompletedAtUtc,
                        },
                        cancellationToken);

                    await LogTorrentEventAsync(
                        "torrent.seeding.stopped_policy",
                        $"Stopped seeding for torrent '{torrent.Name}' because the '{seedingDecision.Reason}' policy was reached.",
                        torrent,
                        new
                        {
                            seedingDecision.Reason,
                            seedingDecision.CurrentRatio,
                            seedingDecision.CurrentSeedingMinutes,
                        },
                        cancellationToken);

                    continue;
                }

                torrent.State = TorrentState.Seeding;
                torrent.ConnectedPeerCount = CalculatePeerCount(torrent);
                torrent.DownloadRateBytesPerSecond = 0;
                torrent.UploadRateBytesPerSecond = CalculateUploadRate(torrent);

                await torrentStateStore.UpdateAsync(torrent, cancellationToken);
                await LogTorrentEventAsync(
                    "torrent.download.completed",
                    $"Completed download for torrent '{torrent.Name}'.",
                    torrent,
                    new
                    {
                        torrent.TotalBytes,
                        torrent.CompletedAtUtc,
                        torrent.SeedingStartedAtUtc,
                    },
                    cancellationToken);

                continue;
            }

            torrent.State = TorrentState.Downloading;
            torrent.ConnectedPeerCount = CalculatePeerCount(torrent);
            torrent.DownloadRateBytesPerSecond = CalculateDownloadRate(torrent);
            torrent.UploadRateBytesPerSecond = CalculateUploadRate(torrent);

            await torrentStateStore.UpdateAsync(torrent, cancellationToken);
        }
    }

    private async Task AdvanceSeedingAsync(
        IReadOnlyList<TorrentSnapshot> torrents,
        RuntimeSettingsSnapshot runtimeSettings,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        foreach (var torrent in torrents.Where(torrent => torrent.DesiredState == TorrentDesiredState.Runnable))
        {
            torrent.CompletedAtUtc ??= now;
            torrent.SeedingStartedAtUtc ??= torrent.CompletedAtUtc ?? now;
            torrent.DownloadRateBytesPerSecond = 0;
            torrent.UploadRateBytesPerSecond = CalculateUploadRate(torrent);
            torrent.ConnectedPeerCount = CalculatePeerCount(torrent);
            torrent.LastActivityAtUtc = now;
            torrent.UploadedBytes += Math.Max(0L, torrent.UploadRateBytesPerSecond * _serviceOptions.RuntimeTickIntervalMilliseconds / 1_000L);

            var seedingDecision = SeedingPolicyEvaluator.Evaluate(
                runtimeSettings.SeedingStopMode,
                runtimeSettings.SeedingStopRatio,
                runtimeSettings.SeedingStopMinutes,
                torrent.UploadedBytes,
                torrent.TotalBytes,
                torrent.SeedingStartedAtUtc,
                now);

            if (seedingDecision.ShouldStop)
            {
                torrent.State = TorrentState.Completed;
                torrent.ConnectedPeerCount = 0;
                torrent.UploadRateBytesPerSecond = 0;

                await torrentStateStore.UpdateAsync(torrent, cancellationToken);
                await LogTorrentEventAsync(
                    "torrent.seeding.stopped_policy",
                    $"Stopped seeding for torrent '{torrent.Name}' because the '{seedingDecision.Reason}' policy was reached.",
                    torrent,
                    new
                    {
                        seedingDecision.Reason,
                        seedingDecision.CurrentRatio,
                        seedingDecision.CurrentSeedingMinutes,
                    },
                    cancellationToken);

                continue;
            }

            await torrentStateStore.UpdateAsync(torrent, cancellationToken);
        }
    }

    private async Task LogTorrentEventAsync(
        string eventType,
        string message,
        TorrentSnapshot torrent,
        object details,
        CancellationToken cancellationToken)
    {
        await activityLogService.WriteAsync(new ActivityLogWriteRequest
        {
            Level = ActivityLogLevel.Information,
            Category = "torrent",
            EventType = eventType,
            Message = message,
            TorrentId = torrent.TorrentId,
            ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
            DetailsJson = JsonSerializer.Serialize(details),
        }, cancellationToken);
    }

    private static long CalculateTotalBytes(TorrentSnapshot torrent)
    {
        var seed = torrent.InfoHash ?? torrent.TorrentId.ToString("N");
        var bucket = Math.Abs(seed[..8].GetHashCode()) % 4;
        return bucket switch
        {
            0 => 512L * 1024 * 1024,
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
        return 2_000_000L + (Math.Abs(seed[..6].GetHashCode()) % 2_500_000);
    }

    private static long CalculateUploadRate(TorrentSnapshot torrent)
    {
        var seed = torrent.InfoHash ?? torrent.TorrentId.ToString("N");
        return 120_000L + (Math.Abs(seed[^5..].GetHashCode()) % 400_000);
    }
}
