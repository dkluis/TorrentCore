using Microsoft.Extensions.Logging;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Core.Torrents;
using TorrentCore.Persistence.Sqlite.Configuration;
using TorrentCore.Persistence.Sqlite.Schema;

namespace TorrentCore.Service.Configuration;

public sealed class SqlitePersistenceInitializer(
    ResolvedTorrentCoreServicePaths servicePaths,
    SqliteSchemaMigrator sqliteSchemaMigrator,
    SqliteRuntimeSettingsStore runtimeSettingsStore,
    IActivityLogService activityLogService,
    ITorrentStateStore torrentStateStore,
    ILogger<SqlitePersistenceInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await sqliteSchemaMigrator.ApplyMigrationsAsync(cancellationToken);
        await activityLogService.EnsureInitializedAsync(cancellationToken);
        await runtimeSettingsStore.EnsureInitializedAsync(cancellationToken);
        await torrentStateStore.EnsureInitializedAsync(cancellationToken);

        logger.LogInformation(
            "TorrentCore SQLite persistence is ready. DatabaseFilePath={DatabaseFilePath}",
            servicePaths.DatabaseFilePath);

        await activityLogService.WriteAsync(new ActivityLogWriteRequest
        {
            Level = ActivityLogLevel.Information,
            Category = "startup",
            EventType = "service.persistence.ready",
            Message = "SQLite persistence is initialized.",
        }, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
