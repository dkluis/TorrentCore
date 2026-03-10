using System.Text.Json;
using Microsoft.Extensions.Logging;
using TorrentCore.Core.Diagnostics;

namespace TorrentCore.Service.Configuration;

public sealed class SqlitePersistenceInitializer(
    ResolvedTorrentCoreServicePaths servicePaths,
    IActivityLogService activityLogService,
    ServiceInstanceContext serviceInstanceContext,
    IHostEnvironment hostEnvironment,
    ILogger<SqlitePersistenceInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await activityLogService.EnsureInitializedAsync(cancellationToken);

        logger.LogInformation(
            "TorrentCore SQLite persistence is ready. DatabaseFilePath={DatabaseFilePath}",
            servicePaths.DatabaseFilePath);

        await activityLogService.WriteAsync(new ActivityLogWriteRequest
        {
            Level = ActivityLogLevel.Information,
            Category = "startup",
            EventType = "service.startup.ready",
            Message = "TorrentCore service startup completed and SQLite persistence is ready.",
            ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
            DetailsJson = JsonSerializer.Serialize(new
            {
                serviceInstanceContext.ServiceInstanceId,
                EnvironmentName = hostEnvironment.EnvironmentName,
                HostName = Environment.MachineName,
                servicePaths.DownloadRootPath,
                servicePaths.StorageRootPath,
                servicePaths.DatabaseFilePath,
            }),
        }, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
