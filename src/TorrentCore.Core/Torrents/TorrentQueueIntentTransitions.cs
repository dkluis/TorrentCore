namespace TorrentCore.Core.Torrents;

public static class TorrentQueueIntentTransitions
{
    public static void AssignOrdinaryOrder(TorrentSnapshot torrent, long ordinaryQueueOrder)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ordinaryQueueOrder);

        torrent.OrdinaryQueueOrder = ordinaryQueueOrder;
    }

    public static void AssignPriorityOrder(TorrentSnapshot torrent, long priorityQueueOrder,
        int priorityMetadataAttempts)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(priorityQueueOrder);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(priorityMetadataAttempts);

        if (IsPaused(torrent))
        {
            throw new InvalidOperationException("A paused torrent cannot retain priority queue intent.");
        }

        torrent.PriorityQueueOrder = priorityQueueOrder;
        torrent.PriorityMetadataAttemptsRemaining = priorityMetadataAttempts;
        torrent.IsQueueHeld        = false;
    }

    public static void SetHeld(TorrentSnapshot torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);

        if (IsPaused(torrent))
        {
            throw new InvalidOperationException("A paused torrent cannot retain hold queue intent.");
        }

        torrent.PriorityQueueOrder = null;
        torrent.PriorityMetadataAttemptsRemaining = null;
        torrent.IsQueueHeld        = true;
    }

    public static void ReleaseHold(TorrentSnapshot torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        torrent.IsQueueHeld = false;
    }

    public static void ClearForPause(TorrentSnapshot torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);
        torrent.PriorityQueueOrder = null;
        torrent.PriorityMetadataAttemptsRemaining = null;
        torrent.IsQueueHeld        = false;
    }

    public static void Normalize(TorrentSnapshot torrent)
    {
        ArgumentNullException.ThrowIfNull(torrent);

        if (IsPaused(torrent))
        {
            ClearForPause(torrent);
            return;
        }

        if (torrent.PriorityQueueOrder is not null)
        {
            torrent.IsQueueHeld = false;
            return;
        }

        torrent.PriorityMetadataAttemptsRemaining = null;
    }

    private static bool IsPaused(TorrentSnapshot torrent)
        => torrent.State == TorrentCore.Contracts.Torrents.TorrentState.Paused ||
           torrent.DesiredState == TorrentDesiredState.Paused;
}
