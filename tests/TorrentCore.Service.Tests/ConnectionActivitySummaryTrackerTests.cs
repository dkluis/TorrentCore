using TorrentCore.Service.Engine;

namespace TorrentCore.Service.Tests;

public sealed class ConnectionActivitySummaryTrackerTests
{
    [Fact]
    public void DrainReady_AggregatesConnectionActivityByTorrentAndReason()
    {
        var tracker = new ConnectionActivitySummaryTracker();
        var torrentId = Guid.NewGuid();
        var startedAt = new DateTimeOffset(2026, 7, 12, 20, 0, 0, TimeSpan.Zero);

        tracker.RegisterPeersFound(torrentId, startedAt, 4);
        tracker.RegisterPeerConnected(torrentId, startedAt.AddSeconds(1));
        tracker.RegisterPeerDisconnected(torrentId, startedAt.AddSeconds(2));
        tracker.RegisterConnectionFailure(torrentId, startedAt.AddSeconds(3), "Unreachable");
        tracker.RegisterConnectionFailure(torrentId, startedAt.AddSeconds(4), "Unreachable");

        var summaries = tracker.DrainReady(startedAt.AddMinutes(1), TimeSpan.FromMinutes(1));

        var summary = Assert.Single(summaries);
        Assert.Equal(torrentId, summary.TorrentId);
        Assert.Equal(1, summary.PeersFoundEvents);
        Assert.Equal(4, summary.NewPeersFound);
        Assert.Equal(1, summary.PeerConnectedEvents);
        Assert.Equal(1, summary.PeerDisconnectedEvents);
        Assert.Equal(2, summary.ConnectionFailureEvents);
        Assert.Equal(2, summary.ConnectionFailuresByReason["Unreachable"]);
        Assert.Empty(tracker.DrainReady(startedAt.AddMinutes(2), TimeSpan.FromMinutes(1)));
    }
}
