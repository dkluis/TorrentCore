namespace TorrentCore.Core.Categories;

public interface ITorrentCategoryStore
{
    Task EnsureInitializedAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<TorrentCategoryDefinition>> ListAsync(CancellationToken cancellationToken);
    Task<TorrentCategoryDefinition?> GetAsync(string key, CancellationToken cancellationToken);
    Task UpsertAsync(TorrentCategoryDefinition category, CancellationToken cancellationToken);
}
