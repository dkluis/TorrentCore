using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TorrentCore.Avalonia.Infrastructure;
using TorrentCore.Avalonia.ViewModels;
using TorrentCore.Client;
using TorrentCore.Contracts.Categories;
using TorrentCore.Contracts.Torrents;

namespace TorrentCore.Service.Tests;

public sealed class AvaloniaTorrentsViewModelTests
{
    [Fact]
    public async Task LoadAsync_PopulatesCategories_AndDefaultsAddCategoryToTv()
    {
        var categories = CreateCategories();
        var torrents = CreateTorrents();

        var torrentCoreClient = CreateClient((request, _) => Task.FromResult(CreateJsonResponse(
            request.RequestUri!.AbsolutePath switch
            {
                "/api/categories" => categories,
                "/api/torrents" => torrents,
                _ => throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}"),
            })));
        var viewModel = new TorrentsViewModel(torrentCoreClient, _ => { }, new TestClipboardTextService());

        await viewModel.LoadAsync();

        Assert.False(viewModel.HasError);
        Assert.Equal("TV", viewModel.SelectedAddCategory?.Key);
        Assert.Equal(3, viewModel.AddCategoryOptions.Count);
        Assert.Equal(4, viewModel.CategoryFilterOptions.Count);
        Assert.Equal(3, viewModel.VisibleTorrentCount);
    }

    [Fact]
    public async Task Filters_RespectCategory_AndCallbackState()
    {
        var categories = CreateCategories();
        var torrents = CreateTorrents();

        var torrentCoreClient = CreateClient((request, _) => Task.FromResult(CreateJsonResponse(
            request.RequestUri!.AbsolutePath switch
            {
                "/api/categories" => categories,
                "/api/torrents" => torrents,
                _ => throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}"),
            })));
        var viewModel = new TorrentsViewModel(torrentCoreClient, _ => { }, new TestClipboardTextService());
        await viewModel.LoadAsync();

        viewModel.SelectedCategoryFilter = Assert.Single(viewModel.CategoryFilterOptions, item => item.Key == "Movies");
        Assert.Equal(1, viewModel.VisibleTorrentCount);
        Assert.Equal("Movies", Assert.Single(viewModel.VisibleTorrents).CategoryKey);

        viewModel.SelectedCategoryFilter = Assert.Single(viewModel.CategoryFilterOptions, item => item.Key == "__all");
        viewModel.SelectedCallbackState = "Failed";
        Assert.Equal(1, viewModel.VisibleTorrentCount);
        Assert.Equal("Beta Movies", Assert.Single(viewModel.VisibleTorrents).Name);

        viewModel.SelectedCallbackState = "All";
        viewModel.SelectedCategoryFilter = Assert.Single(viewModel.CategoryFilterOptions, item => item.Key == "__uncategorized");
        Assert.Equal(1, viewModel.VisibleTorrentCount);
        Assert.Equal("Gamma Misc", Assert.Single(viewModel.VisibleTorrents).Name);
    }

    [Fact]
    public async Task AddMagnetAsync_SendsSelectedCategoryKey_AndResetsSelectionToTv()
    {
        var categories = CreateCategories();
        var torrents = CreateTorrents();
        var capturedRequests = new ConcurrentQueue<CapturedRequest>();

        var torrentCoreClient = CreateClient(async (request, body) =>
        {
            capturedRequests.Enqueue(new CapturedRequest(request.Method.Method, request.RequestUri!.AbsolutePath, body));

            return request.RequestUri!.AbsolutePath switch
            {
                "/api/categories" => CreateJsonResponse(categories),
                "/api/torrents" when request.Method == HttpMethod.Get => CreateJsonResponse(torrents),
                "/api/torrents" when request.Method == HttpMethod.Post => CreateJsonResponse(new TorrentDetailDto
                {
                    TorrentId = Guid.NewGuid(),
                    Name = "Submitted Torrent",
                    CategoryKey = "Movies",
                    State = TorrentState.Downloading,
                    MagnetUri = "magnet:?xt=urn:btih:submitted",
                    SavePath = "/Volumes/Media/Incoming/Movies/Submitted Torrent",
                    ProgressPercent = 0,
                    DownloadedBytes = 0,
                    TotalBytes = null,
                    DownloadRateBytesPerSecond = 0,
                    UploadRateBytesPerSecond = 0,
                    TrackerCount = 0,
                    ConnectedPeerCount = 0,
                    AddedAtUtc = DateTimeOffset.UtcNow,
                    CompletedAtUtc = null,
                    LastActivityAtUtc = null,
                    CompletionCallbackState = null,
                    CompletionCallbackPendingSinceUtc = null,
                    CompletionCallbackInvokedAtUtc = null,
                    CompletionCallbackFinalPayloadPath = null,
                    CompletionCallbackPendingReason = null,
                    CompletionCallbackLastError = null,
                    ErrorMessage = null,
                    CanRefreshMetadata = false,
                    CanRetryCompletionCallback = false,
                    CanPause = true,
                    CanResume = false,
                    CanRemove = true,
                }),
                _ => throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}"),
            };
        });
        var viewModel = new TorrentsViewModel(torrentCoreClient, _ => { }, new TestClipboardTextService());
        await viewModel.LoadAsync();

        viewModel.SelectedAddCategory = Assert.Single(viewModel.AddCategoryOptions, item => item.Key == "Movies");
        viewModel.MagnetUri = "magnet:?xt=urn:btih:submitted";

        await viewModel.AddMagnetAsync();

        var addRequest = capturedRequests.Single(item => item.Method == "POST" && item.Path == "/api/torrents");
        using var addJson = JsonDocument.Parse(addRequest.Body!);
        Assert.Equal("Movies", addJson.RootElement.GetProperty("categoryKey").GetString());
        Assert.Equal("TV", viewModel.SelectedAddCategory?.Key);
        Assert.Contains("Added torrent 'Submitted Torrent'", viewModel.SubmitMessage);
    }

    [Fact]
    public async Task RetryCompletionCallbackAsync_PostsRetryEndpoint()
    {
        var categories = CreateCategories();
        var torrents = CreateTorrents();
        var capturedRequests = new ConcurrentQueue<CapturedRequest>();

        var retryableTorrent = torrents.Single(item => item.CanRetryCompletionCallback);

        var torrentCoreClient = CreateClient(async (request, body) =>
        {
            capturedRequests.Enqueue(new CapturedRequest(request.Method.Method, request.RequestUri!.AbsolutePath, body));

            return request.RequestUri!.AbsolutePath switch
            {
                "/api/categories" => CreateJsonResponse(categories),
                "/api/torrents" when request.Method == HttpMethod.Get => CreateJsonResponse(torrents),
                var path when path == $"/api/torrents/{retryableTorrent.TorrentId:D}/completion-callback/retry" =>
                    CreateJsonResponse(new TorrentActionResultDto
                    {
                        TorrentId = retryableTorrent.TorrentId,
                        Action = "retry_completion_callback",
                        State = TorrentState.Completed,
                        ProcessedAtUtc = DateTimeOffset.UtcNow,
                    }),
                _ => throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}"),
            };
        });
        var viewModel = new TorrentsViewModel(torrentCoreClient, _ => { }, new TestClipboardTextService());
        await viewModel.LoadAsync();

        var torrent = Assert.Single(viewModel.VisibleTorrents, item => item.TorrentId == retryableTorrent.TorrentId);
        await viewModel.RetryCompletionCallbackAsync(torrent);

        Assert.Contains(
            capturedRequests,
            item => item.Method == "POST" && item.Path == $"/api/torrents/{retryableTorrent.TorrentId:D}/completion-callback/retry");
        Assert.Contains("Queued completion callback retry", viewModel.ActionMessage);
    }

    [Fact]
    public async Task RefreshMetadataAsync_PostsRefreshEndpoint()
    {
        var categories = CreateCategories();
        var refreshableTorrent = new TorrentSummaryDto
        {
            TorrentId = Guid.Parse("44444444-4444-4444-4444-444444444444"),
            Name = "Refreshable Magnet",
            CategoryKey = "TV",
            State = TorrentState.ResolvingMetadata,
            ProgressPercent = 0,
            DownloadedBytes = 0,
            TotalBytes = null,
            DownloadRateBytesPerSecond = 0,
            UploadRateBytesPerSecond = 0,
            TrackerCount = 2,
            ConnectedPeerCount = 0,
            WaitReason = null,
            QueuePosition = null,
            AddedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            CompletedAtUtc = null,
            LastActivityAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletionCallbackState = null,
            CompletionCallbackPendingSinceUtc = null,
            CompletionCallbackInvokedAtUtc = null,
            CompletionCallbackLastError = null,
            ErrorMessage = null,
            CanRefreshMetadata = true,
            CanRetryCompletionCallback = false,
            CanPause = true,
            CanResume = false,
            CanRemove = true,
        };
        var torrents = new[] { refreshableTorrent };
        var capturedRequests = new ConcurrentQueue<CapturedRequest>();

        var torrentCoreClient = CreateClient(async (request, body) =>
        {
            capturedRequests.Enqueue(new CapturedRequest(request.Method.Method, request.RequestUri!.AbsolutePath, body));

            return request.RequestUri!.AbsolutePath switch
            {
                "/api/categories" => CreateJsonResponse(categories),
                "/api/torrents" when request.Method == HttpMethod.Get => CreateJsonResponse(torrents),
                var path when path == $"/api/torrents/{refreshableTorrent.TorrentId:D}/metadata/refresh" =>
                    CreateJsonResponse(new TorrentActionResultDto
                    {
                        TorrentId = refreshableTorrent.TorrentId,
                        Action = "refresh_metadata",
                        State = TorrentState.ResolvingMetadata,
                        ProcessedAtUtc = DateTimeOffset.UtcNow,
                    }),
                _ => throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}"),
            };
        });
        var viewModel = new TorrentsViewModel(torrentCoreClient, _ => { }, new TestClipboardTextService());
        await viewModel.LoadAsync();

        var torrent = Assert.Single(viewModel.VisibleTorrents, item => item.TorrentId == refreshableTorrent.TorrentId);
        await viewModel.RefreshMetadataAsync(torrent);

        Assert.Contains(
            capturedRequests,
            item => item.Method == "POST" && item.Path == $"/api/torrents/{refreshableTorrent.TorrentId:D}/metadata/refresh");
        Assert.Contains("Requested metadata refresh", viewModel.ActionMessage);
    }

    [Fact]
    public async Task ResetMetadataSessionAsync_PostsResetEndpoint()
    {
        var categories = CreateCategories();
        var resettableTorrent = new TorrentSummaryDto
        {
            TorrentId = Guid.Parse("45454545-4545-4545-4545-454545454545"),
            Name = "Resettable Magnet",
            CategoryKey = "TV",
            State = TorrentState.ResolvingMetadata,
            ProgressPercent = 0,
            DownloadedBytes = 0,
            TotalBytes = null,
            DownloadRateBytesPerSecond = 0,
            UploadRateBytesPerSecond = 0,
            TrackerCount = 2,
            ConnectedPeerCount = 0,
            WaitReason = null,
            QueuePosition = null,
            AddedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            CompletedAtUtc = null,
            LastActivityAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletionCallbackState = null,
            CompletionCallbackPendingSinceUtc = null,
            CompletionCallbackInvokedAtUtc = null,
            CompletionCallbackLastError = null,
            ErrorMessage = null,
            CanRefreshMetadata = true,
            CanRetryCompletionCallback = false,
            CanPause = true,
            CanResume = false,
            CanRemove = true,
        };
        var torrents = new[] { resettableTorrent };
        var capturedRequests = new ConcurrentQueue<CapturedRequest>();

        var torrentCoreClient = CreateClient(async (request, body) =>
        {
            capturedRequests.Enqueue(new CapturedRequest(request.Method.Method, request.RequestUri!.AbsolutePath, body));

            return request.RequestUri!.AbsolutePath switch
            {
                "/api/categories" => CreateJsonResponse(categories),
                "/api/torrents" when request.Method == HttpMethod.Get => CreateJsonResponse(torrents),
                var path when path == $"/api/torrents/{resettableTorrent.TorrentId:D}/metadata/reset" =>
                    CreateJsonResponse(new TorrentActionResultDto
                    {
                        TorrentId = resettableTorrent.TorrentId,
                        Action = "reset_metadata_session",
                        State = TorrentState.ResolvingMetadata,
                        ProcessedAtUtc = DateTimeOffset.UtcNow,
                    }),
                _ => throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}"),
            };
        });
        var viewModel = new TorrentsViewModel(torrentCoreClient, _ => { }, new TestClipboardTextService());
        await viewModel.LoadAsync();

        var torrent = Assert.Single(viewModel.VisibleTorrents, item => item.TorrentId == resettableTorrent.TorrentId);
        await viewModel.ResetMetadataSessionAsync(torrent);

        Assert.Contains(
            capturedRequests,
            item => item.Method == "POST" && item.Path == $"/api/torrents/{resettableTorrent.TorrentId:D}/metadata/reset");
        Assert.Contains("Recreated metadata session", viewModel.ActionMessage);
    }

    [Fact]
    public async Task PasteMagnetAsync_LoadsClipboardText_AndTrimsIt()
    {
        var torrentCoreClient = CreateClient((request, _) => Task.FromResult(CreateJsonResponse(Array.Empty<TorrentSummaryDto>())));
        var viewModel = new TorrentsViewModel(
            torrentCoreClient,
            _ => { },
            new TestClipboardTextService("  magnet:?xt=urn:btih:pasted-value  "));

        await viewModel.PasteMagnetAsync();

        Assert.Equal("magnet:?xt=urn:btih:pasted-value", viewModel.MagnetUri);
        Assert.Equal("Pasted magnet text from the clipboard.", viewModel.SubmitMessage);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task PasteMagnetAsync_ReportsEmptyClipboard()
    {
        var torrentCoreClient = CreateClient((request, _) => Task.FromResult(CreateJsonResponse(Array.Empty<TorrentSummaryDto>())));
        var viewModel = new TorrentsViewModel(
            torrentCoreClient,
            _ => { },
            new TestClipboardTextService("   "));

        await viewModel.PasteMagnetAsync();

        Assert.Equal(string.Empty, viewModel.MagnetUri);
        Assert.Equal("Clipboard does not contain text to paste.", viewModel.SubmitMessage);
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
            new TorrentCategoryDto
            {
                Key = "Movies",
                DisplayName = "Movies",
                CallbackLabel = "movies",
                DownloadRootPath = "/Volumes/Media/Movies",
                Enabled = true,
                InvokeCompletionCallback = false,
                SortOrder = 20,
            },
        ];
    }

    private static IReadOnlyList<TorrentSummaryDto> CreateTorrents()
    {
        return
        [
            new TorrentSummaryDto
            {
                TorrentId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Name = "Alpha Show",
                CategoryKey = "TV",
                State = TorrentState.Downloading,
                ProgressPercent = 42.5,
                DownloadedBytes = 425_000_000,
                TotalBytes = 1_000_000_000,
                DownloadRateBytesPerSecond = 3_000_000,
                UploadRateBytesPerSecond = 250_000,
                TrackerCount = 3,
                ConnectedPeerCount = 21,
                WaitReason = TorrentWaitReason.PendingDownloadDispatch,
                QueuePosition = 1,
                AddedAtUtc = DateTimeOffset.UtcNow.AddHours(-2),
                CompletedAtUtc = null,
                LastActivityAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                CompletionCallbackState = "PendingFinalization",
                CompletionCallbackPendingSinceUtc = DateTimeOffset.UtcNow.AddMinutes(-3),
                CompletionCallbackInvokedAtUtc = null,
                CompletionCallbackLastError = null,
                ErrorMessage = null,
                CanRefreshMetadata = false,
                CanRetryCompletionCallback = false,
                CanPause = true,
                CanResume = false,
                CanRemove = true,
            },
            new TorrentSummaryDto
            {
                TorrentId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Name = "Beta Movies",
                CategoryKey = "Movies",
                State = TorrentState.Completed,
                ProgressPercent = 100,
                DownloadedBytes = 2_000_000_000,
                TotalBytes = 2_000_000_000,
                DownloadRateBytesPerSecond = 0,
                UploadRateBytesPerSecond = 0,
                TrackerCount = 2,
                ConnectedPeerCount = 0,
                WaitReason = null,
                QueuePosition = null,
                AddedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
                CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                LastActivityAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                CompletionCallbackState = "Failed",
                CompletionCallbackPendingSinceUtc = DateTimeOffset.UtcNow.AddMinutes(-15),
                CompletionCallbackInvokedAtUtc = null,
                CompletionCallbackLastError = "Launcher exited with code 1",
                ErrorMessage = null,
                CanRefreshMetadata = false,
                CanRetryCompletionCallback = true,
                CanPause = false,
                CanResume = false,
                CanRemove = true,
            },
            new TorrentSummaryDto
            {
                TorrentId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Name = "Gamma Misc",
                CategoryKey = null,
                State = TorrentState.Seeding,
                ProgressPercent = 100,
                DownloadedBytes = 900_000_000,
                TotalBytes = 900_000_000,
                DownloadRateBytesPerSecond = 0,
                UploadRateBytesPerSecond = 125_000,
                TrackerCount = 1,
                ConnectedPeerCount = 4,
                WaitReason = null,
                QueuePosition = null,
                AddedAtUtc = DateTimeOffset.UtcNow.AddDays(-2),
                CompletedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
                LastActivityAtUtc = DateTimeOffset.UtcNow.AddMinutes(-30),
                CompletionCallbackState = null,
                CompletionCallbackPendingSinceUtc = null,
                CompletionCallbackInvokedAtUtc = null,
                CompletionCallbackLastError = null,
                ErrorMessage = null,
                CanRefreshMetadata = false,
                CanRetryCompletionCallback = false,
                CanPause = true,
                CanResume = false,
                CanRemove = true,
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

    private sealed class TestClipboardTextService(string? text = null) : IClipboardTextService
    {
        public Task<string?> GetTextAsync(CancellationToken cancellationToken = default) => Task.FromResult(text);
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
