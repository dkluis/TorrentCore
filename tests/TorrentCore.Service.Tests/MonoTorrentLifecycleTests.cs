using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Torrents;
using TorrentCore.Persistence.Sqlite.Configuration;
using TorrentCore.Persistence.Sqlite.Torrents;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Engine;

namespace TorrentCore.Service.Tests;

public sealed class MonoTorrentLifecycleTests
{
    [Fact]
    public async Task RepeatedActivationAndVpnSuspension_RecreatesAndReleasesEngineWithoutRewritingTorrentIntent()
    {
        var rootPath = CreateTempRootPath();
        try
        {
            var gate = new TorrentExecutionGate(initiallyOpen: false);
            await using var factory = CreateFactory(rootPath, gate);
            using var client = factory.CreateClient();
            var torrentId = await AddPersistenceOnlyMagnetAsync(client, TestInfoHash, "Lifecycle Cycles");
            var lifecycle = factory.Services.GetRequiredService<IMonoTorrentLifecycle>();
            var adapter = factory.Services.GetRequiredService<MonoTorrentEngineAdapter>();
            var stateStore = factory.Services.GetRequiredService<ITorrentStateStore>();

            for (var cycle = 0; cycle < 3; cycle++)
            {
                var recovery = await lifecycle.ActivateAsync(CancellationToken.None);
                Assert.Equal(1, recovery.RecoveredTorrentCount);
                Assert.True(adapter.IsInitialized);
                Assert.True(adapter.HasEngineInstance);
                Assert.Equal(1, adapter.ManagedTorrentCount);

                var beforeSuspension = await stateStore.GetAsync(torrentId, CancellationToken.None);
                Assert.NotNull(beforeSuspension);

                var suspension = await lifecycle.SuspendAsync(
                    MonoTorrentSuspensionReason.VpnEgressNotValidated,
                    CancellationToken.None
                );

                Assert.True(suspension.Succeeded);
                Assert.True(suspension.EngineWasActive);
                Assert.True(suspension.EngineReleased);
                Assert.False(adapter.IsInitialized);
                Assert.False(adapter.HasEngineInstance);
                Assert.Equal(0, adapter.ManagedTorrentCount);

                var afterSuspension = await stateStore.GetAsync(torrentId, CancellationToken.None);
                Assert.NotNull(afterSuspension);
                Assert.Equal(beforeSuspension.State, afterSuspension.State);
                Assert.Equal(beforeSuspension.DesiredState, afterSuspension.DesiredState);
                Assert.Equal(0, afterSuspension.ConnectedPeerCount);
                Assert.Equal(0, afterSuspension.DownloadRateBytesPerSecond);
                Assert.Equal(0, afterSuspension.UploadRateBytesPerSecond);
            }
        }
        finally
        {
            DeleteTempRootPath(rootPath);
        }
    }

