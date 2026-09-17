namespace TorrentCore.Service.Infrastructure;

internal static class VpnEgressActivityEvents
{
    public const string Category = "vpn";
    public const string ValidationCompleted = "vpn.egress.validation_completed";
    public const string StateChanged = "vpn.egress.state_changed";
    public const string EngineTransitionFailed = "vpn.egress.engine_transition_failed";
    public const string PiaControllerStateChanged = "vpn.pia.controller_state_changed";
    public const string PiaRecoveryAttempted = "vpn.pia.recovery_attempted";
    public const string PiaRecoveryExhausted = "vpn.pia.recovery_exhausted";
    public const string ExpressVpnControllerStateChanged = "vpn.expressvpn.controller_state_changed";
    public const string ExpressVpnRecoveryAttempted = "vpn.expressvpn.recovery_attempted";
    public const string ExpressVpnLaunchAttempted = "vpn.expressvpn.launch_attempted";
    public const string ExpressVpnRecoveryExhausted = "vpn.expressvpn.recovery_exhausted";
}
