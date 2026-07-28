using System.Text.Json;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Infrastructure;

namespace TorrentCore.Service.Engine;

public sealed class TorrentRemovalCleanupService(
    ILogger<TorrentRemovalCleanupService> logger,
    IActivityLogService activityLogService,
    ServiceInstanceContext serviceInstanceContext) : ITorrentRemovalCleanupScheduler
{
    internal static readonly IReadOnlyList<TimeSpan> RetryDelays =
    [
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
    ];

    public void ScheduleDeleteDataCleanup(Guid torrentId, string downloadRootPath, IReadOnlyList<string> candidatePaths)
    {
        if (candidatePaths.Count == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            var cleanupException = await TryDeleteDataWithRetryAsync(downloadRootPath, candidatePaths);
            if (cleanupException is null)
            {
                return;
            }

            logger.LogWarning(
                cleanupException,
                "Deferred torrent data cleanup failed after retries. TorrentId={TorrentId} DownloadRootPath={DownloadRootPath}",
                torrentId,
                downloadRootPath
            );

            await activityLogService.TryWriteActivityLogAsync(
                new ActivityLogWriteRequest
                {
                    Level = ActivityLogLevel.Warning,
                    Category = "torrent",
                    EventType = "torrent.data_cleanup.failed",
                    Message = "Deferred torrent data cleanup failed after remove/delete retries.",
                    TorrentId = torrentId,
                    ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                    DetailsJson = JsonSerializer.Serialize(
                        new
                        {
                            DownloadRootPath = downloadRootPath,
                            CandidatePaths = candidatePaths,
                            Error = cleanupException.Message,
                            Attempts = RetryDelays.Count + 1,
                        }
                    ),
                },
                CancellationToken.None
            );
        });
    }

    internal static async Task<Exception?> TryDeleteDataWithRetryAsync(
        string downloadRootPath,
        IReadOnlyList<string> candidatePaths,
        Action<string, IReadOnlyList<string>>? deleteData = null,
        Func<TimeSpan, Task>? delay = null)
    {
        deleteData ??= static (rootPath, paths) =>
        {
            TorrentDataPathCleanup.DeletePayloadArtifacts(rootPath, paths);
            TorrentDataPathCleanup.DeleteEmptyDirectories(rootPath, paths);
        };
        delay ??= Task.Delay;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                deleteData(downloadRootPath, candidatePaths);
                return null;
            }
            catch (IOException) when (attempt < RetryDelays.Count)
            {
                await delay(RetryDelays[attempt]);
            }
            catch (Exception exception)
            {
                return exception;
            }
        }
    }
}
