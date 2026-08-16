namespace TorrentCore.Service.Vpn;

internal enum ExpressVpnConnectionState
{
    Connected,
    Disconnected,
    Connecting,
    Reconnecting,
    DisconnectingToReconnect,
    Disconnecting,
}

internal sealed record ExpressVpnControllerStateResult
{
    public required bool IsAvailable { get; init; }
    public ExpressVpnConnectionState? State { get; init; }
    public required bool TimedOut { get; init; }
    public int? ExitCode { get; init; }
    public required TimeSpan Duration { get; init; }
    public string? FailureSummary { get; init; }
}

internal sealed record ExpressVpnControllerActionResult
{
    public required bool Started { get; init; }
    public required bool Succeeded { get; init; }
    public required bool TimedOut { get; init; }
    public int? ExitCode { get; init; }
    public required TimeSpan Duration { get; init; }
    public string? FailureSummary { get; init; }
}

internal interface IExpressVpnController
{
    bool IsSupported { get; }

    Task<ExpressVpnControllerStateResult> GetConnectionStateAsync(CancellationToken cancellationToken);

    Task<ExpressVpnControllerStateResult> WaitForConnectionStateAsync(
        ExpressVpnConnectionState expectedState,
        CancellationToken cancellationToken);

    Task<ExpressVpnControllerActionResult> DisconnectAsync(CancellationToken cancellationToken);

    Task<ExpressVpnControllerActionResult> ConnectAsync(CancellationToken cancellationToken);

    Task<ExpressVpnControllerActionResult> LaunchApplicationAsync(CancellationToken cancellationToken);
}
