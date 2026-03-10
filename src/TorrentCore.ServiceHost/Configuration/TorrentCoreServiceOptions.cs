namespace TorrentCore.Service.Configuration;

public sealed class TorrentCoreServiceOptions
{
    public const string SectionName = "TorrentCore";

    public string DownloadRootPath { get; init; } = TorrentCoreDefaultPaths.GetDefaultDownloadRootPath();
    public string StorageRootPath { get; init; } = TorrentCoreDefaultPaths.GetDefaultStorageRootPath();
    public int MaxActivityLogEntries { get; init; } = 20_000;
}
