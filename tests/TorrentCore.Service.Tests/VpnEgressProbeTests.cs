using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Infrastructure;
using TorrentCore.Service.Tests.Fixtures;
using TorrentCore.Service.Vpn;

namespace TorrentCore.Service.Tests;

public sealed class VpnEgressProbeTests
{
    private static readonly DateTimeOffset InitialUtcNow =
        new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ValidateAsync_ReturnsValidatedEgress_WhenAddressMatchesNoDirectIspCidr()
    {
        var handler = VpnEgressHttpScenarios.Create(VpnEgressHttpScenario.VpnSuccess);
        var activityLog = new RecordingActivityLogService();
        var probe = CreateProbe(handler, activityLog);

        var result = await probe.ValidateAsync(CreateSettings(), CancellationToken.None);

        Assert.Equal(VpnEgressValidationOutcome.ValidatedEgress, result.Outcome);
        Assert.True(result.IsValidated);
        Assert.Equal(IPAddress.Parse(VpnEgressHttpScenarios.VpnIpv4), result.ObservedAddress);
        Assert.Null(result.EndpointFailureReason);
        var write = Assert.Single(activityLog.Writes);
        Assert.Equal(ActivityLogLevel.Information, write.Level);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsDirectIsp_WhenAddressMatchesEitherConfiguredCidr()
    {
        var handler = VpnEgressHttpScenarios.Create(VpnEgressHttpScenario.DirectIsp);
        var probe = CreateProbe(handler);
        var settings = CreateSettings(
            directIspCidrs: ["192.0.2.0/24", "198.51.100.0/24"]
        );

        var result = await probe.ValidateAsync(settings, CancellationToken.None);

        Assert.Equal(VpnEgressValidationOutcome.DirectIsp, result.Outcome);
        Assert.False(result.IsValidated);
        Assert.Equal(IPAddress.Parse(VpnEgressHttpScenarios.DirectIspIpv4), result.ObservedAddress);
    }

    [Theory]
    [InlineData(VpnEgressHttpScenario.Ipv6, VpnEgressHttpScenarios.PublicIpv6)]
    [InlineData(VpnEgressHttpScenario.MalformedJson, null)]
    public async Task ValidateAsync_ReturnsInvalidResponse_ForRejectedPayloads(
        VpnEgressHttpScenario scenario,
        string? expectedObservedAddress)
    {
        var handler = VpnEgressHttpScenarios.Create(scenario);
        var probe = CreateProbe(handler);

        var result = await probe.ValidateAsync(CreateSettings(), CancellationToken.None);

        Assert.Equal(VpnEgressValidationOutcome.InvalidResponse, result.Outcome);
        Assert.Equal(
            expectedObservedAddress is null ? null : IPAddress.Parse(expectedObservedAddress),
            result.ObservedAddress
        );
    }

    [Fact]
    public async Task ValidateAsync_RejectsResponseLargerThanSixteenKibibytes()
    {
        var handler = new ScriptedHttpMessageHandler();
        handler.EnqueueJson($$"""{"ip":"{{new string('1', VpnEgressProbe.MaximumResponseBytes)}}"}""");
        var probe = CreateProbe(handler);

        var result = await probe.ValidateAsync(CreateSettings(), CancellationToken.None);

        Assert.Equal(VpnEgressValidationOutcome.InvalidResponse, result.Outcome);
        Assert.Contains("16 KiB", result.FailureSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_ClassifiesNonSuccessStatusAsEndpointFailure()
    {
        var handler = VpnEgressHttpScenarios.Create(VpnEgressHttpScenario.EndpointFailure);
        var probe = CreateProbe(handler);

        var result = await probe.ValidateAsync(CreateSettings(), CancellationToken.None);

        Assert.Equal(VpnEgressValidationOutcome.EndpointFailure, result.Outcome);
        Assert.Equal(VpnEgressEndpointFailureReason.HttpStatus, result.EndpointFailureReason);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.HttpStatusCode);
    }

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError, "Dns")]
    [InlineData(HttpRequestError.ConnectionError, "Connection")]
    [InlineData(HttpRequestError.SecureConnectionError, "Tls")]
    [InlineData(HttpRequestError.HttpProtocolError, "HttpProtocol")]
    [InlineData(HttpRequestError.ResponseEnded, "HttpProtocol")]
    [InlineData(HttpRequestError.Unknown, "OtherHttp")]
    public async Task ValidateAsync_PreservesStableHttpFailureReason(
        HttpRequestError requestError,
        string expectedReason)
    {
        var handler = new ScriptedHttpMessageHandler();
        handler.EnqueueException(new HttpRequestException(requestError, "scripted", null, null));
        var probe = CreateProbe(handler);

        var result = await probe.ValidateAsync(CreateSettings(), CancellationToken.None);

        Assert.Equal(VpnEgressValidationOutcome.EndpointFailure, result.Outcome);
        Assert.Equal(expectedReason, result.EndpointFailureReason?.ToString());
        Assert.DoesNotContain("scripted", result.FailureSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_UsesConfiguredTimeoutWithoutWallClockDelay()
    {
        var handler = new ScriptedHttpMessageHandler();
        handler.EnqueueWaitForCancellation();
        var clock = new ManualTimeProvider(InitialUtcNow);
        var probe = CreateProbe(handler, timeProvider: clock);

        var validation = probe.ValidateAsync(
            CreateSettings(requestTimeoutSeconds: 10),
            CancellationToken.None
        );
        Assert.Single(handler.Requests);

        clock.Advance(TimeSpan.FromSeconds(10));
        var result = await validation;

        Assert.Equal(VpnEgressValidationOutcome.TimedOut, result.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(10), result.Duration);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsCancelledWithoutLogging_WhenCallerCancels()
    {
        var handler = new ScriptedHttpMessageHandler();
        handler.EnqueueWaitForCancellation();
        var activityLog = new RecordingActivityLogService();
        var probe = CreateProbe(handler, activityLog);
        using var cancellationSource = new CancellationTokenSource();

        var validation = probe.ValidateAsync(CreateSettings(), cancellationSource.Token);
        cancellationSource.Cancel();
        var result = await validation;

        Assert.Equal(VpnEgressValidationOutcome.Cancelled, result.Outcome);
        Assert.Empty(activityLog.Writes);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsUnexpectedFailure_ForNonHttpException()
    {
        var handler = new ScriptedHttpMessageHandler();
        handler.EnqueueException(new InvalidOperationException("sensitive scripted text"));
        var probe = CreateProbe(handler);

        var result = await probe.ValidateAsync(CreateSettings(), CancellationToken.None);

        Assert.Equal(VpnEgressValidationOutcome.UnexpectedFailure, result.Outcome);
        Assert.Contains(nameof(InvalidOperationException), result.FailureSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive scripted text", result.FailureSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_SuppressesRepeatedOutcomeEvenWhenObservedAddressChanges()
    {
        var handler = new ScriptedHttpMessageHandler();
        handler.EnqueueJson("{\"ip\":\"203.0.113.10\"}");
        handler.EnqueueJson("{\"ip\":\"203.0.113.11\"}");
        var activityLog = new RecordingActivityLogService();
        var probe = CreateProbe(handler, activityLog);

        await probe.ValidateAsync(CreateSettings(), CancellationToken.None);
        await probe.ValidateAsync(CreateSettings(), CancellationToken.None);

        Assert.Single(activityLog.Writes);
    }

    [Fact]
    public async Task ValidateAsync_LogsWhenEndpointFailureReasonChanges()
    {
        var handler = new ScriptedHttpMessageHandler();
        handler.EnqueueException(CreateHttpFailure(HttpRequestError.NameResolutionError));
        handler.EnqueueException(CreateHttpFailure(HttpRequestError.NameResolutionError));
        handler.EnqueueException(CreateHttpFailure(HttpRequestError.ConnectionError));
        handler.EnqueueException(CreateHttpFailure(HttpRequestError.ConnectionError));
        var activityLog = new RecordingActivityLogService();
        var probe = CreateProbe(handler, activityLog);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var result = await probe.ValidateAsync(CreateSettings(), CancellationToken.None);
            Assert.Equal(VpnEgressValidationOutcome.EndpointFailure, result.Outcome);
        }

        Assert.Equal(2, activityLog.Writes.Count);
        Assert.Equal(
            ["Dns", "Connection"],
            activityLog.Writes.Select(GetLoggedFailureReason).ToArray()
        );
    }

    [Fact]
    public async Task ValidateAsync_LogsFullAddressAndSanitizedEndpointAuthority()
    {
        const string endpoint = "https://user:password@egress.test.example:8443/ip?token=secret";
        var handler = VpnEgressHttpScenarios.Create(VpnEgressHttpScenario.VpnSuccess);
        var activityLog = new RecordingActivityLogService();
        var probe = CreateProbe(handler, activityLog);

        await probe.ValidateAsync(CreateSettings(endpoint: endpoint), CancellationToken.None);

        var write = Assert.Single(activityLog.Writes);
        Assert.Equal(VpnEgressActivityEvents.ValidationCompleted, write.EventType);
        using var details = JsonDocument.Parse(write.DetailsJson!);
        Assert.Equal(VpnEgressHttpScenarios.VpnIpv4, details.RootElement.GetProperty("ObservedAddress").GetString());
        Assert.Equal(
            "https://egress.test.example:8443",
            details.RootElement.GetProperty("EndpointAuthority").GetString()
        );
        Assert.DoesNotContain("password", write.DetailsJson, StringComparison.Ordinal);
        Assert.DoesNotContain("token", write.DetailsJson, StringComparison.Ordinal);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(endpoint, request.RequestUri?.AbsoluteUri);
    }

    private static HttpRequestException CreateHttpFailure(HttpRequestError error)
        => new(error, "scripted", null, null);

    private static string GetLoggedFailureReason(ActivityLogWriteRequest write)
    {
        using var details = JsonDocument.Parse(write.DetailsJson!);
        return details.RootElement.GetProperty("EndpointFailureReason").GetString()!;
    }

    private static VpnEgressProbe CreateProbe(
        ScriptedHttpMessageHandler handler,
        RecordingActivityLogService? activityLog = null,
        TimeProvider? timeProvider = null)
        => new(
            new TestHttpClientFactory(handler),
            activityLog ?? new RecordingActivityLogService(),
            new ServiceInstanceContext(),
            timeProvider ?? new ManualTimeProvider(InitialUtcNow)
        );

    private static RuntimeSettingsSnapshot CreateSettings(
        string endpoint = "https://egress.test.example/ip",
        IReadOnlyList<string>? directIspCidrs = null,
        int requestTimeoutSeconds = 10)
        => new()
        {
            UsesPersistedOverrides = false,
            PartialFilesEnabled = false,
            PartialFileSuffix = ".partial",
            SeedingStopMode = SeedingStopMode.Unlimited,
            SeedingStopRatio = 1,
            SeedingStopMinutes = 60,
            CompletedTorrentCleanupMode = CompletedTorrentCleanupMode.Never,
            CompletedTorrentCleanupMinutes = 60,
            DeleteLogsForCompletedTorrents = false,
            EngineConnectionFailureLogBurstLimit = 5,
            EngineConnectionFailureLogWindowSeconds = 60,
            EngineMaximumConnections = 150,
            EngineMaximumHalfOpenConnections = 8,
            EngineMaximumDownloadRateBytesPerSecond = 0,
            EngineMaximumUploadRateBytesPerSecond = 0,
            MaxActiveMetadataResolutions = 4,
            MaxActiveDownloads = 4,
            MetadataRefreshStaleSeconds = 90,
            MetadataRefreshRestartDelaySeconds = 30,
            CompletionCallbackEnabled = false,
            CompletionCallbackTimeoutSeconds = 30,
            CompletionCallbackFinalizationTimeoutSeconds = 120,
            VpnEgressValidationEnabled = true,
            VpnEgressValidationEndpoint = endpoint,
            VpnEgressDirectIspCidrs = directIspCidrs ?? ["198.51.100.0/24"],
            VpnEgressDegradedCheckIntervalSeconds = 60,
            VpnEgressReadyCheckIntervalSeconds = 240,
            VpnEgressRequestTimeoutSeconds = requestTimeoutSeconds,
            EngineSettingsRequireRestart = false,
        };

    private sealed class TestHttpClientFactory(ScriptedHttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private sealed class RecordingActivityLogService : IActivityLogService
    {
        private readonly ConcurrentQueue<ActivityLogWriteRequest> _writes = new();

        public IReadOnlyCollection<ActivityLogWriteRequest> Writes => _writes.ToArray();

        public Task EnsureInitializedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteAsync(ActivityLogWriteRequest request, CancellationToken cancellationToken)
        {
            _writes.Enqueue(request);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ActivityLogEntry>> GetRecentAsync(
            ActivityLogQuery query,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ActivityLogEntry>>([]);

        public Task<ActivityLogFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken)
            => Task.FromResult(new ActivityLogFilterOptions { Categories = [], EventTypes = [] });

        public Task<int> DeleteByTorrentIdAsync(Guid torrentId, CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<int> DeleteOrphanedTorrentLogsAsync(CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<int> DeleteInactiveBeforeAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
            => Task.FromResult(0);
    }
}
