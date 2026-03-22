using System.Reflection;
using System.Text.Json;
using TorrentCore.Contracts.Categories;
using TorrentCore.Contracts.Host;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Engine;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TorrentCore.Service.Application;

public sealed class TorrentApplicationService(
    IHostEnvironment hostEnvironment,
    ResolvedTorrentCoreServicePaths servicePaths,
    ITorrentEngineAdapter torrentEngineAdapter,
    IActivityLogService activityLogService,
    IOptions<TorrentCoreServiceOptions> serviceOptions,
    IRuntimeSettingsService runtimeSettingsService,
    ITorrentCategoryService torrentCategoryService,
    AppliedEngineSettingsState appliedEngineSettingsState,
    ServiceInstanceContext serviceInstanceContext,
    StartupRecoveryState startupRecoveryState,
    ILogger<TorrentApplicationService> logger) : ITorrentApplicationService
{
    public async Task<EngineHostStatusDto> GetHostStatusAsync(CancellationToken cancellationToken)
    {
        var runtimeSettings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
        var torrents = await torrentEngineAdapter.GetTorrentsAsync(cancellationToken);

        var resolvingMetadataCount = torrents.Count(torrent => torrent.State == TorrentState.ResolvingMetadata);
        var metadataQueueCount = torrents.Count(torrent => torrent.State == TorrentState.Queued && torrent.TotalBytes is null);
        var downloadingCount = torrents.Count(torrent => torrent.State == TorrentState.Downloading);
        var downloadQueueCount = torrents.Count(torrent => torrent.State == TorrentState.Queued && torrent.TotalBytes is not null);
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
            ServiceName               = "TorrentCore.Service",
            ServiceVersion            = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
            ServiceInstanceId         = serviceInstanceContext.ServiceInstanceId,
            EngineRuntime             = serviceOptions.Value.EngineMode.ToString(),
            EngineListenPort          = serviceOptions.Value.EngineListenPort,
            EngineDhtPort             = serviceOptions.Value.EngineDhtPort,
            EnginePortForwardingEnabled = serviceOptions.Value.EngineAllowPortForwarding,
            EngineLocalPeerDiscoveryEnabled = serviceOptions.Value.EngineAllowLocalPeerDiscovery,
            EngineMaximumConnections = appliedEngineSettingsState.EngineMaximumConnections,
            EngineMaximumHalfOpenConnections = appliedEngineSettingsState.EngineMaximumHalfOpenConnections,
            EngineMaximumDownloadRateBytesPerSecond = appliedEngineSettingsState.EngineMaximumDownloadRateBytesPerSecond,
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
            PartialFilesEnabled        = runtimeSettings.PartialFilesEnabled,
            PartialFileSuffix          = runtimeSettings.PartialFileSuffix,
            SeedingStopMode            = runtimeSettings.SeedingStopMode.ToString(),
            SeedingStopRatio           = runtimeSettings.SeedingStopRatio,
            SeedingStopMinutes         = runtimeSettings.SeedingStopMinutes,
            CompletedTorrentCleanupMode = runtimeSettings.CompletedTorrentCleanupMode.ToString(),
            CompletedTorrentCleanupMinutes = runtimeSettings.CompletedTorrentCleanupMinutes,
            Status                    = startupRecoveryState.Completed ? EngineHostStatus.Ready : EngineHostStatus.Starting,
            EnvironmentName           = hostEnvironment.EnvironmentName,
            DownloadRootPath          = servicePaths.DownloadRootPath,
            TorrentCount              = torrents.Count,
            SupportsMagnetAdds        = true,
            SupportsPause             = true,
            SupportsResume            = true,
            SupportsRemove            = true,
            SupportsPersistentStorage = true,
            SupportsMultiHost         = false,
            StartupRecoveryCompleted  = startupRecoveryState.Completed,
            StartupRecoveredTorrentCount = startupRecoveryState.RecoveredTorrentCount,
            StartupNormalizedTorrentCount = startupRecoveryState.NormalizedTorrentCount,
            StartupRecoveryCompletedAtUtc = startupRecoveryState.CompletedAtUtc,
            CheckedAtUtc              = DateTimeOffset.UtcNow,
        };
    }

    public Task<RuntimeSettingsDto> GetRuntimeSettingsAsync(CancellationToken cancellationToken) =>
        runtimeSettingsService.GetRuntimeSettingsDtoAsync(cancellationToken);

    public Task<RuntimeSettingsDto> UpdateRuntimeSettingsAsync(UpdateRuntimeSettingsRequest request, CancellationToken cancellationToken) =>
        runtimeSettingsService.UpdateAsync(request, cancellationToken);

    public Task<IReadOnlyList<TorrentCategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken) =>
        torrentCategoryService.GetCategoriesAsync(cancellationToken);

    public Task<TorrentCategoryDto> UpdateCategoryAsync(string key, UpdateTorrentCategoryRequest request, CancellationToken cancellationToken) =>
        torrentCategoryService.UpdateCategoryAsync(key, request, cancellationToken);

    public Task<IReadOnlyList<TorrentSummaryDto>> GetTorrentsAsync(CancellationToken cancellationToken) =>
        torrentEngineAdapter.GetTorrentsAsync(cancellationToken);

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

    public async Task<TorrentDetailDto> AddMagnetAsync(AddMagnetRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var categorySelection = await torrentCategoryService.ResolveSelectionAsync(request.CategoryKey, cancellationToken);
            var normalizedRequest = new AddMagnetRequest
            {
                MagnetUri = request.MagnetUri,
                CategoryKey = categorySelection.CategoryKey,
            };

            var torrent = await torrentEngineAdapter.AddMagnetAsync(normalizedRequest, categorySelection, cancellationToken);

            logger.LogInformation("Added torrent {TorrentId} named {TorrentName}", torrent.TorrentId, torrent.Name);

            await activityLogService.WriteAsync(new ActivityLogWriteRequest
            {
                Level = ActivityLogLevel.Information,
                Category = "torrent",
                EventType = "torrent.added",
                Message = $"Added torrent '{torrent.Name}'.",
                TorrentId = torrent.TorrentId,
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    torrent.Name,
                    torrent.CategoryKey,
                    torrent.State,
                    torrent.SavePath,
                }),
            }, cancellationToken);

            return torrent;
        }
        catch (ServiceOperationException exception)
        {
            await LogFailureAsync("torrent", "torrent.add.failed", exception.Message, torrentId: null, cancellationToken);
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
            await LogActionAsync("torrent.metadata.refresh_requested", "Requested metadata discovery refresh.", torrentId, result.State, cancellationToken);
            return result;
        }
        catch (ServiceOperationException exception)
        {
            await LogFailureAsync("torrent", "torrent.metadata.refresh.failed", exception.Message, torrentId, cancellationToken);
            throw;
        }
    }

    public async Task<TorrentActionResultDto> ResetMetadataSessionAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await torrentEngineAdapter.ResetMetadataSessionAsync(torrentId, cancellationToken);
            await LogActionAsync("torrent.metadata.reset_requested", "Recreated metadata discovery session.", torrentId, result.State, cancellationToken);
            return result;
        }
        catch (ServiceOperationException exception)
        {
            await LogFailureAsync("torrent", "torrent.metadata.reset.failed", exception.Message, torrentId, cancellationToken);
            throw;
        }
    }

    public async Task<TorrentActionResultDto> RetryCompletionCallbackAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await torrentEngineAdapter.RetryCompletionCallbackAsync(torrentId, cancellationToken);
            await LogActionAsync("torrent.callback.retry_requested", "Queued completion callback retry.", torrentId, result.State, cancellationToken);
            return result;
        }
        catch (ServiceOperationException exception)
        {
            await LogFailureAsync("torrent", "torrent.callback.retry.failed", exception.Message, torrentId, cancellationToken);
            throw;
        }
    }

    public async Task<TorrentActionResultDto> RemoveAsync(Guid torrentId, RemoveTorrentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await torrentEngineAdapter.RemoveAsync(torrentId, request, cancellationToken);

            await activityLogService.WriteAsync(new ActivityLogWriteRequest
            {
                Level = ActivityLogLevel.Information,
                Category = "torrent",
                EventType = "torrent.removed",
                Message = request.DeleteData ? "Removed torrent and requested data deletion." : "Removed torrent.",
                TorrentId = torrentId,
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(new
                {
                    result.State,
                    request.DeleteData,
                }),
            }, cancellationToken);

            return result;
        }
        catch (ServiceOperationException exception)
        {
            await LogFailureAsync("torrent", "torrent.remove.failed", exception.Message, torrentId, cancellationToken);
            throw;
        }
    }

    private async Task LogActionAsync(string eventType, string message, Guid torrentId, TorrentState state, CancellationToken cancellationToken)
    {
        logger.LogInformation("{Message} TorrentId={TorrentId} State={State}", message, torrentId, state);

        await activityLogService.WriteAsync(new ActivityLogWriteRequest
        {
            Level = ActivityLogLevel.Information,
            Category = "torrent",
            EventType = eventType,
            Message = message,
            TorrentId = torrentId,
            ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
            DetailsJson = JsonSerializer.Serialize(new { state }),
        }, cancellationToken);
    }

    private async Task LogFailureAsync(string category, string eventType, string message, Guid? torrentId, CancellationToken cancellationToken)
    {
        logger.LogWarning("{EventType}: {Message} TorrentId={TorrentId}", eventType, message, torrentId);

        await activityLogService.WriteAsync(new ActivityLogWriteRequest
        {
            Level = ActivityLogLevel.Warning,
            Category = category,
            EventType = eventType,
            Message = message,
            TorrentId = torrentId,
            ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
        }, cancellationToken);
    }
}
