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

    [Fact]
    public void MarkReset_BacksOffTheNextRecoveryCycle_WhenNoUsefulPeerActivityOccursAfterReset()
    {
        var state = new TorrentMetadataRecoveryState();
        var start = new DateTimeOffset(2026, 4, 3, 9, 53, 48, TimeSpan.Zero);

        state.Observe(start, isResolvingMetadata: true, hasMetadata: false);
        state.MarkRefresh(start.AddSeconds(61));
        state.MarkRestart(start.AddSeconds(77));
        state.MarkReset(start.AddSeconds(93));

        var beforeStaleDecision = state.Evaluate(start.AddSeconds(152), staleSeconds: 60, restartDelaySeconds: 15);
        Assert.Equal(MetadataRecoveryAction.None, beforeStaleDecision.Action);

        var stillBackingOffDecision = state.Evaluate(start.AddSeconds(169), staleSeconds: 60,
            restartDelaySeconds: 15);
        Assert.Equal(MetadataRecoveryAction.None, stillBackingOffDecision.Action);
        Assert.Equal(2, stillBackingOffDecision.BackoffMultiplier);
        Assert.Equal(120, stillBackingOffDecision.EffectiveStaleSeconds);
        Assert.Equal(30, stillBackingOffDecision.EffectiveRestartDelaySeconds);

        var restartDecision = state.Evaluate(start.AddSeconds(214), staleSeconds: 60, restartDelaySeconds: 15);
        Assert.Equal(MetadataRecoveryAction.Restart, restartDecision.Action);
        Assert.Equal(start.AddSeconds(93), restartDecision.ResolvingSinceUtc);
        Assert.Null(restartDecision.LastDiscoveryActivityAtUtc);
    }

    [Fact]
    public void NoteDiscoveryActivity_ClearsRecoveryBackoff()
    {
        var state = new TorrentMetadataRecoveryState();
        var start = new DateTimeOffset(2026, 4, 3, 9, 53, 48, TimeSpan.Zero);

        state.Observe(start, isResolvingMetadata: true, hasMetadata: false);
        state.MarkRefresh(start.AddSeconds(61));
        state.MarkRestart(start.AddSeconds(77));
        state.MarkReset(start.AddSeconds(93));
        state.NoteDiscoveryActivity(start.AddSeconds(100));

        var decision = state.Evaluate(start.AddSeconds(161), staleSeconds: 60, restartDelaySeconds: 15);

        Assert.Equal(MetadataRecoveryAction.Refresh, decision.Action);
        Assert.Equal(1, decision.BackoffMultiplier);
        Assert.Equal(0, decision.RecoveryCycle);
    }

    [Fact]
    public void MarkReset_CapsRecoveryBackoffAtEightTimesTheConfiguredWindows()
    {
        var state = new TorrentMetadataRecoveryState();
        var start = new DateTimeOffset(2026, 4, 3, 9, 53, 48, TimeSpan.Zero);

        state.Observe(start, isResolvingMetadata: true, hasMetadata: false);
        for (var cycle = 1; cycle <= 5; cycle++)
        {
            state.MarkReset(start.AddMinutes(cycle));
        }

        var decision = state.Evaluate(start.AddMinutes(5).AddSeconds(1), staleSeconds: 60,
            restartDelaySeconds: 15);

        Assert.Equal(8, decision.BackoffMultiplier);
        Assert.Equal(480, decision.EffectiveStaleSeconds);
        Assert.Equal(120, decision.EffectiveRestartDelaySeconds);
    }
}
