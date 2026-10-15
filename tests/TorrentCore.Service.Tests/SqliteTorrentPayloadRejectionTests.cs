using Microsoft.Data.Sqlite;
using TorrentCore.Core.History;
using TorrentCore.Persistence.Sqlite.History;
using TorrentCore.Persistence.Sqlite.Schema;
using TorrentCore.Persistence.Sqlite.Torrents;
using TorrentCore.Core.Torrents;
using TorrentCore.Contracts.Torrents;

namespace TorrentCore.Service.Tests;

// Uses only a newly generated disposable TorrentCore database, never installed/configured storage or TVMaze.
public sealed class SqliteTorrentPayloadRejectionTests
{
    [Fact]
    public async Task HistoryOwnsRejectionStateAndNormalCleanupRemovesAdmissionBlock()
    {
        var directory = Path.Combine(Path.GetTempPath(), "torrentcore-executable-unit-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "disposable.db");
        try
        {
            await new SqliteSchemaMigrator(path).ApplyMigrationsAsync(default);
            await new SqliteSchemaMigrator(path).ApplyMigrationsAsync(default);
            var history = new SqliteTorrentHistoryStore(path);
            var now = DateTimeOffset.UtcNow.AddDays(-3); var id = Guid.NewGuid();
            const string hash = "ABCDEF0123456789ABCDEF0123456789ABCDEF01";
            await history.InsertAsync(new()
            {
                TorrentId = id, Name = "payload", MagnetUri = "magnet:?xt=urn:btih:" + hash, InfoHash = hash,
                LatestTorrentState = "Error", LatestProgressPercent = 0, LatestDownloadedBytes = 0, LatestUploadedBytes = 0,
                LatestDownloadRateBytesPerSecond = 0, LatestUploadRateBytesPerSecond = 0, LatestTrackerCount = 0,
                LatestConnectedPeerCount = 0, SubmittedAtUtc = now, LastUpdatedAtUtc = now,
                InvokeCompletionCallback = false, DataDeleted = false, RemovedByCleanupPolicy = false,
            }, default);
            var store = new SqliteTorrentPayloadRejectionStore(path);
            var row = new TorrentPayloadRejection(id, hash, "/downloads", ["/downloads/payload"], ["setup.exe"]);
            await store.SaveAsync(row, default);
            Assert.False(await store.IsRejectedAsync(hash, default)); // Detection is not a rejection until season exclusion is resolved.
            await store.SaveAsync(row with { Phase = "Removing" }, default);
            Assert.True(await store.IsRejectedAsync(hash.ToLowerInvariant(), default));
            var states = new SqliteTorrentStateStore(path);
            var submission = new TorrentSnapshot { TorrentId = Guid.NewGuid(), Name = "same hash", InfoHash = hash,
                State = TorrentState.Queued, DesiredState = TorrentDesiredState.Runnable, MagnetUri = "magnet:?xt=urn:btih:" + hash,
                SavePath = "/downloads/payload", ProgressPercent = 0, DownloadedBytes = 0, UploadedBytes = 0,
                DownloadRateBytesPerSecond = 0, UploadRateBytesPerSecond = 0, TrackerCount = 0, ConnectedPeerCount = 0, AddedAtUtc = now };
            await Assert.ThrowsAsync<TorrentPayloadRejectedException>(() => states.InsertAsync(submission, default));
            Assert.Equal("Removing", (await new SqliteTorrentPayloadRejectionStore(path).GetAsync(id, default))!.Phase);
            // Ordinary history updates preserve the additive rejection column.
            var ordinary = (await history.GetAsync(id, default))!; await history.UpdateAsync(ordinary, default);
            Assert.Single(await store.ListPendingAsync(default));
            await store.SaveAsync(row with { Phase = "Notified" }, default);
            Assert.True((await history.GetAsync(id, default))!.DataDeleted);
            Assert.Empty(await store.ListPendingAsync(default));
            Assert.Equal(now, (await history.GetAsync(id, default))!.LastUpdatedAtUtc);
            Assert.Equal(1, await history.DeleteInactiveBeforeAsync(DateTimeOffset.UtcNow.AddDays(-2), default));
            Assert.False(await store.IsRejectedAsync(hash, default)); Assert.Null(await store.GetAsync(id, default));
            await states.InsertAsync(submission, default); // The unchanged cleanup also releases the transaction-level block.
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(row, default));
            await using var connection = new SqliteConnection("Data Source=" + path);
            await connection.OpenAsync(); using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = 23;";
            Assert.Equal(1L, await command.ExecuteScalarAsync());
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) { Directory.Delete(directory, true); } }
    }
}
