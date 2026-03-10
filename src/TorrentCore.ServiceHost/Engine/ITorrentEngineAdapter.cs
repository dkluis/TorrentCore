using TorrentCore.Contracts.Torrents;

namespace TorrentCore.Service.Engine;

public interface ITorrentEngineAdapter
{
    Task<int> GetTorrentCountAsync(CancellationToken cancellationToken);
    Task<TorrentEngineRecoveryResult> RecoverAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<TorrentSummaryDto>> GetTorrentsAsync(CancellationToken cancellationToken);
    Task<TorrentDetailDto> GetTorrentAsync(Guid torrentId, CancellationToken cancellationToken);
    Task<TorrentDetailDto> AddMagnetAsync(AddMagnetRequest request, string downloadRootPath, CancellationToken cancellationToken);
    Task<TorrentActionResultDto> PauseAsync(Guid torrentId, CancellationToken cancellationToken);
    Task<TorrentActionResultDto> ResumeAsync(Guid torrentId, CancellationToken cancellationToken);
    Task<TorrentActionResultDto> RemoveAsync(Guid torrentId, RemoveTorrentRequest request, CancellationToken cancellationToken);
}
