using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TorrentCore.Avalonia.ViewModels;
using TorrentCore.Client;
using TorrentCore.Contracts.Categories;
using TorrentCore.Contracts.Diagnostics;
using TorrentCore.Contracts.Torrents;

namespace TorrentCore.Service.Tests;

public sealed class AvaloniaTorrentDetailViewModelTests
{
    [Fact]
    public async Task LoadAsync_PopulatesCategory_AndCallbackDiagnostics()
    {
        var torrentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var detail = CreateTorrentDetail(torrentId);
        var categories = CreateCategories();
        var logs = CreateLogs(torrentId);

        var torrentCoreClient = CreateClient((request, _) => Task.FromResult(CreateJsonResponse(
            request.RequestUri!.AbsolutePath switch
            {
                var path when path == $"/api/torrents/{torrentId:D}" => detail,
                "/api/categories" => categories,
                "/api/logs" => logs,
                _ => throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}"),
            })));
        var viewModel = new TorrentDetailViewModel(torrentCoreClient, torrentId, () => { });

        await viewModel.LoadAsync();

        Assert.False(viewModel.HasError);
        Assert.True(viewModel.HasTorrent);
        Assert.Equal("TV", viewModel.CategoryText);
        Assert.Equal("Failed", viewModel.CompletionCallbackStateText);
        Assert.Equal("/Volumes/Media/Incoming/TV/Example Show", viewModel.CompletionCallbackFinalPayloadPathText);
        Assert.Equal("Waiting for final visible payload", viewModel.CompletionCallbackPendingReasonText);
        Assert.Equal("Callback exited with code 1", viewModel.CompletionCallbackLastErrorText);
        Assert.Equal("Failed", viewModel.LatestCallbackEventText);
        Assert.Equal("Callback failed after launcher exit.", viewModel.LatestCallbackMessageText);
        Assert.Equal("1234", viewModel.LatestCallbackProcessIdText);
        Assert.Equal("1", viewModel.LatestCallbackExitCodeText);
        Assert.Equal("/Users/dick/TVMaze/Scripts/torrentcore-callback.zsh", viewModel.LatestCallbackCommandPathText);
        Assert.Equal("/Users/dick/TVMaze", viewModel.LatestCallbackWorkingDirectoryText);
        Assert.Equal("30 seconds", viewModel.LatestCallbackProcessTimeoutText);
        Assert.Equal("120 seconds", viewModel.LatestCallbackFinalizationWaitText);
        Assert.True(viewModel.CanRetryCompletionCallback);
        Assert.Equal(2, viewModel.Logs.Count);
    }

    [Fact]
    public async Task RetryCompletionCallbackAsync_PostsRetryEndpoint_AndReloads()
    {
        var torrentId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var detail = CreateTorrentDetail(torrentId);
        var categories = CreateCategories();
        var logs = CreateLogs(torrentId);
        var capturedRequests = new ConcurrentQueue<CapturedRequest>();

        var torrentCoreClient = CreateClient(async (request, body) =>
        {
            capturedRequests.Enqueue(new CapturedRequest(request.Method.Method, request.RequestUri!.AbsolutePath, body));

            return request.RequestUri!.AbsolutePath switch
            {
                var path when path == $"/api/torrents/{torrentId:D}" => CreateJsonResponse(detail),
                "/api/categories" => CreateJsonResponse(categories),
                "/api/logs" => CreateJsonResponse(logs),
                var path when path == $"/api/torrents/{torrentId:D}/completion-callback/retry" => CreateJsonResponse(new TorrentActionResultDto
                {
                    TorrentId = torrentId,
                    Action = "retry_completion_callback",
                    State = TorrentState.Completed,
                    ProcessedAtUtc = DateTimeOffset.UtcNow,
                }),
                _ => throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}"),
            };
        });
        var viewModel = new TorrentDetailViewModel(torrentCoreClient, torrentId, () => { });
        await viewModel.LoadAsync();

        await viewModel.RetryCompletionCallbackAsync();

        Assert.Contains(
            capturedRequests,
            item => item.Method == "POST" && item.Path == $"/api/torrents/{torrentId:D}/completion-callback/retry");
        Assert.Contains("Queued completion callback retry", viewModel.ActionMessage);
    }

    private static TorrentDetailDto CreateTorrentDetail(Guid torrentId)
    {
        return new TorrentDetailDto
        {
            TorrentId = torrentId,
            Name = "Example Show",
            CategoryKey = "TV",
            State = TorrentState.Completed,
            MagnetUri = "magnet:?xt=urn:btih:example",
            InfoHash = "ABCDEF1234567890",
            SavePath = "/Volumes/Media/Incoming/TV/Example Show",
            ProgressPercent = 100,
            DownloadedBytes = 1_500_000_000,
            TotalBytes = 1_500_000_000,
            DownloadRateBytesPerSecond = 0,
            UploadRateBytesPerSecond = 0,
            TrackerCount = 3,
            ConnectedPeerCount = 0,
            WaitReason = null,
            QueuePosition = null,
            AddedAtUtc = DateTimeOffset.UtcNow.AddHours(-3),
            CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-12),
            LastActivityAtUtc = DateTimeOffset.UtcNow.AddMinutes(-12),
            CompletionCallbackState = "Failed",
            CompletionCallbackPendingSinceUtc = DateTimeOffset.UtcNow.AddMinutes(-20),
            CompletionCallbackInvokedAtUtc = null,
            CompletionCallbackFinalPayloadPath = "/Volumes/Media/Incoming/TV/Example Show",
            CompletionCallbackPendingReason = "Waiting for final visible payload",
            CompletionCallbackLastError = "Callback exited with code 1",
            ErrorMessage = null,
            CanRetryCompletionCallback = true,
            CanPause = false,
            CanResume = false,
            CanRemove = true,
        };
    }

    private static IReadOnlyList<TorrentCategoryDto> CreateCategories()
    {
        return
        [
            new TorrentCategoryDto
            {
                Key = "TV",
                DisplayName = "TV",
                CallbackLabel = "television",
                DownloadRootPath = "/Volumes/Media/TV",
                Enabled = true,
                InvokeCompletionCallback = true,
                SortOrder = 10,
            },
        ];
    }

    private static IReadOnlyList<ActivityLogEntryDto> CreateLogs(Guid torrentId)
    {
        return
        [
            new ActivityLogEntryDto
            {
                LogEntryId = 1,
                OccurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-15),
                Level = "Information",
                Category = "torrent",
                EventType = "torrent.callback.pending_finalization",
                Message = "Torrent is waiting for final visible payload.",
                TorrentId = torrentId,
                ServiceInstanceId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                TraceId = "trace-1",
                DetailsJson = null,
            },
            new ActivityLogEntryDto
            {
                LogEntryId = 2,
                OccurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-11),
                Level = "Error",
                Category = "torrent",
                EventType = "torrent.callback.failed",
                Message = "Callback failed after launcher exit.",
                TorrentId = torrentId,
                ServiceInstanceId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                TraceId = "trace-2",
                DetailsJson = """
                              {
                                "CommandPath": "/Users/dick/TVMaze/Scripts/torrentcore-callback.zsh",
                                "WorkingDirectory": "/Users/dick/TVMaze",
                                "ProcessId": 1234,
                                "ExitCode": 1,
                                "CompletionCallbackTimeoutSeconds": 30,
                                "CompletionCallbackFinalizationTimeoutSeconds": 120
                              }
                              """,
            },
        ];
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

    private sealed record CapturedRequest(string Method, string Path, string? Body);

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
