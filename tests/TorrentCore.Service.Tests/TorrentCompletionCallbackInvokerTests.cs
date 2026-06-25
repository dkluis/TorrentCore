using Microsoft.Extensions.Logging.Abstractions;
using TorrentCore.Contracts.Host;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Core.Torrents;
using TorrentCore.Service.Callbacks;
using TorrentCore.Service.Configuration;

namespace TorrentCore.Service.Tests;

public sealed class TorrentCompletionCallbackInvokerTests
{
    [Fact]
    public async Task InvokeAsync_ReturnsInvoked_WhenActivityLogWriteFails()
    {
        var rootPath = CreateTempRootPath("torrentcore-callback-invoker-log-failure");
        var invoker = new TorrentCompletionCallbackInvoker(
            new StubRuntimeSettingsService(
                CreateRuntimeSettings(
                    completionCallbackCommandPath: "/bin/sh",
                    completionCallbackArguments: "-c \"exit 0\"",
                    completionCallbackWorkingDirectory: rootPath
                )
            ),
            new ResolvedTorrentCoreServicePaths
            {
                DownloadRootPath = rootPath,
                StorageRootPath = rootPath,
                DatabaseFilePath = Path.Combine(rootPath, "torrentcore.db"),
            },
            new ThrowingActivityLogService(new IOException("activity log unavailable")),
            new ServiceInstanceContext(),
            NullLogger<TorrentCompletionCallbackInvoker>.Instance
        );

        var result = await invoker.InvokeAsync(
            CreateCompletedSnapshot(rootPath),
            Path.Combine(rootPath, "Final Payload"),
            CancellationToken.None
        );

        Assert.Equal(TorrentCompletionCallbackInvocationStatus.Invoked, result.Status);
        Assert.Null(result.Error);
    }

    private static RuntimeSettingsSnapshot CreateRuntimeSettings(
        string completionCallbackCommandPath,
        string completionCallbackArguments,
        string completionCallbackWorkingDirectory)
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
            CompletionCallbackWorkingDirectory = completionCallbackWorkingDirectory,
            CompletionCallbackTimeoutSeconds = 30,
            CompletionCallbackFinalizationTimeoutSeconds = 120,
            CompletionCallbackApiBaseUrlOverride = null,
            CompletionCallbackApiKeyOverride = null,
            EngineSettingsRequireRestart = false,
            UpdatedAtUtc = null,
        };
    }

    private static TorrentSnapshot CreateCompletedSnapshot(string rootPath)
    {
        var now = DateTimeOffset.UtcNow;
        return new TorrentSnapshot
        {
            TorrentId = Guid.NewGuid(),
            Name = "Invoker Log Failure",
            CategoryKey = "TV",
            CompletionCallbackLabel = "TV",
            InvokeCompletionCallback = true,
            CompletionCallbackState = TorrentCompletionCallbackState.PendingFinalization,
            CompletionCallbackPendingSinceUtc = now,
            CompletionCallbackInvokedAtUtc = null,
            CompletionCallbackLastError = null,
            CompletionCallbackFeedbackReceivedAtUtc = null,
            CompletionCallbackFeedbackJson = null,
            State = TorrentState.Completed,
            DesiredState = TorrentDesiredState.Runnable,
            MagnetUri = "magnet:?xt=urn:btih:BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB&dn=Invoker+Log+Failure",
            InfoHash = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
            DownloadRootPath = rootPath,
            SavePath = Path.Combine(rootPath, "Invoker Log Failure"),
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

    private static string CreateTempRootPath(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class StubRuntimeSettingsService(RuntimeSettingsSnapshot settings) : IRuntimeSettingsService
    {
        public Task<RuntimeSettingsSnapshot> GetEffectiveSettingsAsync(CancellationToken cancellationToken)
            => Task.FromResult(settings);

        public Task<RuntimeSettingsDto> GetRuntimeSettingsDtoAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<RuntimeSettingsDto> UpdateAsync(UpdateRuntimeSettingsRequest request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
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
