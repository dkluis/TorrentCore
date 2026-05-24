using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.History;
using TorrentCore.Core.Torrents;
using TorrentCore.Persistence.Sqlite.History;
using TorrentCore.Persistence.Sqlite.Schema;
using TorrentCore.Service.Application;
using TorrentCore.Service.Configuration;

namespace TorrentCore.Service.Tests;

public sealed class TorrentHistoryServiceTests
{
    [Fact]
    public async Task ObserveSnapshot_DoesNotRewriteMilestoneTimestamps_OnLaterUpdates()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"torrentcore-history-service-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);
        var databaseFilePath = Path.Combine(rootPath, "torrentcore.db");

        try
        {
            var migrator = new SqliteSchemaMigrator(databaseFilePath);
            await migrator.ApplyMigrationsAsync(CancellationToken.None);

            var store = new SqliteTorrentHistoryStore(databaseFilePath);
            var service = new TorrentHistoryService(
                store,
                new ServiceInstanceContext
                {
                    ServiceInstanceId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                });

            var addedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            var metadataAt = addedAt.AddMinutes(1);
            var downloadAt = addedAt.AddMinutes(2);
            var completionAt = addedAt.AddMinutes(3);
            var seedingAt = addedAt.AddMinutes(4);

            await service.ObserveSnapshotAsync(
                CreateSnapshot(TorrentState.ResolvingMetadata, addedAt, addedAt, null, null, 0),
                CancellationToken.None);

            await service.ObserveSnapshotAsync(
                CreateSnapshot(TorrentState.Queued, addedAt, metadataAt, 1_024, null, 0),
                CancellationToken.None);

            await service.ObserveSnapshotAsync(
                CreateSnapshot(TorrentState.Downloading, addedAt, downloadAt, 1_024, null, 25),
                CancellationToken.None);

            var afterStart = await store.GetAsync(TorrentId, CancellationToken.None);

            await service.ObserveSnapshotAsync(
                CreateSnapshot(TorrentState.Downloading, addedAt, downloadAt.AddMinutes(1), 1_024, null, 50),
                CancellationToken.None);

            await service.ObserveSnapshotAsync(
                CreateSnapshot(TorrentState.Seeding, addedAt, seedingAt, 1_024, completionAt, 100, seedingAt),
                CancellationToken.None);

            var final = await store.GetAsync(TorrentId, CancellationToken.None);

            Assert.NotNull(afterStart);
            Assert.NotNull(final);
            Assert.Equal(metadataAt, final.MetadataResolvedAtUtc);
            Assert.Equal(afterStart.DownloadStartedAtUtc, final.DownloadStartedAtUtc);
            Assert.Equal(completionAt, final.DownloadCompletedAtUtc);
            Assert.Equal(seedingAt, final.SeedingStartedAtUtc);
            Assert.True(final.LastUpdatedAtUtc >= afterStart.LastUpdatedAtUtc);
            Assert.Equal(100, final.LatestProgressPercent);
            Assert.Equal(TorrentState.Seeding.ToString(), final.LatestTorrentState);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    private static readonly Guid TorrentId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static TorrentSnapshot CreateSnapshot(TorrentState state, DateTimeOffset addedAtUtc,
        DateTimeOffset lastActivityAtUtc, long? totalBytes, DateTimeOffset? completedAtUtc, double progressPercent,
        DateTimeOffset? seedingStartedAtUtc = null)
    {
        return new TorrentSnapshot
        {
            TorrentId = TorrentId,
            Name = "History Service Torrent",
            CategoryKey = "Movie",
            CompletionCallbackLabel = "Movie",
            InvokeCompletionCallback = true,
            CompletionCallbackState = null,
            CompletionCallbackPendingSinceUtc = null,
            CompletionCallbackInvokedAtUtc = null,
            CompletionCallbackLastError = null,
            State = state,
            DesiredState = TorrentDesiredState.Runnable,
            MagnetUri = "magnet:?xt=urn:btih:3333333333333333333333333333333333333333&dn=History%20Service",
            InfoHash = "3333333333333333333333333333333333333333",
            DownloadRootPath = "/tmp/torrentcore-tests/downloads",
            SavePath = "/tmp/torrentcore-tests/downloads/History Service Torrent",
            ProgressPercent = progressPercent,
            DownloadedBytes = (long)progressPercent,
            UploadedBytes = 0,
            TotalBytes = totalBytes,
            DownloadRateBytesPerSecond = 0,
            UploadRateBytesPerSecond = 0,
            TrackerCount = 1,
            ConnectedPeerCount = 0,
            AddedAtUtc = addedAtUtc,
            CompletedAtUtc = completedAtUtc,
            SeedingStartedAtUtc = seedingStartedAtUtc,
            LastActivityAtUtc = lastActivityAtUtc,
            ErrorMessage = null,
        };
    }
}
