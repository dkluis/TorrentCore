using TorrentCore.Service.Engine;

namespace TorrentCore.Service.Tests;

public sealed class TorrentMetadataRecoveryStateTests
{
    [Fact]
    public void Observe_DoesNotTreatLingeringOpenConnectionsAsUsefulDiscoveryActivity()
    {
        var state = new TorrentMetadataRecoveryState();
        var start = new DateTimeOffset(2026, 4, 3, 9, 53, 48, TimeSpan.Zero);

        state.Observe(start, isResolvingMetadata: true, hasMetadata: false);
        state.Observe(start.AddSeconds(45), isResolvingMetadata: true, hasMetadata: false);

        var decision = state.Evaluate(start.AddSeconds(61), staleSeconds: 60, restartDelaySeconds: 15);

        Assert.Equal(MetadataRecoveryAction.Refresh, decision.Action);
        Assert.Null(decision.LastDiscoveryActivityAtUtc);
        Assert.Equal(start, decision.ResolvingSinceUtc);
    }

    [Fact]
    public void NoteDiscoveryActivity_TracksUsefulPeerConnections()
    {
        var state = new TorrentMetadataRecoveryState();
        var start = new DateTimeOffset(2026, 4, 3, 9, 53, 48, TimeSpan.Zero);

        state.Observe(start, isResolvingMetadata: true, hasMetadata: false);
        state.NoteDiscoveryActivity(start.AddSeconds(45));

        var decision = state.Evaluate(start.AddSeconds(61), staleSeconds: 60, restartDelaySeconds: 15);

        Assert.Equal(MetadataRecoveryAction.None, decision.Action);
        Assert.Equal(start.AddSeconds(45), decision.LastDiscoveryActivityAtUtc);
    }

    [Fact]
    public void Evaluate_EscalatesFromRefreshToRestartToReset_WhenNoUsefulPeerActivityOccurs()
    {
        var state = new TorrentMetadataRecoveryState();
        var start = new DateTimeOffset(2026, 4, 3, 9, 53, 48, TimeSpan.Zero);

        state.Observe(start, isResolvingMetadata: true, hasMetadata: false);

        var refreshDecision = state.Evaluate(start.AddSeconds(61), staleSeconds: 60, restartDelaySeconds: 15);
        Assert.Equal(MetadataRecoveryAction.Refresh, refreshDecision.Action);
        state.MarkRefresh(start.AddSeconds(61));

        var restartDecision = state.Evaluate(start.AddSeconds(77), staleSeconds: 60, restartDelaySeconds: 15);
        Assert.Equal(MetadataRecoveryAction.Restart, restartDecision.Action);
        state.MarkRestart(start.AddSeconds(77));

        var resetDecision = state.Evaluate(start.AddSeconds(93), staleSeconds: 60, restartDelaySeconds: 15);
        Assert.Equal(MetadataRecoveryAction.Reset, resetDecision.Action);
    }
}
