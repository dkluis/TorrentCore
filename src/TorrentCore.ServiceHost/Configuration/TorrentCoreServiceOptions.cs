namespace TorrentCore.Service.Configuration;

public sealed class TorrentCoreServiceOptions
{
    public const string SectionName = "TorrentCore";

    public string DownloadRootPath { get; init; } = TorrentCoreDefaultPaths.GetDefaultDownloadRootPath();
    public string StorageRootPath { get; init; } = TorrentCoreDefaultPaths.GetDefaultStorageRootPath();
    public int MaxActivityLogEntries { get; init; } = 20_000;
    public int MaxActiveDownloads { get; init; } = 1;
    public int RuntimeTickIntervalMilliseconds { get; init; } = 1_000;
    public int MetadataResolutionDelayMilliseconds { get; init; } = 2_000;
    public double DownloadProgressPercentPerTick { get; init; } = 20;
}
