#region

using TorrentCore.Contracts.Categories;

#endregion

namespace TorrentCore.Service.Configuration;

public interface ITorrentCategoryService
{
    Task                                    EnsureDefaultCategoriesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<TorrentCategoryDto>> GetCategoriesAsync(CancellationToken           cancellationToken);

    Task<TorrentCategoryDto> UpdateCategoryAsync(string key, UpdateTorrentCategoryRequest request,
        CancellationToken                               cancellationToken);

    Task<ResolvedTorrentCategorySelection> ResolveSelectionAsync(string? requestedCategoryKey,
        CancellationToken                                                cancellationToken);
}
