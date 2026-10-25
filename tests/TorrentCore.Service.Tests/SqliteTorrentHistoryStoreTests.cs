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
    // All storage and activity logging below use a disposable temporary SQLite database.
    [Theory]
    [InlineData("Executable", "setup.exe")]
    [InlineData("ProhibitedExtension", "episode.7z")]
    [InlineData("MissingVideo", "")]
    public async Task RejectionHistoryProjectsEvidenceAndFiltersBeforeTheLimit(string reason, string offending)
    {
        var root = Path.Combine(Path.GetTempPath(), "torrentcore-rejection-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "torrentcore.db");
        try
        {
            await new SqliteSchemaMigrator(file).ApplyMigrationsAsync(default);
            var store = new SqliteTorrentHistoryStore(file);
            var rejected = CreateRecord(lastUpdatedAtUtc: DateTimeOffset.UtcNow.AddHours(-2));
            await store.InsertAsync(rejected, default);
            await store.InsertAsync(CreateRecord(), default); // A newer ordinary record must not hide the older match.
            var nextAttempt = DateTimeOffset.UtcNow.AddMinutes(1);
            await new SqliteTorrentPayloadRejectionStore(file).SaveAsync(new(rejected.TorrentId, rejected.InfoHash!, "/downloads", [],
                offending.Length == 0 ? [] : [offending], "EligibilityPending", nextAttempt)
                { RejectionReason = reason, ObservedFiles = ["episode.srt"] }, default);
            var service = new TorrentCore.Service.Application.TorrentHistoryService(store, new TorrentCore.Service.Configuration.ServiceInstanceContext());
            var row = Assert.Single(await service.GetHistoryAsync(new() { IncludePayloadRejection = true, RejectionReason = reason.ToLowerInvariant(), Take = 1 }, default));
            Assert.Equal(rejected.TorrentId, row.TorrentId);
            Assert.Equal(reason, row.PayloadRejection?.Reason);
            Assert.Equal("EligibilityPending", row.PayloadRejection?.Phase);
            Assert.Equal(new[] { "episode.srt" }, row.PayloadRejection?.ObservedFiles);
            Assert.Equal(nextAttempt, row.PayloadRejection?.NextAttemptAt);
            Assert.Equal(reason, Assert.Single((await service.GetFilterOptionsAsync(default, true)).RejectionReasons!));
            var detail = await service.GetHistoryByTorrentIdAsync(rejected.TorrentId, default, true);
            Assert.Equal(row.PayloadRejection?.OffendingFiles, detail.PayloadRejection?.OffendingFiles);
            Assert.Null((await service.GetHistoryAsync(new() { Take = 1 }, default)).Single().PayloadRejection);
            // Build 17's strict decoders must see exactly the previous field set without opt-in.
            var legacyList = await service.GetHistoryAsync(new(), default);
            var legacyDetail = await service.GetHistoryByTorrentIdAsync(rejected.TorrentId, default);
            var legacyOptions = await service.GetFilterOptionsAsync(default);
            Assert.DoesNotContain("PayloadRejection", System.Text.Json.JsonSerializer.Serialize(legacyList));
            Assert.DoesNotContain("PayloadRejection", System.Text.Json.JsonSerializer.Serialize(legacyDetail));
            Assert.DoesNotContain("RejectionReasons", System.Text.Json.JsonSerializer.Serialize(legacyOptions));
            // Ordinary lifecycle updates must leave the cleanup coordinator's JSON untouched.
            var reloaded = (await store.GetAsync(rejected.TorrentId, default))!;
            var json = reloaded.PayloadRejectionJson;
            reloaded.Name = "Updated name";
            await store.UpdateAsync(reloaded, default);
            Assert.Equal(json, (await store.GetAsync(rejected.TorrentId, default))!.PayloadRejectionJson);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task InvalidRejectionHistoryReturnsControlledFailureAndPersistsError()
    {
        var root = Path.Combine(Path.GetTempPath(), "torrentcore-rejection-ui-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "torrentcore.db");
        try
        {
            await new SqliteSchemaMigrator(file).ApplyMigrationsAsync(default);
            var store = new SqliteTorrentHistoryStore(file);
            var record = CreateRecord(); await store.InsertAsync(record, default);
            await using (var db = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + file))
            {
                await db.OpenAsync(); using var command = db.CreateCommand();
                command.CommandText = "UPDATE torrent_history SET executable_rejection_json = '{' WHERE torrent_id = $id";
                command.Parameters.AddWithValue("$id", record.TorrentId.ToString());
                await command.ExecuteNonQueryAsync();
            }
            var logs = new TorrentCore.Persistence.Sqlite.Logging.SqliteActivityLogService(file, 100);
            var service = new TorrentCore.Service.Application.TorrentHistoryService(store, new TorrentCore.Service.Configuration.ServiceInstanceContext(), logs);
            var failure = await Assert.ThrowsAsync<TorrentCore.Service.Application.ServiceOperationException>(() => service.GetHistoryAsync(new() { IncludePayloadRejection = true }, default));
            Assert.Equal(503, failure.StatusCode);
            var entries = await logs.GetRecentAsync(new(), default);
            Assert.Contains(entries, x => x.EventType == "history.read.GetHistoryAsync" && x.Level == TorrentCore.Core.Diagnostics.ActivityLogLevel.Error);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

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
