#region

using TorrentCore.Core.Torrents;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Service.Configuration;

#endregion

namespace TorrentCore.Service.Application;

public interface ITorrentHistoryService
{
    Task CreateOnAddAsync(TorrentDetailDto torrent, ResolvedTorrentCategorySelection categorySelection,
        CancellationToken cancellationToken);
    Task ObserveSnapshotAsync(TorrentSnapshot snapshot, CancellationToken cancellationToken);
}
