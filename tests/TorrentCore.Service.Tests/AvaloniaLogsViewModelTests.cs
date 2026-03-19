using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TorrentCore.Avalonia.ViewModels;
using TorrentCore.Client;
using TorrentCore.Contracts.Diagnostics;

namespace TorrentCore.Service.Tests;

public sealed class AvaloniaLogsViewModelTests
{
    [Fact]
    public async Task LoadAsync_ExposesTorrentNavigation_ForTorrentScopedLogs()
    {
        var openedTorrentId = Guid.Empty;
        var expectedTorrentId = Guid.Parse("99999999-8888-7777-6666-555555555555");
        var logs = new[]
        {
            new ActivityLogEntryDto
            {
                LogEntryId = 1,
                OccurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                Level = "Error",
                Category = "torrent",
                EventType = "torrent.callback.failed",
                Message = "Callback failed.",
                TorrentId = expectedTorrentId,
                ServiceInstanceId = Guid.Parse("11111111-aaaa-bbbb-cccc-222222222222"),
                TraceId = "trace-1",
                DetailsJson = "{\"ExitCode\":1}",
            },
            new ActivityLogEntryDto
            {
                LogEntryId = 2,
                OccurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2),
                Level = "Information",
                Category = "engine",
                EventType = "engine.started",
                Message = "Engine started.",
                TorrentId = null,
                ServiceInstanceId = Guid.Parse("11111111-aaaa-bbbb-cccc-222222222222"),
                TraceId = "trace-2",
                DetailsJson = null,
            },
        };

        var client = CreateClient((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath == "/api/logs"
                ? CreateJsonResponse(logs)
                : throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}")));

        var viewModel = new LogsViewModel(client, torrentId => openedTorrentId = torrentId);
        await viewModel.LoadAsync();

        Assert.False(viewModel.HasError);
        Assert.True(viewModel.HasEntries);
        Assert.True(viewModel.HasLastRefreshed);
        Assert.Equal(2, viewModel.Entries.Count);

        var torrentEntry = viewModel.Entries[0];
        var engineEntry = viewModel.Entries[1];

        Assert.True(torrentEntry.CanOpenTorrentDetail);
        Assert.False(engineEntry.CanOpenTorrentDetail);

        torrentEntry.OpenTorrentDetailCommand.Execute(null);

        Assert.Equal(expectedTorrentId, openedTorrentId);
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
