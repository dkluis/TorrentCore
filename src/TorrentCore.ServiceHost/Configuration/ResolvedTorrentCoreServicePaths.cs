namespace TorrentCore.Service.Configuration;

public sealed class ResolvedTorrentCoreServicePaths
{
    public required string DownloadRootPath { get; init; }
    public required string StorageRootPath  { get; init; }
    public required string DatabaseFilePath { get; init; }
}
