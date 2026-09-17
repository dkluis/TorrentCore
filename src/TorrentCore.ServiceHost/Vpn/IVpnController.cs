namespace TorrentCore.Service.Vpn;

internal enum VpnControllerConnectionState
{
    Interrupted,
    Connected,
    Disconnected,
    Connecting,
    Reconnecting,
    DisconnectingToReconnect,
    Disconnecting,
}

internal sealed record VpnControllerStateResult
{
    public required bool IsAvailable { get; init; }
    public VpnControllerConnectionState? State { get; init; }
    public required bool TimedOut { get; init; }
    public int? ExitCode { get; init; }
    public required TimeSpan Duration { get; init; }
    public string? FailureSummary { get; init; }
}

internal sealed record VpnControllerActionResult
{
    public required bool Started { get; init; }
    public required bool Succeeded { get; init; }
    public required bool TimedOut { get; init; }
    public int? ExitCode { get; init; }
    public required TimeSpan Duration { get; init; }
    public string? FailureSummary { get; init; }
}

internal interface IVpnController
{
    bool IsSupported { get; }

    Task<VpnControllerStateResult> GetConnectionStateAsync(CancellationToken cancellationToken);

    Task<VpnControllerStateResult> WaitForConnectionStateAsync(
        VpnControllerConnectionState expectedState,
        CancellationToken cancellationToken);

    Task<VpnControllerActionResult> DisconnectAsync(CancellationToken cancellationToken);

    Task<VpnControllerActionResult> ConnectAsync(CancellationToken cancellationToken);

}

internal interface IPiaVpnController : IVpnController { }

internal interface IExpressVpnController : IVpnController
{
    Task<VpnControllerActionResult> LaunchApplicationAsync(CancellationToken cancellationToken);
}
