using System.Collections.Concurrent;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Engine;
using TorrentCore.Service.Infrastructure;

namespace TorrentCore.Service.Tests;

public sealed class TorrentMetadataResetCoordinatorTests
{
    private static readonly TimeSpan NormalStuckThreshold = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task TryTakeCompletedOrSchedule_AllowsOnlyOneHostWideReset()
    {
        var blockingOperation = new BlockingResetOperation();
        var activityLogs = new RecordingActivityLogService();
        var coordinator = CreateCoordinator(activityLogs);
        var blockedTorrentId = Guid.NewGuid();
        var otherTorrentId = Guid.NewGuid();
        var otherCallCount = 0;

        Assert.False(
            coordinator.TryTakeCompletedOrSchedule(
                blockedTorrentId,
                "Blocked Reset",
                NormalStuckThreshold,
                blockingOperation.ResetAsync,
                CancellationToken.None,
                out var firstResult));
        Assert.Null(firstResult);
        Assert.True(blockingOperation.Started.Wait(TimeSpan.FromSeconds(2)));

        Assert.False(
            coordinator.TryTakeCompletedOrSchedule(
                blockedTorrentId,
                "Blocked Reset",
                NormalStuckThreshold,
                blockingOperation.ResetAsync,
                CancellationToken.None,
                out var duplicateResult));
        Assert.Null(duplicateResult);
        Assert.Equal(1, blockingOperation.CallCount);
        Assert.True(coordinator.IsRunning(blockedTorrentId));

        Assert.False(
            coordinator.TryTakeCompletedOrSchedule(
                otherTorrentId,
                "Suppressed Reset",
                NormalStuckThreshold,
                _ =>
                {
                    Interlocked.Increment(ref otherCallCount);
                    return Task.CompletedTask;
                },
                CancellationToken.None,
                out _));
        Assert.Equal(0, Volatile.Read(ref otherCallCount));
        await WaitForLogAsync(activityLogs, "runtime.metadata.reset_suppressed");

        blockingOperation.Release.Set();
        var blockedResult = await WaitForResultAsync(coordinator, blockedTorrentId);
        Assert.True(blockedResult.Succeeded);

        Assert.False(
            coordinator.TryTakeCompletedOrSchedule(
                otherTorrentId,
                "Next Reset",
                NormalStuckThreshold,
                _ =>
                {
                    Interlocked.Increment(ref otherCallCount);
                    return Task.CompletedTask;
                },
                CancellationToken.None,
                out _));
        var otherResult = await WaitForResultAsync(coordinator, otherTorrentId);
        Assert.True(otherResult.Succeeded);
        Assert.Equal(1, Volatile.Read(ref otherCallCount));
    }

    [Fact]
    public async Task TryTakeCompletedOrSchedule_WhenResetFails_RetainsFailureDuringRetryCooldown()
    {
        var activityLogs = new RecordingActivityLogService();
        var coordinator = CreateCoordinator(activityLogs);
        var torrentId = Guid.NewGuid();
        var unexpectedRetryCount = 0;

        Assert.False(
            coordinator.TryTakeCompletedOrSchedule(
                torrentId,
                "Failed Reset",
                NormalStuckThreshold,
                _ => throw new IOException("manager stop failed"),
                CancellationToken.None,
                out _));

        var failedResult = await WaitForResultAsync(coordinator, torrentId);
        Assert.False(failedResult.Succeeded);
        Assert.Contains("manager stop failed", failedResult.Error);

        Assert.True(
            coordinator.TryTakeCompletedOrSchedule(
                torrentId,
                "Failed Reset",
                NormalStuckThreshold,
                _ =>
                {
                    Interlocked.Increment(ref unexpectedRetryCount);
                    return Task.CompletedTask;
                },
                CancellationToken.None,
                out var retainedResult));
        Assert.False(retainedResult!.Succeeded);
        Assert.Equal(0, Volatile.Read(ref unexpectedRetryCount));
        Assert.Contains(activityLogs.Entries, entry => entry.EventType == "runtime.metadata.reset_failed");
    }

