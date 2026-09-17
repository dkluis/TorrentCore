namespace TorrentCore.Service.Vpn;

internal sealed class ExpressVpnController(
    IExternalProcessRunner processRunner,
    TimeProvider timeProvider) : IExpressVpnController
{
    internal const string ControllerPath = "/usr/local/bin/expressvpnctl";
    internal const string OpenPath = "/usr/bin/open";
    internal static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StatePollInterval = TimeSpan.FromSeconds(1);

    public bool IsSupported => OperatingSystem.IsMacOS();

    public async Task<VpnControllerStateResult> GetConnectionStateAsync(
        CancellationToken cancellationToken)
        => await GetConnectionStateAsync(OperationTimeout, cancellationToken);

    public async Task<VpnControllerStateResult> WaitForConnectionStateAsync(
        VpnControllerConnectionState expectedState,
        CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow();
        var deadline = startedAt + OperationTimeout;
        VpnControllerStateResult? latest = null;

        while (timeProvider.GetUtcNow() < deadline)
        {
            var remaining = deadline - timeProvider.GetUtcNow();
            latest = await GetConnectionStateAsync(remaining, cancellationToken);
            if (!latest.IsAvailable || latest.State == expectedState)
            {
                return latest with { Duration = timeProvider.GetUtcNow() - startedAt };
            }

            remaining = deadline - timeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            await Task.Delay(
                remaining < StatePollInterval ? remaining : StatePollInterval,
                timeProvider,
                cancellationToken
            );
        }

        return new VpnControllerStateResult
        {
            IsAvailable = latest?.IsAvailable ?? false,
            State = latest?.State,
            TimedOut = true,
            ExitCode = latest?.ExitCode,
            Duration = timeProvider.GetUtcNow() - startedAt,
            FailureSummary = $"ExpressVPN did not reach {expectedState} within 60 seconds.",
        };
    }

    public Task<VpnControllerActionResult> DisconnectAsync(CancellationToken cancellationToken)
        => RunActionAsync(ControllerPath, ["disconnect"], cancellationToken);

    public Task<VpnControllerActionResult> ConnectAsync(CancellationToken cancellationToken)
        => RunActionAsync(ControllerPath, ["connect"], cancellationToken);

    public Task<VpnControllerActionResult> LaunchApplicationAsync(CancellationToken cancellationToken)
        => RunActionAsync(OpenPath, ["-g", "-a", "ExpressVPN"], cancellationToken);

    private async Task<VpnControllerStateResult> GetConnectionStateAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow();
        var result = await processRunner.RunAsync(
            new ExternalProcessRequest(ControllerPath, ["get", "connectionstate"], timeout),
            cancellationToken
        );
        var duration = timeProvider.GetUtcNow() - startedAt;
        if (!result.Succeeded)
        {
            return new VpnControllerStateResult
            {
                IsAvailable = false,
                TimedOut = result.TimedOut,
                ExitCode = result.ExitCode,
                Duration = duration,
                FailureSummary = result.FailureSummary,
            };
        }

        var state = ParseConnectionState(result.StandardOutput);
        if (state is null)
        {
            return new VpnControllerStateResult
            {
                IsAvailable = false,
                TimedOut = false,
                ExitCode = result.ExitCode,
                Duration = duration,
                FailureSummary = "ExpressVPN returned an unknown connection state.",
            };
        }

        return new VpnControllerStateResult
        {
            IsAvailable = true,
            State = state.Value,
            TimedOut = false,
            ExitCode = result.ExitCode,
            Duration = duration,
        };
    }

    private static VpnControllerConnectionState? ParseConnectionState(string output)
        => output.Trim() switch
        {
            var value when value.Equals("Connected", StringComparison.OrdinalIgnoreCase) =>
                VpnControllerConnectionState.Connected,
            var value when value.Equals("Disconnected", StringComparison.OrdinalIgnoreCase) =>
                VpnControllerConnectionState.Disconnected,
            var value when value.Equals("Connecting", StringComparison.OrdinalIgnoreCase) =>
                VpnControllerConnectionState.Connecting,
            var value when value.Equals("Reconnecting", StringComparison.OrdinalIgnoreCase) =>
                VpnControllerConnectionState.Reconnecting,
            var value when value.Equals("DisconnectingToReconnect", StringComparison.OrdinalIgnoreCase) =>
                VpnControllerConnectionState.DisconnectingToReconnect,
            var value when value.Equals("Disconnecting", StringComparison.OrdinalIgnoreCase) =>
                VpnControllerConnectionState.Disconnecting,
            _ => null,
        };

    private async Task<VpnControllerActionResult> RunActionAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow();
        var result = await processRunner.RunAsync(
            new ExternalProcessRequest(fileName, arguments, OperationTimeout),
            cancellationToken
        );
        return new VpnControllerActionResult
        {
            Started = result.Started,
            Succeeded = result.Succeeded,
            TimedOut = result.TimedOut,
            ExitCode = result.ExitCode,
            Duration = timeProvider.GetUtcNow() - startedAt,
            FailureSummary = result.FailureSummary,
        };
    }
}
