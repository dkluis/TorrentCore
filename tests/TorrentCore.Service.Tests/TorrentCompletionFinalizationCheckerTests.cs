using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Torrents;
using TorrentCore.Service.Callbacks;
using TorrentCore.Service.Configuration;

namespace TorrentCore.Service.Tests;

public sealed class TorrentCompletionFinalizationCheckerTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(
        Path.GetTempPath(), "torrentcore-finalization-tests", Guid.NewGuid().ToString("N")
    );

    [Fact]
    public void Check_ObservedSingleFilePaths_UsesExactCompletePath_AndBlocksOnActivePartialSibling()
    {
        Directory.CreateDirectory(_rootPath);
        var finalFilePath   = Path.Combine(_rootPath, "Actual Episode.mkv");
        var partialFilePath = finalFilePath + ".!mt";
        File.WriteAllText(finalFilePath, "final");
        File.WriteAllText(partialFilePath, "partial");

        var checker = CreateChecker();
        var result = checker.Check(
            CreateSnapshot("Different Torrent Name"),
            CreateRuntimeSettings(),
            [
                new TorrentCompletionObservedFilePaths
                {
                    CompletePath = finalFilePath,
                    ActivePath = partialFilePath,
                    IncompletePath = partialFilePath,
                }
            ]
        );

        Assert.False(result.IsReady);
        Assert.Equal(finalFilePath, result.FinalPayloadPath);
        Assert.Equal("The partial-suffix sibling is still visible.", result.PendingReason);
    }

    [Fact]
    public void Check_ObservedSingleFilePaths_IgnoresStalePartialSibling_WhenMonoTorrentUsesCompletePath()
    {
        Directory.CreateDirectory(_rootPath);
        var finalFilePath   = Path.Combine(_rootPath, "Actual Episode.mkv");
        var partialFilePath = finalFilePath + ".!mt";
        File.WriteAllText(finalFilePath, "final");
        File.WriteAllText(partialFilePath, "stale");

        var checker = CreateChecker();
        var result = checker.Check(
            CreateSnapshot("Different Torrent Name"),
            CreateRuntimeSettings(),
            [
                new TorrentCompletionObservedFilePaths
                {
                    CompletePath = finalFilePath,
                    ActivePath = finalFilePath,
                    IncompletePath = partialFilePath,
                }
            ]
        );

        Assert.True(result.IsReady);
        Assert.Equal(finalFilePath, result.FinalPayloadPath);
        Assert.Null(result.PendingReason);
    }

    [Fact]
    public void Check_ObservedSingleFilePaths_AllowsSingleFileNameDifferentFromSnapshotName()
    {
        Directory.CreateDirectory(_rootPath);
        var finalFilePath = Path.Combine(_rootPath, "Actual Movie.mkv");
        File.WriteAllText(finalFilePath, "final");

        var checker = CreateChecker();
        var result = checker.Check(
            CreateSnapshot("Different Torrent Name"),
            CreateRuntimeSettings(),
            [
                new TorrentCompletionObservedFilePaths
                {
                    CompletePath = finalFilePath,
                    ActivePath = finalFilePath,
                    IncompletePath = finalFilePath + ".!mt",
                }
            ]
        );

        Assert.True(result.IsReady);
        Assert.Equal(finalFilePath, result.FinalPayloadPath);
        Assert.Null(result.PendingReason);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    private TorrentCompletionFinalizationChecker CreateChecker()
    {
        return new TorrentCompletionFinalizationChecker(
            new ResolvedTorrentCoreServicePaths
            {
                DownloadRootPath = _rootPath,
                StorageRootPath = Path.Combine(_rootPath, "storage"),
                DatabaseFilePath = Path.Combine(_rootPath, "storage", "torrentcore.db"),
            }
        );
    }

    private TorrentSnapshot CreateSnapshot(string name)
    {
        return new TorrentSnapshot
        {
            TorrentId = Guid.NewGuid(),
            Name = name,
            CategoryKey = "TV",
            CompletionCallbackLabel = "TV",
            InvokeCompletionCallback = true,
            CompletionCallbackState = TorrentCompletionCallbackState.PendingFinalization,
            CompletionCallbackPendingSinceUtc = DateTimeOffset.UtcNow,
            CompletionCallbackInvokedAtUtc = null,
            CompletionCallbackLastError = null,
            State = TorrentState.Completed,
            DesiredState = TorrentDesiredState.Runnable,
            MagnetUri = "magnet:?xt=urn:btih:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&dn=Example",
            InfoHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            DownloadRootPath = _rootPath,
            SavePath = _rootPath,
            ProgressPercent = 100,
            DownloadedBytes = 100,
            UploadedBytes = 0,
            TotalBytes = 100,
            DownloadRateBytesPerSecond = 0,
            UploadRateBytesPerSecond = 0,
            TrackerCount = 0,
            ConnectedPeerCount = 0,
            AddedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            SeedingStartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            LastActivityAtUtc = DateTimeOffset.UtcNow,
            ErrorMessage = null,
        };
    }

    private static RuntimeSettingsSnapshot CreateRuntimeSettings()
    {
        return new RuntimeSettingsSnapshot
        {
            UsesPersistedOverrides = false,
            PartialFilesEnabled = true,
            PartialFileSuffix = ".!mt",
            SeedingStopMode = SeedingStopMode.Unlimited,
            SeedingStopRatio = 1.0,
            SeedingStopMinutes = 60,
            CompletedTorrentCleanupMode = CompletedTorrentCleanupMode.Never,
            CompletedTorrentCleanupMinutes = 60,
            DeleteLogsForCompletedTorrents = false,
            EngineConnectionFailureLogBurstLimit = 10,
            EngineConnectionFailureLogWindowSeconds = 60,
            EngineMaximumConnections = 200,
            EngineMaximumHalfOpenConnections = 8,
            EngineMaximumDownloadRateBytesPerSecond = 0,
            EngineMaximumUploadRateBytesPerSecond = 0,
            MaxActiveMetadataResolutions = 4,
            MaxActiveDownloads = 4,
            MetadataRefreshStaleSeconds = 30,
            MetadataRefreshRestartDelaySeconds = 30,
            CompletionCallbackEnabled = true,
            CompletionCallbackCommandPath = "/bin/sh",
            CompletionCallbackArguments = null,
            CompletionCallbackWorkingDirectory = null,
            CompletionCallbackTimeoutSeconds = 30,
            CompletionCallbackFinalizationTimeoutSeconds = 120,
            CompletionCallbackApiBaseUrlOverride = null,
            CompletionCallbackApiKeyOverride = null,
            EngineSettingsRequireRestart = false,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
    }
}
