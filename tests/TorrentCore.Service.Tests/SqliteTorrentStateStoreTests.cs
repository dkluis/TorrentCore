using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Torrents;
using TorrentCore.Persistence.Sqlite.Schema;
using TorrentCore.Persistence.Sqlite.Torrents;

namespace TorrentCore.Service.Tests;

public sealed class SqliteTorrentStateStoreTests
{
    [Fact]
    public async Task InsertAndGet_PreservesCallbackLifecycleFields()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"torrentcore-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);
        var databaseFilePath = Path.Combine(rootPath, "torrentcore.db");

        try
        {
            var migrator = new SqliteSchemaMigrator(databaseFilePath);
            await migrator.ApplyMigrationsAsync(CancellationToken.None);

            var store = new SqliteTorrentStateStore(databaseFilePath);
            var torrent = CreateSnapshot();

            await store.InsertAsync(torrent, CancellationToken.None);

            var reloaded = await store.GetAsync(torrent.TorrentId, CancellationToken.None);

            Assert.NotNull(reloaded);
            Assert.Equal(TorrentCompletionCallbackState.Failed, reloaded.CompletionCallbackState);
            Assert.Equal(torrent.CompletionCallbackPendingSinceUtc, reloaded.CompletionCallbackPendingSinceUtc);
            Assert.Equal(torrent.CompletionCallbackInvokedAtUtc, reloaded.CompletionCallbackInvokedAtUtc);
            Assert.Equal("The callback exited with code 1.", reloaded.CompletionCallbackLastError);
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
    public async Task UpdateAfterDelete_DoesNotRecreateTorrentRow()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"torrentcore-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);
        var databaseFilePath = Path.Combine(rootPath, "torrentcore.db");

        try
        {
            var migrator = new SqliteSchemaMigrator(databaseFilePath);
            await migrator.ApplyMigrationsAsync(CancellationToken.None);

            var store = new SqliteTorrentStateStore(databaseFilePath);
            var torrent = CreateSnapshot();

            await store.InsertAsync(torrent, CancellationToken.None);
            await store.DeleteAsync(torrent.TorrentId, CancellationToken.None);

            torrent.State = TorrentState.Downloading;
            torrent.ProgressPercent = 42;
            torrent.DownloadedBytes = 420;
            torrent.LastActivityAtUtc = DateTimeOffset.UtcNow;

            await store.UpdateAsync(torrent, CancellationToken.None);

            var reloaded = await store.GetAsync(torrent.TorrentId, CancellationToken.None);
            Assert.Null(reloaded);
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
    public async Task Update_PreservesStoredCallbackFeedbackAgainstStaleSnapshot()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"torrentcore-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);
        var databaseFilePath = Path.Combine(rootPath, "torrentcore.db");

        try
        {
            var migrator = new SqliteSchemaMigrator(databaseFilePath);
            await migrator.ApplyMigrationsAsync(CancellationToken.None);

            var store = new SqliteTorrentStateStore(databaseFilePath);
            var now = DateTimeOffset.UtcNow;
            var stored = CreateSnapshot();
            stored.CompletionCallbackState = TorrentCompletionCallbackState.Invoked;
            stored.CompletionCallbackPendingSinceUtc = now;
            stored.CompletionCallbackInvokedAtUtc = now.AddSeconds(5);
            stored.CompletionCallbackLastError = null;
            stored.CompletionCallbackFeedbackReceivedAtUtc = now.AddSeconds(10);
            stored.CompletionCallbackFeedbackJson = """{"FinalState":"Success"}""";

            await store.InsertAsync(stored, CancellationToken.None);

            var stale = CreateSnapshot(stored.TorrentId);
            stale.CompletionCallbackState = TorrentCompletionCallbackState.WaitingForFeedback;
            stale.CompletionCallbackPendingSinceUtc = stored.CompletionCallbackPendingSinceUtc;
            stale.CompletionCallbackInvokedAtUtc = stored.CompletionCallbackInvokedAtUtc;
            stale.CompletionCallbackLastError = null;
            stale.CompletionCallbackFeedbackReceivedAtUtc = null;
            stale.CompletionCallbackFeedbackJson = null;
            stale.State = TorrentState.Completed;
            stale.ProgressPercent = 100;
            stale.DownloadedBytes = stored.TotalBytes ?? 0;
            stale.LastActivityAtUtc = now.AddMinutes(1);

            await store.UpdateAsync(stale, CancellationToken.None);

            var reloaded = await store.GetAsync(stored.TorrentId, CancellationToken.None);

            Assert.NotNull(reloaded);
            Assert.Equal(TorrentCompletionCallbackState.Invoked, reloaded.CompletionCallbackState);
            Assert.Equal(stored.CompletionCallbackFeedbackReceivedAtUtc, reloaded.CompletionCallbackFeedbackReceivedAtUtc);
            Assert.Equal(stored.CompletionCallbackFeedbackJson, reloaded.CompletionCallbackFeedbackJson);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    private static TorrentSnapshot CreateSnapshot(Guid? torrentId = null)
    {
        var now = DateTimeOffset.UtcNow;

        return new TorrentSnapshot
        {
            TorrentId = torrentId ?? Guid.NewGuid(),
            Name = "Store Regression Torrent",
            CategoryKey = "Movie",
            CompletionCallbackLabel = "Movie",
            InvokeCompletionCallback = true,
            CompletionCallbackState = TorrentCompletionCallbackState.Failed,
            CompletionCallbackPendingSinceUtc = now,
            CompletionCallbackInvokedAtUtc = now.AddMinutes(2),
            CompletionCallbackLastError = "The callback exited with code 1.",
            CompletionCallbackFeedbackReceivedAtUtc = now.AddMinutes(3),
            CompletionCallbackFeedbackJson = """{"FinalState":"Failed"}""",
            State = TorrentState.Queued,
            DesiredState = TorrentDesiredState.Runnable,
            MagnetUri = "magnet:?xt=urn:btih:1111111111111111111111111111111111111111&dn=Store%20Regression",
            InfoHash = "1111111111111111111111111111111111111111",
            DownloadRootPath = "/tmp/torrentcore-tests/downloads",
            SavePath = "/tmp/torrentcore-tests/downloads/Store Regression Torrent",
            ProgressPercent = 0,
            DownloadedBytes = 0,
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
