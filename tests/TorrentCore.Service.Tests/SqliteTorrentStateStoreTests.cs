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
            Assert.Equal(torrent.DownloadColdSinceUtc, reloaded.DownloadColdSinceUtc);
            Assert.Equal(torrent.SeedingPolicyAppliedAtUtc, reloaded.SeedingPolicyAppliedAtUtc);
            Assert.Equal(1, reloaded.OrdinaryQueueOrder);
            Assert.Null(reloaded.PriorityQueueOrder);
            Assert.False(reloaded.IsQueueHeld);
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
    public async Task QueueIntent_RoundTripsThroughFreshStore_AndNormalizesPausedIntent()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"torrentcore-queue-intent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);
        var databaseFilePath = Path.Combine(rootPath, "torrentcore.db");

        try
        {
            await new SqliteSchemaMigrator(databaseFilePath).ApplyMigrationsAsync(CancellationToken.None);
            var store = new SqliteTorrentStateStore(databaseFilePath);

            var priority = CreateSnapshot(infoHash: null);
            priority.PriorityQueueOrder = 7;
            priority.PriorityMetadataAttemptsRemaining = 2;
            var held = CreateSnapshot(infoHash: null);
            held.IsQueueHeld = true;
            var paused = CreateSnapshot(infoHash: null);
            paused.State = TorrentState.Paused;
            paused.DesiredState = TorrentDesiredState.Paused;
            paused.PriorityQueueOrder = 8;
            paused.IsQueueHeld = true;

            await store.InsertAsync(priority, CancellationToken.None);
            await store.InsertAsync(held, CancellationToken.None);
            await store.InsertAsync(paused, CancellationToken.None);

            var freshStore = new SqliteTorrentStateStore(databaseFilePath);
            var reloadedPriority = await freshStore.GetAsync(priority.TorrentId, CancellationToken.None);
            var reloadedHeld = await freshStore.GetAsync(held.TorrentId, CancellationToken.None);
            var reloadedPaused = await freshStore.GetAsync(paused.TorrentId, CancellationToken.None);

            Assert.NotNull(reloadedPriority);
            Assert.Equal(1, reloadedPriority.OrdinaryQueueOrder);
            Assert.Equal(7, reloadedPriority.PriorityQueueOrder);
            Assert.Equal(2, reloadedPriority.PriorityMetadataAttemptsRemaining);
            Assert.False(reloadedPriority.IsQueueHeld);

            Assert.NotNull(reloadedHeld);
            Assert.Equal(2, reloadedHeld.OrdinaryQueueOrder);
            Assert.Null(reloadedHeld.PriorityQueueOrder);
            Assert.True(reloadedHeld.IsQueueHeld);

            Assert.NotNull(reloadedPaused);
            Assert.Equal(3, reloadedPaused.OrdinaryQueueOrder);
            Assert.Null(reloadedPaused.PriorityQueueOrder);
            Assert.False(reloadedPaused.IsQueueHeld);
            Assert.Equal(TorrentDesiredState.Paused, reloadedPaused.DesiredState);
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
    public async Task QueueOrderAllocations_AreSerialized_AndIntentChangesAreAtomic()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"torrentcore-queue-allocation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);
        var databaseFilePath = Path.Combine(rootPath, "torrentcore.db");

        try
        {
            await new SqliteSchemaMigrator(databaseFilePath).ApplyMigrationsAsync(CancellationToken.None);
            var store = new SqliteTorrentStateStore(databaseFilePath);
            var first = CreateSnapshot(infoHash: null);
            var second = CreateSnapshot(infoHash: null);

            await store.InsertAsync(first, CancellationToken.None);
            await store.InsertAsync(second, CancellationToken.None);
            var staleFirst = await store.GetAsync(first.TorrentId, CancellationToken.None);
            Assert.NotNull(staleFirst);

            var firstPriorityRequest = store.AssignNextPriorityQueueOrderAsync(
                first.TorrentId, 3, CancellationToken.None);
            var secondPriorityRequest = store.AssignNextPriorityQueueOrderAsync(
                second.TorrentId, 3, CancellationToken.None);
            var priorityOrders = await Task.WhenAll(firstPriorityRequest, secondPriorityRequest);

            Assert.Equal([1L, 2L], priorityOrders);

            staleFirst.DownloadedBytes = 512;
            await store.UpdateAsync(staleFirst, CancellationToken.None);
            Assert.Equal(
                1,
                (await store.GetAsync(first.TorrentId, CancellationToken.None))!.PriorityQueueOrder
            );

            Assert.True(await store.SetQueueHeldAsync(first.TorrentId, true, CancellationToken.None));
            staleFirst.DownloadedBytes = 1_024;
            await store.UpdateAsync(staleFirst, CancellationToken.None);
            var held = await store.GetAsync(first.TorrentId, CancellationToken.None);
            Assert.NotNull(held);
            Assert.True(held.IsQueueHeld);
            Assert.Null(held.PriorityQueueOrder);

            Assert.Equal(
                3,
                await store.AssignNextPriorityQueueOrderAsync(first.TorrentId, 3, CancellationToken.None)
            );
            var reprioritized = await store.GetAsync(first.TorrentId, CancellationToken.None);
            Assert.NotNull(reprioritized);
            Assert.Equal(3, reprioritized.PriorityQueueOrder);
            Assert.Equal(3, reprioritized.PriorityMetadataAttemptsRemaining);
            Assert.False(reprioritized.IsQueueHeld);

            Assert.Equal(
                3,
                await store.AssignNextOrdinaryQueueOrderAsync(first.TorrentId, CancellationToken.None)
            );
            Assert.Equal(3, (await store.GetAsync(first.TorrentId, CancellationToken.None))!.OrdinaryQueueOrder);
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
    public async Task PriorityMetadataAttempts_YieldToPriorityTail_ThenExpireToOrdinaryTail()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"torrentcore-priority-attempts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);
        var databaseFilePath = Path.Combine(rootPath, "torrentcore.db");

        try
        {
            await new SqliteSchemaMigrator(databaseFilePath).ApplyMigrationsAsync(CancellationToken.None);
            var store = new SqliteTorrentStateStore(databaseFilePath);
            var first = CreateSnapshot(infoHash: null);
            var second = CreateSnapshot(infoHash: null);
            await store.InsertAsync(first, CancellationToken.None);
            await store.InsertAsync(second, CancellationToken.None);
            await store.AssignNextPriorityQueueOrderAsync(first.TorrentId, 3, CancellationToken.None);
            await store.AssignNextPriorityQueueOrderAsync(second.TorrentId, 3, CancellationToken.None);

            Assert.True(await store.YieldPriorityMetadataAttemptAsync(first.TorrentId, 2, CancellationToken.None));
            var yielded = await new SqliteTorrentStateStore(databaseFilePath)
                .GetAsync(first.TorrentId, CancellationToken.None);
            Assert.NotNull(yielded);
            Assert.Equal(3, yielded.PriorityQueueOrder);
            Assert.Equal(2, yielded.PriorityMetadataAttemptsRemaining);
            Assert.Equal(3, yielded.OrdinaryQueueOrder);

            Assert.True(await store.YieldPriorityMetadataAttemptAsync(first.TorrentId, 0, CancellationToken.None));
            var expired = await new SqliteTorrentStateStore(databaseFilePath)
                .GetAsync(first.TorrentId, CancellationToken.None);
            Assert.NotNull(expired);
            Assert.Null(expired.PriorityQueueOrder);
            Assert.Null(expired.PriorityMetadataAttemptsRemaining);
            Assert.Equal(4, expired.OrdinaryQueueOrder);
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
    public async Task ResumeModes_AtomicallyAllocateTailPriorityAndHoldIntent()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"torrentcore-queue-resume-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);
        var databaseFilePath = Path.Combine(rootPath, "torrentcore.db");

        try
        {
            await new SqliteSchemaMigrator(databaseFilePath).ApplyMigrationsAsync(CancellationToken.None);
            var store = new SqliteTorrentStateStore(databaseFilePath);
            var normal = CreatePausedSnapshot();
            var priority = CreatePausedSnapshot();
            var held = CreatePausedSnapshot();
            await store.InsertAsync(normal, CancellationToken.None);
            await store.InsertAsync(priority, CancellationToken.None);
            await store.InsertAsync(held, CancellationToken.None);

            var resumedAt = DateTimeOffset.UtcNow;
            var resumedNormal = await store.ResumeWithQueueIntentAsync(
                normal.TorrentId, TorrentQueueResumeMode.Normal, resumedAt, 3, CancellationToken.None);
            var resumedPriority = await store.ResumeWithQueueIntentAsync(
                priority.TorrentId, TorrentQueueResumeMode.Priority, resumedAt, 3, CancellationToken.None);
            var resumedHeld = await store.ResumeWithQueueIntentAsync(
                held.TorrentId, TorrentQueueResumeMode.Hold, resumedAt, 3, CancellationToken.None);

            Assert.NotNull(resumedNormal);
            Assert.NotNull(resumedPriority);
            Assert.NotNull(resumedHeld);
            Assert.Equal([4L, 5L, 6L], new[]
            {
                resumedNormal.OrdinaryQueueOrder!.Value,
                resumedPriority.OrdinaryQueueOrder!.Value,
                resumedHeld.OrdinaryQueueOrder!.Value,
            });
            Assert.Null(resumedNormal.PriorityQueueOrder);
            Assert.Equal(1, resumedPriority.PriorityQueueOrder);
            Assert.False(resumedPriority.IsQueueHeld);
            Assert.Null(resumedHeld.PriorityQueueOrder);
            Assert.True(resumedHeld.IsQueueHeld);

            Assert.Equal(1, await store.ReleaseQueueHoldsAsync(
                [held.TorrentId], CancellationToken.None));
            Assert.False((await store.GetAsync(held.TorrentId, CancellationToken.None))!.IsQueueHeld);
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
            stale.SeedingPolicyAppliedAtUtc = null;
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
            Assert.Equal(stored.SeedingPolicyAppliedAtUtc, reloaded.SeedingPolicyAppliedAtUtc);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    private static TorrentSnapshot CreateSnapshot(Guid? torrentId = null,
        string? infoHash = "1111111111111111111111111111111111111111")
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
            InfoHash = infoHash,
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
            DownloadColdSinceUtc = now.AddHours(-2),
            SeedingPolicyAppliedAtUtc = now.AddMinutes(-1),
            LastActivityAtUtc = now,
        };
    }

    private static TorrentSnapshot CreatePausedSnapshot()
    {
        var snapshot = CreateSnapshot(infoHash: null);
        snapshot.State = TorrentState.Paused;
        snapshot.DesiredState = TorrentDesiredState.Paused;
        return snapshot;
    }
}
