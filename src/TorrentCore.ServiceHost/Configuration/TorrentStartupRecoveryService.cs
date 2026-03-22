#region

using System.Text.Json;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Service.Engine;

#endregion

namespace TorrentCore.Service.Configuration;

public sealed class TorrentStartupRecoveryService(ITorrentEngineAdapter torrentEngineAdapter,
    IActivityLogService activityLogService, ServiceInstanceContext serviceInstanceContext,
    StartupRecoveryState startupRecoveryState, IHostEnvironment hostEnvironment,
    ResolvedTorrentCoreServicePaths servicePaths, ILogger<TorrentStartupRecoveryService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var result = await torrentEngineAdapter.RecoverAsync(cancellationToken);
        startupRecoveryState.MarkCompleted(
            result.RecoveredTorrentCount, result.NormalizedTorrentCount, result.CompletedAtUtc
        );

        foreach (var change in result.Changes)
        {
            await activityLogService.WriteAsync(
                new ActivityLogWriteRequest
                {
                    Level     = ActivityLogLevel.Information,
                    Category  = "torrent",
                    EventType = "torrent.recovery.normalized",
                    Message =
                            $"Normalized torrent '{change.Name}' from '{change.PreviousState}' to '{change.CurrentState}' during startup recovery.",
                    TorrentId         = change.TorrentId,
                    ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                    DetailsJson = JsonSerializer.Serialize(
                        new
                        {
                            change.Name, change.PreviousState, change.CurrentState,
                        }
                    ),
                }, cancellationToken
            );
        }

        logger.LogInformation(
            "Torrent startup recovery completed. RecoveredTorrentCount={RecoveredTorrentCount} NormalizedTorrentCount={NormalizedTorrentCount}",
            result.RecoveredTorrentCount, result.NormalizedTorrentCount
        );

        await activityLogService.WriteAsync(
            new ActivityLogWriteRequest
            {
                Level             = ActivityLogLevel.Information,
                Category          = "startup",
                EventType         = "service.recovery.completed",
                Message           = $"Startup recovery completed for {result.RecoveredTorrentCount} torrent(s).",
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(
                    new
                    {
                        result.RecoveredTorrentCount,
                        result.NormalizedTorrentCount,
                        result.CompletedAtUtc,
                    }
                ),
            }, cancellationToken
        );

        await activityLogService.WriteAsync(
            new ActivityLogWriteRequest
            {
                Level             = ActivityLogLevel.Information,
                Category          = "startup",
                EventType         = "service.startup.ready",
                Message           = "TorrentCore service startup completed and recovery is ready.",
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(
                    new
                    {
                        serviceInstanceContext.ServiceInstanceId, hostEnvironment.EnvironmentName,
                        HostName = Environment.MachineName,
                        servicePaths.DownloadRootPath,
                        servicePaths.StorageRootPath,
                        servicePaths.DatabaseFilePath,
                        result.RecoveredTorrentCount,
                        result.NormalizedTorrentCount,
                        result.CompletedAtUtc,
                    }
                ),
            }, cancellationToken
        );
    }

    public Task StopAsync(CancellationToken cancellationToken) { return Task.CompletedTask; }
}
