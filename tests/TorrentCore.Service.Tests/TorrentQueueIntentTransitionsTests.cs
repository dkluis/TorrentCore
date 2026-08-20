using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Torrents;

namespace TorrentCore.Service.Tests;

public sealed class TorrentQueueIntentTransitionsTests
{
    [Fact]
    public void PriorityAndHoldTransitions_AreMutuallyExclusive()
    {
        var snapshot = CreateSnapshot();
        snapshot.DownloadNoProgressStartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-30);
        snapshot.IsDownloadYielded = true;
        TorrentQueueIntentTransitions.SetHeld(snapshot);

        Assert.True(snapshot.IsQueueHeld);
        Assert.Null(snapshot.PriorityQueueOrder);
        Assert.Null(snapshot.DownloadNoProgressStartedAtUtc);
        Assert.False(snapshot.IsDownloadYielded);

        TorrentQueueIntentTransitions.AssignPriorityOrder(snapshot, 4, 3);

        Assert.False(snapshot.IsQueueHeld);
        Assert.Equal(4, snapshot.PriorityQueueOrder);
        Assert.Equal(3, snapshot.PriorityMetadataAttemptsRemaining);

        TorrentQueueIntentTransitions.SetHeld(snapshot);

        Assert.True(snapshot.IsQueueHeld);
        Assert.Null(snapshot.PriorityQueueOrder);
        Assert.Null(snapshot.PriorityMetadataAttemptsRemaining);
    }

    [Fact]
    public void PausedTorrent_CannotRetainPriorityOrHoldIntent()
    {
        var snapshot = CreateSnapshot();
        snapshot.State = TorrentState.Paused;
        snapshot.DesiredState = TorrentDesiredState.Paused;
        snapshot.PriorityQueueOrder = 9;
        snapshot.IsQueueHeld = true;
        snapshot.DownloadNoProgressStartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-30);
        snapshot.IsDownloadYielded = true;

        TorrentQueueIntentTransitions.Normalize(snapshot);

        Assert.Null(snapshot.PriorityQueueOrder);
        Assert.False(snapshot.IsQueueHeld);
        Assert.Null(snapshot.DownloadNoProgressStartedAtUtc);
        Assert.False(snapshot.IsDownloadYielded);
        Assert.Throws<InvalidOperationException>(
            () => TorrentQueueIntentTransitions.AssignPriorityOrder(snapshot, 1, 3));
        Assert.Throws<InvalidOperationException>(() => TorrentQueueIntentTransitions.SetHeld(snapshot));
    }

    private static TorrentSnapshot CreateSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        return new TorrentSnapshot
        {
            TorrentId = Guid.NewGuid(),
            Name = "Queue Intent",
            State = TorrentState.Queued,
            DesiredState = TorrentDesiredState.Runnable,
            MagnetUri = "magnet:?xt=urn:btih:2121212121212121212121212121212121212121",
            SavePath = "/tmp/queue-intent",
            ProgressPercent = 0,
            DownloadedBytes = 0,
            UploadedBytes = 0,
            DownloadRateBytesPerSecond = 0,
            UploadRateBytesPerSecond = 0,
            TrackerCount = 0,
            ConnectedPeerCount = 0,
            AddedAtUtc = now,
        };
    }
}
