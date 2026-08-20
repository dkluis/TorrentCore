using TorrentCore.Service.Engine;
using TorrentCore.Service.Tests.Fixtures;

namespace TorrentCore.Service.Tests;

public sealed class PayloadStaleDownloadSliceZeroCharacterizationTests
{
    private static readonly DateTimeOffset InitialUtcNow =
        new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CurrentRecovery_TreatsConnectedPeerAsUsefulWithoutPayloadGrowth()
    {
        var fixture = new DownloadObservationFixture(InitialUtcNow, downloadedBytes: 1_024);

        fixture.Observe(downloadRateBytesPerSecond: 0, openConnections: 0);
        fixture.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(DownloadRecoveryAction.Refresh, fixture.Evaluate().Action);

        fixture.Observe(downloadRateBytesPerSecond: 0, openConnections: 1);
        fixture.Advance(TimeSpan.FromSeconds(61));
        fixture.Observe(downloadRateBytesPerSecond: 0, openConnections: 1);

        Assert.Equal(DownloadRecoveryAction.None, fixture.Evaluate().Action);
        Assert.Null(fixture.State.GetColdSinceUtc());
    }

    [Fact]
    public void CurrentRecovery_TreatsReportedRateAsUsefulWithoutPayloadGrowth()
    {
        var fixture = new DownloadObservationFixture(InitialUtcNow, downloadedBytes: 1_024);

        fixture.Observe(downloadRateBytesPerSecond: 0, openConnections: 0);
        fixture.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(DownloadRecoveryAction.Refresh, fixture.Evaluate().Action);

        fixture.Observe(downloadRateBytesPerSecond: 512, openConnections: 0);
        fixture.Advance(TimeSpan.FromSeconds(61));
        fixture.Observe(downloadRateBytesPerSecond: 512, openConnections: 0);

        Assert.Equal(DownloadRecoveryAction.None, fixture.Evaluate().Action);
        Assert.Null(fixture.State.GetColdSinceUtc());
    }

    [Fact]
    public void CurrentRecovery_RefreshesZeroPeerZeroRateDownloadWithoutPayloadGrowth()
    {
        var fixture = new DownloadObservationFixture(InitialUtcNow, downloadedBytes: 1_024);

        fixture.Observe(downloadRateBytesPerSecond: 0, openConnections: 0);
        fixture.Advance(TimeSpan.FromSeconds(61));

        Assert.Equal(DownloadRecoveryAction.Refresh, fixture.Evaluate().Action);
        Assert.Equal(InitialUtcNow, fixture.State.GetColdSinceUtc());
    }

    [Fact]
    public void CurrentRecovery_OperatorPauseResetsAndResumeStartsFreshWindow()
    {
        var fixture = new DownloadObservationFixture(InitialUtcNow, downloadedBytes: 1_024);

        fixture.Observe(downloadRateBytesPerSecond: 0, openConnections: 0);
        fixture.Advance(TimeSpan.FromSeconds(45));
        fixture.ObserveNotTracked();
        fixture.Advance(TimeSpan.FromHours(2));
        fixture.Observe(downloadRateBytesPerSecond: 0, openConnections: 0);
        fixture.Advance(TimeSpan.FromSeconds(59));

        Assert.Equal(DownloadRecoveryAction.None, fixture.Evaluate().Action);

        fixture.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal(DownloadRecoveryAction.Refresh, fixture.Evaluate().Action);
    }

    [Fact]
    public void CurrentRecovery_RestartRestoresPersistedColdStart()
    {
        var clock = new ManualTimeProvider(InitialUtcNow.AddMinutes(10));
        var state = new TorrentDownloadRecoveryState(InitialUtcNow, downloadedBytes: 1_024);

        state.Observe(
            clock.GetUtcNow(), isTrackedDownload: true, downloadedBytes: 1_024,
            downloadRateBytesPerSecond: 0, openConnections: 0
        );

        var decision = state.Evaluate(
            clock.GetUtcNow(), staleSeconds: 60, restartDelaySeconds: 15
        );

        Assert.Equal(DownloadRecoveryAction.Refresh, decision.Action);
        Assert.Equal(InitialUtcNow, decision.DownloadingSinceUtc);
        Assert.Equal(InitialUtcNow, decision.StaleSinceUtc);
    }

    [Fact]
    public void CurrentRecovery_WithoutWaitingWorkRequestsRecoveryButHasNoYieldTransition()
    {
        var fixture = new DownloadObservationFixture(InitialUtcNow, downloadedBytes: 1_024);

        fixture.Observe(downloadRateBytesPerSecond: 0, openConnections: 0);
        fixture.Advance(TimeSpan.FromSeconds(61));

        var decision = fixture.Evaluate();

        Assert.Equal(DownloadRecoveryAction.Refresh, decision.Action);
        Assert.DoesNotContain("Yield", Enum.GetNames<DownloadRecoveryAction>());
    }

    private sealed class DownloadObservationFixture(DateTimeOffset initialUtcNow, long downloadedBytes)
    {
        private const int StaleSeconds = 60;
        private const int RestartDelaySeconds = 15;

        private long _downloadedBytes = downloadedBytes;

        public ManualTimeProvider Clock { get; } = new(initialUtcNow);
        public TorrentDownloadRecoveryState State { get; } = new();

        public void Advance(TimeSpan elapsed)
            => Clock.Advance(elapsed);

        public TorrentDownloadRecoveryDecision Evaluate()
            => State.Evaluate(
                Clock.GetUtcNow(), StaleSeconds, RestartDelaySeconds
            );

        public void Observe(long downloadRateBytesPerSecond, int openConnections, long? downloadedBytes = null)
        {
            if (downloadedBytes is not null)
            {
                _downloadedBytes = downloadedBytes.Value;
            }

            State.Observe(
                Clock.GetUtcNow(), isTrackedDownload: true, _downloadedBytes,
                downloadRateBytesPerSecond, openConnections
            );
        }

        public void ObserveNotTracked()
            => State.Observe(
                Clock.GetUtcNow(), isTrackedDownload: false, _downloadedBytes,
                downloadRateBytesPerSecond: 0, openConnections: 0
            );
    }
}
