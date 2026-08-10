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
    string? OperatorMessage)
{
    public bool IsTorrentProcessingAvailable => Phase is VpnConnectionPhase.Disabled or VpnConnectionPhase.Ready;
}

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

    internal bool Set(VpnConnectionRuntimeSnapshot snapshot)
    {
        lock (_syncRoot)
        {
            if (_snapshot == snapshot)
            {
                return false;
            }

            _snapshot = snapshot;
            return true;
        }
    }
}
