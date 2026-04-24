#region

using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TorrentCore.Contracts.Categories;
using TorrentCore.Contracts.Host;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Engine;
using TorrentCore.Service.Infrastructure;

#endregion

namespace TorrentCore.Service.Application;

public sealed class TorrentApplicationService(IHostEnvironment hostEnvironment,
    ResolvedTorrentCoreServicePaths servicePaths, ITorrentEngineAdapter torrentEngineAdapter,
    IActivityLogService activityLogService, IOptions<TorrentCoreServiceOptions> serviceOptions,
    IRuntimeSettingsService runtimeSettingsService, ITorrentCategoryService torrentCategoryService,
    AppliedEngineSettingsState appliedEngineSettingsState, ServiceInstanceContext serviceInstanceContext,
    StartupRecoveryState startupRecoveryState, ILaunchAgentServiceRestartScheduler restartScheduler,
    ILogger<TorrentApplicationService> logger) : ITorrentApplicationService
{
    private static readonly HashSet<string> DashboardLifecycleRecentEventTypes = new(StringComparer.Ordinal)
    {
        "service.recovery.completed",
        "service.startup.ready",
        "torrent.added",
        "torrent.removed",
        "torrent.metadata.resolved",
        "torrent.metadata.refresh_requested",
        "torrent.metadata.reset_requested",
        "torrent.metadata.restart_requested",
        "torrent.callback.pending_finalization",
        "torrent.callback.retry_requested",
        "torrent.callback.invoked",
        "torrent.callback.failed",
        "torrent.callback.finalization_timed_out",
        "torrent.cleanup.auto_removed",
        "torrent.logs.orphaned_deleted",
    };

    public async Task<EngineHostStatusDto> GetHostStatusAsync(CancellationToken cancellationToken)
    {
        var runtimeSettings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
        var torrents        = await torrentEngineAdapter.GetTorrentsAsync(cancellationToken);

        var resolvingMetadataCount = torrents.Count(torrent => torrent.State == TorrentState.ResolvingMetadata);
        var metadataQueueCount =
                torrents.Count(torrent => torrent.State == TorrentState.Queued && torrent.TotalBytes is null);
        var downloadingCount = torrents.Count(torrent => torrent.State == TorrentState.Downloading);
        var downloadQueueCount =
                torrents.Count(torrent => torrent.State == TorrentState.Queued && torrent.TotalBytes is not null);
        var seedingCount = torrents.Count(torrent => torrent.State == TorrentState.Seeding);
        var pausedCount = torrents.Count(torrent => torrent.State == TorrentState.Paused);
        var completedCount = torrents.Count(torrent => torrent.State == TorrentState.Completed);
        var errorCount = torrents.Count(torrent => torrent.State == TorrentState.Error);
        var availableMetadataSlots = Math.Max(0, runtimeSettings.MaxActiveMetadataResolutions - resolvingMetadataCount);
        var availableDownloadSlots = Math.Max(0, runtimeSettings.MaxActiveDownloads - downloadingCount);
        var currentConnectedPeerCount = torrents.Sum(torrent => torrent.ConnectedPeerCount);
        var currentDownloadRateBytesPerSecond = torrents.Sum(torrent => torrent.DownloadRateBytesPerSecond);
        var currentUploadRateBytesPerSecond = torrents.Sum(torrent => torrent.UploadRateBytesPerSecond);

        return new EngineHostStatusDto
        {
            ServiceName                      = "TorrentCore.Service",
            ServiceVersion                   = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
            ServiceInstanceId                = serviceInstanceContext.ServiceInstanceId,
            EngineRuntime                    = serviceOptions.Value.EngineMode.ToString(),
            EngineListenPort                 = serviceOptions.Value.EngineListenPort,
            EngineDhtPort                    = serviceOptions.Value.EngineDhtPort,
            EnginePortForwardingEnabled      = serviceOptions.Value.EngineAllowPortForwarding,
            EngineLocalPeerDiscoveryEnabled  = serviceOptions.Value.EngineAllowLocalPeerDiscovery,
            EngineMaximumConnections         = appliedEngineSettingsState.EngineMaximumConnections,
            EngineMaximumHalfOpenConnections = appliedEngineSettingsState.EngineMaximumHalfOpenConnections,
            EngineMaximumDownloadRateBytesPerSecond =
                    appliedEngineSettingsState.EngineMaximumDownloadRateBytesPerSecond,
            EngineMaximumUploadRateBytesPerSecond = appliedEngineSettingsState.EngineMaximumUploadRateBytesPerSecond,
            EngineConnectionFailureLogBurstLimit = runtimeSettings.EngineConnectionFailureLogBurstLimit,
            EngineConnectionFailureLogWindowSeconds = runtimeSettings.EngineConnectionFailureLogWindowSeconds,
            MaxActiveMetadataResolutions = runtimeSettings.MaxActiveMetadataResolutions,
            MaxActiveDownloads = runtimeSettings.MaxActiveDownloads,
            AvailableMetadataResolutionSlots = availableMetadataSlots,
            AvailableDownloadSlots = availableDownloadSlots,
            ResolvingMetadataCount = resolvingMetadataCount,
            MetadataQueueCount = metadataQueueCount,
            DownloadingCount = downloadingCount,
            DownloadQueueCount = downloadQueueCount,
            SeedingCount = seedingCount,
            PausedCount = pausedCount,
            CompletedCount = completedCount,
            ErrorCount = errorCount,
            CurrentConnectedPeerCount = currentConnectedPeerCount,
            CurrentDownloadRateBytesPerSecond = currentDownloadRateBytesPerSecond,
            CurrentUploadRateBytesPerSecond = currentUploadRateBytesPerSecond,
            PartialFilesEnabled = runtimeSettings.PartialFilesEnabled,
            PartialFileSuffix = runtimeSettings.PartialFileSuffix,
            SeedingStopMode = runtimeSettings.SeedingStopMode.ToString(),
            SeedingStopRatio = runtimeSettings.SeedingStopRatio,
            SeedingStopMinutes = runtimeSettings.SeedingStopMinutes,
            CompletedTorrentCleanupMode = runtimeSettings.CompletedTorrentCleanupMode.ToString(),
            CompletedTorrentCleanupMinutes = runtimeSettings.CompletedTorrentCleanupMinutes,
            DeleteLogsForCompletedTorrents = runtimeSettings.DeleteLogsForCompletedTorrents,
            Status = startupRecoveryState.Completed ? EngineHostStatus.Ready : EngineHostStatus.Starting,
            EnvironmentName = hostEnvironment.EnvironmentName,
            DownloadRootPath = servicePaths.DownloadRootPath,
            TorrentCount = torrents.Count,
            SupportsMagnetAdds = true,
            SupportsPause = true,
            SupportsResume = true,
            SupportsRemove = true,
            SupportsPersistentStorage = true,
            SupportsMultiHost = false,
            StartupRecoveryCompleted = startupRecoveryState.Completed,
            StartupRecoveredTorrentCount = startupRecoveryState.RecoveredTorrentCount,
            StartupNormalizedTorrentCount = startupRecoveryState.NormalizedTorrentCount,
            StartupRecoveryCompletedAtUtc = startupRecoveryState.CompletedAtUtc,
            CheckedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    public async Task<DashboardLifecycleSummaryDto> GetDashboardLifecycleAsync(CancellationToken cancellationToken)
    {
        var logs = await activityLogService.GetRecentAsync(
            new ActivityLogQuery
            {
                Take = int.MaxValue,
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
            },
            cancellationToken
        );

        DateTimeOffset? firstEventAtUtc = logs.Count > 0 ? logs[^1].OccurredAtUtc : null;
        DateTimeOffset? lastEventAtUtc = logs.Count > 0 ? logs[0].OccurredAtUtc : null;
        var startupReadyAtUtc = logs.FirstOrDefault(log => log.EventType == "service.startup.ready")?.OccurredAtUtc;
        var recoveryCompletedAtUtc = logs.FirstOrDefault(log => log.EventType == "service.recovery.completed")?.OccurredAtUtc;

        return new DashboardLifecycleSummaryDto
        {
            ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
            FirstEventAtUtc = firstEventAtUtc,
            LastEventAtUtc = lastEventAtUtc,
            StartupReadyAtUtc = startupReadyAtUtc,
            RecoveryCompletedAtUtc = recoveryCompletedAtUtc,
            StartupRecoveredTorrentCount = startupRecoveryState.RecoveredTorrentCount,
            StartupNormalizedTorrentCount = startupRecoveryState.NormalizedTorrentCount,
            TorrentsAddedCount = logs.Count(log => log.EventType == "torrent.added"),
            TorrentsRemovedCount = logs.Count(log => log.EventType == "torrent.removed"),
            MetadataResolvedCount = logs.Count(log => log.EventType == "torrent.metadata.resolved"),
            MetadataRefreshRequestedCount = logs.Count(log => log.EventType == "torrent.metadata.refresh_requested"),
            MetadataResetRequestedCount = logs.Count(log => log.EventType == "torrent.metadata.reset_requested"),
            MetadataRestartRequestedCount = logs.Count(log => log.EventType == "torrent.metadata.restart_requested"),
            CallbackInvokedCount = logs.Count(log => log.EventType == "torrent.callback.invoked"),
            CallbackFailedCount = logs.Count(log => log.EventType == "torrent.callback.failed"),
            CallbackTimedOutCount = logs.Count(log => log.EventType == "torrent.callback.finalization_timed_out"),
            CompletedAutoRemovedCount = logs.Count(log => log.EventType == "torrent.cleanup.auto_removed"),
            OrphanedTorrentLogsDeletedCount = logs.Count(log => log.EventType == "torrent.logs.orphaned_deleted"),
            RecentEvents = logs
                .Where(log => DashboardLifecycleRecentEventTypes.Contains(log.EventType))
                .Take(12)
                .Select(
                    log => new DashboardLifecycleEventDto
                    {
                        OccurredAtUtc = log.OccurredAtUtc,
                        Level = log.Level.ToString(),
                        Category = log.Category,
                        EventType = log.EventType,
                        Message = log.Message,
                        TorrentId = log.TorrentId,
                    }
                )
                .ToArray(),
        };
    }

    public Task<RuntimeSettingsDto> GetRuntimeSettingsAsync(CancellationToken cancellationToken)
    {
        return runtimeSettingsService.GetRuntimeSettingsDtoAsync(cancellationToken);
    }

    public async Task<ServiceRestartRequestResultDto> RequestServiceRestartAsync(CancellationToken cancellationToken)
    {
        var result = await restartScheduler.ScheduleRestartAsync(cancellationToken);

        await activityLogService.WriteAsync(
            new ActivityLogWriteRequest
            {
                Level = ActivityLogLevel.Warning,
                Category = "service",
                EventType = "service.restart.requested",
                Message = result.Message,
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(
                    new
                    {
                        result.ServiceLabel,
                    }
                ),
            },
            cancellationToken
        );

        logger.LogWarning("TorrentCore service restart requested for {ServiceLabel}.", result.ServiceLabel);

        return new ServiceRestartRequestResultDto
        {
            RequestedAtUtc = DateTimeOffset.UtcNow,
            ServiceLabel = result.ServiceLabel,
            Message = result.Message,
        };
    }

    public Task<RuntimeSettingsDto> UpdateRuntimeSettingsAsync(UpdateRuntimeSettingsRequest request,
        CancellationToken                                                                   cancellationToken)
    {
        return runtimeSettingsService.UpdateAsync(request, cancellationToken);
    }

    public Task<IReadOnlyList<TorrentCategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        return torrentCategoryService.GetCategoriesAsync(cancellationToken);
    }

    public Task<TorrentCategoryDto> UpdateCategoryAsync(string key, UpdateTorrentCategoryRequest request,
        CancellationToken                                      cancellationToken)
    {
        return torrentCategoryService.UpdateCategoryAsync(key, request, cancellationToken);
    }

    public Task<IReadOnlyList<TorrentSummaryDto>> GetTorrentsAsync(CancellationToken cancellationToken)
    {
        return torrentEngineAdapter.GetTorrentsAsync(cancellationToken);
    }

    public async Task<TorrentDetailDto> GetTorrentAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        try
        {
            return await torrentEngineAdapter.GetTorrentAsync(torrentId, cancellationToken);
        }
        catch (ServiceOperationException exception)
        {
            await LogFailureAsync("torrent", "torrent.lookup.failed", exception.Message, torrentId, cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<TorrentPeerDto>> GetTorrentPeersAsync(Guid torrentId,
        CancellationToken                                                      cancellationToken)
    {
        try
        {
            return await torrentEngineAdapter.GetTorrentPeersAsync(torrentId, cancellationToken);
        }
        catch (ServiceOperationException exception)
        {
            await LogFailureAsync("torrent", "torrent.peers.lookup.failed", exception.Message, torrentId, cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<TorrentTrackerDto>> GetTorrentTrackersAsync(Guid torrentId,
        CancellationToken                                                         cancellationToken)
    {
        try
        {
            return await torrentEngineAdapter.GetTorrentTrackersAsync(torrentId, cancellationToken);
        }
        catch (ServiceOperationException exception)
        {
            await LogFailureAsync("torrent", "torrent.trackers.lookup.failed", exception.Message, torrentId, cancellationToken);
            throw;
        }
    }

    public async Task<TorrentDetailDto> AddMagnetAsync(AddMagnetRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var categorySelection =
                    await torrentCategoryService.ResolveSelectionAsync(request.CategoryKey, cancellationToken);
            var normalizedRequest = new AddMagnetRequest
            {
                MagnetUri   = request.MagnetUri,
                CategoryKey = categorySelection.CategoryKey,
            };

            var torrent = await torrentEngineAdapter.AddMagnetAsync(
                normalizedRequest, categorySelection, cancellationToken
            );

            logger.LogInformation("Added torrent {TorrentId} named {TorrentName}", torrent.TorrentId, torrent.Name);

            await activityLogService.WriteAsync(
                new ActivityLogWriteRequest
                {
                    Level             = ActivityLogLevel.Information,
                    Category          = "torrent",
                    EventType         = "torrent.added",
                    Message           = $"Added torrent '{torrent.Name}'.",
                    TorrentId         = torrent.TorrentId,
                    ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                    DetailsJson = JsonSerializer.Serialize(
                        new
                        {
                            torrent.Name,
                            torrent.CategoryKey,
                            torrent.State,
                            torrent.SavePath,
                        }
                    ),
                }, cancellationToken
            );

            return torrent;
        }
        catch (ServiceOperationException exception)
        {
            await LogFailureAsync("torrent", "torrent.add.failed", exception.Message, null, cancellationToken);
            throw;
        }
    }

    public async Task<TorrentActionResultDto> PauseAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await torrentEngineAdapter.PauseAsync(torrentId, cancellationToken);
            await LogActionAsync("torrent.paused", "Paused torrent.", torrentId, result.State, cancellationToken);
            return result;
        }
        catch (ServiceOperationException exception)
        {
            await LogFailureAsync("torrent", "torrent.pause.failed", exception.Message, torrentId, cancellationToken);
            throw;
        }
    }

    public async Task<TorrentActionResultDto> ResumeAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await torrentEngineAdapter.ResumeAsync(torrentId, cancellationToken);
            await LogActionAsync("torrent.resumed", "Resumed torrent.", torrentId, result.State, cancellationToken);
            return result;
        }
        catch (ServiceOperationException exception)
        {
            await LogFailureAsync("torrent", "torrent.resume.failed", exception.Message, torrentId, cancellationToken);
            throw;
        }
    }

    public async Task<TorrentActionResultDto> RefreshMetadataAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await torrentEngineAdapter.RefreshMetadataAsync(torrentId, cancellationToken);
            await LogActionAsync(
                "torrent.metadata.refresh_requested", "Requested metadata discovery refresh.", torrentId, result.State,
                cancellationToken
            );
            return result;
        }
        catch (ServiceOperationException exception)
        {
            await LogFailureAsync(
                "torrent", "torrent.metadata.refresh.failed", exception.Message, torrentId, cancellationToken
            );
            throw;
        }
    }

    public async Task<TorrentActionResultDto> ResetMetadataSessionAsync(Guid torrentId,
        CancellationToken                                                    cancellationToken)
    {
        try
        {
            var result = await torrentEngineAdapter.ResetMetadataSessionAsync(torrentId, cancellationToken);
            await LogActionAsync(
                "torrent.metadata.reset_requested", "Recreated metadata discovery session.", torrentId, result.State,
                cancellationToken
            );
            return result;
        }
        catch (ServiceOperationException exception)
        {
            await LogFailureAsync(
                "torrent", "torrent.metadata.reset.failed", exception.Message, torrentId, cancellationToken
            );
            throw;
        }
    }

    public async Task<TorrentActionResultDto> RetryCompletionCallbackAsync(Guid torrentId,
        CancellationToken                                                       cancellationToken)
    {
        try
        {
            var result = await torrentEngineAdapter.RetryCompletionCallbackAsync(torrentId, cancellationToken);
            await LogActionAsync(
                "torrent.callback.retry_requested", "Queued completion callback retry.", torrentId, result.State,
                cancellationToken
            );
            return result;
        }
        catch (ServiceOperationException exception)
        {
            await LogFailureAsync(
                "torrent", "torrent.callback.retry.failed", exception.Message, torrentId, cancellationToken
            );
            throw;
        }
    }

    public async Task<TorrentActionResultDto> RemoveAsync(Guid torrentId, RemoveTorrentRequest request,
        CancellationToken                                      cancellationToken)
    {
        try
        {
            var result = await torrentEngineAdapter.RemoveAsync(torrentId, request, cancellationToken);

            await activityLogService.WriteAsync(
                new ActivityLogWriteRequest
                {
                    Level = ActivityLogLevel.Information,
                    Category = "torrent",
                    EventType = "torrent.removed",
                    Message = request.DeleteData ? "Removed torrent and requested data deletion." : "Removed torrent.",
                    TorrentId = torrentId,
                    ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                    DetailsJson = JsonSerializer.Serialize(
                        new
                        {
                            result.State,
                            request.DeleteData,
                        }
                    ),
                }, cancellationToken
            );

            return result;
        }
        catch (ServiceOperationException exception)
        {
            await LogFailureAsync("torrent", "torrent.remove.failed", exception.Message, torrentId, cancellationToken);
            throw;
        }
    }

    private async Task LogActionAsync(string eventType, string message, Guid torrentId, TorrentState state,
        CancellationToken                    cancellationToken)
    {
        logger.LogInformation("{Message} TorrentId={TorrentId} State={State}", message, torrentId, state);

        await activityLogService.WriteAsync(
            new ActivityLogWriteRequest
            {
                Level             = ActivityLogLevel.Information,
                Category          = "torrent",
                EventType         = eventType,
                Message           = message,
                TorrentId         = torrentId,
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson       = JsonSerializer.Serialize(new {state}),
            }, cancellationToken
        );
    }

    private async Task LogFailureAsync(string category, string eventType, string message, Guid? torrentId,
        CancellationToken                     cancellationToken)
    {
        logger.LogWarning("{EventType}: {Message} TorrentId={TorrentId}", eventType, message, torrentId);

        await activityLogService.WriteAsync(
            new ActivityLogWriteRequest
            {
                Level             = ActivityLogLevel.Warning,
                Category          = category,
                EventType         = eventType,
                Message           = message,
                TorrentId         = torrentId,
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
            }, cancellationToken
        );
    }
}
