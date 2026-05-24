namespace TorrentCore.Core.History;

public interface ITorrentHistoryStore
{
    Task                  EnsureInitializedAsync(CancellationToken cancellationToken);
    Task<TorrentHistoryRecord?> GetAsync(Guid torrentId, CancellationToken cancellationToken);
    Task                  InsertAsync(TorrentHistoryRecord record, CancellationToken cancellationToken);
}
