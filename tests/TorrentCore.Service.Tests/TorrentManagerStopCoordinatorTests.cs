using System.Collections.Concurrent;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Engine;
using TorrentCore.Service.Infrastructure;

namespace TorrentCore.Service.Tests;

public sealed class TorrentManagerStopCoordinatorTests
{
    [Fact]
    public async Task TryTakeCompletedOrSchedule_DoesNotWaitForStopAndDeduplicatesByTorrent()
    {
        var stopOperation = new BlockingStopOperation();
        var activityLogs = new RecordingActivityLogService();
        var coordinator = CreateCoordinator(activityLogs);
        var torrentId = Guid.NewGuid();

        Assert.False(
            coordinator.TryTakeCompletedOrSchedule(
                torrentId, "Slow Stop Torrent", stopOperation.StopAsync, CancellationToken.None, out var firstResult
            )
        );
        Assert.Null(firstResult);
        Assert.True(stopOperation.Started.Wait(TimeSpan.FromSeconds(2)));

        Assert.False(
            coordinator.TryTakeCompletedOrSchedule(
                torrentId, "Slow Stop Torrent", stopOperation.StopAsync, CancellationToken.None, out var secondResult
            )
        );
        Assert.Null(secondResult);
        Assert.Equal(1, stopOperation.CallCount);

        stopOperation.Release.Set();
        var result = await WaitForSuccessfulResultAsync(coordinator, torrentId, stopOperation.StopAsync);

        Assert.True(result.Succeeded);
        Assert.Equal(1, stopOperation.CallCount);
        Assert.Contains(activityLogs.Entries,
            entry => entry.EventType == "runtime.completion.manager_stop_completed");
    }

    [Fact]
    public async Task TryTakeCompletedOrSchedule_WhenStopFails_GatesCompletionAndWritesDiagnostic()
    {
        var activityLogs = new RecordingActivityLogService();
        var coordinator = CreateCoordinator(activityLogs);
        var torrentId = Guid.NewGuid();

        Assert.False(
            coordinator.TryTakeCompletedOrSchedule(
                torrentId,
                "Failed Stop Torrent",
                _ => throw new IOException("peer shutdown failed"),
                CancellationToken.None,
                out _
            )
        );

        var result = await WaitForFailedResultAsync(coordinator, torrentId);

        Assert.False(result.Succeeded);
        Assert.Contains("peer shutdown failed", result.Error);
        Assert.Contains(activityLogs.Entries,
            entry => entry.EventType == "runtime.completion.manager_stop_failed");
    }

    [Fact]
    public async Task WaitForPendingAsync_WaitsForExistingStopBeforeRemovalContinues()
    {
        var stopOperation = new BlockingStopOperation();
        var coordinator = CreateCoordinator(new RecordingActivityLogService());
        var torrentId = Guid.NewGuid();
        coordinator.TryTakeCompletedOrSchedule(
            torrentId, "Removed Torrent", stopOperation.StopAsync, CancellationToken.None, out _
        );
        Assert.True(stopOperation.Started.Wait(TimeSpan.FromSeconds(2)));

        var waitTask = coordinator.WaitForPendingAsync(torrentId, CancellationToken.None);
        Assert.False(waitTask.IsCompleted);

        stopOperation.Release.Set();
        await waitTask;

        Assert.Equal(1, stopOperation.CallCount);
    }

    private static TorrentManagerStopCoordinator CreateCoordinator(IActivityLogService activityLogService)
    {
        var serviceInstanceContext = new ServiceInstanceContext();
        return new TorrentManagerStopCoordinator(
            activityLogService,
            serviceInstanceContext,
            new RuntimeOperationDurationDiagnostics(activityLogService, serviceInstanceContext)
        );
    }

    private static async Task<TorrentManagerStopResult> WaitForSuccessfulResultAsync(
        TorrentManagerStopCoordinator coordinator,
        Guid torrentId,
        Func<CancellationToken, Task> stopOperation)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            if (coordinator.TryTakeCompletedOrSchedule(
                    torrentId, "Slow Stop Torrent", stopOperation, CancellationToken.None, out var result
                ))
            {
                return result!;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("The manager stop did not complete.");
    }

    private static async Task<TorrentManagerStopResult> WaitForFailedResultAsync(
        TorrentManagerStopCoordinator coordinator,
        Guid torrentId)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            coordinator.TryTakeCompletedOrSchedule(
                torrentId,
                "Failed Stop Torrent",
                _ => throw new InvalidOperationException("unexpected retry"),
                CancellationToken.None,
                out var result
            );
            if (result is not null)
            {
                return result;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("The failed manager stop did not report its result.");
    }

    private sealed class BlockingStopOperation
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);
        public ManualResetEventSlim Started { get; } = new();
        public ManualResetEventSlim Release { get; } = new();

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            Started.Set();
            Release.Wait(cancellationToken);
            return Task.CompletedTask;
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

        public Task<int> DeleteByTorrentIdAsync(Guid torrentId, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<int> DeleteOrphanedTorrentLogsAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<int> DeleteInactiveBeforeAsync(DateTimeOffset cutoffUtc,
            CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
