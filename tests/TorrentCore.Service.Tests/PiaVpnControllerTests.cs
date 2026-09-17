using TorrentCore.Service.Vpn;

namespace TorrentCore.Service.Tests;

public sealed class PiaVpnControllerTests
{
    [Theory]
    [InlineData("Connected", "Connected")]
    [InlineData("Disconnected", "Disconnected")]
    [InlineData("Connecting", "Connecting")]
    [InlineData("Reconnecting", "Reconnecting")]
    [InlineData("DisconnectingToReconnect", "DisconnectingToReconnect")]
    [InlineData("Disconnecting", "Disconnecting")]
    [InlineData(" Connected \n", "Connected")]
    [InlineData("Interrupted", "Interrupted")]
    public async Task GetConnectionStateAsync_ParsesDocumentedStates(
        string output,
        string expectedState)
    {
        var runner = new ScriptedProcessRunner(Success(output));
        var controller = new PiaVpnController(runner, TimeProvider.System);

        var result = await controller.GetConnectionStateAsync(CancellationToken.None);

        Assert.True(result.IsAvailable);
        Assert.Equal(expectedState, result.State?.ToString());
        Assert.False(result.TimedOut);
        var request = Assert.Single(runner.Requests);
        Assert.Equal(PiaVpnController.ControllerPath, request.FileName);
        Assert.Equal(["--timeout", "5", "get", "connectionstate"], request.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(5), request.Timeout);
    }

    [Theory]
    [InlineData("Connected to USA - New York")]
    [InlineData("0")]
    [InlineData("")]
    public async Task GetConnectionStateAsync_RejectsMalformedOutput(string output)
    {
        var runner = new ScriptedProcessRunner(Success(output));
        var controller = new PiaVpnController(runner, TimeProvider.System);

        var result = await controller.GetConnectionStateAsync(CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Null(result.State);
        Assert.Equal("PIA returned an unknown connection state.", result.FailureSummary);
    }

    [Fact]
    public async Task GetConnectionStateAsync_MapsNonzeroExitToUnavailable()
    {
        var runner = new ScriptedProcessRunner(new ExternalProcessResult
        {
            Started = true,
            TimedOut = false,
            ExitCode = 17,
            StandardError = "controller unavailable",
            FailureSummary = "controller unavailable",
        });
        var controller = new PiaVpnController(runner, TimeProvider.System);

        var result = await controller.GetConnectionStateAsync(CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Equal(17, result.ExitCode);
        Assert.Equal("controller unavailable", result.FailureSummary);
    }

    [Fact]
    public async Task GetConnectionStateAsync_MapsTimeoutToUnavailable()
    {
        var runner = new ScriptedProcessRunner(new ExternalProcessResult
        {
            Started = true,
            TimedOut = true,
            FailureSummary = "The process timed out.",
        });
        var controller = new PiaVpnController(runner, TimeProvider.System);

        var result = await controller.GetConnectionStateAsync(CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.True(result.TimedOut);
    }

    [Fact]
    public async Task GetConnectionStateAsync_PropagatesCancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var runner = new CancellingProcessRunner();
        var controller = new PiaVpnController(runner, TimeProvider.System);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => controller.GetConnectionStateAsync(cancellationSource.Token)
        );
    }

    [Fact]
    public async Task MutatingCommands_UseOnlyBoundedConnectAndDisconnect()
    {
        var runner = new ScriptedProcessRunner(Success(), Success());
        var controller = new PiaVpnController(runner, TimeProvider.System);
        Assert.True((await controller.DisconnectAsync(CancellationToken.None)).Succeeded);
        Assert.True((await controller.ConnectAsync(CancellationToken.None)).Succeeded);
        Assert.Collection(runner.Requests,
            request => AssertRequest(request, PiaVpnController.ControllerPath, "--timeout", "5", "disconnect"),
            request => AssertRequest(request, PiaVpnController.ControllerPath, "--timeout", "5", "connect"));
    }

    [Fact]
    public async Task WaitForState_ConfirmsActualStateAndStopsOnControllerFailure()
    {
        var runner = new ScriptedProcessRunner(Success("Connected"), new ExternalProcessResult
        {
            Started = true, TimedOut = true,
        });
        var controller = new PiaVpnController(runner, TimeProvider.System);
        Assert.Equal(VpnControllerConnectionState.Connected,
            (await controller.WaitForConnectionStateAsync(VpnControllerConnectionState.Connected, CancellationToken.None)).State);
        var unavailable = await controller.WaitForConnectionStateAsync(VpnControllerConnectionState.Disconnected, CancellationToken.None);
        Assert.False(unavailable.IsAvailable);
        Assert.True(unavailable.TimedOut);
    }

    [Fact]
    public async Task WaitForState_StopsAtSixtySecondsAndPreservesInterruptedState()
    {
        var time = new TorrentCore.Service.Tests.Fixtures.ManualTimeProvider(DateTimeOffset.UtcNow);
        var runner = new ScriptedProcessRunner(Success("Interrupted"));
        var controller = new PiaVpnController(runner, time);
        var pending = controller.WaitForConnectionStateAsync(VpnControllerConnectionState.Connected, CancellationToken.None);
        Assert.False(pending.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(60));
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(result.IsAvailable);
        Assert.True(result.TimedOut);
        Assert.Equal(VpnControllerConnectionState.Interrupted, result.State);
        Assert.Equal(TimeSpan.FromSeconds(60), result.Duration);
        Assert.Single(runner.Requests);
    }

    private static ExternalProcessResult Success(string output = "") => new()
    {
        Started = true,
        TimedOut = false,
        ExitCode = 0,
        StandardOutput = output,
    };

    private static void AssertRequest(
        ExternalProcessRequest request,
        string expectedPath,
        params string[] expectedArguments)
    {
        Assert.Equal(expectedPath, request.FileName);
        Assert.Equal(expectedArguments, request.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(5), request.Timeout);
    }

    private sealed class ScriptedProcessRunner(params ExternalProcessResult[] results) : IExternalProcessRunner
    {
        private readonly Queue<ExternalProcessResult> _results = new(results);
        public List<ExternalProcessRequest> Requests { get; } = [];

        public Task<ExternalProcessResult> RunAsync(
            ExternalProcessRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed class CancellingProcessRunner : IExternalProcessRunner
    {
        public Task<ExternalProcessResult> RunAsync(
            ExternalProcessRequest request,
            CancellationToken cancellationToken)
            => Task.FromCanceled<ExternalProcessResult>(cancellationToken);
    }
}
