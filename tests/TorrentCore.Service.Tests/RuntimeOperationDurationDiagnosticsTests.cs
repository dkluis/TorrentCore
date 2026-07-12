using System.Collections.Concurrent;
using System.Text.Json;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Infrastructure;

namespace TorrentCore.Service.Tests;

public sealed class RuntimeOperationDurationDiagnosticsTests
{
    [Fact]
    public async Task RecordIfSlowAsync_PersistsOperationContext_WhenThresholdIsReached()
    {
        var activityLogService = new RecordingActivityLogService();
        var torrentId = Guid.NewGuid();
        var diagnostics = new RuntimeOperationDurationDiagnostics(
            activityLogService,
            new ServiceInstanceContext()
        );

        await diagnostics.RecordIfSlowAsync(
            "monotorrent",
            "engine_add_magnet",
            RuntimeOperationDurationDiagnostics.MonoTorrentSlowThreshold + TimeSpan.FromMilliseconds(1),
            RuntimeOperationDurationDiagnostics.MonoTorrentSlowThreshold,
            "succeeded",
            torrentId,
            new { TorrentName = "Example" }
        );

        var entry = Assert.Single(activityLogService.Writes);
        Assert.Equal(ActivityLogLevel.Warning, entry.Level);
        Assert.Equal("runtime", entry.Category);
        Assert.Equal("runtime.operation.slow", entry.EventType);
        Assert.Null(entry.TorrentId);
        using var details = JsonDocument.Parse(entry.DetailsJson!);
        Assert.Equal("monotorrent", details.RootElement.GetProperty("Subsystem").GetString());
        Assert.Equal("engine_add_magnet", details.RootElement.GetProperty("Operation").GetString());
        Assert.Equal("succeeded", details.RootElement.GetProperty("Outcome").GetString());
        Assert.Equal(torrentId, details.RootElement.GetProperty("TorrentId").GetGuid());
    }

    [Fact]
    public async Task RecordIfSlowAsync_DoesNotPersist_WhenBelowThreshold()
    {
        var activityLogService = new RecordingActivityLogService();
        var diagnostics = new RuntimeOperationDurationDiagnostics(
            activityLogService,
            new ServiceInstanceContext()
        );

        await diagnostics.RecordIfSlowAsync(
            "storage",
            "torrent_snapshot_persistence_phase",
            TimeSpan.FromMilliseconds(10),
            RuntimeOperationDurationDiagnostics.StorageSlowThreshold,
            "succeeded"
        );

        Assert.Empty(activityLogService.Writes);
    }

    [Fact]
    public async Task CompletedOperationDiagnostics_AreServiceScopedAndRetainTorrentContextInDetails()
    {
        var activityLogService = new RecordingActivityLogService();
        var torrentId = Guid.NewGuid();
        var diagnostics = new RuntimeOperationDurationDiagnostics(
            activityLogService,
            new ServiceInstanceContext()
        );

        await diagnostics.WriteRecoveryActionCompletedAsync(
            "metadata",
            "Restart",
            2,
            TimeSpan.FromSeconds(12),
            "succeeded",
            torrentId,
            "Example",
            new { OpenConnections = 0 }
        );
        await diagnostics.WriteCallbackDispatchCompletedAsync(
            TimeSpan.FromSeconds(9),
            "succeeded",
            torrentId,
            "Example",
            "Movie"
        );

        Assert.All(activityLogService.Writes, entry => Assert.Null(entry.TorrentId));
        Assert.Contains(activityLogService.Writes, entry => entry.EventType == "runtime.recovery.action_completed");
        Assert.Contains(activityLogService.Writes, entry => entry.EventType == "runtime.callback.dispatch_completed");
        Assert.All(
            activityLogService.Writes,
            entry =>
            {
                using var details = JsonDocument.Parse(entry.DetailsJson!);
                Assert.Equal(torrentId, details.RootElement.GetProperty("TorrentId").GetGuid());
            }
        );
    }

    private sealed class RecordingActivityLogService : IActivityLogService
    {
        private readonly ConcurrentQueue<ActivityLogWriteRequest> _writes = new();

        public IReadOnlyCollection<ActivityLogWriteRequest> Writes => _writes.ToArray();

        public Task EnsureInitializedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteAsync(ActivityLogWriteRequest request, CancellationToken cancellationToken)
        {
            _writes.Enqueue(request);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ActivityLogEntry>> GetRecentAsync(
            ActivityLogQuery query,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ActivityLogEntry>>([]);

        public Task<int> DeleteByTorrentIdAsync(Guid torrentId, CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<int> DeleteOrphanedTorrentLogsAsync(CancellationToken cancellationToken)
            => Task.FromResult(0);
    }
}
