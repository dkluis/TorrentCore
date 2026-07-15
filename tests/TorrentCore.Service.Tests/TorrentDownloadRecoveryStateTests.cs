using TorrentCore.Service.Engine;

namespace TorrentCore.Service.Tests;

public sealed class TorrentDownloadRecoveryStateTests
{
    [Fact]
    public void Evaluate_RefreshesZeroPeerDownloadAfterStaleWindow()
    {
        var state = new TorrentDownloadRecoveryState();
        var start = new DateTimeOffset(2026, 4, 3, 18, 49, 38, TimeSpan.Zero);

        state.Observe(start, isTrackedDownload: true, downloadedBytes: 1_024, downloadRateBytesPerSecond: 0,
            openConnections: 0);

        var decision = state.Evaluate(start.AddSeconds(61), staleSeconds: 60, restartDelaySeconds: 15);

        Assert.Equal(DownloadRecoveryAction.Refresh, decision.Action);
        Assert.Equal(start, decision.DownloadingSinceUtc);
        Assert.Null(decision.LastUsefulActivityAtUtc);
    }

    [Fact]
    public void Observe_UsefulActivityClearsRecoveryCycle()
    {
        var state = new TorrentDownloadRecoveryState();
        var start = new DateTimeOffset(2026, 4, 3, 18, 49, 38, TimeSpan.Zero);

        state.Observe(start, isTrackedDownload: true, downloadedBytes: 1_024, downloadRateBytesPerSecond: 0,
            openConnections: 0);
        state.MarkRefresh(start.AddSeconds(61));
        state.Observe(start.AddSeconds(70), isTrackedDownload: true, downloadedBytes: 2_048,
            downloadRateBytesPerSecond: 512, openConnections: 1);

        var decision = state.Evaluate(start.AddSeconds(71), staleSeconds: 60, restartDelaySeconds: 15);

        Assert.Equal(DownloadRecoveryAction.None, decision.Action);
        Assert.Equal(start.AddSeconds(70), decision.LastUsefulActivityAtUtc);
        Assert.Equal(DownloadRecoveryAction.None, decision.LastRecoveryAction);
        Assert.Null(decision.LastActionAtUtc);
    }

    [Fact]
    public void Evaluate_EscalatesFromRefreshToRestart_WhenNoUsefulActivityOccurs()
    {
        var state = new TorrentDownloadRecoveryState();
        var start = new DateTimeOffset(2026, 4, 3, 18, 49, 38, TimeSpan.Zero);

        state.Observe(start, isTrackedDownload: true, downloadedBytes: 1_024, downloadRateBytesPerSecond: 0,
            openConnections: 0);

        var refreshDecision = state.Evaluate(start.AddSeconds(61), staleSeconds: 60, restartDelaySeconds: 15);
        Assert.Equal(DownloadRecoveryAction.Refresh, refreshDecision.Action);
        state.MarkRefresh(start.AddSeconds(61));

        var restartDecision = state.Evaluate(start.AddSeconds(77), staleSeconds: 60, restartDelaySeconds: 15);
        Assert.Equal(DownloadRecoveryAction.Restart, restartDecision.Action);
    }

    [Fact]
    public void Evaluate_BacksOffTheNextRefreshCycleAfterRestartStaysCold()
    {
        var state = new TorrentDownloadRecoveryState();
        var start = new DateTimeOffset(2026, 4, 3, 18, 49, 38, TimeSpan.Zero);

        state.Observe(start, isTrackedDownload: true, downloadedBytes: 1_024, downloadRateBytesPerSecond: 0,
            openConnections: 0);
        state.MarkRefresh(start.AddSeconds(61));
        state.MarkRestart(start.AddSeconds(77));

        var beforeBackoffExpires = state.Evaluate(start.AddSeconds(138), staleSeconds: 60,
            restartDelaySeconds: 15);
        Assert.Equal(DownloadRecoveryAction.None, beforeBackoffExpires.Action);
        Assert.Equal(2, beforeBackoffExpires.BackoffMultiplier);

        var decision = state.Evaluate(start.AddSeconds(198), staleSeconds: 60, restartDelaySeconds: 15);

        Assert.Equal(DownloadRecoveryAction.Refresh, decision.Action);
        Assert.Equal(DownloadRecoveryAction.Restart, decision.LastRecoveryAction);
        Assert.Equal(start.AddSeconds(77), decision.LastActionAtUtc);
    }

    [Fact]
    public void Observe_UsefulActivityClearsRecoveryBackoff()
    {
        var state = new TorrentDownloadRecoveryState();
        var start = new DateTimeOffset(2026, 4, 3, 18, 49, 38, TimeSpan.Zero);

        state.Observe(start, isTrackedDownload: true, downloadedBytes: 1_024, downloadRateBytesPerSecond: 0,
            openConnections: 0);
        state.MarkRefresh(start.AddSeconds(61));
        state.MarkRestart(start.AddSeconds(77));
        state.Observe(start.AddSeconds(80), isTrackedDownload: true, downloadedBytes: 2_048,
            downloadRateBytesPerSecond: 512, openConnections: 1);
        state.Observe(start.AddSeconds(81), isTrackedDownload: true, downloadedBytes: 2_048,
            downloadRateBytesPerSecond: 0, openConnections: 0);

        var decision = state.Evaluate(start.AddSeconds(141), staleSeconds: 60, restartDelaySeconds: 15);

        Assert.Equal(DownloadRecoveryAction.Refresh, decision.Action);
        Assert.Equal(1, decision.BackoffMultiplier);
        Assert.Equal(0, decision.RecoveryCycle);
    }

    [Fact]
    public void MarkRestart_CapsRecoveryBackoffAtEightTimesTheConfiguredWindows()
    {
        var state = new TorrentDownloadRecoveryState();
        var start = new DateTimeOffset(2026, 4, 3, 18, 49, 38, TimeSpan.Zero);

        state.Observe(start, isTrackedDownload: true, downloadedBytes: 1_024, downloadRateBytesPerSecond: 0,
            openConnections: 0);
        for (var cycle = 1; cycle <= 5; cycle++)
        {
            state.MarkRestart(start.AddMinutes(cycle));
        }

        var decision = state.Evaluate(start.AddMinutes(5).AddSeconds(1), staleSeconds: 60,
            restartDelaySeconds: 15);

        Assert.Equal(8, decision.BackoffMultiplier);
        Assert.Equal(480, decision.EffectiveStaleSeconds);
        Assert.Equal(120, decision.EffectiveRestartDelaySeconds);
    }
}
