using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Core.Torrents;
using TorrentCore.Service.Callbacks;
using TorrentCore.Service.Configuration;

namespace TorrentCore.Service.Tests;

public sealed class TorrentCompletionCallbackProcessorTests
{
    [Fact]
    public async Task MarkPendingIfTriggered_WhenCompletedTorrentIsQueued_StillMarksPending()
    {
        var processor = new TorrentCompletionCallbackProcessor(
            new StubFinalizationChecker(),
            new StubCompletionCallbackInvoker(),
            new RecordingActivityLogService(),
            new ServiceInstanceContext()
        );

        var now = DateTimeOffset.UtcNow;
        var snapshot = CreateCompletedSnapshot(now);

        var changed = await processor.MarkPendingIfTriggeredAsync(
            previousCompletedAtUtc: null,
            snapshot,
            CreateRuntimeSettings(),
            now,
            CancellationToken.None,
            new TorrentCompletionFinalizationCheckResult
            {
                IsReady = true,
                FinalPayloadPath = snapshot.SavePath,
            }
        );

        Assert.True(changed);
        Assert.Equal(TorrentCompletionCallbackState.PendingFinalization, snapshot.CompletionCallbackState);
        Assert.Equal(now, snapshot.CompletionCallbackPendingSinceUtc);
        Assert.Null(snapshot.CompletionCallbackInvokedAtUtc);
        Assert.Null(snapshot.CompletionCallbackLastError);
    }

    [Fact]
    public async Task MarkPendingIfTriggered_Succeeds_WhenActivityLogWriteFails()
    {
        var processor = new TorrentCompletionCallbackProcessor(
            new StubFinalizationChecker(),
            new StubCompletionCallbackInvoker(),
            new ThrowingActivityLogService(new IOException("activity log unavailable")),
            new ServiceInstanceContext()
        );

        var now = DateTimeOffset.UtcNow;
        var snapshot = CreateCompletedSnapshot(now);

        var changed = await processor.MarkPendingIfTriggeredAsync(
            previousCompletedAtUtc: null,
            snapshot,
            CreateRuntimeSettings(),
            now,
            CancellationToken.None,
            new TorrentCompletionFinalizationCheckResult
            {
                IsReady = true,
                FinalPayloadPath = snapshot.SavePath,
            }
        );

        Assert.True(changed);
        Assert.Equal(TorrentCompletionCallbackState.PendingFinalization, snapshot.CompletionCallbackState);
        Assert.Equal(now, snapshot.CompletionCallbackPendingSinceUtc);
    }

    [Fact]
    public async Task ProcessPendingAsync_TimesOut_WhenActivityLogWriteFails()
    {
        var processor = new TorrentCompletionCallbackProcessor(
            new StubFinalizationChecker(),
            new StubCompletionCallbackInvoker(),
            new ThrowingActivityLogService(new IOException("activity log unavailable")),
            new ServiceInstanceContext()
        );

        var now = DateTimeOffset.UtcNow;
        var snapshot = CreateCompletedSnapshot(now);
        snapshot.CompletionCallbackState           = TorrentCompletionCallbackState.PendingFinalization;
        snapshot.CompletionCallbackPendingSinceUtc = now.AddSeconds(-10);

        var changed = await processor.ProcessPendingAsync(
            snapshot,
            CreateRuntimeSettings(completionCallbackFinalizationTimeoutSeconds: 1),
            now,
            CancellationToken.None,
            new TorrentCompletionFinalizationCheckResult
            {
                IsReady = false,
                FinalPayloadPath = snapshot.SavePath,
                PendingReason = "The final payload is not visible yet.",
            }
        );

        Assert.True(changed);
        Assert.Equal(TorrentCompletionCallbackState.TimedOut, snapshot.CompletionCallbackState);
        Assert.Contains("Timed out waiting for final payload visibility", snapshot.CompletionCallbackLastError);
    }

