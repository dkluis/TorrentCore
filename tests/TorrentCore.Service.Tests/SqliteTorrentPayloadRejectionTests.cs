using Microsoft.Data.Sqlite;
using TorrentCore.Core.History;
using TorrentCore.Persistence.Sqlite.History;
using TorrentCore.Persistence.Sqlite.Schema;
using TorrentCore.Persistence.Sqlite.Torrents;
using TorrentCore.Core.Torrents;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Service.Configuration;

namespace TorrentCore.Service.Tests;

// Uses only a newly generated disposable TorrentCore database, never installed/configured storage or TVMaze.
public sealed class SqliteTorrentPayloadRejectionTests
{
    [Fact]
    public async Task RoutineScansDoNotWriteSuccessLogsButReadsAndScanFailuresRemainLogged()
    {
        var directory = Path.Combine(Path.GetTempPath(), "torrentcore-rejection-logging-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "disposable.db");
        try
        {
            await new SqliteSchemaMigrator(path).ApplyMigrationsAsync(default);
            var logs = new CapturedLogs();
            var store = new SqliteTorrentPayloadRejectionStore(path, logs, () => true);
            for (var tick = 0; tick < 100; tick++)
            {
                Assert.Empty(await store.ListPendingAsync(default));
            }
            Assert.Empty(logs.Requests);

            Assert.Null(await store.GetAsync(Guid.NewGuid(), default));
            Assert.Equal(2, logs.Requests.Count);
            Assert.All(logs.Requests, entry => Assert.Equal("history.payload_rejection.read", entry.EventType));
            logs.Requests.Clear();

            // Break only this disposable schema to verify that scan errors still reach the activity channel.
            await using (var connection = new SqliteConnection("Data Source=" + path))
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "DROP TABLE torrent_history;";
                await command.ExecuteNonQueryAsync();
            }
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.ListPendingAsync(default));
            var failure = Assert.Single(logs.Requests);
            Assert.Equal("history.payload_rejection.list", failure.EventType);
            Assert.Equal(ActivityLogLevel.Error, failure.Level);
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) { Directory.Delete(directory, true); } }
    }

    [Fact]
    public async Task ExistingDiagnosticsSwitchGatesDebugLiveWithoutSuppressingErrors()
    {
        var directory = Path.Combine(Path.GetTempPath(), "torrentcore-rejection-debug-switch-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "disposable.db");
        try
        {
            await new SqliteSchemaMigrator(path).ApplyMigrationsAsync(default);
            var logs = new CapturedLogs();
            var state = new RuntimeTickDurationSummaryState();
            var store = new SqliteTorrentPayloadRejectionStore(path, logs, () => state.Enabled);
            const string hash = "ABCDEF0123456789ABCDEF0123456789ABCDEF01";

            Assert.False(await store.IsRejectedAsync(hash, default));
            Assert.Empty(logs.Requests);
            state.Set(true);
            Assert.False(await store.IsRejectedAsync(hash, default));
            Assert.Equal(2, logs.Requests.Count);
            Assert.All(logs.Requests, entry => Assert.Equal(ActivityLogLevel.Debug, entry.Level));
            state.Set(false);
            Assert.False(await store.IsRejectedAsync(hash, default));
            Assert.Equal(2, logs.Requests.Count); // No new detail rows after the live switch is turned off.
            logs.Requests.Clear();

            var row = new TorrentPayloadRejection(Guid.NewGuid(), hash, "/downloads", ["/downloads/payload"], ["setup.exe"]);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(row, default));
            var failure = Assert.Single(logs.Requests);
            Assert.Equal(ActivityLogLevel.Error, failure.Level);
            Assert.Equal("history.payload_rejection.save", failure.EventType);
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) { Directory.Delete(directory, true); } }
    }

    [Theory]
    [InlineData("Executable", "Notified")]
    [InlineData("ProhibitedExtension", "Notified")]
    [InlineData("MissingVideo", "Notified")]
    [InlineData("MissingVideo", "NotificationNotRequired")]
    public async Task HistoryOwnsRejectionStateAndNormalCleanupRemovesAdmissionBlock(string reason, string terminalPhase)
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
            var row = new TorrentPayloadRejection(id, hash, "/downloads", ["/downloads/payload"], reason == "MissingVideo" ? [] : ["setup.exe"], "EligibilityPending") { RejectionReason = reason, ObservedFiles = ["payload.srt"] };
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
            await store.SaveAsync(row with { Phase = terminalPhase }, default);
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

    private sealed class CapturedLogs : IActivityLogService
    {
        public List<ActivityLogWriteRequest> Requests { get; } = [];
        public Task WriteAsync(ActivityLogWriteRequest request, CancellationToken cancellationToken)
        { Requests.Add(request); return Task.CompletedTask; }
        public Task EnsureInitializedAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ActivityLogEntry>> GetRecentAsync(ActivityLogQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ActivityLogFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> DeleteByTorrentIdAsync(Guid torrentId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> DeleteOrphanedTorrentLogsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> DeleteInactiveBeforeAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
