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
                CreateSnapshot(TorrentState.ResolvingMetadata, addedAt, addedAt, null, 0),
                CancellationToken.None);

            await service.ObserveSnapshotAsync(
                CreateSnapshot(TorrentState.Queued, addedAt, metadataAt, 1_024, 0),
                CancellationToken.None);

            await service.ObserveSnapshotAsync(
                CreateSnapshot(TorrentState.Downloading, addedAt, downloadAt, 1_024, 25),
                CancellationToken.None);

            var afterStart = await store.GetAsync(TorrentId, CancellationToken.None);

            await service.ObserveSnapshotAsync(
                CreateSnapshot(TorrentState.Downloading, addedAt, downloadAt.AddMinutes(1), 1_024, 50),
                CancellationToken.None);

            await service.ObserveSnapshotAsync(
                CreateSnapshot(
                    TorrentState.Seeding,
                    addedAt,
                    seedingAt,
                    1_024,
                    100,
                    seedingStartedAtUtc: seedingAt,
                    completedAtUtc: completionAt),
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

    [Fact]
    public async Task ObserveSnapshot_TracksCallbackLifecycle_AndRetryResetsAttemptTimestamps()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"torrentcore-history-callback-{Guid.NewGuid():N}");
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
            var pendingAt = addedAt.AddMinutes(1);
            var failedAt = addedAt.AddMinutes(2);
            var retryAt = addedAt.AddMinutes(3);
            var invokedAt = addedAt.AddMinutes(4);

            await service.ObserveSnapshotAsync(
                CreateSnapshot(
                    TorrentState.Completed,
                    addedAt,
                    pendingAt,
                    1_024,
                    100,
                    completedAtUtc: addedAt,
                    callbackState: TorrentCompletionCallbackState.PendingFinalization,
                    callbackPendingSinceUtc: pendingAt),
                CancellationToken.None);

            await service.ObserveSnapshotAsync(
                CreateSnapshot(
                    TorrentState.Completed,
                    addedAt,
                    failedAt,
                    1_024,
                    100,
                    completedAtUtc: addedAt,
                    callbackState: TorrentCompletionCallbackState.Failed,
                    callbackPendingSinceUtc: pendingAt,
                    callbackLastError: "failed"),
                CancellationToken.None);

            var failedHistory = await store.GetAsync(TorrentId, CancellationToken.None);

            await service.ObserveSnapshotAsync(
                CreateSnapshot(
                    TorrentState.Completed,
                    addedAt,
                    retryAt,
                    1_024,
                    100,
                    completedAtUtc: addedAt,
                    callbackState: TorrentCompletionCallbackState.PendingFinalization,
                    callbackPendingSinceUtc: retryAt),
                CancellationToken.None);

            await service.ObserveSnapshotAsync(
                CreateSnapshot(
                    TorrentState.Completed,
                    addedAt,
                    invokedAt,
                    1_024,
                    100,
                    completedAtUtc: addedAt,
                    callbackState: TorrentCompletionCallbackState.Invoked,
                    callbackPendingSinceUtc: retryAt,
                    callbackInvokedAtUtc: invokedAt),
                CancellationToken.None);

            var finalHistory = await store.GetAsync(TorrentId, CancellationToken.None);

            Assert.NotNull(failedHistory);
            Assert.NotNull(finalHistory);
            Assert.Equal(pendingAt, failedHistory.CallbackStartedAtUtc);
            Assert.Equal(failedAt, failedHistory.CallbackCompletedAtUtc);
            Assert.Equal("Failed", failedHistory.LatestCallbackStatus);
            Assert.Equal(retryAt, finalHistory.CallbackStartedAtUtc);
            Assert.Equal(invokedAt, finalHistory.CallbackCompletedAtUtc);
            Assert.Equal("Invoked", finalHistory.LatestCallbackStatus);
            Assert.Null(finalHistory.CallbackLastError);
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
        DateTimeOffset lastActivityAtUtc, long? totalBytes, double progressPercent,
        DateTimeOffset? seedingStartedAtUtc = null, DateTimeOffset? completedAtUtc = null,
        TorrentCompletionCallbackState? callbackState = null, DateTimeOffset? callbackPendingSinceUtc = null,
        DateTimeOffset? callbackInvokedAtUtc = null, string? callbackLastError = null)
    {
        return new TorrentSnapshot
        {
            TorrentId = TorrentId,
            Name = "History Service Torrent",
            CategoryKey = "Movie",
            CompletionCallbackLabel = "Movie",
            InvokeCompletionCallback = true,
            CompletionCallbackState = callbackState,
            CompletionCallbackPendingSinceUtc = callbackPendingSinceUtc,
            CompletionCallbackInvokedAtUtc = callbackInvokedAtUtc,
            CompletionCallbackLastError = callbackLastError,
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
