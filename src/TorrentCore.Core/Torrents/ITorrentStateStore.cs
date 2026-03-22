namespace TorrentCore.Core.Torrents;

public interface ITorrentStateStore
{
    Task                                 EnsureInitializedAsync(CancellationToken cancellationToken);
    Task<int>                            CountAsync(CancellationToken cancellationToken);
    Task<bool>                           ExistsByInfoHashAsync(string infoHash, CancellationToken cancellationToken);
    Task<IReadOnlyList<TorrentSnapshot>> ListAsync(CancellationToken cancellationToken);
    Task<TorrentSnapshot?>               GetAsync(Guid torrentId, CancellationToken cancellationToken);
    Task                                 InsertAsync(TorrentSnapshot torrent, CancellationToken cancellationToken);
    Task                                 UpdateAsync(TorrentSnapshot torrent, CancellationToken cancellationToken);
    Task                                 DeleteAsync(Guid torrentId, CancellationToken cancellationToken);
}
