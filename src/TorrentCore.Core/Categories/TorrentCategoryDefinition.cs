namespace TorrentCore.Core.Categories;

public sealed class TorrentCategoryDefinition
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required string CallbackLabel { get; init; }
    public required string DownloadRootPath { get; init; }
    public required bool Enabled { get; init; }
    public required bool InvokeCompletionCallback { get; init; }
    public required int SortOrder { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
}
