using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Torrents;
using TorrentCore.Persistence.Sqlite.Schema;
using TorrentCore.Persistence.Sqlite.Torrents;

namespace TorrentCore.Service.Tests;

public sealed class SqliteTorrentStateStoreTests
{
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

    private static TorrentSnapshot CreateSnapshot()
    {
        var now = DateTimeOffset.UtcNow;

        return new TorrentSnapshot
        {
            TorrentId = Guid.NewGuid(),
            Name = "Store Regression Torrent",
            CategoryKey = "Movie",
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
