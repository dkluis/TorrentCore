using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TorrentCore.Avalonia.ViewModels;
using TorrentCore.Client;
using TorrentCore.Contracts.Host;
using TorrentCore.Contracts.Torrents;

namespace TorrentCore.Service.Tests;

public sealed class AvaloniaDashboardViewModelTests
{
    [Fact]
    public async Task LoadAsync_AggregatesCompletionCallbackCounts()
    {
        var hostStatus = new EngineHostStatusDto
        {
            Status = EngineHostStatus.Ready,
            ServiceName = "TorrentCore.Service",
            ServiceVersion = "1.0.0",
            EnvironmentName = "Production",
            ServiceInstanceId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            CheckedAtUtc = DateTimeOffset.UtcNow,
            EngineRuntime = "MonoTorrent",
            EngineListenPort = 51413,
            EngineDhtPort = 51413,
            EnginePortForwardingEnabled = true,
            EngineLocalPeerDiscoveryEnabled = true,
            EngineMaximumConnections = 300,
            EngineMaximumHalfOpenConnections = 50,
            EngineConnectionFailureLogBurstLimit = 10,
            EngineConnectionFailureLogWindowSeconds = 60,
            CurrentConnectedPeerCount = 12,
            CurrentDownloadRateBytesPerSecond = 4_000_000,
            CurrentUploadRateBytesPerSecond = 300_000,
            EngineMaximumDownloadRateBytesPerSecond = 0,
            EngineMaximumUploadRateBytesPerSecond = 0,
            TorrentCount = 5,
            MaxActiveMetadataResolutions = 2,
            MaxActiveDownloads = 4,
            AvailableMetadataResolutionSlots = 1,
            AvailableDownloadSlots = 2,
            ResolvingMetadataCount = 1,
            MetadataQueueCount = 0,
            DownloadingCount = 2,
            DownloadQueueCount = 1,
            SeedingCount = 1,
            PausedCount = 0,
            CompletedCount = 1,
            ErrorCount = 1,
            PartialFilesEnabled = true,
            PartialFileSuffix = ".!mt",
            SeedingStopMode = "Ratio",
            SeedingStopRatio = 1.5,
            SeedingStopMinutes = 120,
            CompletedTorrentCleanupMode = "Age",
            CompletedTorrentCleanupMinutes = 60,
            SupportsPersistentStorage = true,
            SupportsMultiHost = false,
            SupportsMagnetAdds = true,
            SupportsPause = true,
            SupportsResume = true,
            SupportsRemove = true,
            StartupRecoveryCompleted = true,
            StartupRecoveredTorrentCount = 2,
            StartupNormalizedTorrentCount = 1,
            StartupRecoveryCompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-3),
            DownloadRootPath = "/Volumes/Media/Incoming",
        };

        var torrents = new[]
        {
            CreateTorrent("PendingFinalization"),
            CreateTorrent("Invoked"),
            CreateTorrent("Failed"),
            CreateTorrent("TimedOut"),
            CreateTorrent(null),
        };

        var client = CreateClient((request, _) => Task.FromResult(CreateJsonResponse(
            request.RequestUri!.AbsolutePath switch
            {
                "/api/host/status" => hostStatus,
                "/api/torrents" => torrents,
                _ => throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}"),
            })));

        var viewModel = new DashboardViewModel(client);
        await viewModel.LoadAsync();

        Assert.False(viewModel.HasError);
        Assert.True(viewModel.HasHostStatus);
        Assert.True(viewModel.HasLastRefreshed);
        Assert.Equal(1, viewModel.CallbackPendingCount);
        Assert.Equal(1, viewModel.CallbackInvokedCount);
        Assert.Equal(1, viewModel.CallbackFailedCount);
        Assert.Equal(1, viewModel.CallbackTimedOutCount);
        Assert.Equal(2, viewModel.CallbackRetryableCount);
    }

    private static TorrentSummaryDto CreateTorrent(string? completionCallbackState)
    {
        return new TorrentSummaryDto
        {
            TorrentId = Guid.NewGuid(),
            Name = $"Torrent-{completionCallbackState ?? "None"}",
            CategoryKey = "TV",
            State = TorrentState.Completed,
            ProgressPercent = 100,
            DownloadedBytes = 1_000_000_000,
            TotalBytes = 1_000_000_000,
            DownloadRateBytesPerSecond = 0,
            UploadRateBytesPerSecond = 0,
            TrackerCount = 1,
            ConnectedPeerCount = 0,
            AddedAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
            CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            LastActivityAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletionCallbackState = completionCallbackState,
            CompletionCallbackPendingSinceUtc = null,
            CompletionCallbackInvokedAtUtc = null,
            CompletionCallbackLastError = null,
            ErrorMessage = null,
            CanRefreshMetadata = false,
            CanRetryCompletionCallback = string.Equals(completionCallbackState, "Failed", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(completionCallbackState, "TimedOut", StringComparison.OrdinalIgnoreCase),
            CanPause = false,
            CanResume = false,
            CanRemove = true,
        };
    }

    private static TorrentCoreClient CreateClient(Func<HttpRequestMessage, string?, Task<HttpResponseMessage>> responseFactory)
    {
        var endpointProvider = new MutableTorrentCoreEndpointProvider("http://localhost:7033/");
        var httpClient = new HttpClient(new TestHttpMessageHandler(responseFactory));
        return new TorrentCoreClient(httpClient, endpointProvider);
    }

    private static HttpResponseMessage CreateJsonResponse(object payload)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
    }

    private sealed class TestHttpMessageHandler(Func<HttpRequestMessage, string?, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var response = await responseFactory(request, body);
            response.RequestMessage = request;
            response.Content.Headers.ContentType ??= new MediaTypeHeaderValue("application/json");
            return response;
        }
    }
}
