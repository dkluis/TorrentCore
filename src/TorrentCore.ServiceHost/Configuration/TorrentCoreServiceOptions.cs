namespace TorrentCore.Service.Configuration;

public sealed class TorrentCoreServiceOptions
{
    public const string SectionName = "TorrentCore";

    public TorrentEngineMode EngineMode { get; init; } = TorrentEngineMode.MonoTorrent;
    public int EngineListenPort { get; init; } = 55_123;
    public int EngineDhtPort { get; init; } = 55_124;
    public bool EngineAllowPortForwarding { get; init; } = true;
    public bool EngineAllowLocalPeerDiscovery { get; init; } = true;
    public int EngineMaximumConnections { get; init; } = 150;
    public int EngineMaximumHalfOpenConnections { get; init; } = 8;
    public int EngineMaximumDownloadRateBytesPerSecond { get; init; } = 0;
    public int EngineMaximumUploadRateBytesPerSecond { get; init; } = 0;
    public int EngineConnectionFailureLogBurstLimit { get; init; } = 5;
    public int EngineConnectionFailureLogWindowSeconds { get; init; } = 60;
    public bool UsePartialFiles { get; init; } = true;
    public SeedingStopMode SeedingStopMode { get; init; } = SeedingStopMode.Unlimited;
    public double SeedingStopRatio { get; init; } = 1.0;
    public int SeedingStopMinutes { get; init; } = 60;
    public CompletedTorrentCleanupMode CompletedTorrentCleanupMode { get; init; } = CompletedTorrentCleanupMode.Never;
    public int CompletedTorrentCleanupMinutes { get; init; } = 60;
    public string DownloadRootPath { get; init; } = TorrentCoreDefaultPaths.GetDefaultDownloadRootPath();
    public string StorageRootPath { get; init; } = TorrentCoreDefaultPaths.GetDefaultStorageRootPath();
    public int MaxActivityLogEntries { get; init; } = 20_000;
    public int MaxActiveMetadataResolutions { get; init; } = 4;
    public int MaxActiveDownloads { get; init; } = 4;
    public bool CompletionCallbackEnabled { get; init; }
    public string CompletionCallbackCommandPath { get; init; } = string.Empty;
    public string? CompletionCallbackArguments { get; init; }
    public string? CompletionCallbackWorkingDirectory { get; init; }
    public int CompletionCallbackTimeoutSeconds { get; init; } = 30;
    public int CompletionCallbackFinalizationTimeoutSeconds { get; init; } = 120;
    public string? CompletionCallbackApiBaseUrlOverride { get; init; }
    public string? CompletionCallbackApiKeyOverride { get; init; }
    public int RuntimeTickIntervalMilliseconds { get; init; } = 1_000;
    public int MetadataResolutionDelayMilliseconds { get; init; } = 2_000;
    public double DownloadProgressPercentPerTick { get; init; } = 20;
}
