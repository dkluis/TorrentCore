namespace TorrentCore.Service.Infrastructure;

internal static class VpnEgressActivityEvents
{
    public const string Category = "vpn";
    public const string ValidationCompleted = "vpn.egress.validation_completed";
    public const string StateChanged = "vpn.egress.state_changed";
    public const string EngineTransitionFailed = "vpn.egress.engine_transition_failed";
}
