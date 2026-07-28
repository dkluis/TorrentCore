using TorrentCore.Contracts.History;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.History;
using TorrentCore.Core.Torrents;
using TorrentCore.Persistence.Sqlite.History;
using TorrentCore.Persistence.Sqlite.Schema;
using TorrentCore.Persistence.Sqlite.Torrents;

namespace TorrentCore.Service.Tests;

public sealed class SqliteTorrentHistoryStoreTests
{
    [Fact]
    public async Task InsertAndGet_PreservesInsertedHistoryRow()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"torrentcore-history-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);
        var databaseFilePath = Path.Combine(rootPath, "torrentcore.db");

        try
        {
            var migrator = new SqliteSchemaMigrator(databaseFilePath);
            await migrator.ApplyMigrationsAsync(CancellationToken.None);

            var store = new SqliteTorrentHistoryStore(databaseFilePath);
            var record = CreateRecord();

            await store.InsertAsync(record, CancellationToken.None);

            var reloaded = await store.GetAsync(record.TorrentId, CancellationToken.None);

            Assert.NotNull(reloaded);
            Assert.Equal(record.TorrentId, reloaded.TorrentId);
            Assert.Equal(record.Name, reloaded.Name);
            Assert.Equal(record.MagnetUri, reloaded.MagnetUri);
            Assert.Equal(record.InfoHash, reloaded.InfoHash);
            Assert.Equal(record.CategoryKey, reloaded.CategoryKey);
            Assert.Equal(record.DownloadRootPath, reloaded.DownloadRootPath);
            Assert.Equal(record.LatestTorrentState, reloaded.LatestTorrentState);
            Assert.Equal(record.LatestProgressPercent, reloaded.LatestProgressPercent);
            Assert.Equal(record.LatestDownloadedBytes, reloaded.LatestDownloadedBytes);
            Assert.Equal(record.SubmittedAtUtc, reloaded.SubmittedAtUtc);
            Assert.Equal(record.LastUpdatedAtUtc, reloaded.LastUpdatedAtUtc);
            Assert.Equal(record.InvokeCompletionCallback, reloaded.InvokeCompletionCallback);
            Assert.Equal(record.CompletionCallbackLabel, reloaded.CompletionCallbackLabel);
            Assert.Equal(record.RemovalKind, reloaded.RemovalKind);
            Assert.Equal(record.ServiceInstanceIdLastSeen, reloaded.ServiceInstanceIdLastSeen);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DeleteInactiveBefore_UsesLastUpdatedExclusiveCutoff_AndProtectsLiveTorrents()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"torrentcore-history-cleanup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);
        var databaseFilePath = Path.Combine(rootPath, "torrentcore.db");

        try
        {
            var migrator = new SqliteSchemaMigrator(databaseFilePath);
            await migrator.ApplyMigrationsAsync(CancellationToken.None);

            var cutoffUtc = new DateTimeOffset(2026, 6, 28, 4, 0, 0, TimeSpan.Zero);
            var activeTorrentId = Guid.NewGuid();
            var oldInactive = CreateRecord(Guid.NewGuid(), cutoffUtc.AddMinutes(-1));
            var oldActive = CreateRecord(activeTorrentId, cutoffUtc.AddMinutes(-1));
            var atCutoff = CreateRecord(Guid.NewGuid(), cutoffUtc);
            var newerInactive = CreateRecord(Guid.NewGuid(), cutoffUtc.AddMinutes(1));

            var store = new SqliteTorrentHistoryStore(databaseFilePath);
            await store.InsertAsync(oldInactive, CancellationToken.None);
            await store.InsertAsync(oldActive, CancellationToken.None);
            await store.InsertAsync(atCutoff, CancellationToken.None);
            await store.InsertAsync(newerInactive, CancellationToken.None);

            var torrentStore = new SqliteTorrentStateStore(databaseFilePath);
            await torrentStore.InsertAsync(CreateSnapshot(activeTorrentId), CancellationToken.None);

            var deletedCount = await store.DeleteInactiveBeforeAsync(cutoffUtc, CancellationToken.None);
            var remaining = await store.ListAsync(CancellationToken.None);

            Assert.Equal(1, deletedCount);
            Assert.DoesNotContain(remaining, record => record.TorrentId == oldInactive.TorrentId);
            Assert.Contains(remaining, record => record.TorrentId == oldActive.TorrentId);
            Assert.Contains(remaining, record => record.TorrentId == atCutoff.TorrentId);
            Assert.Contains(remaining, record => record.TorrentId == newerInactive.TorrentId);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    private static TorrentHistoryRecord CreateRecord(Guid? torrentId = null,
        DateTimeOffset? lastUpdatedAtUtc = null)
    {
        var now = DateTimeOffset.UtcNow;

        return new TorrentHistoryRecord
        {
            TorrentId = torrentId ?? Guid.NewGuid(),
            Name = "History Regression Torrent",
            MagnetUri = "magnet:?xt=urn:btih:2222222222222222222222222222222222222222&dn=History%20Regression",
            InfoHash = "2222222222222222222222222222222222222222",
            CategoryKey = "TV",
            DownloadRootPath = "/tmp/torrentcore-tests/downloads",
            LatestTorrentState = "ResolvingMetadata",
            LatestWaitReason = null,
            LatestErrorMessage = null,
            LatestProgressPercent = 0,
            LatestDownloadedBytes = 0,
            LatestUploadedBytes = 0,
            LatestTotalBytes = null,
            LatestDownloadRateBytesPerSecond = 0,
            LatestUploadRateBytesPerSecond = 0,
            LatestTrackerCount = 0,
            LatestConnectedPeerCount = 0,
            SubmittedAtUtc = now,
            MetadataResolvedAtUtc = null,
            DownloadStartedAtUtc = null,
            DownloadCompletedAtUtc = null,
            SeedingStartedAtUtc = null,
            LastActivityAtUtc = now,
            LastUpdatedAtUtc = lastUpdatedAtUtc ?? now,
            RemovedAtUtc = null,
            InvokeCompletionCallback = true,
            CompletionCallbackLabel = "TV",
            LatestCallbackStatus = null,
            CallbackStartedAtUtc = null,
            CallbackCompletedAtUtc = null,
            CallbackLastError = null,
            DataDeleted = false,
            RemovalReason = null,
            RemovalKind = TorrentRemovalKind.ColdDownloadAbandonment,
            RemovedByCleanupPolicy = false,
            FinalPayloadPath = null,
            ServiceInstanceIdLastSeen = Guid.NewGuid(),
        };
    }

    private static TorrentSnapshot CreateSnapshot(Guid torrentId)
    {
        var now = DateTimeOffset.UtcNow;
        return new TorrentSnapshot
        {
            TorrentId = torrentId,
            Name = "Protected Live Torrent",
            State = TorrentState.Downloading,
            DesiredState = TorrentDesiredState.Runnable,
            MagnetUri = "magnet:?xt=urn:btih:4444444444444444444444444444444444444444",
            InfoHash = "4444444444444444444444444444444444444444",
            SavePath = "/tmp/torrentcore-tests/protected-live-torrent",
            ProgressPercent = 50,
            DownloadedBytes = 512,
            UploadedBytes = 0,
            TotalBytes = 1_024,
            DownloadRateBytesPerSecond = 0,
            UploadRateBytesPerSecond = 0,
            TrackerCount = 0,
            ConnectedPeerCount = 0,
            AddedAtUtc = now,
            LastActivityAtUtc = now,
        };
    }
}
