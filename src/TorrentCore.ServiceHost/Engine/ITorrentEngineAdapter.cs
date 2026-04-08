#region

using TorrentCore.Contracts.Torrents;
using TorrentCore.Service.Configuration;

#endregion

namespace TorrentCore.Service.Engine;

public interface ITorrentEngineAdapter
{
    Task<int>                              GetTorrentCountAsync(CancellationToken cancellationToken);
    Task<TorrentEngineRecoveryResult>      RecoverAsync(CancellationToken cancellationToken);
    Task                                   SynchronizeAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<TorrentSummaryDto>> GetTorrentsAsync(CancellationToken cancellationToken);
    Task<TorrentDetailDto>                 GetTorrentAsync(Guid torrentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TorrentPeerDto>>    GetTorrentPeersAsync(Guid torrentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TorrentTrackerDto>> GetTorrentTrackersAsync(Guid torrentId, CancellationToken cancellationToken);

    Task<TorrentDetailDto> AddMagnetAsync(AddMagnetRequest request, ResolvedTorrentCategorySelection categorySelection,
        CancellationToken                                  cancellationToken);

    Task<TorrentActionResultDto> PauseAsync(Guid                   torrentId, CancellationToken cancellationToken);
    Task<TorrentActionResultDto> ResumeAsync(Guid                  torrentId, CancellationToken cancellationToken);
    Task<TorrentActionResultDto> RefreshMetadataAsync(Guid         torrentId, CancellationToken cancellationToken);
    Task<TorrentActionResultDto> ResetMetadataSessionAsync(Guid    torrentId, CancellationToken cancellationToken);
    Task<TorrentActionResultDto> RetryCompletionCallbackAsync(Guid torrentId, CancellationToken cancellationToken);

    Task<TorrentActionResultDto> RemoveAsync(Guid torrentId, RemoveTorrentRequest request,
        CancellationToken                         cancellationToken);
}
