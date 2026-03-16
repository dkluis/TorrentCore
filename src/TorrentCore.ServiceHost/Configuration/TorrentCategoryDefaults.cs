using TorrentCore.Core.Categories;

namespace TorrentCore.Service.Configuration;

public static class TorrentCategoryDefaults
{
    public static IReadOnlyList<TorrentCategoryDefinition> Create(string baseDownloadRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDownloadRootPath);

        return
        [
            Create("TV", 1, baseDownloadRootPath),
            Create("Movie", 2, baseDownloadRootPath),
            Create("Audiobook", 3, baseDownloadRootPath),
            Create("Music", 4, baseDownloadRootPath),
        ];
    }

    private static TorrentCategoryDefinition Create(string key, int sortOrder, string baseDownloadRootPath)
    {
        var now = DateTimeOffset.UtcNow;
        return new TorrentCategoryDefinition
        {
            Key = key,
            DisplayName = key,
            CallbackLabel = key,
            DownloadRootPath = Path.Combine(baseDownloadRootPath, key),
            Enabled = true,
            InvokeCompletionCallback = true,
            SortOrder = sortOrder,
            UpdatedAtUtc = now,
        };
    }
}
