using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Engine;
using TorrentCore.Service.Infrastructure;
using TorrentCore.Service.Tests.Fixtures;
using TorrentCore.Service.Vpn;

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
            new RuntimeOperationDurationDiagnostics(activityLogService, new ServiceInstanceContext()),
            new RuntimeTickDurationSummaryState(),
            new VpnConnectionRuntimeState(),
            TimeProvider.System
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

    [Fact]
    public async Task DurationSummary_DefaultOff_KeepsSynchronizingWithoutWritingSummary()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero));
        var adapter = new FlakySynchronizationEngineAdapter(failFirstCall: false);
        var activityLogService = new RecordingActivityLogService();
        var summaryState = new RuntimeTickDurationSummaryState();
        using var service = CreateService(adapter, activityLogService, summaryState, new VpnConnectionRuntimeState(), clock);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitForSynchronizationCountAsync(adapter, 1);
            await AdvanceTicksAsync(clock, adapter, 65);

            Assert.True(adapter.SynchronizeCallCount >= 66);
            Assert.DoesNotContain(
                activityLogService.Writes,
                request => request.EventType == "runtime.tick.duration_summary"
            );
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DurationSummary_EnabledLive_StartsFreshWindowAndWritesAfterOneMinute()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero));
        var adapter = new FlakySynchronizationEngineAdapter(failFirstCall: false);
        var activityLogService = new RecordingActivityLogService();
        var summaryState = new RuntimeTickDurationSummaryState();
        using var service = CreateService(adapter, activityLogService, summaryState, new VpnConnectionRuntimeState(), clock);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitForSynchronizationCountAsync(adapter, 1);
            await AdvanceTicksAsync(clock, adapter, 30);
            summaryState.Set(true);
            await AdvanceTicksAsync(clock, adapter, 60);
            Assert.DoesNotContain(
                activityLogService.Writes,
                request => request.EventType == "runtime.tick.duration_summary"
            );

            await AdvanceTicksAsync(clock, adapter, 1);
            Assert.Contains(
                activityLogService.Writes,
                request => request.EventType == "runtime.tick.duration_summary"
            );
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DurationSummary_DegradedWindowIsDiscardedAndReadyStartsFreshWindow()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero));
        var adapter = new FlakySynchronizationEngineAdapter(failFirstCall: false);
        var activityLogService = new RecordingActivityLogService();
        var summaryState = new RuntimeTickDurationSummaryState();
        summaryState.Set(true);
        var vpnState = new VpnConnectionRuntimeState();
        using var service = CreateService(adapter, activityLogService, summaryState, vpnState, clock);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await WaitForSynchronizationCountAsync(adapter, 1);
            await AdvanceTicksAsync(clock, adapter, 30);
            vpnState.Set(
                new VpnConnectionRuntimeSnapshot(
                    true,
                    VpnConnectionPhase.Degraded,
                    VpnConnectionReason.DirectIsp,
                    "Torrent processing is paused."
                )
            );
            await AdvanceTicksAsync(clock, adapter, 35);
            Assert.DoesNotContain(
                activityLogService.Writes,
                request => request.EventType == "runtime.tick.duration_summary"
            );

            vpnState.Set(new VpnConnectionRuntimeSnapshot(true, VpnConnectionPhase.Ready, null, null));
            await AdvanceTicksAsync(clock, adapter, 60);
            Assert.DoesNotContain(
                activityLogService.Writes,
                request => request.EventType == "runtime.tick.duration_summary"
            );

            await AdvanceTicksAsync(clock, adapter, 1);
            Assert.Contains(
                activityLogService.Writes,
                request => request.EventType == "runtime.tick.duration_summary"
            );
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static TorrentEngineSynchronizationService CreateService(
        ITorrentEngineAdapter adapter,
        IActivityLogService activityLogService,
        RuntimeTickDurationSummaryState summaryState,
        VpnConnectionRuntimeState vpnState,
        TimeProvider timeProvider)
    {
        var instanceContext = new ServiceInstanceContext();
        return new TorrentEngineSynchronizationService(
            adapter,
            Options.Create(new TorrentCoreServiceOptions { RuntimeTickIntervalMilliseconds = 1_000 }),
            activityLogService,
            instanceContext,
            new RuntimeOperationDurationDiagnostics(activityLogService, instanceContext),
            summaryState,
            vpnState,
            timeProvider
        );
    }

    private static async Task AdvanceTicksAsync(
        ManualTimeProvider clock,
        FlakySynchronizationEngineAdapter adapter,
        int count)
    {
        for (var index = 0; index < count; index++)
        {
            var expectedCount = adapter.SynchronizeCallCount + 1;
            clock.Advance(TimeSpan.FromSeconds(1));
            await WaitForSynchronizationCountAsync(adapter, expectedCount);
        }
    }

    private static async Task WaitForSynchronizationCountAsync(
        FlakySynchronizationEngineAdapter adapter,
        int expectedCount)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2);
        while (adapter.SynchronizeCallCount < expectedCount && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(1);
        }

        Assert.True(
            adapter.SynchronizeCallCount >= expectedCount,
            $"Expected at least {expectedCount} synchronization calls, observed {adapter.SynchronizeCallCount}."
        );
    }

    private sealed class FlakySynchronizationEngineAdapter(bool failFirstCall = true) : ITorrentEngineAdapter
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
            if (callCount == 1 && failFirstCall)
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

        public Task<TorrentActionResultDto> MakeNextAsync(Guid torrentId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TorrentActionResultDto> HoldAsync(Guid torrentId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TorrentActionResultDto> ReleaseHoldAsync(Guid torrentId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TorrentActionResultDto> ResumeNextAsync(Guid torrentId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TorrentActionResultDto> ResumeOnHoldAsync(Guid torrentId, CancellationToken cancellationToken)
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

        public Task<ActivityLogFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken)
            => Task.FromResult(new ActivityLogFilterOptions { Categories = [], EventTypes = [] });

        public Task<int> DeleteByTorrentIdAsync(Guid torrentId, CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<int> DeleteOrphanedTorrentLogsAsync(CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<int> DeleteInactiveBeforeAsync(DateTimeOffset cutoffUtc,
            CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
