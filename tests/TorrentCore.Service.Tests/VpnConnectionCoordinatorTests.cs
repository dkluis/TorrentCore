using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TorrentCore.Contracts.Host;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Engine;
using TorrentCore.Service.Tests.Fixtures;
using TorrentCore.Service.Vpn;

namespace TorrentCore.Service.Tests;

public sealed class VpnConnectionCoordinatorTests
{
    [Fact]
    public async Task PendingStartupCheck_DoesNotDelayApiAvailability()
    {
        var handler = new ScriptedHttpMessageHandler();
        handler.EnqueueWaitForCancellation();
        await using var factory = CreateFactory(
            handler,
            requestTimeoutSeconds: 5,
            checkIntervalSeconds: 6
        );
        using var client = factory.CreateClient();

        await WaitUntilAsync(() => handler.Requests.Count == 1, TimeSpan.FromSeconds(1));
        var status = await client.GetFromJsonAsync<EngineHostStatusDto>("api/host/status");

        Assert.NotNull(status);
        Assert.Equal(EngineHostStatus.Degraded, status.Status);
        Assert.Equal("Checking", status.VpnConnectionPhase);
        Assert.False(status.StartupRecoveryCompleted);
        Assert.False(status.TorrentProcessingAvailable);
    }

    [Fact]
    public async Task StartupFailure_LeavesApiAvailableAndAcceptsQueuedMagnets()
    {
        var handler = CreateHandler(VpnEgressHttpScenarios.DirectIspIpv4, repeat: 4);
        await using var factory = CreateFactory(handler);
        using var client = factory.CreateClient();

        var status = await WaitForHostAsync(client, value => value.VpnConnectionPhase == "Degraded");

        Assert.Equal(EngineHostStatus.Degraded, status.Status);
        Assert.False(status.StartupRecoveryCompleted);
        Assert.False(status.TorrentProcessingAvailable);
        Assert.Equal("DirectIsp", status.VpnConnectionReason);

        using var response = await client.PostAsJsonAsync(
            "api/torrents",
            new AddMagnetRequest
            {
                MagnetUri = "magnet:?xt=urn:btih:0123456789ABCDEF0123456789ABCDEF01234567&dn=Queued%20While%20Paused",
            }
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var added = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();
        Assert.NotNull(added);
        Assert.Equal(TorrentState.Queued, added.State);
    }

    [Fact]
    public async Task StartupSuccess_RecoversAndOpensTorrentProcessing()
    {
        var handler = CreateHandler(VpnEgressHttpScenarios.VpnIpv4, repeat: 4);
        await using var factory = CreateFactory(handler);
        using var client = factory.CreateClient();

        var status = await WaitForHostAsync(client, value => value.VpnConnectionPhase == "Ready");

        Assert.Equal(EngineHostStatus.Ready, status.Status);
        Assert.True(status.StartupRecoveryCompleted);
        Assert.True(status.TorrentProcessingAvailable);
        Assert.Null(status.VpnConnectionReason);
    }

    [Fact]
    public async Task DegradedCheckSuccess_AutomaticallyRecoversWithoutOperatorAction()
    {
        var handler = new ScriptedHttpMessageHandler();
        handler.EnqueueJson($$"""{"ip":"{{VpnEgressHttpScenarios.DirectIspIpv4}}"}""");
        handler.EnqueueJson($$"""{"ip":"{{VpnEgressHttpScenarios.VpnIpv4}}"}""");
        handler.EnqueueJson($$"""{"ip":"{{VpnEgressHttpScenarios.VpnIpv4}}"}""");
        await using var factory = CreateFactory(handler);
        using var client = factory.CreateClient();

        await WaitForHostAsync(client, value => value.VpnConnectionPhase == "Degraded");
        var recovered = await WaitForHostAsync(
            client,
            value => value.VpnConnectionPhase == "Ready" && value.StartupRecoveryCompleted,
            timeout: TimeSpan.FromSeconds(8)
        );

        Assert.True(recovered.TorrentProcessingAvailable);
        Assert.True(handler.Requests.Count >= 2);
    }

    [Fact]
    public async Task RoutineCheck_DoesNotReportDegradedWhileCheckIsRunning()
    {
        var handler = new ScriptedHttpMessageHandler();
        handler.EnqueueJson($$"""{"ip":"{{VpnEgressHttpScenarios.VpnIpv4}}"}""");
        handler.EnqueueWaitForCancellation();
        await using var factory = CreateFactory(handler);
        using var client = factory.CreateClient();

        await WaitForHostAsync(client, value => value.VpnConnectionPhase == "Ready");
        await WaitUntilAsync(() => handler.Requests.Count >= 2, TimeSpan.FromSeconds(7));
        var whileChecking = await client.GetFromJsonAsync<EngineHostStatusDto>("api/host/status");

        Assert.NotNull(whileChecking);
        Assert.Equal(EngineHostStatus.Ready, whileChecking.Status);
        Assert.Equal("Ready", whileChecking.VpnConnectionPhase);
        Assert.True(whileChecking.TorrentProcessingAvailable);
    }

    [Fact]
    public async Task SettingsApi_DisablesImmediatelyAndReenableStartsAnImmediateCheck()
    {
        var handler = CreateHandler(VpnEgressHttpScenarios.DirectIspIpv4, repeat: 6);
        await using var factory = CreateFactory(handler);
        using var client = factory.CreateClient();

        await WaitForHostAsync(client, value => value.VpnConnectionPhase == "Degraded");
        await SetValidationEnabledAsync(client, enabled: false);
        var disabled = await WaitForHostAsync(client, value => value.VpnConnectionPhase == "Disabled");
        Assert.True(disabled.TorrentProcessingAvailable);
        Assert.True(disabled.StartupRecoveryCompleted);

        var requestCountBeforeEnable = handler.Requests.Count;
        await SetValidationEnabledAsync(client, enabled: true);
        await WaitUntilAsync(() => handler.Requests.Count > requestCountBeforeEnable, TimeSpan.FromSeconds(1));
        var degraded = await WaitForHostAsync(client, value => value.VpnConnectionPhase == "Degraded");
        Assert.False(degraded.TorrentProcessingAvailable);
    }

    [Fact]
    public async Task ActivationFailure_RetriesEngineDirectlyWithoutAnotherVpnCheck()
    {
        var handler = CreateHandler(VpnEgressHttpScenarios.VpnIpv4, repeat: 2);
        var lifecycle = new FailFirstActivationLifecycle();
        await using var factory = CreateFactory(handler, services =>
        {
            services.RemoveAll<IMonoTorrentLifecycle>();
            services.AddSingleton<IMonoTorrentLifecycle>(lifecycle);
        });
        using var client = factory.CreateClient();

        await WaitForHostAsync(
            client,
            value => value.VpnConnectionPhase == "Degraded" &&
                     value.VpnConnectionReason == "EngineActivationFailed"
        );
        var ready = await WaitForHostAsync(
            client,
            value => value.VpnConnectionPhase == "Ready",
            timeout: TimeSpan.FromSeconds(8)
        );

        Assert.True(ready.TorrentProcessingAvailable);
        Assert.Equal(2, lifecycle.ActivationCount);
        Assert.Single(handler.Requests);
    }

    private static ScriptedHttpMessageHandler CreateHandler(string publicAddress, int repeat)
    {
        var handler = new ScriptedHttpMessageHandler();
        for (var index = 0; index < repeat; index++)
        {
            handler.EnqueueJson($$"""{"ip":"{{publicAddress}}"}""");
        }
        return handler;
    }

    private static async Task SetValidationEnabledAsync(HttpClient client, bool enabled)
    {
        var settingsJson = await client.GetStringAsync("api/host/runtime-settings");
        var request = JsonNode.Parse(settingsJson)?.AsObject()
            ?? throw new Xunit.Sdk.XunitException("Runtime settings response was not a JSON object.");
        request["vpnEgressValidationEnabled"] = enabled;
        using var response = await client.PutAsJsonAsync("api/host/runtime-settings", request);
        response.EnsureSuccessStatusCode();
    }

    private static WebApplicationFactory<Program> CreateFactory(
        ScriptedHttpMessageHandler handler,
        Action<IServiceCollection>? configureServices = null,
        int requestTimeoutSeconds = 1,
        int checkIntervalSeconds = 2)
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"torrentcore-vpn-coordinator-{Guid.NewGuid():N}");
        var portOffset = Random.Shared.Next(0, 5_000);
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{TorrentCoreServiceOptions.SectionName}:EngineMode"] = TorrentEngineMode.MonoTorrent.ToString(),
                    [$"{TorrentCoreServiceOptions.SectionName}:DownloadRootPath"] = Path.Combine(rootPath, "downloads"),
                    [$"{TorrentCoreServiceOptions.SectionName}:StorageRootPath"] = Path.Combine(rootPath, "storage"),
                    [$"{TorrentCoreServiceOptions.SectionName}:EngineListenPort"] = (40_000 + portOffset).ToString(),
                    [$"{TorrentCoreServiceOptions.SectionName}:EngineDhtPort"] = (50_000 + portOffset).ToString(),
                    [$"{TorrentCoreServiceOptions.SectionName}:EngineAllowPortForwarding"] = bool.FalseString,
                    [$"{TorrentCoreServiceOptions.SectionName}:EngineAllowLocalPeerDiscovery"] = bool.FalseString,
                    [$"{TorrentCoreServiceOptions.SectionName}:VpnEgressValidationEnabled"] = bool.TrueString,
                    [$"{TorrentCoreServiceOptions.SectionName}:VpnEgressValidationEndpoint"] = "https://vpn-check.example.test/ip",
                    [$"{TorrentCoreServiceOptions.SectionName}:VpnEgressDirectIspCidrs:0"] = "198.51.100.0/24",
                    [$"{TorrentCoreServiceOptions.SectionName}:VpnEgressDegradedCheckIntervalSeconds"] = checkIntervalSeconds.ToString(),
                    [$"{TorrentCoreServiceOptions.SectionName}:VpnEgressReadyCheckIntervalSeconds"] = checkIntervalSeconds.ToString(),
                    [$"{TorrentCoreServiceOptions.SectionName}:VpnEgressRequestTimeoutSeconds"] = requestTimeoutSeconds.ToString(),
                    [$"{TorrentCoreServiceOptions.SectionName}:VpnEgressEngineSuspensionTimeoutSeconds"] = "2",
                });
            });
            builder.ConfigureServices(services =>
            {
                services.AddHttpClient(VpnEgressProbe.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => handler);
                configureServices?.Invoke(services);
            });
        });
    }

    private static async Task<EngineHostStatusDto> WaitForHostAsync(
        HttpClient client,
        Func<EngineHostStatusDto, bool> predicate,
        TimeSpan? timeout = null)
    {
        EngineHostStatusDto? last = null;
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTimeOffset.UtcNow < deadline)
        {
            last = await client.GetFromJsonAsync<EngineHostStatusDto>("api/host/status");
            if (last is not null && predicate(last))
            {
                return last;
            }
            await Task.Delay(50);
        }
        throw new Xunit.Sdk.XunitException(
            $"Timed out waiting for VPN host state. Last phase: {last?.VpnConnectionPhase ?? "(none)"}."
        );
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }
            await Task.Delay(25);
        }
        throw new Xunit.Sdk.XunitException("Timed out waiting for the scheduled VPN check.");
    }

    private sealed class FailFirstActivationLifecycle : IMonoTorrentLifecycle
    {
        private int _activationCount;
        public int ActivationCount => Volatile.Read(ref _activationCount);

        public Task<TorrentEngineRecoveryResult> ActivateAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _activationCount) == 1)
            {
                throw new InvalidOperationException("Scripted first activation failure.");
            }

            return Task.FromResult(new TorrentEngineRecoveryResult
            {
                RecoveredTorrentCount = 0,
                NormalizedTorrentCount = 0,
                CompletedAtUtc = DateTimeOffset.UtcNow,
                Changes = [],
            });
        }

        public Task<MonoTorrentSuspensionResult> SuspendAsync(
            MonoTorrentSuspensionReason reason,
            CancellationToken cancellationToken)
            => Task.FromResult(new MonoTorrentSuspensionResult(false, true, [], DateTimeOffset.UtcNow));
    }
}
