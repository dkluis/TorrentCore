namespace TorrentCore.Service.Configuration;

public sealed class ResolvedTorrentCategorySelection
{
    public string? CategoryKey { get; init; }
    public required string DownloadRootPath { get; init; }
    public string? CompletionCallbackLabel { get; init; }
    public required bool InvokeCompletionCallback { get; init; }
}
