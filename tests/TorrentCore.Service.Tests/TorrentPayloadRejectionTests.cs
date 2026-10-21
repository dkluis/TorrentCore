using TorrentCore.Core.History;
using TorrentCore.Core.Torrents;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Service.Application;
using TorrentCore.Service.Engine;

namespace TorrentCore.Service.Tests;

// Direct inputs and narrow delegates; no installed database, engine, filesystem or external API.
public sealed class TorrentPayloadRejectionTests
{
    private const string Hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private static TorrentSnapshot Snapshot(string category = "TV") => new()
    {
        TorrentId = Guid.NewGuid(), Name = "payload", CategoryKey = category, State = TorrentState.Downloading,
        DesiredState = TorrentDesiredState.Runnable, MagnetUri = "magnet:?xt=urn:btih:" + Hash, InfoHash = Hash,
        DownloadRootPath = "/downloads", SavePath = "/downloads/payload", ProgressPercent = 0,
        DownloadedBytes = 0, UploadedBytes = 0, DownloadRateBytesPerSecond = 0, UploadRateBytesPerSecond = 0,
        TrackerCount = 0, ConnectedPeerCount = 0, AddedAtUtc = DateTimeOffset.UtcNow,
    };
    private static TorrentPayloadRejection Row(string phase = "Removed") =>
        new(Guid.NewGuid(), Hash, "/downloads", ["/downloads/payload"], ["payload/install.EXE"], phase);

    [Theory]
    [InlineData("TV", true)] [InlineData("Movie", true)] [InlineData("Music", false)] [InlineData("Audiobook", false)]
    public async Task ManifestInspectionIsScopedToMediaCategories(string category, bool blocked)
    {
        var events = new List<string>(); var store = new Store(events); var effects = new Effects(events);
        var service = Service(store, effects);
        var result = await service.InspectAsync(Snapshot(category), ["episode.mkv", "nested/setup.EXE"], ["/downloads/payload"],
            () => { events.Add("stop"); return Task.CompletedTask; }, () => { events.Add("remove"); return Task.CompletedTask; }, default);
        Assert.Equal(blocked, result);
        if (blocked)
        {
            Assert.DoesNotContain("report", events);
            await service.ReconcileRemovedAsync(default);
            Assert.True(events.IndexOf("save:Removing") < events.IndexOf("remove"));
            Assert.True(events.IndexOf("delete") < events.IndexOf("report"));
            Assert.Equal("Notified", store.Saved!.Phase);
        }
        else { Assert.Empty(events); }
    }
    [Theory]
    [InlineData("EligibilityPending")]
    [InlineData("Excluded")]
    public async Task EarlierSeasonCheckIntentNowRemovesWithoutPreRemovalNotification(string phase)
    {
        var events = new List<string>(); var snapshot = Snapshot();
        var row = new TorrentPayloadRejection(snapshot.TorrentId, Hash, "/downloads", ["/downloads/payload"], ["setup.exe"], phase);
        var store = new Store(events) { Saved = row }; var effects = new Effects(events);
        var service = Service(store, effects);
        Assert.True(await service.InspectAsync(snapshot, ["setup.exe"], ["/downloads/payload"],
            () => Task.CompletedTask, () => { events.Add("remove"); return Task.CompletedTask; }, default));
        Assert.DoesNotContain("report", events);
        await service.ReconcileRemovedAsync(default);
        Assert.True(events.IndexOf("remove") < events.IndexOf("delete"));
        Assert.True(events.IndexOf("delete") < events.IndexOf("report"));
        Assert.Equal("Notified", store.Saved!.Phase);
    }
    [Fact]
    public async Task FailedIntentSaveNeverRemovesOrReportsAcceptance()
    {
        var events = new List<string>(); var store = new Store(events) { ThrowSave = true };
        Assert.True(await Service(store, new Effects(events)).InspectAsync(Snapshot(), ["setup.exe"], ["/downloads/payload"],
            () => Task.CompletedTask, () => { events.Add("remove"); return Task.CompletedTask; }, default));
        Assert.DoesNotContain("remove", events); Assert.DoesNotContain("report", events);
    }
    [Fact]
    public async Task ConflictingStoredIdentityCannotRemoveTheCurrentPayload()
    {
        var events = new List<string>(); var store = new Store(events) { Saved = Row("Removing") };
        var service = Service(store, new Effects(events));
        Assert.True(await service.InspectAsync(Snapshot(), ["setup.exe"], ["/downloads/payload"], () => Task.CompletedTask,
            () => { events.Add("remove"); return Task.CompletedTask; }, default));
        Assert.DoesNotContain("remove", events); Assert.DoesNotContain("delete", events);
    }
    [Fact]
    public async Task FailedRemovalCannotDeleteOrNotifyUntilRemovalIsConfirmed()
    {
        var events = new List<string>(); var store = new Store(events); var snapshot = Snapshot();
        var service = Service(store, new Effects(events));
        Assert.True(await service.InspectAsync(snapshot, ["setup.exe"], ["/downloads/payload"], () => Task.CompletedTask,
            () => throw new IOException("Removal failed"), default));
        Assert.Equal("Removing", store.Saved!.Phase);
        Assert.DoesNotContain("delete", events); Assert.DoesNotContain("report", events);
        Assert.True(service.IsBlocked(snapshot.TorrentId));
    }
    [Fact]
    public async Task LostNotificationReplyRemainsPendingAndReusesTheSameHistoryEvent()
    {
        var events = new List<string>(); var row = Row("Deleted"); var store = new Store(events) { Saved = row };
        await Service(store, new Effects(events) { Unconfirmed = true }).ReconcileRemovedAsync(default);
        Assert.Equal("Deleted", store.Saved!.Phase); Assert.NotNull(store.Saved.NextAttemptAtUtc);
        store.Saved = store.Saved with { NextAttemptAtUtc = null };
        await Service(store, new Effects(events)).ReconcileRemovedAsync(default);
        Assert.Equal("Notified", store.Saved.Phase); Assert.Equal(row.TorrentId, store.Saved.TorrentId); Assert.DoesNotContain("delete", events);
    }

