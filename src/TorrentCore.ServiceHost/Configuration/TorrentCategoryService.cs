using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using TorrentCore.Contracts.Categories;
using TorrentCore.Core.Categories;
using TorrentCore.Service.Application;

namespace TorrentCore.Service.Configuration;

public sealed class TorrentCategoryService(
    ITorrentCategoryStore torrentCategoryStore,
    IHostEnvironment hostEnvironment,
    ResolvedTorrentCoreServicePaths servicePaths) : ITorrentCategoryService
{
    public async Task EnsureDefaultCategoriesAsync(CancellationToken cancellationToken)
    {
        foreach (var category in TorrentCategoryDefaults.Create(servicePaths.DownloadRootPath))
        {
            var existing = await torrentCategoryStore.GetAsync(category.Key, cancellationToken);
            if (existing is not null)
            {
                continue;
            }

            Directory.CreateDirectory(category.DownloadRootPath);
            await torrentCategoryStore.UpsertAsync(category, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<TorrentCategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        await EnsureDefaultCategoriesAsync(cancellationToken);
        var categories = await torrentCategoryStore.ListAsync(cancellationToken);
        return categories.Select(MapDto).ToArray();
    }

    public async Task<TorrentCategoryDto> UpdateCategoryAsync(string key, UpdateTorrentCategoryRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await EnsureDefaultCategoriesAsync(cancellationToken);

        var category = await torrentCategoryStore.GetAsync(key.Trim(), cancellationToken);
        if (category is null)
        {
            throw new ServiceOperationException(
                "category_not_found",
                $"Category '{key}' was not found.",
                StatusCodes.Status404NotFound,
                nameof(key));
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            throw new ServiceOperationException(
                "invalid_category",
                "DisplayName is required.",
                StatusCodes.Status400BadRequest,
                nameof(request.DisplayName));
        }

        if (string.IsNullOrWhiteSpace(request.CallbackLabel))
        {
            throw new ServiceOperationException(
                "invalid_category",
                "CallbackLabel is required.",
                StatusCodes.Status400BadRequest,
                nameof(request.CallbackLabel));
        }

        if (string.IsNullOrWhiteSpace(request.DownloadRootPath))
        {
            throw new ServiceOperationException(
                "invalid_category",
                "DownloadRootPath is required.",
                StatusCodes.Status400BadRequest,
                nameof(request.DownloadRootPath));
        }

        if (request.SortOrder < 0)
        {
            throw new ServiceOperationException(
                "invalid_category",
                "SortOrder must be 0 or greater.",
                StatusCodes.Status400BadRequest,
                nameof(request.SortOrder));
        }

        var resolvedDownloadRootPath = ResolveAbsolutePath(request.DownloadRootPath);
        Directory.CreateDirectory(resolvedDownloadRootPath);

        var updated = new TorrentCategoryDefinition
        {
            Key = category.Key,
            DisplayName = request.DisplayName.Trim(),
            CallbackLabel = request.CallbackLabel.Trim(),
            DownloadRootPath = resolvedDownloadRootPath,
            Enabled = request.Enabled,
            InvokeCompletionCallback = request.InvokeCompletionCallback,
            SortOrder = request.SortOrder,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        await torrentCategoryStore.UpsertAsync(updated, cancellationToken);
        return MapDto(updated);
    }

    public async Task<ResolvedTorrentCategorySelection> ResolveSelectionAsync(string? requestedCategoryKey, CancellationToken cancellationToken)
    {
        await EnsureDefaultCategoriesAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(requestedCategoryKey))
        {
            return new ResolvedTorrentCategorySelection
            {
                CategoryKey = null,
                DownloadRootPath = servicePaths.DownloadRootPath,
            };
        }

        var normalizedKey = requestedCategoryKey.Trim();
        var category = await torrentCategoryStore.GetAsync(normalizedKey, cancellationToken);
        if (category is null)
        {
            throw new ServiceOperationException(
                "invalid_category",
                $"Category '{normalizedKey}' was not found.",
                StatusCodes.Status400BadRequest,
                nameof(requestedCategoryKey));
        }

        if (!category.Enabled)
        {
            throw new ServiceOperationException(
                "category_disabled",
                $"Category '{normalizedKey}' is disabled.",
                StatusCodes.Status400BadRequest,
                nameof(requestedCategoryKey));
        }

        Directory.CreateDirectory(category.DownloadRootPath);

        return new ResolvedTorrentCategorySelection
        {
            CategoryKey = category.Key,
            DownloadRootPath = category.DownloadRootPath,
        };
    }

    private string ResolveAbsolutePath(string configuredPath)
    {
        var path = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(hostEnvironment.ContentRootPath, configuredPath);

        return Path.GetFullPath(path);
    }

    private static TorrentCategoryDto MapDto(TorrentCategoryDefinition category)
    {
        return new TorrentCategoryDto
        {
            Key = category.Key,
            DisplayName = category.DisplayName,
            CallbackLabel = category.CallbackLabel,
            DownloadRootPath = category.DownloadRootPath,
            Enabled = category.Enabled,
            InvokeCompletionCallback = category.InvokeCompletionCallback,
            SortOrder = category.SortOrder,
        };
    }
}
