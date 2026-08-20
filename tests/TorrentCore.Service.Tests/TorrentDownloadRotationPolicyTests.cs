using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Torrents;
using TorrentCore.Service.Engine;

namespace TorrentCore.Service.Tests;

public sealed class TorrentDownloadRotationPolicyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StaleActiveDownload_WithoutWaitingWork_DoesNotYield()
    {
        var stale = Item("stale", isActive: true, ordinaryOrder: 1, noProgressStartedAtUtc: Now.AddHours(-1));

        var result = Evaluate([stale], maxActiveDownloads: 1);

        Assert.Empty(result.YieldTorrentIds);
        Assert.Empty(result.ReplacementTorrentIds);
    }

    [Fact]
    public void SelectsOnlyEnoughOldestStaleDownloadsToAdmitWaitingWork()
    {
        var oldest = Item("oldest", isActive: true, ordinaryOrder: 1,
            noProgressStartedAtUtc: Now.AddMinutes(-50));
        var newer = Item("newer", isActive: true, ordinaryOrder: 2,
            noProgressStartedAtUtc: Now.AddMinutes(-40));
        var newest = Item("newest", isActive: true, ordinaryOrder: 3,
            noProgressStartedAtUtc: Now.AddMinutes(-35));
        var waitingOne = Item("waiting-one", isActive: false, ordinaryOrder: 4);
        var waitingTwo = Item("waiting-two", isActive: false, ordinaryOrder: 5);

        var result = Evaluate(
            [oldest, newer, newest, waitingOne, waitingTwo], maxActiveDownloads: 3);

        Assert.Equal(
            [oldest.Snapshot.TorrentId, newer.Snapshot.TorrentId],
            result.YieldTorrentIds);
        Assert.Equal(
            [waitingOne.Snapshot.TorrentId, waitingTwo.Snapshot.TorrentId],
            result.ReplacementTorrentIds);
    }

    [Fact]
    public void ProductiveOrNotYetExpiredDownload_DoesNotYield()
    {
        var productive = Item("productive", isActive: true, ordinaryOrder: 1,
            noProgressStartedAtUtc: Now.AddMinutes(-10));
        var waiting = Item("waiting", isActive: false, ordinaryOrder: 2);

        var result = Evaluate([productive, waiting], maxActiveDownloads: 1);

        Assert.Empty(result.YieldTorrentIds);
    }

    [Fact]
    public void EqualClockUsesTorrentIdAsStableTieBreaker()
    {
        var higherId = Item("higher", isActive: true, ordinaryOrder: 1,
            noProgressStartedAtUtc: Now.AddHours(-1), torrentId: Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        var lowerId = Item("lower", isActive: true, ordinaryOrder: 2,
            noProgressStartedAtUtc: Now.AddHours(-1), torrentId: Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var waiting = Item("waiting", isActive: false, ordinaryOrder: 3);

        var result = Evaluate([higherId, lowerId, waiting], maxActiveDownloads: 2);

        Assert.Equal([lowerId.Snapshot.TorrentId], result.YieldTorrentIds);
    }

    private static TorrentDownloadRotationSelection Evaluate(
        IReadOnlyList<TorrentQueuePolicyItem> items,
        int maxActiveDownloads)
        => TorrentDownloadRotationPolicy.Evaluate(
            items,
            maxActiveMetadataResolutions: 2,
            maxActiveDownloads,
            TimeSpan.FromMinutes(30),
            Now);

    private static TorrentQueuePolicyItem Item(
        string name,
        bool isActive,
        long ordinaryOrder,
        DateTimeOffset? noProgressStartedAtUtc = null,
        Guid? torrentId = null)
    {
        var snapshot = new TorrentSnapshot
        {
            TorrentId = torrentId ?? Guid.NewGuid(),
            Name = name,
            State = isActive ? TorrentState.Downloading : TorrentState.Queued,
            DesiredState = TorrentDesiredState.Runnable,
            MagnetUri = $"magnet:?xt=urn:btih:{Guid.NewGuid():N}00000000",
            SavePath = $"/tmp/{name}",
            ProgressPercent = 10,
            DownloadedBytes = 1_024,
            UploadedBytes = 0,
            TotalBytes = 10_240,
            DownloadRateBytesPerSecond = 0,
            UploadRateBytesPerSecond = 0,
            TrackerCount = 0,
            ConnectedPeerCount = 0,
            AddedAtUtc = Now.AddHours(-2),
            OrdinaryQueueOrder = ordinaryOrder,
            DownloadNoProgressStartedAtUtc = noProgressStartedAtUtc,
        };
        return new TorrentQueuePolicyItem(snapshot, TorrentQueueWorkKind.Download, isActive);
    }
}