    [Fact]
    public async Task PendingCallbackDoesNotBlockTicksOrLaunchAgainAndItsReplyIsConsumedWithoutRetryDelay()
    {
        var events = new List<string>(); var store = new Store(events) { Saved = Row("Deleted") };
        var completion = new TaskCompletionSource<PayloadRejectionReply>();
        var effects = new Effects(events) { PendingReply = completion.Task };
        var service = Service(store, effects);
        var pass = service.ReconcileRemovedAsync(default);
        Assert.True(pass.IsCompleted); await pass;
        Assert.Equal("Deleted", store.Saved!.Phase);
        await service.ReconcileRemovedAsync(default);
        Assert.Single(events, value => value == "report");
        completion.SetResult(new(true));
        await service.ReconcileRemovedAsync(default);
        Assert.Equal("Notified", store.Saved.Phase);
        Assert.Single(events, value => value == "report");
    }
    [Fact]
    public async Task CleanupFailureNeverSendsConfirmedDeletion()
    {
        var events = new List<string>(); var store = new Store(events) { Saved = Row() };
        await Service(store, new Effects(events) { ThrowDelete = true }).ReconcileRemovedAsync(default);
        Assert.Equal("Removed", store.Saved.Phase); Assert.DoesNotContain("report", events);
    }
    [Fact]
    public async Task RestartAfterDeletionResendsSameEventWithoutRepeatingDeletion()
    {
        var events = new List<string>(); var row = Row("Deleted"); var store = new Store(events) { Saved = row };
        await Service(store, new Effects(events)).ReconcileRemovedAsync(default);
        Assert.DoesNotContain("delete", events); Assert.Contains("report", events);
        Assert.Equal(row.TorrentId, store.Saved!.TorrentId); Assert.Equal("Notified", store.Saved.Phase);
    }
    [Fact]
    public async Task CrashAfterEngineRemovalResumesBeforeDeletingData()
    {
        var events = new List<string>(); var store = new Store(events) { Saved = Row("Removing") };
        await Service(store, new Effects(events)).ReconcileRemovedAsync(default);
        Assert.True(events.IndexOf("mark_removed") < events.IndexOf("delete")); Assert.Equal("Notified", store.Saved!.Phase);
    }
    [Fact]
    public async Task AdmissionUsesOnlyRetainedRejectionHistoryAndContainsReadFailures()
    {
        var events = new List<string>(); var store = new Store(events) { Rejected = true }; var service = Service(store, new Effects(events));
        Assert.Equal(409, (await Assert.ThrowsAsync<ServiceOperationException>(() => service.EnsureAdmissionAllowedAsync(Hash, default))).StatusCode);
        store.Rejected = false; await service.EnsureAdmissionAllowedAsync(Hash, default);
        store.ThrowRead = true;
        Assert.Equal(503, (await Assert.ThrowsAsync<ServiceOperationException>(() => service.EnsureAdmissionAllowedAsync(Hash, default))).StatusCode);
    }
    [Fact]
    public void DetectionUsesFinalMetadataExtensionAndCleanupCannotLeaveTheRecordedRoot()
    {
        Assert.Equal(new[] { "nested/setup.ExE" }, TorrentPayloadRejectionCoordinator.Executables(["episode.exe.mkv", "nested/setup.ExE", "subs.srt"]));
        Assert.Equal(new[] { "/downloads/payload" }, TorrentPayloadRejectionEffects.ValidatePaths("/downloads", ["/downloads", "/downloads/payload"]));
        Assert.Throws<InvalidOperationException>(() => TorrentPayloadRejectionEffects.ValidatePaths("/downloads", ["/downloads/../other"]));
        Assert.Throws<InvalidOperationException>(() => TorrentPayloadRejectionEffects.ValidatePaths("/downloads", ["/downloads"]));
    }

