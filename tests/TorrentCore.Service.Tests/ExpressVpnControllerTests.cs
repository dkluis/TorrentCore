using TorrentCore.Service.Vpn;

namespace TorrentCore.Service.Tests;

public sealed class ExpressVpnControllerTests
{
    [Theory]
    [InlineData("Connected", "Connected")]
    [InlineData("Disconnected", "Disconnected")]
    [InlineData("Connecting", "Connecting")]
    [InlineData("Reconnecting", "Reconnecting")]
    [InlineData("DisconnectingToReconnect", "DisconnectingToReconnect")]
    [InlineData("Disconnecting", "Disconnecting")]
    [InlineData(" connected \n", "Connected")]
    public async Task GetConnectionStateAsync_ParsesDocumentedStates(
        string output,
        string expectedState)
    {
        var runner = new ScriptedProcessRunner(Success(output));
        var controller = new ExpressVpnController(runner, TimeProvider.System);

        var result = await controller.GetConnectionStateAsync(CancellationToken.None);

        Assert.True(result.IsAvailable);
        Assert.Equal(expectedState, result.State?.ToString());
        Assert.False(result.TimedOut);
        var request = Assert.Single(runner.Requests);
        Assert.Equal(ExpressVpnController.ControllerPath, request.FileName);
        Assert.Equal(["get", "connectionstate"], request.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(60), request.Timeout);
    }

    [Theory]
    [InlineData("Connected to USA - New York")]
    [InlineData("0")]
    [InlineData("")]
    public async Task GetConnectionStateAsync_RejectsMalformedOutput(string output)
    {
        var runner = new ScriptedProcessRunner(Success(output));
        var controller = new ExpressVpnController(runner, TimeProvider.System);

        var result = await controller.GetConnectionStateAsync(CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Null(result.State);
        Assert.Equal("ExpressVPN returned an unknown connection state.", result.FailureSummary);
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
        var controller = new ExpressVpnController(runner, TimeProvider.System);

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
        var controller = new ExpressVpnController(runner, TimeProvider.System);

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
        var controller = new ExpressVpnController(runner, TimeProvider.System);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => controller.GetConnectionStateAsync(cancellationSource.Token)
        );
    }

    [Fact]
    public async Task MutatingCommands_UseFixedPathsAndArgumentLists()
    {
        var runner = new ScriptedProcessRunner(Success(), Success(), Success());
        var controller = new ExpressVpnController(runner, TimeProvider.System);

        Assert.True((await controller.DisconnectAsync(CancellationToken.None)).Succeeded);
        Assert.True((await controller.ConnectAsync(CancellationToken.None)).Succeeded);
        Assert.True((await controller.LaunchApplicationAsync(CancellationToken.None)).Succeeded);

        Assert.Collection(
            runner.Requests,
            request => AssertRequest(request, ExpressVpnController.ControllerPath, "disconnect"),
            request => AssertRequest(request, ExpressVpnController.ControllerPath, "connect"),
            request => AssertRequest(request, ExpressVpnController.OpenPath, "-g", "-a", "ExpressVPN")
        );
    }

    [Fact]
    public async Task LaunchApplicationAsync_ReturnsLaunchFailure()
    {
        var runner = new ScriptedProcessRunner(new ExternalProcessResult
        {
            Started = true,
            TimedOut = false,
            ExitCode = 1,
            FailureSummary = "LaunchServices rejected the request.",
        });
        var controller = new ExpressVpnController(runner, TimeProvider.System);

        var result = await controller.LaunchApplicationAsync(CancellationToken.None);

        Assert.True(result.Started);
        Assert.False(result.Succeeded);
        Assert.Equal("LaunchServices rejected the request.", result.FailureSummary);
    }

    [Fact]
    public async Task ExternalProcessRunner_ReturnsSanitizedMissingExecutableFailure()
    {
        var runner = new ExternalProcessRunner(TimeProvider.System);

        var result = await runner.RunAsync(
            new ExternalProcessRequest(
                "/private/tmp/torrentcore-expressvpn-controller-does-not-exist",
                [],
                TimeSpan.FromSeconds(1)
            ),
            CancellationToken.None
        );

        Assert.False(result.Started);
        Assert.False(result.Succeeded);
        Assert.NotNull(result.FailureSummary);
        Assert.DoesNotContain('\n', result.FailureSummary);
    }

    [Fact]
    public void Sanitize_CollapsesLinesRemovesControlsAndBoundsLength()
    {
        var value = " first\nsecond\u0001" + new string('x', 600);

        var sanitized = ExternalProcessRunner.Sanitize(value);

        Assert.StartsWith("first second", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', sanitized);
        Assert.DoesNotContain('\u0001', sanitized);
        Assert.Equal(512, sanitized.Length);
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
        Assert.Equal(TimeSpan.FromSeconds(60), request.Timeout);
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
