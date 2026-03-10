using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using TorrentCore.Contracts.Diagnostics;
using TorrentCore.Contracts.Host;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Service.Configuration;

namespace TorrentCore.Service.Tests;

public sealed class TorrentApiTests
{
    [Fact]
    public async Task GetHostStatus_ReturnsReadyHostContract()
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        var hostStatus = await httpClient.GetFromJsonAsync<EngineHostStatusDto>("api/host/status");

        Assert.NotNull(hostStatus);
        Assert.Equal("TorrentCore.Service", hostStatus.ServiceName);
        Assert.Equal(EngineHostStatus.Ready, hostStatus.Status);
        Assert.True(hostStatus.SupportsMagnetAdds);
        Assert.True(hostStatus.SupportsPersistentStorage);
        Assert.True(hostStatus.StartupRecoveryCompleted);
        Assert.NotEqual(Guid.Empty, hostStatus.ServiceInstanceId);
    }

    [Fact]
    public async Task GetHostStatus_UsesConfiguredPaths_AndCreatesDirectories()
    {
        var rootPath = CreateTempRootPath("torrentcore-phase2-host");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");

        await using var factory = CreateFactory(downloadPath, storagePath);
        using var httpClient = factory.CreateClient();

        var hostStatus = await httpClient.GetFromJsonAsync<EngineHostStatusDto>("api/host/status");

        Assert.NotNull(hostStatus);
        Assert.Equal(Path.GetFullPath(downloadPath), hostStatus.DownloadRootPath);
        Assert.True(Directory.Exists(downloadPath));
        Assert.True(Directory.Exists(storagePath));
        Assert.True(File.Exists(Path.Combine(storagePath, "torrentcore.db")));
        Assert.True(hostStatus.StartupRecoveryCompleted);
    }

    [Fact]
    public async Task GetTorrents_ReturnsPersistedTorrentAfterAdd()
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        await AddMagnetAsync(httpClient, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "Listed Torrent");

        var torrents = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents");

        Assert.NotNull(torrents);
        Assert.Contains(torrents, torrent => torrent.Name == "Listed Torrent");
    }

    [Fact]
    public async Task AddMagnet_ReturnsCreatedTorrent_ForValidMagnet()
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        var response = await AddMagnetAsync(httpClient, "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", "API Test Torrent");
        var torrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(torrent);
        Assert.Equal("API Test Torrent", torrent.Name);
        Assert.Equal(TorrentState.ResolvingMetadata, torrent.State);
        Assert.Equal("BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB", torrent.InfoHash);
        Assert.Equal(0, torrent.TrackerCount);
        Assert.Equal(0, torrent.ConnectedPeerCount);
    }

    [Fact]
    public async Task FakeRuntime_EventuallyResolvesMetadata_AndCompletesDownload()
    {
        await using var factory = CreateFactory(
            runtimeTickIntervalMilliseconds: 50,
            metadataResolutionDelayMilliseconds: 0,
            downloadProgressPercentPerTick: 50);
        using var httpClient = factory.CreateClient();

        var response = await AddMagnetAsync(httpClient, "ABABABABABABABABABABABABABABABABABABABAB", "Runtime Torrent");
        var addedTorrent = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();

        var completedTorrent = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{addedTorrent!.TorrentId}"),
            torrent => torrent is not null && torrent.State == TorrentState.Completed,
            timeout: TimeSpan.FromSeconds(5));

        var logs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>("api/logs?take=50&torrentId=" + addedTorrent!.TorrentId);

        Assert.NotNull(completedTorrent);
        Assert.True(completedTorrent.TotalBytes > 0);
        Assert.Equal(completedTorrent.TotalBytes, completedTorrent.DownloadedBytes);
        Assert.Equal(100, completedTorrent.ProgressPercent);
        Assert.True(completedTorrent.TrackerCount > 0);
        Assert.NotNull(completedTorrent.CompletedAtUtc);

        Assert.NotNull(logs);
        Assert.Contains(logs, log => log.EventType == "torrent.metadata.resolved");
        Assert.Contains(logs, log => log.EventType == "torrent.download.started");
        Assert.Contains(logs, log => log.EventType == "torrent.download.completed");
    }

    [Fact]
    public async Task FakeRuntime_UsesSingleActiveDownloadQueue_ByDefault()
    {
        await using var factory = CreateFactory(
            runtimeTickIntervalMilliseconds: 50,
            metadataResolutionDelayMilliseconds: 0,
            downloadProgressPercentPerTick: 5,
            maxActiveDownloads: 1);
        using var httpClient = factory.CreateClient();

        var firstResponse = await AddMagnetAsync(httpClient, "1010101010101010101010101010101010101010", "Queue One");
        var firstTorrent = await firstResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();

        var secondResponse = await AddMagnetAsync(httpClient, "2020202020202020202020202020202020202020", "Queue Two");
        var secondTorrent = await secondResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();

        var queuedAndActive = await WaitForAsync(
            async () => await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents"),
            torrents => torrents is not null &&
                        torrents.Count == 2 &&
                        torrents.Count(torrent => torrent.State == TorrentState.Downloading) == 1 &&
                        torrents.Count(torrent => torrent.State == TorrentState.Queued) == 1,
            timeout: TimeSpan.FromSeconds(5));

        Assert.NotNull(queuedAndActive);
        Assert.Contains(queuedAndActive, torrent => torrent.TorrentId == firstTorrent!.TorrentId || torrent.TorrentId == secondTorrent!.TorrentId);
        Assert.Contains(queuedAndActive, torrent => torrent.State == TorrentState.Queued);
        Assert.Contains(queuedAndActive, torrent => torrent.State == TorrentState.Downloading);
    }

    [Fact]
    public async Task TorrentState_SurvivesRestart()
    {
        var rootPath = CreateTempRootPath("torrentcore-phase2-restart");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");

        Guid torrentId;

        await using (var factory = CreateFactory(downloadPath, storagePath))
        {
            using var httpClient = factory.CreateClient();
            var addResponse = await AddMagnetAsync(httpClient, "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC", "Restarted Torrent");
            var addedTorrent = await addResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();
            torrentId = addedTorrent!.TorrentId;

            var pauseResponse = await httpClient.PostAsync($"api/torrents/{torrentId}/pause", content: null);
            pauseResponse.EnsureSuccessStatusCode();
        }

        await using (var factory = CreateFactory(downloadPath, storagePath))
        {
            using var httpClient = factory.CreateClient();

            var torrent = await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{torrentId}");
            var torrents = await httpClient.GetFromJsonAsync<IReadOnlyList<TorrentSummaryDto>>("api/torrents");

            Assert.NotNull(torrent);
            Assert.Equal(TorrentState.Paused, torrent.State);
            Assert.Contains(torrents!, summary => summary.TorrentId == torrentId && summary.State == TorrentState.Paused);
        }
    }

    [Fact]
    public async Task StartupRecovery_NormalizesActiveTorrentState_AfterRestart()
    {
        var rootPath = CreateTempRootPath("torrentcore-phase2-recovery");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");

        Guid torrentId;

        await using (var factory = CreateFactory(downloadPath, storagePath))
        {
            using var httpClient = factory.CreateClient();
            var addResponse = await AddMagnetAsync(httpClient, "1212121212121212121212121212121212121212", "Recovery Torrent");
            var addedTorrent = await addResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();
            torrentId = addedTorrent!.TorrentId;
        }

        await using (var factory = CreateFactory(downloadPath, storagePath))
        {
            using var httpClient = factory.CreateClient();

            var recoveredTorrent = await httpClient.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{torrentId}");
            var hostStatus = await httpClient.GetFromJsonAsync<EngineHostStatusDto>("api/host/status");
            var logs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>("api/logs?take=50");

            Assert.NotNull(recoveredTorrent);
            Assert.NotEqual(TorrentState.ResolvingMetadata, recoveredTorrent.State);

            Assert.NotNull(hostStatus);
            Assert.Equal(1, hostStatus.StartupRecoveredTorrentCount);
            Assert.Equal(1, hostStatus.StartupNormalizedTorrentCount);
            Assert.NotNull(hostStatus.StartupRecoveryCompletedAtUtc);

            Assert.NotNull(logs);
            Assert.Contains(logs, log => log.EventType == "service.recovery.completed" && log.ServiceInstanceId == hostStatus.ServiceInstanceId);
            Assert.Contains(logs, log => log.EventType == "torrent.recovery.normalized" && log.TorrentId == torrentId);
            Assert.Contains(logs, log => log.EventType == "service.startup.ready" && log.ServiceInstanceId == hostStatus.ServiceInstanceId);
        }
    }

    [Fact]
    public async Task GetLogs_ReturnsStartupAndTorrentEvents()
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        await AddMagnetAsync(httpClient, "DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD", "Logged Torrent");

        var logs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>("api/logs?take=20");

        Assert.NotNull(logs);
        Assert.Contains(logs, log => log.EventType == "service.startup.ready");
        Assert.Contains(logs, log => log.EventType == "torrent.added");
        Assert.Contains(logs, log => log.ServiceInstanceId is not null);
    }

    [Fact]
    public async Task GetLogs_FiltersByCategory_AndEventType()
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        await AddMagnetAsync(httpClient, "EEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE", "Filtered Torrent");

        var logs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>("api/logs?take=20&category=torrent&eventType=torrent.added");

        Assert.NotNull(logs);
        Assert.NotEmpty(logs);
        Assert.All(logs, log =>
        {
            Assert.Equal("torrent", log.Category);
            Assert.Equal("torrent.added", log.EventType);
        });
    }

    [Fact]
    public async Task GetLogs_RetentionEnforcesConfiguredMaximum()
    {
        var rootPath = CreateTempRootPath("torrentcore-logs-retention");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");

        await using var factory = CreateFactory(downloadPath, storagePath, maxActivityLogEntries: 100);
        using var httpClient = factory.CreateClient();

        for (var index = 0; index < 130; index++)
        {
            var hash = index.ToString("D40");
            var response = await AddMagnetAsync(httpClient, hash, $"Retention {index}");
            response.EnsureSuccessStatusCode();
        }

        var logs = await httpClient.GetFromJsonAsync<IReadOnlyList<ActivityLogEntryDto>>("api/logs?take=500");

        Assert.NotNull(logs);
        Assert.True(logs.Count <= 100);
    }

    [Fact]
    public async Task AddMagnet_ReturnsBadRequest_ForInvalidMagnet()
    {
        await using var factory = CreateFactory();
        using var httpClient = factory.CreateClient();

        var response = await httpClient.PostAsJsonAsync("api/torrents", new AddMagnetRequest
        {
            MagnetUri = "https://example.com/not-a-magnet",
        });

        var error = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("invalid_magnet", error.GetProperty("code").GetString());
        Assert.Equal("MagnetUri must be a valid magnet URI.", error.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task AddMagnet_ReturnsConflict_ForPersistedDuplicate()
    {
        var rootPath = CreateTempRootPath("torrentcore-duplicate");
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");

        await using (var factory = CreateFactory(downloadPath, storagePath))
        {
            using var httpClient = factory.CreateClient();
            var firstResponse = await AddMagnetAsync(httpClient, "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", "First Torrent");
            firstResponse.EnsureSuccessStatusCode();
        }

        await using (var factory = CreateFactory(downloadPath, storagePath))
        {
            using var httpClient = factory.CreateClient();
            var duplicateResponse = await AddMagnetAsync(httpClient, "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", "Duplicate Torrent");
            var error = await duplicateResponse.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
            Assert.Equal("duplicate_magnet", error.GetProperty("code").GetString());
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string? downloadPath = null,
        string? storagePath = null,
        int? maxActivityLogEntries = null,
        int? maxActiveDownloads = null,
        int? runtimeTickIntervalMilliseconds = null,
        int? metadataResolutionDelayMilliseconds = null,
        double? downloadProgressPercentPerTick = null)
    {
        var rootPath = CreateTempRootPath("torrentcore-api");
        var resolvedDownloadPath = downloadPath ?? Path.Combine(rootPath, "downloads");
        var resolvedStoragePath = storagePath ?? Path.Combine(rootPath, "storage");

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configurationBuilder) =>
                {
                    var settings = new Dictionary<string, string?>
                    {
                        [$"{TorrentCoreServiceOptions.SectionName}:DownloadRootPath"] = resolvedDownloadPath,
                        [$"{TorrentCoreServiceOptions.SectionName}:StorageRootPath"] = resolvedStoragePath,
                    };

                    if (maxActivityLogEntries is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:MaxActivityLogEntries"] = maxActivityLogEntries.Value.ToString();
                    }

                    if (maxActiveDownloads is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:MaxActiveDownloads"] = maxActiveDownloads.Value.ToString();
                    }

                    if (runtimeTickIntervalMilliseconds is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:RuntimeTickIntervalMilliseconds"] = runtimeTickIntervalMilliseconds.Value.ToString();
                    }

                    if (metadataResolutionDelayMilliseconds is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:MetadataResolutionDelayMilliseconds"] = metadataResolutionDelayMilliseconds.Value.ToString();
                    }

                    if (downloadProgressPercentPerTick is not null)
                    {
                        settings[$"{TorrentCoreServiceOptions.SectionName}:DownloadProgressPercentPerTick"] = downloadProgressPercentPerTick.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }

                    configurationBuilder.AddInMemoryCollection(settings);
                });
            });
    }

    private static async Task<HttpResponseMessage> AddMagnetAsync(HttpClient httpClient, string infoHash, string name)
    {
        return await httpClient.PostAsJsonAsync("api/torrents", new AddMagnetRequest
        {
            MagnetUri = $"magnet:?xt=urn:btih:{infoHash}&dn={Uri.EscapeDataString(name)}",
        });
    }

    private static string CreateTempRootPath(string prefix)
    {
        return Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
    }

    private static async Task<T> WaitForAsync<T>(
        Func<Task<T>> action,
        Func<T, bool> predicate,
        TimeSpan timeout,
        int pollIntervalMilliseconds = 50)
    {
        var startedAt = DateTimeOffset.UtcNow;

        while (DateTimeOffset.UtcNow - startedAt < timeout)
        {
            var result = await action();
            if (predicate(result))
            {
                return result;
            }

            await Task.Delay(pollIntervalMilliseconds);
        }

        var finalResult = await action();
        Assert.True(predicate(finalResult), "Timed out waiting for the expected condition.");
        return finalResult;
    }
}
