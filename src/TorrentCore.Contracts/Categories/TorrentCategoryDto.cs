namespace TorrentCore.Contracts.Categories;

public sealed class TorrentCategoryDto
{
    public required string Key                      { get; init; }
    public required string DisplayName              { get; init; }
    public required string CallbackLabel            { get; init; }
    public required string DownloadRootPath         { get; init; }
    public required bool   Enabled                  { get; init; }
    public required bool   InvokeCompletionCallback { get; init; }
    public required int    SortOrder                { get; init; }
}