    [Fact]
    public async Task StuckReset_StaysQuarantined_ThenAllowsOneSuccessfulHalfOpenProbe()
    {
        var blockingOperation = new BlockingResetOperation();
        var activityLogs = new RecordingActivityLogService();
        var circuitOpenDuration = TimeSpan.FromMilliseconds(300);
        var coordinator = CreateCoordinator(activityLogs, circuitOpenDuration);
        var stuckTorrentId = Guid.NewGuid();
        var suppressedTorrentId = Guid.NewGuid();
        var suppressedCallCount = 0;

        coordinator.TryTakeCompletedOrSchedule(
            stuckTorrentId,
            "Stuck Reset",
            TimeSpan.FromMilliseconds(50),
            blockingOperation.ResetAsync,
            CancellationToken.None,
            out _);
        Assert.True(blockingOperation.Started.Wait(TimeSpan.FromSeconds(2)));

        await WaitForLogAsync(activityLogs, "runtime.metadata.reset_timed_out");
        Assert.True(coordinator.IsRunning(stuckTorrentId));

        Assert.False(
            coordinator.TryTakeCompletedOrSchedule(
                suppressedTorrentId,
                "Suppressed Reset",
                NormalStuckThreshold,
                _ =>
                {
                    Interlocked.Increment(ref suppressedCallCount);
                    return Task.CompletedTask;
                },
                CancellationToken.None,
                out _));
        Assert.Equal(0, Volatile.Read(ref suppressedCallCount));

        blockingOperation.Release.Set();
        var lateResult = await WaitForResultAsync(coordinator, stuckTorrentId);
        Assert.True(lateResult.Succeeded);
        await WaitForLogAsync(activityLogs, "runtime.metadata.reset_late_completion");

        Assert.False(
            coordinator.TryTakeCompletedOrSchedule(
                suppressedTorrentId,
                "Open Circuit Reset",
                NormalStuckThreshold,
                _ =>
                {
                    Interlocked.Increment(ref suppressedCallCount);
                    return Task.CompletedTask;
                },
                CancellationToken.None,
                out _));
        Assert.Equal(0, Volatile.Read(ref suppressedCallCount));

        await Task.Delay(circuitOpenDuration.Add(TimeSpan.FromMilliseconds(100)));

        Assert.False(
            coordinator.TryTakeCompletedOrSchedule(
                suppressedTorrentId,
                "Half Open Probe",
                NormalStuckThreshold,
                _ =>
                {
                    Interlocked.Increment(ref suppressedCallCount);
                    return Task.CompletedTask;
                },
                CancellationToken.None,
                out _));
        var probeResult = await WaitForResultAsync(coordinator, suppressedTorrentId);
        Assert.True(probeResult.Succeeded);
        Assert.Equal(1, Volatile.Read(ref suppressedCallCount));
        await WaitForLogAsync(activityLogs, "runtime.metadata.reset_half_open");
        await WaitForLogAsync(activityLogs, "runtime.metadata.reset_circuit_closed");
    }

    [Fact]
    public async Task DrainAsync_ReturnsFalseWhileResetIsBlocked_ThenCompletesAfterRelease()
    {
        var blockingOperation = new BlockingResetOperation();
        var coordinator = CreateCoordinator(new RecordingActivityLogService());
        var torrentId = Guid.NewGuid();

        coordinator.TryTakeCompletedOrSchedule(
            torrentId,
            "Shutdown Reset",
            NormalStuckThreshold,
            blockingOperation.ResetAsync,
            CancellationToken.None,
            out _);
        Assert.True(blockingOperation.Started.Wait(TimeSpan.FromSeconds(2)));

        Assert.False(await coordinator.DrainAsync(TimeSpan.FromMilliseconds(50)));

        blockingOperation.Release.Set();
        Assert.True(await coordinator.DrainAsync(TimeSpan.FromSeconds(2)));
    }

    private static TorrentMetadataResetCoordinator CreateCoordinator(
        IActivityLogService activityLogService,
        TimeSpan? circuitOpenDuration = null)
    {
        var serviceInstanceContext = new ServiceInstanceContext();
        var diagnostics = new RuntimeOperationDurationDiagnostics(activityLogService, serviceInstanceContext);
        return circuitOpenDuration is null ?
                new TorrentMetadataResetCoordinator(activityLogService, serviceInstanceContext, diagnostics) :
                new TorrentMetadataResetCoordinator(
                    activityLogService,
                    serviceInstanceContext,
                    diagnostics,
                    circuitOpenDuration.Value);
    }

    private static async Task<TorrentMetadataResetResult> WaitForResultAsync(
        TorrentMetadataResetCoordinator coordinator,
        Guid torrentId)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            if (coordinator.TryTakeCompleted(torrentId, out var result))
            {
                return result!;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("The background metadata reset did not report its result.");
    }

    private static async Task WaitForLogAsync(RecordingActivityLogService activityLogs, string eventType)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            if (activityLogs.Entries.Any(entry => entry.EventType == eventType))
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException($"Activity log '{eventType}' was not written.");
    }

    private sealed class BlockingResetOperation
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);
        public ManualResetEventSlim Started { get; } = new();
        public ManualResetEventSlim Release { get; } = new();

        public async Task ResetAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            Started.Set();
            while (!Release.IsSet)
            {
                await Task.Delay(10, cancellationToken);
            }
        }
    }

    private sealed class RecordingActivityLogService : IActivityLogService
    {
        private readonly ConcurrentQueue<ActivityLogWriteRequest> _entries = new();

        public IReadOnlyCollection<ActivityLogWriteRequest> Entries => _entries.ToArray();

        public Task EnsureInitializedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteAsync(ActivityLogWriteRequest request, CancellationToken cancellationToken)
        {
            _entries.Enqueue(request);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ActivityLogEntry>> GetRecentAsync(
            ActivityLogQuery query,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ActivityLogEntry>>([]);

        public Task<ActivityLogFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken)
            => Task.FromResult(new ActivityLogFilterOptions { Categories = [], EventTypes = [] });

        public Task<int> DeleteByTorrentIdAsync(Guid torrentId, CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<int> DeleteOrphanedTorrentLogsAsync(CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<int> DeleteInactiveBeforeAsync(DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
            => Task.FromResult(0);
    }
}
