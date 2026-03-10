namespace TorrentCore.Service.Configuration;

public sealed class TorrentCoreServiceOptions
{
    public const string SectionName = "TorrentCore";

    public TorrentEngineMode EngineMode { get; init; } = TorrentEngineMode.MonoTorrent;
    public int EngineListenPort { get; init; } = 55_123;
    public int EngineDhtPort { get; init; } = 55_124;
    public bool EngineAllowPortForwarding { get; init; } = true;
    public bool EngineAllowLocalPeerDiscovery { get; init; } = true;
    public int EngineConnectionFailureLogBurstLimit { get; init; } = 5;
    public int EngineConnectionFailureLogWindowSeconds { get; init; } = 60;
    public bool UsePartialFiles { get; init; } = true;
    public SeedingStopMode SeedingStopMode { get; init; } = SeedingStopMode.Unlimited;
    public double SeedingStopRatio { get; init; } = 1.0;
    public int SeedingStopMinutes { get; init; } = 60;
    public string DownloadRootPath { get; init; } = TorrentCoreDefaultPaths.GetDefaultDownloadRootPath();
    public string StorageRootPath { get; init; } = TorrentCoreDefaultPaths.GetDefaultStorageRootPath();
    public int MaxActivityLogEntries { get; init; } = 20_000;
    public int MaxActiveDownloads { get; init; } = 1;
    public int RuntimeTickIntervalMilliseconds { get; init; } = 1_000;
    public int MetadataResolutionDelayMilliseconds { get; init; } = 2_000;
    public double DownloadProgressPercentPerTick { get; init; } = 20;
}
