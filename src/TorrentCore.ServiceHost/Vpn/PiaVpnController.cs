namespace TorrentCore.Service.Vpn;

internal sealed class PiaVpnController(
    IExternalProcessRunner processRunner,
    TimeProvider timeProvider) : IPiaVpnController
{
    internal const string ControllerPath = "/Applications/Private Internet Access.app/Contents/MacOS/piactl";
    internal static readonly TimeSpan TransitionTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);

    public bool IsSupported => OperatingSystem.IsMacOS();

    public Task<VpnControllerStateResult> GetConnectionStateAsync(CancellationToken cancellationToken)
        => ReadStateAsync(CommandTimeout, cancellationToken);

    public async Task<VpnControllerStateResult> WaitForConnectionStateAsync(
        VpnControllerConnectionState expectedState, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetUtcNow();
        var deadline = started + TransitionTimeout;
        VpnControllerStateResult? latest = null;
        while (timeProvider.GetUtcNow() < deadline)
        {
            var remaining = deadline - timeProvider.GetUtcNow();
            latest = await ReadStateAsync(remaining < CommandTimeout ? remaining : CommandTimeout, cancellationToken);
            if (!latest.IsAvailable || latest.State == expectedState)
                return latest with { Duration = timeProvider.GetUtcNow() - started };

            remaining = deadline - timeProvider.GetUtcNow();
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1),
                    timeProvider, cancellationToken);
        }

        return new VpnControllerStateResult
        {
            IsAvailable = latest?.IsAvailable ?? false,
            State = latest?.State,
            TimedOut = true,
            ExitCode = latest?.ExitCode,
            Duration = timeProvider.GetUtcNow() - started,
            FailureSummary = $"PIA did not reach {expectedState} within 60 seconds.",
        };
    }

    public Task<VpnControllerActionResult> DisconnectAsync(CancellationToken cancellationToken)
        => RunActionAsync("disconnect", cancellationToken);

    public Task<VpnControllerActionResult> ConnectAsync(CancellationToken cancellationToken)
        => RunActionAsync("connect", cancellationToken);

    private async Task<VpnControllerStateResult> ReadStateAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetUtcNow();
        var result = await processRunner.RunAsync(
            new ExternalProcessRequest(ControllerPath, ["--timeout", "5", "get", "connectionstate"], timeout),
            cancellationToken);
        var state = result.Succeeded ? ParseState(result.StandardOutput) : null;
        return new VpnControllerStateResult
        {
            IsAvailable = result.Succeeded && state is not null,
            State = state,
            TimedOut = result.TimedOut,
            ExitCode = result.ExitCode,
            Duration = timeProvider.GetUtcNow() - started,
            FailureSummary = result.Succeeded
                ? state is null ? "PIA returned an unknown connection state." : null
                : result.FailureSummary,
        };
    }

    private static VpnControllerConnectionState? ParseState(string output)
        => output.Trim() switch
        {
            "Connected" => VpnControllerConnectionState.Connected,
            "Disconnected" => VpnControllerConnectionState.Disconnected,
            "Interrupted" => VpnControllerConnectionState.Interrupted,
            "Connecting" => VpnControllerConnectionState.Connecting,
            "Reconnecting" => VpnControllerConnectionState.Reconnecting,
            "DisconnectingToReconnect" => VpnControllerConnectionState.DisconnectingToReconnect,
            "Disconnecting" => VpnControllerConnectionState.Disconnecting,
            _ => null,
        };

    private async Task<VpnControllerActionResult> RunActionAsync(string command, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetUtcNow();
        var result = await processRunner.RunAsync(
            new ExternalProcessRequest(ControllerPath, ["--timeout", "5", command], CommandTimeout), cancellationToken);
        return new VpnControllerActionResult
        {
            Started = result.Started,
            Succeeded = result.Succeeded,
            TimedOut = result.TimedOut,
            ExitCode = result.ExitCode,
            Duration = timeProvider.GetUtcNow() - started,
            FailureSummary = result.FailureSummary,
        };
    }
}
