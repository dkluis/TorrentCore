namespace TorrentCore.Service.Vpn;

public enum VpnConnectionPhase
{
    Disabled,
    Checking,
    Activating,
    Ready,
    Suspending,
    Degraded,
}

public enum VpnConnectionReason
{
    DirectIsp,
    InvalidResponse,
    TimedOut,
    EndpointFailure,
    UnexpectedFailure,
    EngineActivationFailed,
    EngineSuspensionFailed,
}

public sealed record VpnConnectionRuntimeSnapshot(
    bool ValidationEnabled,
    VpnConnectionPhase Phase,
    VpnConnectionReason? Reason,
    string? OperatorMessage,
    DateTimeOffset? LastCheckAtUtc = null,
    DateTimeOffset? LastSuccessAtUtc = null,
    DateTimeOffset? NextAutomaticRetryAtUtc = null,
    string? ObservedPublicIpv4 = null,
    string? FailureSummary = null)
{
    public bool IsTorrentProcessingAvailable => Phase is VpnConnectionPhase.Disabled or VpnConnectionPhase.Ready;
}

internal sealed record VpnConnectionRuntimeTransition(
    VpnConnectionRuntimeSnapshot Previous,
    VpnConnectionRuntimeSnapshot Current);

public sealed class VpnConnectionRuntimeState
{
    private readonly object _syncRoot = new();
    private VpnConnectionRuntimeSnapshot _snapshot = new(false, VpnConnectionPhase.Disabled, null, null);

    public VpnConnectionRuntimeSnapshot Snapshot
    {
        get
        {
            lock (_syncRoot)
            {
                return _snapshot;
            }
        }
    }

    internal VpnConnectionRuntimeTransition? Set(VpnConnectionRuntimeSnapshot snapshot)
    {
        lock (_syncRoot)
        {
            var previous = _snapshot;
            _snapshot = snapshot;
            return HasSameConnectionOutcome(previous, snapshot)
                ? null
                : new VpnConnectionRuntimeTransition(previous, snapshot);
        }
    }

    internal void Update(Func<VpnConnectionRuntimeSnapshot, VpnConnectionRuntimeSnapshot> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (_syncRoot)
        {
            _snapshot = update(_snapshot);
        }
    }

    private static bool HasSameConnectionOutcome(
        VpnConnectionRuntimeSnapshot first,
        VpnConnectionRuntimeSnapshot second)
        => first.ValidationEnabled == second.ValidationEnabled &&
           first.Phase == second.Phase &&
           first.Reason == second.Reason;
}
