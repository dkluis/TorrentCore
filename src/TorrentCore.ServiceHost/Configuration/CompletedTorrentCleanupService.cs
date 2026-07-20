#region

using System.Text.Json;
using Microsoft.Extensions.Options;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Core.Torrents;
using TorrentCore.Service.Application;
using TorrentCore.Service.Engine;
using TorrentCore.Service.Infrastructure;

#endregion

namespace TorrentCore.Service.Configuration;

public sealed class CompletedTorrentCleanupService(ITorrentStateStore torrentStateStore,
    ITorrentEngineAdapter torrentEngineAdapter, ITorrentHistoryService torrentHistoryService,
    IRuntimeSettingsService runtimeSettingsService,
    IActivityLogService activityLogService, ServiceInstanceContext serviceInstanceContext,
    IOptions<TorrentCoreServiceOptions> serviceOptions, ILogger<CompletedTorrentCleanupService> logger)
        : BackgroundService
{
    private readonly SemaphoreSlim             _gate           = new(1, 1);
    private readonly HashSet<Guid>             _prunedTorrentLogIds = [];
    private readonly TorrentCoreServiceOptions _serviceOptions = serviceOptions.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
                logger.LogError(exception, "Completed torrent cleanup tick failed.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }
    }

    private async Task ProcessTickAsync(CancellationToken cancellationToken)
    {
        var runtimeSettings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
        if (runtimeSettings.CompletedTorrentCleanupMode != CompletedTorrentCleanupMode.AfterCompletedMinutes &&
            !runtimeSettings.DeleteLogsForCompletedTorrents &&
            runtimeSettings.ColdDownloadAbandonAfterHours == 0)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var now      = DateTimeOffset.UtcNow;
            var torrents = await torrentStateStore.ListAsync(cancellationToken);
            await ProcessAbandonedDownloadsAsync(torrents, runtimeSettings, now, cancellationToken);

            var completedCandidates = torrents
                                     .Where(IsSuccessfulCompletedTorrent)
                                     .Where(torrent
                                              => (now - torrent.CompletedAtUtc!.Value).TotalMinutes >=
                                              runtimeSettings.CompletedTorrentCleanupMinutes
                                      )
                                     .OrderBy(torrent => torrent.CompletedAtUtc)
                                     .ThenBy(torrent => torrent.TorrentId)
                                     .ToList();

            _prunedTorrentLogIds.IntersectWith(completedCandidates.Select(torrent => torrent.TorrentId));

            var autoRemoveCandidateIds = runtimeSettings.CompletedTorrentCleanupMode ==
                    CompletedTorrentCleanupMode.AfterCompletedMinutes
                    ? completedCandidates.Select(torrent => torrent.TorrentId).ToHashSet()
                    : [];

            if (runtimeSettings.DeleteLogsForCompletedTorrents)
            {
                foreach (var torrent in completedCandidates.Where(torrent =>
                             !autoRemoveCandidateIds.Contains(torrent.TorrentId) &&
                             !_prunedTorrentLogIds.Contains(torrent.TorrentId)))
                {
                    await TryDeleteCompletedTorrentLogsAsync(torrent, cancellationToken);
                }
            }

            foreach (var torrent in completedCandidates.Where(torrent => autoRemoveCandidateIds.Contains(torrent.TorrentId)))
            {
                try
                {
                    await torrentEngineAdapter.RemoveAsync(
                        torrent.TorrentId, new RemoveTorrentRequest
                        {
                            DeleteData = false,
                        }, cancellationToken
                    );

                    await torrentHistoryService.MarkRemovedAsync(
                        torrent,
                        dataDeleted: false,
                        removalReason: "automatic_cleanup",
                        removedByCleanupPolicy: true,
                        removedAtUtc: DateTimeOffset.UtcNow,
                        cancellationToken);

                    await activityLogService.TryWriteActivityLogAsync(
                        new ActivityLogWriteRequest
                        {
                            Level     = ActivityLogLevel.Information,
                            Category  = "torrent",
                            EventType = "torrent.cleanup.auto_removed",
                            Message =
                                    $"Automatically removed completed torrent '{torrent.Name}' from TorrentCore tracking.",
                            TorrentId         = torrent.TorrentId,
                            ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                            DetailsJson = JsonSerializer.Serialize(
                                new
                                {
                                    Mode = runtimeSettings.CompletedTorrentCleanupMode,
                                    runtimeSettings.CompletedTorrentCleanupMinutes,
                                    DeleteData = false,
                                    torrent.CompletedAtUtc,
                                }
                            ),
                        }, cancellationToken
                    );

                    if (runtimeSettings.DeleteLogsForCompletedTorrents)
                    {
                        await TryDeleteCompletedTorrentLogsAsync(torrent, cancellationToken);
                    }
                }
                catch (Application.ServiceOperationException exception) when (exception.Code == "torrent_not_found")
                {
                    logger.LogDebug(
                        "Completed torrent {TorrentId} was already removed before cleanup executed.", torrent.TorrentId
                    );
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        exception, "Automatic cleanup failed for completed torrent {TorrentId}", torrent.TorrentId
                    );

                    await activityLogService.TryWriteActivityLogAsync(
                        new ActivityLogWriteRequest
                        {
                            Level             = ActivityLogLevel.Warning,
                            Category          = "torrent",
                            EventType         = "torrent.cleanup.auto_remove.failed",
                            Message           = exception.Message,
                            TorrentId         = torrent.TorrentId,
                            ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                            DetailsJson = JsonSerializer.Serialize(
                                new
                                {
                                    Mode = runtimeSettings.CompletedTorrentCleanupMode,
                                    runtimeSettings.CompletedTorrentCleanupMinutes,
                                    DeleteData    = false,
                                    ExceptionType = exception.GetType().FullName,
                                }
                            ),
                        }, cancellationToken
                    );
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ProcessAbandonedDownloadsAsync(IReadOnlyList<TorrentSnapshot> torrents,
        RuntimeSettingsSnapshot runtimeSettings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (runtimeSettings.ColdDownloadAbandonAfterHours == 0)
        {
            return;
        }

        var candidates = torrents
                        .Where(torrent => torrent.State == TorrentState.Downloading)
                        .Where(torrent => torrent.DesiredState == TorrentDesiredState.Runnable)
                        .Where(torrent => torrent.DownloadColdSinceUtc is not null)
                        .Where(torrent => now - torrent.DownloadColdSinceUtc!.Value >=
                                          TimeSpan.FromHours(runtimeSettings.ColdDownloadAbandonAfterHours))
                        .OrderBy(torrent => torrent.DownloadColdSinceUtc)
                        .ThenBy(torrent => torrent.TorrentId)
                        .ToList();

        foreach (var torrent in candidates)
        {
            var hourLabel = runtimeSettings.ColdDownloadAbandonAfterHours == 1 ? "hour" : "hours";
            var removalReason =
                    $"Download abandoned after {runtimeSettings.ColdDownloadAbandonAfterHours} {hourLabel} without peer or transfer activity.";
            try
            {
                torrent.ErrorMessage = removalReason;
                await torrentEngineAdapter.RemoveAsync(
                    torrent.TorrentId,
                    new RemoveTorrentRequest
                    {
                        DeleteData = true,
                    },
                    cancellationToken
                );
                await torrentHistoryService.MarkRemovedAsync(
                    torrent,
                    dataDeleted: true,
                    removalReason,
                    removedByCleanupPolicy: true,
                    removedAtUtc: DateTimeOffset.UtcNow,
                    cancellationToken
                );

                var deletedLogCount = await activityLogService.DeleteByTorrentIdAsync(
                    torrent.TorrentId,
                    cancellationToken
                );
                await activityLogService.TryWriteActivityLogAsync(
                    new ActivityLogWriteRequest
                    {
                        Level = ActivityLogLevel.Warning,
                        Category = "torrent",
                        EventType = "torrent.download.abandoned",
                        Message = $"Automatically abandoned cold download '{torrent.Name}'.",
                        ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                        DetailsJson = JsonSerializer.Serialize(
                            new
                            {
                                torrent.TorrentId,
                                torrent.Name,
                                torrent.ProgressPercent,
                                torrent.DownloadedBytes,
                                torrent.TotalBytes,
                                torrent.DownloadColdSinceUtc,
                                runtimeSettings.ColdDownloadAbandonAfterHours,
                                DeletedTorrentLogCount = deletedLogCount,
                                PartialDataDeleted = true,
                                CompletionCallbackInvoked = false,
                            }
                        ),
                    },
                    cancellationToken
                );
            }
            catch (Application.ServiceOperationException exception) when (exception.Code == "torrent_not_found")
            {
                logger.LogDebug(
                    "Cold download {TorrentId} was already removed before abandonment cleanup executed.",
                    torrent.TorrentId
                );
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Automatic abandonment failed for cold download {TorrentId}",
                    torrent.TorrentId
                );
                await activityLogService.TryWriteActivityLogAsync(
                    new ActivityLogWriteRequest
                    {
                        Level = ActivityLogLevel.Warning,
                        Category = "torrent",
                        EventType = "torrent.download.abandon_failed",
                        Message = exception.Message,
                        TorrentId = torrent.TorrentId,
                        ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                        DetailsJson = JsonSerializer.Serialize(
                            new
                            {
                                torrent.DownloadColdSinceUtc,
                                runtimeSettings.ColdDownloadAbandonAfterHours,
                                ExceptionType = exception.GetType().FullName,
                            }
                        ),
                    },
                    cancellationToken
                );
            }
        }
    }

    public override void Dispose()
    {
        _gate.Dispose();
        base.Dispose();
    }

    private async Task TryDeleteCompletedTorrentLogsAsync(TorrentSnapshot torrent, CancellationToken cancellationToken)
    {
        var deletedCount = await activityLogService.DeleteByTorrentIdAsync(torrent.TorrentId, cancellationToken);

        if (deletedCount <= 0)
        {
            _prunedTorrentLogIds.Add(torrent.TorrentId);
            return;
        }

        await activityLogService.TryWriteActivityLogAsync(
            new ActivityLogWriteRequest
            {
                Level             = ActivityLogLevel.Information,
                Category          = "torrent",
                EventType         = "torrent.logs.auto_deleted",
                Message           = $"Automatically deleted completed torrent log history for '{torrent.Name}'.",
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(
                    new
                    {
                        torrent.TorrentId,
                        torrent.Name,
                        torrent.CompletedAtUtc,
                        DeletedCount = deletedCount,
                    }
                ),
            }, cancellationToken
        );
    }

    private static bool IsSuccessfulCompletedTorrent(TorrentSnapshot torrent)
    {
        return torrent.State == TorrentState.Completed &&
               torrent.CompletedAtUtc is not null &&
               torrent.CompletionCallbackState is null or TorrentCompletionCallbackState.Invoked;
    }
}
