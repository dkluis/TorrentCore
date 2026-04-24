using TorrentCore.Contracts.Categories;
using TorrentCore.Contracts.Diagnostics;
using TorrentCore.Contracts.Host;
using TorrentCore.Contracts.Torrents;

namespace TorrentCore.WebUI.Services;

public interface ITorrentCoreApiAdapter
{
    Task<ServiceCallResult<ServiceHealthDto?>> GetHealthAsync(CancellationToken cancellationToken = default);
    Task<ServiceCallResult<EngineHostStatusDto?>> GetHostStatusAsync(CancellationToken cancellationToken = default);
    Task<ServiceCallResult<DashboardLifecycleSummaryDto?>> GetDashboardLifecycleAsync(
        CancellationToken cancellationToken = default
    );
    Task<ServiceCallResult<ServiceRestartRequestResultDto>> RequestServiceRestartAsync(
        CancellationToken cancellationToken = default
    );
    Task<ServiceCallResult<RuntimeSettingsDto?>> GetRuntimeSettingsAsync(CancellationToken cancellationToken = default);
    Task<ServiceCallResult<RuntimeSettingsDto>> UpdateRuntimeSettingsAsync(
        UpdateRuntimeSettingsRequest request,
        CancellationToken cancellationToken = default
    );
    Task<ServiceCallResult<IReadOnlyList<TorrentCategoryDto>>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<ServiceCallResult<TorrentCategoryDto>> UpdateCategoryAsync(
        string key,
        UpdateTorrentCategoryRequest request,
        CancellationToken cancellationToken = default
    );
    Task<ServiceCallResult<IReadOnlyList<TorrentSummaryDto>>> GetTorrentsAsync(CancellationToken cancellationToken = default);
    Task<ServiceCallResult<IReadOnlyList<ActivityLogEntryDto>>> GetRecentLogsAsync(
        int take = 100,
        string? category = null,
        string? eventType = null,
        string? level = null,
        Guid? torrentId = null,
        Guid? serviceInstanceId = null,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        CancellationToken cancellationToken = default
    );
    Task<ServiceCallResult<DeleteOrphanedTorrentLogsResultDto>> DeleteOrphanedTorrentLogsAsync(
        CancellationToken cancellationToken = default
    );
    Task<ServiceCallResult<TorrentDetailDto?>> GetTorrentAsync(Guid torrentId, CancellationToken cancellationToken = default);
    Task<ServiceCallResult<IReadOnlyList<TorrentPeerDto>>> GetTorrentPeersAsync(Guid torrentId, CancellationToken cancellationToken = default);
    Task<ServiceCallResult<IReadOnlyList<TorrentTrackerDto>>> GetTorrentTrackersAsync(Guid torrentId, CancellationToken cancellationToken = default);
    Task<ServiceCallResult<TorrentDetailDto>> AddMagnetAsync(AddMagnetRequest request, CancellationToken cancellationToken = default);
    Task<ServiceCallResult<TorrentActionResultDto>> PauseAsync(Guid torrentId, CancellationToken cancellationToken = default);
    Task<ServiceCallResult<TorrentActionResultDto>> ResumeAsync(Guid torrentId, CancellationToken cancellationToken = default);
    Task<ServiceCallResult<TorrentActionResultDto>> RefreshMetadataAsync(Guid torrentId, CancellationToken cancellationToken = default);
    Task<ServiceCallResult<TorrentActionResultDto>> ResetMetadataSessionAsync(Guid torrentId, CancellationToken cancellationToken = default);
    Task<ServiceCallResult<TorrentActionResultDto>> RetryCompletionCallbackAsync(Guid torrentId, CancellationToken cancellationToken = default);
    Task<ServiceCallResult<TorrentActionResultDto>> RemoveAsync(
        Guid torrentId,
        RemoveTorrentRequest request,
        CancellationToken cancellationToken = default
    );
}
