namespace TorrentCore.Core.Torrents;

public interface ITorrentStateStore
{
    Task                                 EnsureInitializedAsync(CancellationToken cancellationToken);
    Task<int>                            CountAsync(CancellationToken cancellationToken);
    Task<bool>                           ExistsByInfoHashAsync(string infoHash, CancellationToken cancellationToken);
    Task<IReadOnlyList<TorrentSnapshot>> ListAsync(CancellationToken cancellationToken);
    Task<TorrentSnapshot?>               GetAsync(Guid torrentId, CancellationToken cancellationToken);
    Task                                 InsertAsync(TorrentSnapshot torrent, CancellationToken cancellationToken);
    Task                                 UpdateAsync(TorrentSnapshot torrent, CancellationToken cancellationToken);
    Task<long?>                          AssignNextOrdinaryQueueOrderAsync(Guid torrentId,
        CancellationToken cancellationToken);
    Task<long?>                          AssignNextPriorityQueueOrderAsync(Guid torrentId,
        int priorityMetadataAttempts, CancellationToken cancellationToken);
    Task<bool>                           YieldPriorityMetadataAttemptAsync(Guid torrentId,
        int remainingAttempts, CancellationToken cancellationToken);
    Task<bool>                           SetQueueHeldAsync(Guid torrentId, bool isHeld,
        CancellationToken cancellationToken);
    Task<bool>                           ClearPriorityQueueOrderAsync(Guid torrentId,
        CancellationToken cancellationToken);
    Task<int>                            ReleaseQueueHoldsAsync(IReadOnlyList<Guid> torrentIds,
        CancellationToken cancellationToken);
    Task<TorrentSnapshot?>               ResumeWithQueueIntentAsync(Guid torrentId, TorrentQueueResumeMode mode,
        DateTimeOffset resumedAtUtc, int priorityMetadataAttempts, CancellationToken cancellationToken);
    Task                                 DeleteAsync(Guid torrentId, CancellationToken cancellationToken);
}
