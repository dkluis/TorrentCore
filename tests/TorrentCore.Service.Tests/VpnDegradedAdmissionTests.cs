using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TorrentCore.Contracts.History;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Torrents;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Engine;

namespace TorrentCore.Service.Tests;

public sealed class VpnDegradedAdmissionTests
{
    [Fact]
    public async Task ClosedGate_AcceptsAndPersistsMagnetWithoutInitializingMonoTorrent()
    {
        var rootPath = CreateTempRootPath();
        try
        {
            var downloadPath = Path.Combine(rootPath, "downloads");
            var gate = new TorrentExecutionGate(initiallyOpen: false);
            await using var factory = CreateFactory(rootPath, gate);
            using var client = factory.CreateClient();
            Assert.True(Directory.Exists(downloadPath));
            Directory.Delete(downloadPath, recursive: true);

            using var response = await AddMagnetAsync(client, TestInfoHash, "Queued While Degraded");

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var added = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();
            Assert.NotNull(added);
            Assert.Equal(TorrentState.Queued, added.State);
            Assert.True(Directory.Exists(downloadPath));

            var adapter = factory.Services.GetRequiredService<MonoTorrentEngineAdapter>();
            Assert.False(adapter.IsInitialized);
            Assert.Equal(0, adapter.ManagedTorrentCount);

            var store = factory.Services.GetRequiredService<ITorrentStateStore>();
            var snapshot = await store.GetAsync(added.TorrentId, CancellationToken.None);
            Assert.NotNull(snapshot);
            Assert.Equal(TorrentState.Queued, snapshot.State);
            Assert.Equal(TorrentDesiredState.Runnable, snapshot.DesiredState);
            Assert.Equal(TestInfoHash, snapshot.InfoHash);

            var torrents = await client.GetFromJsonAsync<TorrentSummaryDto[]>("api/torrents");
            Assert.Single(torrents!);
            var detail = await client.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{added.TorrentId:D}");
            Assert.Equal(added.TorrentId, detail!.TorrentId);
            var history = await client.GetFromJsonAsync<TorrentHistorySummaryDto[]>("api/history");
            Assert.Contains(history!, item => item.TorrentId == added.TorrentId);
        }
        finally
        {
            DeleteTempRootPath(rootPath);
        }
    }

    [Fact]
    public async Task ClosedGate_PreservesMagnetCategoryAndDuplicateAcceptanceChecks()
    {
        var rootPath = CreateTempRootPath();
        try
        {
            var gate = new TorrentExecutionGate(initiallyOpen: false);
            await using var factory = CreateFactory(rootPath, gate);
            using var client = factory.CreateClient();

            using var invalidMagnetResponse = await client.PostAsJsonAsync(
                "api/torrents",
                new AddMagnetRequest { MagnetUri = "not-a-magnet" }
            );
            await AssertProblemCodeAsync(invalidMagnetResponse, HttpStatusCode.BadRequest, "invalid_magnet");

            using var invalidCategoryResponse = await client.PostAsJsonAsync(
                "api/torrents",
                new AddMagnetRequest
                {
                    MagnetUri = CreateMagnet(TestInfoHash, "Invalid Category"),
                    CategoryKey = "MissingCategory",
                }
            );
            await AssertProblemCodeAsync(invalidCategoryResponse, HttpStatusCode.BadRequest, "invalid_category");

            using var acceptedResponse = await AddMagnetAsync(client, TestInfoHash, "Accepted Once");
            Assert.Equal(HttpStatusCode.Created, acceptedResponse.StatusCode);

            using var duplicateResponse = await AddMagnetAsync(client, TestInfoHash, "Duplicate");
            await AssertProblemCodeAsync(duplicateResponse, HttpStatusCode.Conflict, "duplicate_magnet");

            var defaultDownloadPath = Path.Combine(rootPath, "downloads");
            Directory.Delete(defaultDownloadPath, recursive: true);
            await File.WriteAllTextAsync(defaultDownloadPath, "not a directory");
            using var unavailableRootResponse = await AddMagnetAsync(
                client,
                AlternateInfoHash,
                "Unavailable Default Root"
            );
            await AssertProblemCodeAsync(
                unavailableRootResponse,
                HttpStatusCode.ServiceUnavailable,
                "category_download_root_unavailable"
            );

            var adapter = factory.Services.GetRequiredService<MonoTorrentEngineAdapter>();
            Assert.False(adapter.IsInitialized);
            Assert.Equal(0, adapter.ManagedTorrentCount);
        }
        finally
        {
            DeleteTempRootPath(rootPath);
        }
    }

