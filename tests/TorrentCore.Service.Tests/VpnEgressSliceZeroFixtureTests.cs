using System.Net;
using System.Text.Json;
using TorrentCore.Service.Infrastructure;
using TorrentCore.Service.Tests.Fixtures;

namespace TorrentCore.Service.Tests;

public sealed class VpnEgressSliceZeroFixtureTests
{
    private static readonly Uri EgressEndpoint = new("https://egress.test.example/ip");

    [Fact]
    public async Task ManualTimeProvider_AdvancesScheduledWorkWithoutWallClockDelay()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero));
        var delay = Task.Delay(TimeSpan.FromMinutes(4), clock);

        clock.Advance(TimeSpan.FromSeconds(239));
        Assert.False(delay.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(1));
        await delay;

        Assert.Equal(new DateTimeOffset(2026, 8, 6, 12, 4, 0, TimeSpan.Zero), clock.GetUtcNow());
        Assert.Equal(TimeSpan.FromMinutes(4).Ticks, clock.GetTimestamp());
    }

    [Theory]
    [InlineData(VpnEgressHttpScenario.VpnSuccess, HttpStatusCode.OK, VpnEgressHttpScenarios.VpnIpv4)]
    [InlineData(VpnEgressHttpScenario.DirectIsp, HttpStatusCode.OK, VpnEgressHttpScenarios.DirectIspIpv4)]
    [InlineData(VpnEgressHttpScenario.Ipv6, HttpStatusCode.OK, VpnEgressHttpScenarios.PublicIpv6)]
    [InlineData(VpnEgressHttpScenario.MalformedJson, HttpStatusCode.OK, null)]
    [InlineData(VpnEgressHttpScenario.EndpointFailure, HttpStatusCode.ServiceUnavailable, null)]
    public async Task HttpFixture_RepresentsResponseOutcomes(
        VpnEgressHttpScenario scenario,
        HttpStatusCode expectedStatus,
        string? expectedAddress)
    {
        using var handler = VpnEgressHttpScenarios.Create(scenario);
        using var httpClient = new HttpClient(handler);
        using var response = await httpClient.GetAsync(EgressEndpoint);

        Assert.Equal(expectedStatus, response.StatusCode);
        if (expectedAddress is not null)
        {
            var json = await response.Content.ReadAsStringAsync();
            Assert.Contains(expectedAddress, json, StringComparison.Ordinal);
        }

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(EgressEndpoint, request.RequestUri);
    }

    [Fact]
    public async Task HttpFixture_RepresentsTimeoutWithoutNetworkAccess()
    {
        using var handler = VpnEgressHttpScenarios.Create(VpnEgressHttpScenario.Timeout);
        using var httpClient = new HttpClient(handler);

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => httpClient.GetAsync(EgressEndpoint));

        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HttpFixture_RepresentsCancellationWithoutNetworkAccess()
    {
        using var handler = VpnEgressHttpScenarios.Create(VpnEgressHttpScenario.Cancellation);
        using var httpClient = new HttpClient(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => httpClient.GetAsync(EgressEndpoint));
    }

    [Fact]
    public void EngineLifecycleFixture_DistinguishesDisposedAndReplacementInstances()
    {
        var lifecycle = new RecordingEngineLifecycle();
        var firstInstance = lifecycle.RecordCreated();
        lifecycle.Record(firstInstance, EngineLifecycleStage.Started);
        lifecycle.Record(firstInstance, EngineLifecycleStage.StopRequested);
        lifecycle.Record(firstInstance, EngineLifecycleStage.Stopped);
        lifecycle.Record(firstInstance, EngineLifecycleStage.Disposed);
        var replacementInstance = lifecycle.RecordCreated();
        lifecycle.Record(replacementInstance, EngineLifecycleStage.Started);

        Assert.NotEqual(firstInstance, replacementInstance);
        Assert.Equal(
            [
                EngineLifecycleStage.Created,
                EngineLifecycleStage.Started,
                EngineLifecycleStage.StopRequested,
                EngineLifecycleStage.Stopped,
                EngineLifecycleStage.Disposed,
            ],
            lifecycle.Observations
                .Where(observation => observation.InstanceId == firstInstance)
                .Select(observation => observation.Stage)
        );
        Assert.Equal(2, lifecycle.Observations.Count(observation => observation.Stage == EngineLifecycleStage.Created));
    }

    [Fact]
    public async Task CaDesktopLayoutFixture_ContainsPathsButNoMachineLocalValues()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ca-desktop-layout.json");
        var fixtureJson = await File.ReadAllTextAsync(fixturePath);
        using var fixture = JsonDocument.Parse(fixtureJson);
        var root = fixture.RootElement;

        Assert.Equal("CA-Desktop", root.GetProperty("machineRole").GetString());
        Assert.Equal("arm64", root.GetProperty("architecture").GetString());
        Assert.Equal(
            "~/TorrentCore/Service/TorrentCoreService",
            root.GetProperty("currentLayout").GetProperty("serviceExecutable").GetString()
        );
        Assert.Equal(
            "~/Library/Application Support/TorrentCore/storage/torrentcore.db",
            root.GetProperty("currentLayout").GetProperty("databaseFile").GetString()
        );
        Assert.DoesNotContain("/Users/", fixtureJson, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKey", fixtureJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", fixtureJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", fixtureJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActivityEventNames_AreStableBeforePersistenceIsImplemented()
    {
        Assert.Equal("vpn", VpnEgressActivityEvents.Category);
        Assert.Equal("vpn.egress.validation_completed", VpnEgressActivityEvents.ValidationCompleted);
        Assert.Equal("vpn.egress.state_changed", VpnEgressActivityEvents.StateChanged);
        Assert.Equal("vpn.egress.engine_transition_failed", VpnEgressActivityEvents.EngineTransitionFailed);
        Assert.Equal(
            "vpn.expressvpn.controller_state_changed",
            VpnEgressActivityEvents.ExpressVpnControllerStateChanged
        );
        Assert.Equal("vpn.expressvpn.recovery_attempted", VpnEgressActivityEvents.ExpressVpnRecoveryAttempted);
        Assert.Equal("vpn.expressvpn.launch_attempted", VpnEgressActivityEvents.ExpressVpnLaunchAttempted);
        Assert.Equal("vpn.expressvpn.recovery_exhausted", VpnEgressActivityEvents.ExpressVpnRecoveryExhausted);
    }
}