    [Fact]
    public async Task DegradedColdRestart_DoesNotCreateEngineAndRecoversAfterExplicitActivation()
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
                using var client = firstFactory.CreateClient();
                torrentId = await AddPersistenceOnlyMagnetAsync(client, TestInfoHash, "Cold Restart");
                var adapter = firstFactory.Services.GetRequiredService<MonoTorrentEngineAdapter>();
                Assert.False(adapter.HasEngineInstance);
            }

            await using (var secondFactory = CreateFactory(
                             rootPath,
                             new TorrentExecutionGate(initiallyOpen: false)
                         ))
            {
                using var client = secondFactory.CreateClient();
                var adapter = secondFactory.Services.GetRequiredService<MonoTorrentEngineAdapter>();
                var lifecycle = secondFactory.Services.GetRequiredService<IMonoTorrentLifecycle>();

                Assert.False(adapter.HasEngineInstance);
                var persisted = await client.GetFromJsonAsync<TorrentDetailDto>($"api/torrents/{torrentId:D}");
                Assert.NotNull(persisted);
                Assert.Equal(TorrentState.Queued, persisted.State);

                var recovery = await lifecycle.ActivateAsync(CancellationToken.None);

                Assert.Equal(1, recovery.RecoveredTorrentCount);
                Assert.True(adapter.HasEngineInstance);
                Assert.Equal(1, adapter.ManagedTorrentCount);
                await lifecycle.SuspendAsync(
                    MonoTorrentSuspensionReason.VpnEgressNotValidated,
                    CancellationToken.None
                );
            }
        }
        finally
        {
            DeleteTempRootPath(rootPath);
        }
    }

    [Fact]
    public async Task SnapshotFlushFailure_StillReleasesEngineAndCanRecoverOnLaterActivation()
    {
        var rootPath = CreateTempRootPath();
        try
        {
            var gate = new TorrentExecutionGate(initiallyOpen: false);
            var failingStore = new ArmedFailingTorrentStateStore();
            await using var factory = CreateFactory(rootPath, gate, failingStore);
            using var client = factory.CreateClient();
            var torrentId = await AddPersistenceOnlyMagnetAsync(client, TestInfoHash, "Flush Failure");
            var lifecycle = factory.Services.GetRequiredService<IMonoTorrentLifecycle>();
            var adapter = factory.Services.GetRequiredService<MonoTorrentEngineAdapter>();

            await lifecycle.ActivateAsync(CancellationToken.None);
            failingStore.FailUpdates = true;

            var suspension = await lifecycle.SuspendAsync(
                MonoTorrentSuspensionReason.VpnEgressNotValidated,
                CancellationToken.None
            );

            Assert.False(suspension.Succeeded);
            Assert.True(suspension.EngineReleased);
            Assert.Contains(
                suspension.Failures,
                failure => failure.Phase == "flush_snapshot" && failure.TorrentId == torrentId
            );
            Assert.False(adapter.HasEngineInstance);
            Assert.Equal(0, adapter.ManagedTorrentCount);

            failingStore.FailUpdates = false;
            var recovery = await lifecycle.ActivateAsync(CancellationToken.None);

            Assert.Equal(1, recovery.RecoveredTorrentCount);
            Assert.True(adapter.HasEngineInstance);
            Assert.Equal(1, adapter.ManagedTorrentCount);
            await lifecycle.SuspendAsync(
                MonoTorrentSuspensionReason.VpnEgressNotValidated,
                CancellationToken.None
            );
        }
        finally
        {
            DeleteTempRootPath(rootPath);
        }
    }

    [Fact]
    public async Task Activation_RecreatesEngineFromLatestPersistedEngineSettings()
    {
        var rootPath = CreateTempRootPath();
        try
        {
            var gate = new TorrentExecutionGate(initiallyOpen: false);
            await using var factory = CreateFactory(rootPath, gate);
            _ = factory.CreateClient();
            var lifecycle = factory.Services.GetRequiredService<IMonoTorrentLifecycle>();
            var adapter = factory.Services.GetRequiredService<MonoTorrentEngineAdapter>();
            var runtimeSettingsStore = factory.Services.GetRequiredService<SqliteRuntimeSettingsStore>();
            var appliedSettings = factory.Services.GetRequiredService<AppliedEngineSettingsState>();

            await lifecycle.ActivateAsync(CancellationToken.None);
            Assert.Equal(150, appliedSettings.EngineMaximumConnections);
            await lifecycle.SuspendAsync(
                MonoTorrentSuspensionReason.VpnEgressNotValidated,
                CancellationToken.None
            );

            await runtimeSettingsStore.UpsertAsync(
                new Dictionary<string, string>
                {
                    [RuntimeSettingsKeys.EngineMaximumConnections] = "73",
                },
                CancellationToken.None
            );

            await lifecycle.ActivateAsync(CancellationToken.None);

            Assert.True(adapter.HasEngineInstance);
            Assert.Equal(73, appliedSettings.EngineMaximumConnections);
            await lifecycle.SuspendAsync(
                MonoTorrentSuspensionReason.VpnEgressNotValidated,
                CancellationToken.None
            );
        }
        finally
        {
            DeleteTempRootPath(rootPath);
        }
    }

    [Fact]
    public async Task ConcurrentLifecycleRequests_AreSingleFlightAndIdempotent()
    {
        var rootPath = CreateTempRootPath();
        try
        {
            var gate = new TorrentExecutionGate(initiallyOpen: false);
            await using var factory = CreateFactory(rootPath, gate);
            _ = factory.CreateClient();
            var lifecycle = factory.Services.GetRequiredService<IMonoTorrentLifecycle>();
            var adapter = factory.Services.GetRequiredService<MonoTorrentEngineAdapter>();

            var activations = await Task.WhenAll(
                Enumerable.Range(0, 4).Select(_ => lifecycle.ActivateAsync(CancellationToken.None))
            );

            Assert.All(activations, result => Assert.Equal(0, result.RecoveredTorrentCount));
            Assert.True(adapter.HasEngineInstance);

            var suspensions = await Task.WhenAll(
                Enumerable.Range(0, 4).Select(
                    _ => lifecycle.SuspendAsync(
                        MonoTorrentSuspensionReason.VpnEgressNotValidated,
                        CancellationToken.None
                    )
                )
            );

            Assert.Single(suspensions, result => result.EngineWasActive);
            Assert.All(suspensions, result => Assert.True(result.EngineReleased));
            Assert.False(adapter.HasEngineInstance);
            Assert.Equal(0, adapter.ManagedTorrentCount);
        }
        finally
        {
            DeleteTempRootPath(rootPath);
        }
    }

    private const string TestInfoHash = "0123456789ABCDEF0123456789ABCDEF01234567";

    private static WebApplicationFactory<Program> CreateFactory(
        string rootPath,
        TorrentExecutionGate executionGate,
        ArmedFailingTorrentStateStore? failingStore = null)
    {
        var downloadPath = Path.Combine(rootPath, "downloads");
        var storagePath = Path.Combine(rootPath, "storage");
        var portOffset = Random.Shared.Next(0, 5_000);

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
                        [$"{TorrentCoreServiceOptions.SectionName}:EngineListenPort"] = (40_000 + portOffset).ToString(),
                        [$"{TorrentCoreServiceOptions.SectionName}:EngineDhtPort"] = (50_000 + portOffset).ToString(),
                        [$"{TorrentCoreServiceOptions.SectionName}:EngineAllowPortForwarding"] = bool.FalseString,
                        [$"{TorrentCoreServiceOptions.SectionName}:EngineAllowLocalPeerDiscovery"] = bool.FalseString,
                    }
                );
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TorrentExecutionGate>();
                services.AddSingleton(executionGate);
                if (failingStore is not null)
                {
                    services.RemoveAll<ITorrentStateStore>();
                    services.AddSingleton<ITorrentStateStore>(serviceProvider =>
                    {
                        var paths = serviceProvider.GetRequiredService<ResolvedTorrentCoreServicePaths>();
                        failingStore.Inner = new SqliteTorrentStateStore(paths.DatabaseFilePath);
                        return failingStore;
                    });
                }
            });
        });
    }

    private static async Task<Guid> AddPersistenceOnlyMagnetAsync(
        HttpClient client,
        string infoHash,
        string displayName)
    {
        using var response = await client.PostAsJsonAsync(
            "api/torrents",
            new AddMagnetRequest
            {
                MagnetUri = $"magnet:?xt=urn:btih:{infoHash}&dn={Uri.EscapeDataString(displayName)}",
            }
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var added = await response.Content.ReadFromJsonAsync<TorrentDetailDto>();
        return Assert.IsType<TorrentDetailDto>(added).TorrentId;
    }

    private static string CreateTempRootPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"torrentcore-lifecycle-{Guid.NewGuid():N}");
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

    private sealed class ArmedFailingTorrentStateStore : ITorrentStateStore
    {
        public ITorrentStateStore Inner { private get; set; } = null!;
        public bool FailUpdates { get; set; }

        public Task EnsureInitializedAsync(CancellationToken cancellationToken)
            => Inner.EnsureInitializedAsync(cancellationToken);

        public Task<int> CountAsync(CancellationToken cancellationToken)
            => Inner.CountAsync(cancellationToken);

        public Task<bool> ExistsByInfoHashAsync(string infoHash, CancellationToken cancellationToken)
            => Inner.ExistsByInfoHashAsync(infoHash, cancellationToken);

        public Task<IReadOnlyList<TorrentSnapshot>> ListAsync(CancellationToken cancellationToken)
            => Inner.ListAsync(cancellationToken);

        public Task<TorrentSnapshot?> GetAsync(Guid torrentId, CancellationToken cancellationToken)
            => Inner.GetAsync(torrentId, cancellationToken);

        public Task InsertAsync(TorrentSnapshot torrent, CancellationToken cancellationToken)
            => Inner.InsertAsync(torrent, cancellationToken);

        public Task UpdateAsync(TorrentSnapshot torrent, CancellationToken cancellationToken)
            => FailUpdates
                ? Task.FromException(new IOException("Injected snapshot update failure."))
                : Inner.UpdateAsync(torrent, cancellationToken);

        public Task DeleteAsync(Guid torrentId, CancellationToken cancellationToken)
            => Inner.DeleteAsync(torrentId, cancellationToken);
    }
}
