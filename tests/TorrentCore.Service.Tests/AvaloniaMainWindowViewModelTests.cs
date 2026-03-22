using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using TorrentCore.Avalonia.Infrastructure;
using TorrentCore.Avalonia.ViewModels;
using TorrentCore.Client;
using TorrentCore.Contracts.Categories;
using TorrentCore.Contracts.Diagnostics;
using TorrentCore.Contracts.Torrents;

namespace TorrentCore.Service.Tests;

public sealed class AvaloniaMainWindowViewModelTests
{
    [Fact]
    public async Task ShowTorrentDetail_LoadsDetail_AndBackReturnsToTorrents()
    {
        var torrentId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        var torrentCoreClient = CreateClient((request, _) => Task.FromResult(CreateJsonResponse(
            request.RequestUri!.AbsolutePath switch
            {
                "/api/torrents" => CreateTorrentSummaries(torrentId),
                var path when path == $"/api/torrents/{torrentId:D}" => CreateTorrentDetail(torrentId),
                "/api/categories" => CreateCategories(),
                "/api/logs" => CreateLogs(torrentId),
                _ => throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}"),
            })));

        var viewModel = CreateMainWindowViewModel(torrentCoreClient);
        viewModel.IsConnectionSetupRequired = false;
        viewModel.SelectedSection = Assert.Single(viewModel.Sections, section => section.Key == "torrents");
        await WaitUntilAsync(
            () => viewModel.CurrentViewModel is TorrentsViewModel torrentsViewModel && torrentsViewModel.VisibleTorrentCount == 1,
            isLoaded => isLoaded);

        InvokeShowTorrentDetail(viewModel, torrentId);

        var detailViewModel = await WaitUntilAsync(
            () => viewModel.CurrentViewModel as TorrentDetailViewModel,
            detailViewModel => detailViewModel?.HasTorrent == true);

        Assert.Equal("Torrent Detail", viewModel.CurrentTitle);
        Assert.Equal("Example Show", detailViewModel!.Torrent?.Name);

        detailViewModel.Back();

        var torrentsAfterBack = await WaitUntilAsync(
            () => viewModel.CurrentViewModel as TorrentsViewModel,
            torrentsViewModel => torrentsViewModel is not null);

        Assert.Equal("Torrents", viewModel.CurrentTitle);
        Assert.Equal(1, torrentsAfterBack!.VisibleTorrentCount);
    }

    private static MainWindowViewModel CreateMainWindowViewModel(TorrentCoreClient client)
    {
        var connectionManager = new AvaloniaServiceConnectionManager(
            new AppConnectionSettingsStore(),
            new MutableTorrentCoreEndpointProvider("http://localhost:7033/"),
            new TorrentCoreClientOptions { BaseUrl = "http://localhost:7033/" });

        return new MainWindowViewModel(client, connectionManager, new TestClipboardTextService());
    }

    private static void InvokeShowTorrentDetail(MainWindowViewModel viewModel, Guid torrentId)
    {
        var method = typeof(MainWindowViewModel).GetMethod("ShowTorrentDetail", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(viewModel, [torrentId]);
    }

    private static async Task<T> WaitUntilAsync<T>(Func<T> valueFactory, Func<T, bool> predicate, int attempts = 20)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var value = valueFactory();
            if (predicate(value))
            {
                return value;
            }

            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException("Condition was not satisfied within the allotted time.");
    }

    private static IReadOnlyList<TorrentSummaryDto> CreateTorrentSummaries(Guid torrentId)
    {
        return
        [
            new TorrentSummaryDto
            {
                TorrentId = torrentId,
                Name = "Example Show",
                CategoryKey = "TV",
                State = TorrentState.Completed,
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
                CompletionCallbackState = "Invoked",
                CompletionCallbackPendingSinceUtc = DateTimeOffset.UtcNow.AddMinutes(-20),
                CompletionCallbackInvokedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-11),
                CompletionCallbackLastError = null,
                ErrorMessage = null,
                CanRefreshMetadata = false,
                CanRetryCompletionCallback = false,
                CanPause = false,
                CanResume = false,
                CanRemove = true,
            },
        ];
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
            CompletionCallbackState = "Invoked",
            CompletionCallbackPendingSinceUtc = DateTimeOffset.UtcNow.AddMinutes(-20),
            CompletionCallbackInvokedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-11),
            CompletionCallbackFinalPayloadPath = "/Volumes/Media/Incoming/TV/Example Show",
            CompletionCallbackPendingReason = null,
            CompletionCallbackLastError = null,
            ErrorMessage = null,
            CanRefreshMetadata = false,
            CanRetryCompletionCallback = false,
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
                OccurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-11),
                Level = "Information",
                Category = "torrent",
                EventType = "torrent.callback.invoked",
                Message = "Callback completed successfully.",
                TorrentId = torrentId,
                ServiceInstanceId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                TraceId = "trace-1",
                DetailsJson = null,
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

    private sealed class TestClipboardTextService : IClipboardTextService
    {
        public Task<string?> GetTextAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
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
