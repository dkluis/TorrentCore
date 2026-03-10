using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TorrentCore.Contracts;
using TorrentCore.Contracts.Host;
using TorrentCore.Contracts.Torrents;

namespace TorrentCore.Service.Tests;

public sealed class TorrentApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _httpClient;

    public TorrentApiTests(WebApplicationFactory<Program> factory)
    {
        _httpClient = factory.WithWebHostBuilder(_ => { }).CreateClient();
    }

    [Fact]
    public async Task GetHostStatus_ReturnsReadyHostContract()
    {
        var hostStatus = await _httpClient.GetFromJsonAsync<EngineHostStatusDto>("api/host/status");

        Assert.NotNull(hostStatus);
        Assert.Equal("TorrentCore.Service", hostStatus.ServiceName);
        Assert.Equal(EngineHostStatus.Ready, hostStatus.Status);
        Assert.True(hostStatus.SupportsMagnetAdds);
    }

    [Fact]
    public async Task GetTorrents_ReturnsSeededTorrents()
    {
        var torrents = await _httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents");

        Assert.NotNull(torrents);
        Assert.NotEmpty(torrents);
        Assert.Contains(torrents, torrent => torrent.State is TorrentState.Downloading or TorrentState.Paused);
    }

    [Fact]
    public async Task AddMagnet_ReturnsCreatedTorrent_ForValidMagnet()
    {
        var response = await _httpClient.PostAsJsonAsync("api/torrents", new AddMagnetRequest
        {
            MagnetUri = "magnet:?xt=urn:btih:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&dn=API%20Test%20Torrent",
        });

        response.EnsureSuccessStatusCode();

        var torrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(torrent);
        Assert.Equal("API Test Torrent", torrent.Name);
        Assert.Equal(TorrentState.ResolvingMetadata, torrent.State);
        Assert.Equal("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", torrent.InfoHash);
    }

    [Fact]
    public async Task AddMagnet_ReturnsBadRequest_ForInvalidMagnet()
    {
        var response = await _httpClient.PostAsJsonAsync("api/torrents", new AddMagnetRequest
        {
            MagnetUri = "https://example.com/not-a-magnet",
        });

        var error = await response.Content.ReadFromJsonAsync<ServiceErrorDto>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal("invalid_magnet", error.Code);
    }
}