    private static RuntimeSettingsSnapshot CreateRuntimeSettings(
        int completionCallbackFinalizationTimeoutSeconds = 120,
        string completionCallbackCommandPath = "/bin/sh",
        string? completionCallbackArguments = null)
    {
        return new RuntimeSettingsSnapshot
        {
            UsesPersistedOverrides = false,
            PartialFilesEnabled = true,
            PartialFileSuffix = ".!mt",
            SeedingStopMode = SeedingStopMode.Unlimited,
            SeedingStopRatio = 1,
            SeedingStopMinutes = 60,
            CompletedTorrentCleanupMode = CompletedTorrentCleanupMode.Never,
            CompletedTorrentCleanupMinutes = 60,
            DeleteLogsForCompletedTorrents = false,
            EngineConnectionFailureLogBurstLimit = 5,
            EngineConnectionFailureLogWindowSeconds = 60,
            EngineEncryptionMode = TorrentEncryptionMode.EncryptedPreferred,
            EngineMaximumConnections = 150,
            EngineMaximumHalfOpenConnections = 8,
            EngineMaximumDownloadRateBytesPerSecond = 0,
            EngineMaximumUploadRateBytesPerSecond = 0,
            MaxActiveMetadataResolutions = 4,
            MaxActiveDownloads = 4,
            MetadataRefreshStaleSeconds = 90,
            MetadataRefreshRestartDelaySeconds = 30,
            CompletionCallbackEnabled = true,
            CompletionCallbackCommandPath = completionCallbackCommandPath,
            CompletionCallbackArguments = completionCallbackArguments,
            CompletionCallbackWorkingDirectory = null,
            CompletionCallbackTimeoutSeconds = 30,
            CompletionCallbackFinalizationTimeoutSeconds = completionCallbackFinalizationTimeoutSeconds,
            CompletionCallbackApiBaseUrlOverride = null,
            CompletionCallbackApiKeyOverride = null,
            EngineSettingsRequireRestart = false,
            UpdatedAtUtc = null,
        };
    }

    private static TorrentSnapshot CreateCompletedSnapshot(DateTimeOffset now)
    {
        return new TorrentSnapshot
        {
            TorrentId = Guid.NewGuid(),
            Name = "Queued Complete Torrent",
            CategoryKey = "TV",
            CompletionCallbackLabel = "TV",
            InvokeCompletionCallback = true,
            CompletionCallbackState = null,
            CompletionCallbackPendingSinceUtc = null,
            CompletionCallbackInvokedAtUtc = null,
            CompletionCallbackLastError = null,
            CompletionCallbackFeedbackReceivedAtUtc = null,
            CompletionCallbackFeedbackJson = null,
            State = TorrentState.Queued,
            DesiredState = TorrentDesiredState.Runnable,
            MagnetUri = "magnet:?xt=urn:btih:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&dn=Queued+Complete+Torrent",
            InfoHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            DownloadRootPath = "/downloads/TV",
            SavePath = "/downloads/TV/Queued Complete Torrent",
            ProgressPercent = 100,
            DownloadedBytes = 100,
            UploadedBytes = 0,
            TotalBytes = 100,
            DownloadRateBytesPerSecond = 0,
            UploadRateBytesPerSecond = 0,
            TrackerCount = 1,
            ConnectedPeerCount = 0,
            AddedAtUtc = now.AddMinutes(-5),
            CompletedAtUtc = now,
            SeedingStartedAtUtc = null,
            LastActivityAtUtc = now,
            ErrorMessage = null,
        };
    }

    private sealed class StubFinalizationChecker : ITorrentCompletionFinalizationChecker
    {
        public TorrentCompletionFinalizationCheckResult Check(
            TorrentSnapshot snapshot,
            RuntimeSettingsSnapshot runtimeSettings,
            IReadOnlyList<TorrentCompletionObservedFilePaths>? observedFiles = null)
        {
            return new TorrentCompletionFinalizationCheckResult
            {
                IsReady = true,
                FinalPayloadPath = snapshot.SavePath,
            };
        }
    }

    private sealed class StubCompletionCallbackInvoker : ITorrentCompletionCallbackInvoker
    {
        public Task<TorrentCompletionCallbackInvocationResult> InvokeAsync(
            TorrentSnapshot currentSnapshot,
            string? finalPayloadPath,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new TorrentCompletionCallbackInvocationResult
            {
                Status = TorrentCompletionCallbackInvocationStatus.Invoked,
            });
        }
    }

    private sealed class RecordingActivityLogService : IActivityLogService
    {
        public Task EnsureInitializedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteAsync(ActivityLogWriteRequest request, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ActivityLogEntry>> GetRecentAsync(
            ActivityLogQuery query,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ActivityLogEntry>>([]);

        public Task<int> DeleteByTorrentIdAsync(Guid torrentId, CancellationToken cancellationToken)
            => Task.FromResult(0);

        public Task<int> DeleteOrphanedTorrentLogsAsync(CancellationToken cancellationToken)
            => Task.FromResult(0);
    }

    private sealed class ThrowingActivityLogService(Exception writeFailure) : IActivityLogService
    {
        public Task EnsureInitializedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WriteAsync(ActivityLogWriteRequest request, CancellationToken cancellationToken)
            => Task.FromException(writeFailure);

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
