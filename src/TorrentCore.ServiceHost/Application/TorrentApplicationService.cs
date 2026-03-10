using System.Reflection;
using System.Text.Json;
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
    ServiceInstanceContext serviceInstanceContext,
    StartupRecoveryState startupRecoveryState,
    ILogger<TorrentApplicationService> logger) : ITorrentApplicationService
{
    public async Task<EngineHostStatusDto> GetHostStatusAsync(CancellationToken cancellationToken)
    {
        return new EngineHostStatusDto
        {
            ServiceName               = "TorrentCore.Service",
            ServiceVersion            = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
            ServiceInstanceId         = serviceInstanceContext.ServiceInstanceId,
            EngineRuntime             = serviceOptions.Value.EngineMode.ToString(),
            Status                    = startupRecoveryState.Completed ? EngineHostStatus.Ready : EngineHostStatus.Starting,
            EnvironmentName           = hostEnvironment.EnvironmentName,
            DownloadRootPath          = servicePaths.DownloadRootPath,
            TorrentCount              = await torrentEngineAdapter.GetTorrentCountAsync(cancellationToken),
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
            var torrent = await torrentEngineAdapter.AddMagnetAsync(request, servicePaths.DownloadRootPath, cancellationToken);

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
