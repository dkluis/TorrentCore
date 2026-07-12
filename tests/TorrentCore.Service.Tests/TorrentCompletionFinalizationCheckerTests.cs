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
    public void Check_ObservedSingleFile_UsesExactCompletePath()
    {
        Directory.CreateDirectory(_rootPath);
        var finalFilePath = Path.Combine(_rootPath, "Actual Episode.mkv");
        File.WriteAllText(finalFilePath, "final");

        var result = CreateChecker().Check(
            CreateSnapshot("Different Torrent Name"),
            CreateRuntimeSettings(),
            [new TorrentCompletionObservedFilePaths { CompletePath = finalFilePath }]
        );

        Assert.True(result.IsReady);
        Assert.Equal(finalFilePath, result.FinalPayloadPath);
        Assert.Null(result.PendingReason);
    }

    [Fact]
    public void Check_ObservedFiles_RequiresEveryCompletePath()
    {
        Directory.CreateDirectory(_rootPath);
        var firstFilePath = Path.Combine(_rootPath, "Season 01", "Episode 01.mkv");
        var secondFilePath = Path.Combine(_rootPath, "Season 01", "Episode 02.mkv");
        Directory.CreateDirectory(Path.GetDirectoryName(firstFilePath)!);
        File.WriteAllText(firstFilePath, "final");

        var result = CreateChecker().Check(
            CreateSnapshot("Example Show"),
            CreateRuntimeSettings(),
            [
                new TorrentCompletionObservedFilePaths { CompletePath = firstFilePath },
                new TorrentCompletionObservedFilePaths { CompletePath = secondFilePath },
            ]
        );

        Assert.False(result.IsReady);
        Assert.Equal(Path.Combine(_rootPath, "Example Show"), result.FinalPayloadPath);
        Assert.Equal($"A final payload file is not visible yet: '{secondFilePath}'.", result.PendingReason);
    }

    [Fact]
    public void Check_DefaultPayloadPath_WaitsForVisibility()
    {
        Directory.CreateDirectory(_rootPath);
        var snapshot = CreateSnapshot("Example Movie.mkv");
        var checker = CreateChecker();

        var pendingResult = checker.Check(snapshot, CreateRuntimeSettings());
        File.WriteAllText(Path.Combine(_rootPath, snapshot.Name), "final");
        var readyResult = checker.Check(snapshot, CreateRuntimeSettings());

        Assert.False(pendingResult.IsReady);
        Assert.Equal("The final payload path is not visible yet.", pendingResult.PendingReason);
        Assert.True(readyResult.IsReady);
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
            PartialFilesEnabled = false,
            PartialFileSuffix = string.Empty,
            SeedingStopMode = SeedingStopMode.Unlimited,
            SeedingStopRatio = 1.0,
            SeedingStopMinutes = 60,
            CompletedTorrentCleanupMode = CompletedTorrentCleanupMode.Never,
            CompletedTorrentCleanupMinutes = 60,
            DeleteLogsForCompletedTorrents = false,
            EngineConnectionFailureLogBurstLimit = 10,
            EngineConnectionFailureLogWindowSeconds = 60,
            EngineEncryptionMode = TorrentEncryptionMode.EncryptedPreferred,
            EngineMaximumConnections = 200,
            EngineMaximumHalfOpenConnections = 25,
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
