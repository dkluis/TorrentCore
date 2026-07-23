#region

using TorrentCore.Contracts.History;
using TorrentCore.Core.Torrents;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Service.Configuration;

#endregion

namespace TorrentCore.Service.Application;

public interface ITorrentHistoryService
{
    Task CreateOnAddAsync(TorrentDetailDto torrent, ResolvedTorrentCategorySelection categorySelection,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<TorrentHistorySummaryDto>> GetHistoryAsync(TorrentHistoryQueryRequest request,
        CancellationToken cancellationToken);
    Task<TorrentHistoryDetailDto> GetHistoryByTorrentIdAsync(Guid torrentId, CancellationToken cancellationToken);
    Task ObserveSnapshotAsync(TorrentSnapshot snapshot, CancellationToken cancellationToken);
    Task MarkRemovedAsync(TorrentSnapshot snapshot, bool dataDeleted, string removalReason,
        TorrentRemovalKind removalKind, bool removedByCleanupPolicy, DateTimeOffset removedAtUtc,
        CancellationToken cancellationToken);
    Task MarkRemovedAsync(Guid torrentId, bool dataDeleted, string removalReason,
        TorrentRemovalKind removalKind, bool removedByCleanupPolicy, DateTimeOffset removedAtUtc,
        CancellationToken cancellationToken);
}