    private static TorrentPayloadRejectionCoordinator Service(Store store, Effects effects) =>
        new(store, (_, _) => Task.FromResult(false), (_, _) => { effects.Events.Add("mark_removed"); return Task.CompletedTask; }, effects,
            (_, _, _, _) => Task.CompletedTask);
    private sealed class Store(List<string> events) : ITorrentPayloadRejectionStore
    {
        public TorrentPayloadRejection? Saved; public bool ThrowSave; public bool ThrowRead; public bool Rejected;
        public Task<TorrentPayloadRejection?> GetAsync(Guid id, CancellationToken token) => Task.FromResult(Saved);
        public Task<IReadOnlyList<TorrentPayloadRejection>> ListPendingAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<TorrentPayloadRejection>>(Saved is null ? [] : [Saved]);
        public Task<bool> IsRejectedAsync(string hash, CancellationToken token) => ThrowRead ? throw new IOException("Fake read failure") : Task.FromResult(Rejected);
        public Task SaveAsync(TorrentPayloadRejection row, CancellationToken token)
        { if (ThrowSave) { throw new IOException("Fake save failure"); } Saved = row; events.Add("save:" + row.Phase); return Task.CompletedTask; }
    }
    private sealed class Effects(List<string> events) : ITorrentPayloadRejectionEffects
    {
        public List<string> Events => events;
        public bool Unconfirmed; public bool ThrowDelete;
        public Task<PayloadRejectionReply>? PendingReply;
        public Task<PayloadRejectionReply> ReportAsync(TorrentPayloadRejection row, CancellationToken token)
        { events.Add("report"); Assert.Equal("Deleted", row.Phase); return PendingReply ?? Task.FromResult(new PayloadRejectionReply(!Unconfirmed)); }
        public Task DeleteAsync(TorrentPayloadRejection row, CancellationToken token)
        { events.Add("delete"); return ThrowDelete ? throw new IOException("Fake deletion failure") : Task.CompletedTask; }
    }
}
