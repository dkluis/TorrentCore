using System.Collections.Concurrent;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Core.Torrents;
using TorrentCore.Service.Callbacks;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Infrastructure;

namespace TorrentCore.Service.Tests;

public sealed class TorrentCompletionFinalizationProbeCoordinatorTests
{
    [Fact]
    public async Task TryTakeCompletedOrSchedule_DoesNotWaitForFilesystemProbe_AndDeduplicatesByTorrent()
    {
        var checker = new BlockingFinalizationChecker();
        var coordinator = CreateCoordinator(checker, new RecordingActivityLogService());
        var snapshot = CreateSnapshot();
        var runtimeSettings = CreateRuntimeSettings();

        Assert.False(
            coordinator.TryTakeCompletedOrSchedule(
                snapshot,
                runtimeSettings,
                observedFiles: null,
                out var firstResult
            )
        );
        Assert.Null(firstResult);
        Assert.True(checker.Started.Wait(TimeSpan.FromSeconds(2)));

        Assert.False(
            coordinator.TryTakeCompletedOrSchedule(
                snapshot,
                runtimeSettings,
                observedFiles: null,
                out var secondResult
            )
        );
        Assert.Null(secondResult);
        Assert.Equal(1, checker.CallCount);

        checker.Release.Set();
        var completedResult = await WaitForResultAsync(coordinator, snapshot, runtimeSettings);

        Assert.True(completedResult.IsReady);
        Assert.Equal(snapshot.SavePath, completedResult.FinalPayloadPath);
        Assert.Equal(1, checker.CallCount);

        Assert.True(
            coordinator.TryTakeCompletedOrSchedule(snapshot, runtimeSettings, observedFiles: null, out var retainedResult)
        );
        Assert.True(retainedResult!.IsReady);
        Assert.Equal(1, checker.CallCount);
    }

    [Fact]
    public async Task TryTakeCompletedOrSchedule_WhenProbeFails_ReturnsNotReadyAndWritesDiagnostic()
    {
        var activityLogs = new RecordingActivityLogService();
        var coordinator = CreateCoordinator(
            new ThrowingFinalizationChecker(new IOException("volume unavailable")),
            activityLogs
        );
        var snapshot = CreateSnapshot();
        var runtimeSettings = CreateRuntimeSettings();

        Assert.False(
            coordinator.TryTakeCompletedOrSchedule(
                snapshot,
                runtimeSettings,
                observedFiles: null,
                out _
            )
        );

        var completedResult = await WaitForResultAsync(coordinator, snapshot, runtimeSettings);

        Assert.False(completedResult.IsReady);
        Assert.Contains("volume unavailable", completedResult.PendingReason);
        Assert.Contains(activityLogs.Entries, entry => entry.EventType == "runtime.finalization.probe_failed");
    }

    private static TorrentCompletionFinalizationProbeCoordinator CreateCoordinator(
        ITorrentCompletionFinalizationChecker checker,
        IActivityLogService activityLogService)
    {
        var serviceInstanceContext = new ServiceInstanceContext();
        return new TorrentCompletionFinalizationProbeCoordinator(
            checker,
            activityLogService,
            serviceInstanceContext,
            new RuntimeOperationDurationDiagnostics(activityLogService, serviceInstanceContext)
        );
    }

    private static TorrentSnapshot CreateSnapshot()
    {
        return new TorrentSnapshot
        {
            TorrentId = Guid.NewGuid(),
            Name = "Background Finalization Movie",
            State = TorrentState.Completed,
            DesiredState = TorrentDesiredState.Runnable,
            MagnetUri = "magnet:?xt=urn:btih:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            InfoHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            DownloadRootPath = "/downloads/Movie",
            SavePath = "/downloads/Movie/Background Finalization Movie",
            ProgressPercent = 100,
            DownloadedBytes = 100,
            UploadedBytes = 0,
            DownloadRateBytesPerSecond = 0,
            UploadRateBytesPerSecond = 0,
            TrackerCount = 0,
            ConnectedPeerCount = 0,
            AddedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static RuntimeSettingsSnapshot CreateRuntimeSettings()
    {
        return new RuntimeSettingsSnapshot
        {
            UsesPersistedOverrides = false,
            PartialFilesEnabled = false,
            PartialFileSuffix = string.Empty,
            SeedingStopMode = SeedingStopMode.Unlimited,
            SeedingStopRatio = 1,
            SeedingStopMinutes = 60,
            CompletedTorrentCleanupMode = CompletedTorrentCleanupMode.Never,
            CompletedTorrentCleanupMinutes = 60,
            DeleteLogsForCompletedTorrents = false,
            EngineConnectionFailureLogBurstLimit = 5,
            EngineConnectionFailureLogWindowSeconds = 60,
            EngineMaximumConnections = 200,
            EngineMaximumHalfOpenConnections = 25,
            EngineMaximumDownloadRateBytesPerSecond = 0,
            EngineMaximumUploadRateBytesPerSecond = 0,
            MaxActiveMetadataResolutions = 4,
            MaxActiveDownloads = 4,
            MetadataRefreshStaleSeconds = 90,
            MetadataRefreshRestartDelaySeconds = 30,
            CompletionCallbackEnabled = true,
            CompletionCallbackTimeoutSeconds = 30,
            CompletionCallbackFinalizationTimeoutSeconds = 45,
            EngineSettingsRequireRestart = false,
        };
    }

    private static async Task<TorrentCompletionFinalizationCheckResult> WaitForResultAsync(
        TorrentCompletionFinalizationProbeCoordinator coordinator,
        TorrentSnapshot snapshot,
        RuntimeSettingsSnapshot runtimeSettings)
    {
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            if (coordinator.TryTakeCompletedOrSchedule(
                    snapshot,
                    runtimeSettings,
                    observedFiles: null,
                    out var result
                ))
            {
                return result!;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("The finalization probe did not complete.");
    }

    private sealed class BlockingFinalizationChecker : ITorrentCompletionFinalizationChecker
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);
        public ManualResetEventSlim Started { get; } = new();
        public ManualResetEventSlim Release { get; } = new();

        public TorrentCompletionFinalizationCheckResult Check(
            TorrentSnapshot snapshot,
            RuntimeSettingsSnapshot runtimeSettings,
            IReadOnlyList<TorrentCompletionObservedFilePaths>? observedFiles = null)
        {
            Interlocked.Increment(ref _callCount);
            Started.Set();
            Release.Wait(TimeSpan.FromSeconds(2));
            return new TorrentCompletionFinalizationCheckResult
            {
                IsReady = true,
                FinalPayloadPath = snapshot.SavePath,
            };
        }
    }

    private sealed class ThrowingFinalizationChecker(Exception exception) : ITorrentCompletionFinalizationChecker
    {
        public TorrentCompletionFinalizationCheckResult Check(
            TorrentSnapshot snapshot,
            RuntimeSettingsSnapshot runtimeSettings,
            IReadOnlyList<TorrentCompletionObservedFilePaths>? observedFiles = null)
        {
            throw exception;
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
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ActivityLogEntry>>([]);
        }

        public Task<ActivityLogFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken)
            => Task.FromResult(new ActivityLogFilterOptions { Categories = [], EventTypes = [] });

        public Task<int> DeleteByTorrentIdAsync(Guid torrentId, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<int> DeleteOrphanedTorrentLogsAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
