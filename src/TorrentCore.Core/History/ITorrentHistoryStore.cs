namespace TorrentCore.Core.History;

public interface ITorrentHistoryStore
{
    Task                  EnsureInitializedAsync(CancellationToken cancellationToken);
    Task<TorrentHistoryRecord?> GetAsync(Guid torrentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TorrentHistoryRecord>> ListAsync(CancellationToken cancellationToken);
    Task                  InsertAsync(TorrentHistoryRecord record, CancellationToken cancellationToken);
    Task                  UpdateAsync(TorrentHistoryRecord record, CancellationToken cancellationToken);
}