    [Fact]
    public async Task ClosedGate_BlocksEngineDependentApisWithStructuredUnavailableResponse()
    {
        var rootPath = CreateTempRootPath();
        try
        {
            var gate = new TorrentExecutionGate(initiallyOpen: false);
            await using var factory = CreateFactory(rootPath, gate);
            using var client = factory.CreateClient();
            using var addResponse = await AddMagnetAsync(client, TestInfoHash, "Blocked Actions");
            var added = await addResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();
            Assert.NotNull(added);

            var requests = new HttpRequestMessage[]
            {
                new(HttpMethod.Get, $"api/torrents/{added.TorrentId:D}/peers"),
                new(HttpMethod.Get, $"api/torrents/{added.TorrentId:D}/trackers"),
                new(HttpMethod.Post, $"api/torrents/{added.TorrentId:D}/pause"),
                new(HttpMethod.Post, $"api/torrents/{added.TorrentId:D}/resume"),
                new(HttpMethod.Post, $"api/torrents/{added.TorrentId:D}/metadata/refresh"),
                new(HttpMethod.Post, $"api/torrents/{added.TorrentId:D}/metadata/reset"),
                new(HttpMethod.Post, $"api/torrents/{added.TorrentId:D}/completion-callback/retry"),
                new(HttpMethod.Post, $"api/torrents/{added.TorrentId:D}/remove")
                {
                    Content = JsonContent.Create(new RemoveTorrentRequest()),
                },
            };

            foreach (var request in requests)
            {
                using (request)
                using (var response = await client.SendAsync(request))
                {
                    await AssertProblemCodeAsync(
                        response,
                        HttpStatusCode.ServiceUnavailable,
                        "vpn_egress_not_validated"
                    );
                }
            }

            var adapter = factory.Services.GetRequiredService<MonoTorrentEngineAdapter>();
            Assert.False(adapter.IsInitialized);
            Assert.Equal(0, adapter.ManagedTorrentCount);
        }
        finally
        {
            DeleteTempRootPath(rootPath);
        }
    }

    [Fact]
    public async Task PersistenceOnlyMagnet_RemainsQueuedAcrossClosedGateServiceRestart()
    {
        var rootPath = CreateTempRootPath();
        try
        {
            Guid torrentId;
            await using (var firstFactory = CreateFactory(
                             rootPath,
                             new TorrentExecutionGate(initiallyOpen: false)
                         ))
            {
                using var firstClient = firstFactory.CreateClient();
                using var addResponse = await AddMagnetAsync(firstClient, TestInfoHash, "Restart Durable");
                var added = await addResponse.Content.ReadFromJsonAsync<TorrentDetailDto>();
                Assert.NotNull(added);
                torrentId = added.TorrentId;
            }

            await using (var secondFactory = CreateFactory(
                             rootPath,
                             new TorrentExecutionGate(initiallyOpen: false)
                         ))
            {
                using var secondClient = secondFactory.CreateClient();
                var detail = await secondClient.GetFromJsonAsync<TorrentDetailDto>(
                    $"api/torrents/{torrentId:D}"
                );

                Assert.NotNull(detail);
                Assert.Equal(TorrentState.Queued, detail.State);
                Assert.Equal(TestInfoHash, detail.InfoHash);
                var adapter = secondFactory.Services.GetRequiredService<MonoTorrentEngineAdapter>();
                Assert.False(adapter.IsInitialized);
                Assert.Equal(0, adapter.ManagedTorrentCount);
            }
        }
        finally
        {
            DeleteTempRootPath(rootPath);
        }
    }

    [Fact]
    public async Task ClosedGate_ConstrainsDerivedSavePathToDefaultDownloadRoot()
    {
        var rootPath = CreateTempRootPath();
        try
        {
            var gate = new TorrentExecutionGate(initiallyOpen: false);
            await using var factory = CreateFactory(rootPath, gate);
            using var client = factory.CreateClient();

            using var response = await AddMagnetAsync(client, TestInfoHash, "..");
            var added = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();

            Assert.NotNull(added);
            var expectedPath = Path.Combine(rootPath, "downloads", "torrent");
            Assert.Equal(Path.GetFullPath(expectedPath), added.SavePath);
        }
        finally
        {
            DeleteTempRootPath(rootPath);
        }
    }

    private const string TestInfoHash = "0123456789ABCDEF0123456789ABCDEF01234567";
    private const string AlternateInfoHash = "89ABCDEF0123456789ABCDEF0123456789ABCDEF";

    private static WebApplicationFactory<Program> CreateFactory(
        string rootPath,
        TorrentExecutionGate executionGate)
    {
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        [$"{TorrentCoreServiceOptions.SectionName}:EngineMode"] = TorrentEngineMode.MonoTorrent.ToString(),
                        [$"{TorrentCoreServiceOptions.SectionName}:DownloadRootPath"] = downloadPath,
                        [$"{TorrentCoreServiceOptions.SectionName}:StorageRootPath"] = storagePath,
                        [$"{TorrentCoreServiceOptions.SectionName}:EngineAllowPortForwarding"] = bool.FalseString,
                        [$"{TorrentCoreServiceOptions.SectionName}:EngineAllowLocalPeerDiscovery"] = bool.FalseString,
                    }
                );
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TorrentExecutionGate>();
                services.AddSingleton(executionGate);
            });
        });
    }

    private static Task<HttpResponseMessage> AddMagnetAsync(
        HttpClient client,
        string infoHash,
        string displayName)
        => client.PostAsJsonAsync(
            "api/torrents",
            new AddMagnetRequest { MagnetUri = CreateMagnet(infoHash, displayName) }
        );

    private static string CreateMagnet(string infoHash, string displayName)
        => $"magnet:?xt=urn:btih:{infoHash}&dn={Uri.EscapeDataString(displayName)}";

    private static async Task AssertProblemCodeAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expectedCode, problem.GetProperty("code").GetString());
    }

    private static string CreateTempRootPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"torrentcore-vpn-degraded-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempRootPath(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
