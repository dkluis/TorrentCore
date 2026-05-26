namespace TorrentCore.Service.Engine;

public interface ITorrentRemovalCleanupScheduler
{
    void ScheduleDeleteDataCleanup(Guid torrentId, string downloadRootPath, IReadOnlyList<string> candidatePaths);
}
