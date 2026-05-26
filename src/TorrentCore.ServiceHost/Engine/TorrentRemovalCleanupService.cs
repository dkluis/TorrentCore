using System.Text.Json;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Service.Configuration;

namespace TorrentCore.Service.Engine;

public sealed class TorrentRemovalCleanupService(
    ILogger<TorrentRemovalCleanupService> logger,
    IActivityLogService activityLogService,
    ServiceInstanceContext serviceInstanceContext) : ITorrentRemovalCleanupScheduler
{
    public void ScheduleDeleteDataCleanup(Guid torrentId, string downloadRootPath, IReadOnlyList<string> candidatePaths)
    {
        if (candidatePaths.Count == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                TorrentDataPathCleanup.DeletePayloadArtifacts(downloadRootPath, candidatePaths);
                TorrentDataPathCleanup.DeleteEmptyDirectories(downloadRootPath, candidatePaths);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Deferred torrent data cleanup failed. TorrentId={TorrentId} DownloadRootPath={DownloadRootPath}",
                    torrentId,
                    downloadRootPath
                );

                await activityLogService.WriteAsync(
                    new ActivityLogWriteRequest
                    {
                        Level = ActivityLogLevel.Warning,
                        Category = "torrent",
                        EventType = "torrent.data_cleanup.failed",
                        Message = "Deferred torrent data cleanup failed after remove/delete.",
                        TorrentId = torrentId,
                        ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                        DetailsJson = JsonSerializer.Serialize(
                            new
                            {
                                DownloadRootPath = downloadRootPath,
                                CandidatePaths = candidatePaths,
                                Error = exception.Message,
                            }
                        ),
                    },
                    CancellationToken.None
                );
            }
        });
    }
}
