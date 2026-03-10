using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using TorrentCore.Contracts;
using TorrentCore.Contracts.Host;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Service.Configuration;

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
    public async Task GetHostStatus_UsesConfiguredPaths_AndCreatesDirectories()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"torrentcore-phase1-{Guid.NewGuid():N}");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");

        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configurationBuilder) =>
                {
                    configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        [$"{TorrentCoreServiceOptions.SectionName}:DownloadRootPath"] = downloadPath,
                        [$"{TorrentCoreServiceOptions.SectionName}:StorageRootPath"] = storagePath,
                    });
                });
            });

        using var httpClient = factory.CreateClient();

        var hostStatus = await httpClient.GetFromJsonAsync<EngineHostStatusDto>("api/host/status");

        Assert.NotNull(hostStatus);
        Assert.Equal(Path.GetFullPath(downloadPath), hostStatus.DownloadRootPath);
        Assert.True(Directory.Exists(downloadPath));
        Assert.True(Directory.Exists(storagePath));
    }

    [Fact]
    public async Task GetTorrents_ReturnsSeededTorrents()
    {
        var torrents = await _httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents");

        Assert.NotNull(torrents);
        Assert.NotEmpty(torrents);
        Assert.Contains(torrents, torrent => torrent.State is TorrentState.Downloading or TorrentState.Paused);
        Assert.Contains(torrents, torrent => torrent.TrackerCount > 0);
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
        Assert.Equal(0, torrent.TrackerCount);
        Assert.Equal(0, torrent.ConnectedPeerCount);
    }

    [Fact]
    public async Task AddMagnet_ReturnsBadRequest_ForInvalidMagnet()
    {
        var response = await _httpClient.PostAsJsonAsync("api/torrents", new AddMagnetRequest
        {
            MagnetUri = "https://example.com/not-a-magnet",
        });

        var error = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("invalid_magnet", error.GetProperty("code").GetString());
        Assert.Equal("MagnetUri must be a valid magnet URI.", error.GetProperty("detail").GetString());
    }
}
