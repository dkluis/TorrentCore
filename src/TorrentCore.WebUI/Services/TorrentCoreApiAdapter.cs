using TorrentCore.Client;
using TorrentCore.Contracts.Categories;
using TorrentCore.Contracts.Diagnostics;
using TorrentCore.Contracts.Host;
using TorrentCore.Contracts.Torrents;

namespace TorrentCore.WebUI.Services;

public sealed class TorrentCoreApiAdapter(TorrentCoreClient client) : ITorrentCoreApiAdapter
{
    public Task<ServiceCallResult<ServiceHealthDto?>> GetHealthAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync(() => client.GetHealthAsync(cancellationToken));

    public Task<ServiceCallResult<EngineHostStatusDto?>> GetHostStatusAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync(() => client.GetHostStatusAsync(cancellationToken));

    public Task<ServiceCallResult<DashboardLifecycleSummaryDto?>> GetDashboardLifecycleAsync(
        CancellationToken cancellationToken = default)
        => ExecuteAsync(() => client.GetDashboardLifecycleAsync(cancellationToken));

    public Task<ServiceCallResult<RuntimeSettingsDto?>> GetRuntimeSettingsAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync(() => client.GetRuntimeSettingsAsync(cancellationToken));

    public Task<ServiceCallResult<RuntimeSettingsDto>> UpdateRuntimeSettingsAsync(
        UpdateRuntimeSettingsRequest request,
        CancellationToken cancellationToken = default
    )
        => ExecuteAsync(() => client.UpdateRuntimeSettingsAsync(request, cancellationToken));

    public Task<ServiceCallResult<IReadOnlyList<TorrentCategoryDto>>> GetCategoriesAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync(() => client.GetCategoriesAsync(cancellationToken));

    public Task<ServiceCallResult<TorrentCategoryDto>> UpdateCategoryAsync(
        string key,
        UpdateTorrentCategoryRequest request,
        CancellationToken cancellationToken = default
    )
        => ExecuteAsync(() => client.UpdateCategoryAsync(key, request, cancellationToken));

    public Task<ServiceCallResult<IReadOnlyList<TorrentSummaryDto>>> GetTorrentsAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync(() => client.GetTorrentsAsync(cancellationToken));

    public Task<ServiceCallResult<IReadOnlyList<ActivityLogEntryDto>>> GetRecentLogsAsync(
        int take = 100,
        string? category = null,
        string? eventType = null,
        string? level = null,
        Guid? torrentId = null,
        Guid? serviceInstanceId = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default
    )
    {
        return ExecuteAsync(
            () => client.GetRecentLogsAsync(
                take,
                category,
                eventType,
                level,
                torrentId,
                serviceInstanceId,
                fromUtc,
                toUtc,
                cancellationToken
            )
        );
    }

    public Task<ServiceCallResult<DeleteOrphanedTorrentLogsResultDto>> DeleteOrphanedTorrentLogsAsync(
        CancellationToken cancellationToken = default
    )
        => ExecuteAsync(() => client.DeleteOrphanedTorrentLogsAsync(cancellationToken));

    public Task<ServiceCallResult<TorrentDetailDto?>> GetTorrentAsync(
        Guid torrentId,
        CancellationToken cancellationToken = default
    )
        => ExecuteAsync(() => client.GetTorrentAsync(torrentId, cancellationToken));

    public Task<ServiceCallResult<IReadOnlyList<TorrentPeerDto>>> GetTorrentPeersAsync(
        Guid torrentId,
        CancellationToken cancellationToken = default
    )
        => ExecuteAsync(() => client.GetTorrentPeersAsync(torrentId, cancellationToken));

    public Task<ServiceCallResult<IReadOnlyList<TorrentTrackerDto>>> GetTorrentTrackersAsync(
        Guid torrentId,
        CancellationToken cancellationToken = default
    )
        => ExecuteAsync(() => client.GetTorrentTrackersAsync(torrentId, cancellationToken));

    public Task<ServiceCallResult<TorrentDetailDto>> AddMagnetAsync(
        AddMagnetRequest request,
        CancellationToken cancellationToken = default
    )
        => ExecuteAsync(() => client.AddMagnetAsync(request, cancellationToken));

    public Task<ServiceCallResult<TorrentActionResultDto>> PauseAsync(
        Guid torrentId,
        CancellationToken cancellationToken = default
    )
        => ExecuteAsync(() => client.PauseAsync(torrentId, cancellationToken));

    public Task<ServiceCallResult<TorrentActionResultDto>> ResumeAsync(
        Guid torrentId,
        CancellationToken cancellationToken = default
    )
        => ExecuteAsync(() => client.ResumeAsync(torrentId, cancellationToken));

    public Task<ServiceCallResult<TorrentActionResultDto>> RefreshMetadataAsync(
        Guid torrentId,
        CancellationToken cancellationToken = default
    )
        => ExecuteAsync(() => client.RefreshMetadataAsync(torrentId, cancellationToken));

    public Task<ServiceCallResult<TorrentActionResultDto>> ResetMetadataSessionAsync(
        Guid torrentId,
        CancellationToken cancellationToken = default
    )
        => ExecuteAsync(() => client.ResetMetadataSessionAsync(torrentId, cancellationToken));

    public Task<ServiceCallResult<TorrentActionResultDto>> RetryCompletionCallbackAsync(
        Guid torrentId,
        CancellationToken cancellationToken = default
    )
        => ExecuteAsync(() => client.RetryCompletionCallbackAsync(torrentId, cancellationToken));

    public Task<ServiceCallResult<TorrentActionResultDto>> RemoveAsync(
        Guid torrentId,
        RemoveTorrentRequest request,
        CancellationToken cancellationToken = default
    )
        => ExecuteAsync(() => client.RemoveAsync(torrentId, request, cancellationToken));

    private static async Task<ServiceCallResult<T>> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            var value = await operation();
            return ServiceCallResult<T>.Success(value);
        }
        catch (TorrentCoreClientException exception)
        {
            return ServiceCallResult<T>.Failure(
                exception.ServiceError?.Message ?? exception.Message,
                exception.StatusCode,
                exception.ServiceError?.Code,
                exception.ServiceError?.TraceId
            );
        }
        catch (Exception exception)
        {
            return ServiceCallResult<T>.Failure($"Unable to reach TorrentCore service: {exception.Message}");
        }
    }
}
