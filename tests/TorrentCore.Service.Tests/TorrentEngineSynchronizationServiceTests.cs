using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Engine;
using TorrentCore.Service.Infrastructure;

namespace TorrentCore.Service.Tests;

public sealed class TorrentEngineSynchronizationServiceTests
{
    [Fact]
    public async Task ExecuteAsync_ContinuesAfterSynchronizeException()
    {
        var adapter = new FlakySynchronizationEngineAdapter();
        var activityLogService = new RecordingActivityLogService(new IOException("activity log unavailable"));
        using var service = new TorrentEngineSynchronizationService(
            adapter,
            Options.Create(
                new TorrentCoreServiceOptions
                {
                    RuntimeTickIntervalMilliseconds = 10,
                }
            ),
            activityLogService,
            new ServiceInstanceContext(),
            new RuntimeOperationDurationDiagnostics(activityLogService, new ServiceInstanceContext())
        );

        await service.StartAsync(CancellationToken.None);

        try
        {
            var completedTask = await Task.WhenAny(
                adapter.SecondSynchronization.Task,
                Task.Delay(TimeSpan.FromSeconds(2))
            );

            Assert.Same(adapter.SecondSynchronization.Task, completedTask);
            Assert.True(adapter.SynchronizeCallCount >= 2);
            Assert.Contains(activityLogService.Writes, request => request.EventType == "runtime.tick.failed");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private sealed class FlakySynchronizationEngineAdapter : ITorrentEngineAdapter
    {
        private int _synchronizeCallCount;

        public TaskCompletionSource SecondSynchronization { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SynchronizeCallCount => Volatile.Read(ref _synchronizeCallCount);

        public Task<int> GetTorrentCountAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TorrentEngineRecoveryResult> RecoverAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task SynchronizeAsync(CancellationToken cancellationToken)
        {
            var callCount = Interlocked.Increment(ref _synchronizeCallCount);
            if (callCount == 1)
            {
                throw new IOException("Simulated synchronization failure.");
            }

            if (callCount == 2)
            {
                SecondSynchronization.TrySetResult();
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TorrentSummaryDto>> GetTorrentsAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TorrentDetailDto> GetTorrentAsync(Guid torrentId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<TorrentPeerDto>> GetTorrentPeersAsync(Guid torrentId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<TorrentTrackerDto>> GetTorrentTrackersAsync(Guid torrentId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TorrentDetailDto> AddMagnetAsync(AddMagnetRequest request,
            ResolvedTorrentCategorySelection categorySelection, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TorrentActionResultDto> PauseAsync(Guid torrentId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TorrentActionResultDto> ResumeAsync(Guid torrentId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TorrentActionResultDto> RefreshMetadataAsync(Guid torrentId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TorrentActionResultDto> ResetMetadataSessionAsync(Guid torrentId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TorrentActionResultDto> RetryCompletionCallbackAsync(Guid torrentId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TorrentActionResultDto> RemoveAsync(Guid torrentId, RemoveTorrentRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class RecordingActivityLogService(Exception? writeFailure = null) : IActivityLogService
    {
        private readonly ConcurrentQueue<ActivityLogWriteRequest> _writes = new();

        public IReadOnlyCollection<ActivityLogWriteRequest> Writes => _writes.ToArray();

        public Task EnsureInitializedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteAsync(ActivityLogWriteRequest request, CancellationToken cancellationToken)
        {
            _writes.Enqueue(request);
            return writeFailure is null ? Task.CompletedTask : Task.FromException(writeFailure);
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
