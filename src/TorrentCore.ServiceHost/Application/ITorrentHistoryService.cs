#region

using TorrentCore.Contracts.Torrents;
using TorrentCore.Service.Configuration;

#endregion

namespace TorrentCore.Service.Application;

public interface ITorrentHistoryService
{
    Task CreateOnAddAsync(TorrentDetailDto torrent, ResolvedTorrentCategorySelection categorySelection,
        CancellationToken cancellationToken);
}
