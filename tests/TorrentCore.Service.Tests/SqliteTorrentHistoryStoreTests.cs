using TorrentCore.Contracts.History;
using TorrentCore.Core.History;
using TorrentCore.Persistence.Sqlite.History;
using TorrentCore.Persistence.Sqlite.Schema;

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

    private static TorrentHistoryRecord CreateRecord()
    {
        var now = DateTimeOffset.UtcNow;

        return new TorrentHistoryRecord
        {
            TorrentId = Guid.NewGuid(),
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
            LastUpdatedAtUtc = now,
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
}
