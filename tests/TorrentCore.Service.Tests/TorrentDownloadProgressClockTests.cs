using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Torrents;
using TorrentCore.Service.Engine;

namespace TorrentCore.Service.Tests;

public sealed class TorrentDownloadProgressClockTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FirstActiveObservation_StartsFreshClock()
    {
        var snapshot = Snapshot(downloadedBytes: 1_024);

        var transition = TorrentDownloadProgressClock.Evaluate(
            snapshot, snapshot.DownloadedBytes, TorrentDownloadActivityState.Active, Now);

        Assert.Equal(Now, transition.NoProgressStartedAtUtc);
        Assert.False(transition.IsDownloadYielded);
    }

    [Fact]
    public void ZeroGrowth_PreservesOriginalClockRegardlessOfPeersOrRate()
    {
        var startedAt = Now.AddMinutes(-25);
        var snapshot = Snapshot(downloadedBytes: 1_024);
        snapshot.DownloadNoProgressStartedAtUtc = startedAt;
        snapshot.ConnectedPeerCount = 8;
        snapshot.DownloadRateBytesPerSecond = 4_096;

        var transition = TorrentDownloadProgressClock.Evaluate(
            snapshot, snapshot.DownloadedBytes, TorrentDownloadActivityState.Active, Now);

        Assert.Equal(startedAt, transition.NoProgressStartedAtUtc);
        Assert.False(transition.IsDownloadYielded);
    }

    [Fact]
    public void PositiveDurableGrowth_RestartsClock()
    {
        var snapshot = Snapshot(downloadedBytes: 1_024);
        snapshot.DownloadNoProgressStartedAtUtc = Now.AddMinutes(-25);

        var transition = TorrentDownloadProgressClock.Evaluate(
            snapshot, 1_025, TorrentDownloadActivityState.Active, Now);

        Assert.Equal(Now, transition.NoProgressStartedAtUtc);
    }

    [Fact]
    public void QueuedYield_PreservesYieldClassButSuspendsClock()
    {
        var snapshot = Snapshot(downloadedBytes: 1_024);
        snapshot.DownloadNoProgressStartedAtUtc = Now.AddMinutes(-25);
        snapshot.IsDownloadYielded = true;

        var transition = TorrentDownloadProgressClock.Evaluate(
            snapshot, snapshot.DownloadedBytes, TorrentDownloadActivityState.Queued, Now);

        Assert.Null(transition.NoProgressStartedAtUtc);
        Assert.True(transition.IsDownloadYielded);
    }

    [Fact]
    public void RecoverySuspension_PreservesActiveClockAcrossEngineRecreation()
    {
        var startedAt = Now.AddMinutes(-25);
        var snapshot = Snapshot(downloadedBytes: 1_024);
        snapshot.DownloadNoProgressStartedAtUtc = startedAt;

        var transition = TorrentDownloadProgressClock.Evaluate(
            snapshot, snapshot.DownloadedBytes, TorrentDownloadActivityState.Suspended, Now);

        Assert.Equal(startedAt, transition.NoProgressStartedAtUtc);
        Assert.False(transition.IsDownloadYielded);
    }

    [Fact]
    public void ReadmittedYield_StartsFreshClockAndClearsYieldClass()
    {
        var snapshot = Snapshot(downloadedBytes: 1_024);
        snapshot.IsDownloadYielded = true;
        snapshot.DownloadLastYieldedAtUtc = Now.AddMinutes(-5);

        var transition = TorrentDownloadProgressClock.Evaluate(
            snapshot, snapshot.DownloadedBytes, TorrentDownloadActivityState.Active, Now);

        Assert.Equal(Now, transition.NoProgressStartedAtUtc);
        Assert.False(transition.IsDownloadYielded);
    }

    [Fact]
    public void InactiveState_ClearsClockAndCurrentYieldClass()
    {
        var snapshot = Snapshot(downloadedBytes: 1_024);
        snapshot.DownloadNoProgressStartedAtUtc = Now.AddMinutes(-25);
        snapshot.IsDownloadYielded = true;

        var transition = TorrentDownloadProgressClock.Evaluate(
            snapshot, snapshot.DownloadedBytes, TorrentDownloadActivityState.Inactive, Now);

        Assert.Null(transition.NoProgressStartedAtUtc);
        Assert.False(transition.IsDownloadYielded);
    }

    private static TorrentSnapshot Snapshot(long downloadedBytes)
        => new()
        {
            TorrentId = Guid.NewGuid(),
            Name = "payload-clock",
            State = TorrentState.Downloading,
            DesiredState = TorrentDesiredState.Runnable,
            MagnetUri = $"magnet:?xt=urn:btih:{Guid.NewGuid():N}00000000",
            SavePath = "/tmp/payload-clock",
            ProgressPercent = 25,
            DownloadedBytes = downloadedBytes,
            UploadedBytes = 0,
            TotalBytes = 4_096,
            DownloadRateBytesPerSecond = 0,
            UploadRateBytesPerSecond = 0,
            TrackerCount = 0,
            ConnectedPeerCount = 0,
            AddedAtUtc = Now.AddHours(-1),
        };
}
