namespace TorrentCore.Service.Configuration;

public sealed class ResolvedTorrentCategorySelection
{
    public string? CategoryKey { get; init; }
    public required string DownloadRootPath { get; init; }
}
