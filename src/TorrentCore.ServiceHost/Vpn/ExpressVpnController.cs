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

    public async Task<ExpressVpnControllerStateResult> GetConnectionStateAsync(
        CancellationToken cancellationToken)
        => await GetConnectionStateAsync(OperationTimeout, cancellationToken);

    public async Task<ExpressVpnControllerStateResult> WaitForConnectionStateAsync(
        ExpressVpnConnectionState expectedState,
        CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow();
        var deadline = startedAt + OperationTimeout;
        ExpressVpnControllerStateResult? latest = null;

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

        return new ExpressVpnControllerStateResult
        {
            IsAvailable = latest?.IsAvailable ?? false,
            State = latest?.State,
            TimedOut = true,
            ExitCode = latest?.ExitCode,
            Duration = timeProvider.GetUtcNow() - startedAt,
            FailureSummary = $"ExpressVPN did not reach {expectedState} within 60 seconds.",
        };
    }

    public Task<ExpressVpnControllerActionResult> DisconnectAsync(CancellationToken cancellationToken)
        => RunActionAsync(ControllerPath, ["disconnect"], cancellationToken);

    public Task<ExpressVpnControllerActionResult> ConnectAsync(CancellationToken cancellationToken)
        => RunActionAsync(ControllerPath, ["connect"], cancellationToken);

    public Task<ExpressVpnControllerActionResult> LaunchApplicationAsync(CancellationToken cancellationToken)
        => RunActionAsync(OpenPath, ["-g", "-a", "ExpressVPN"], cancellationToken);

    private async Task<ExpressVpnControllerStateResult> GetConnectionStateAsync(
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
            return new ExpressVpnControllerStateResult
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
            return new ExpressVpnControllerStateResult
            {
                IsAvailable = false,
                TimedOut = false,
                ExitCode = result.ExitCode,
                Duration = duration,
                FailureSummary = "ExpressVPN returned an unknown connection state.",
            };
        }

        return new ExpressVpnControllerStateResult
        {
            IsAvailable = true,
            State = state.Value,
            TimedOut = false,
            ExitCode = result.ExitCode,
            Duration = duration,
        };
    }

    private static ExpressVpnConnectionState? ParseConnectionState(string output)
        => output.Trim() switch
        {
            var value when value.Equals("Connected", StringComparison.OrdinalIgnoreCase) =>
                ExpressVpnConnectionState.Connected,
            var value when value.Equals("Disconnected", StringComparison.OrdinalIgnoreCase) =>
                ExpressVpnConnectionState.Disconnected,
            var value when value.Equals("Connecting", StringComparison.OrdinalIgnoreCase) =>
                ExpressVpnConnectionState.Connecting,
            var value when value.Equals("Reconnecting", StringComparison.OrdinalIgnoreCase) =>
                ExpressVpnConnectionState.Reconnecting,
            var value when value.Equals("DisconnectingToReconnect", StringComparison.OrdinalIgnoreCase) =>
                ExpressVpnConnectionState.DisconnectingToReconnect,
            var value when value.Equals("Disconnecting", StringComparison.OrdinalIgnoreCase) =>
                ExpressVpnConnectionState.Disconnecting,
            _ => null,
        };

    private async Task<ExpressVpnControllerActionResult> RunActionAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow();
        var result = await processRunner.RunAsync(
            new ExternalProcessRequest(fileName, arguments, OperationTimeout),
            cancellationToken
        );
        return new ExpressVpnControllerActionResult
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
