using TorrentCore.Contracts.Host;
using TorrentCore.Contracts.Torrents;

namespace TorrentCore.Service.Application;

public interface ITorrentApplicationService
{
    Task<EngineHostStatusDto> GetHostStatusAsync(CancellationToken cancellationToken);
    Task<RuntimeSettingsDto> GetRuntimeSettingsAsync(CancellationToken cancellationToken);
    Task<RuntimeSettingsDto> UpdateRuntimeSettingsAsync(UpdateRuntimeSettingsRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<TorrentSummaryDto>> GetTorrentsAsync(CancellationToken cancellationToken);
    Task<TorrentDetailDto> GetTorrentAsync(Guid torrentId, CancellationToken cancellationToken);
    Task<TorrentDetailDto> AddMagnetAsync(AddMagnetRequest request, CancellationToken cancellationToken);
    Task<TorrentActionResultDto> PauseAsync(Guid torrentId, CancellationToken cancellationToken);
    Task<TorrentActionResultDto> ResumeAsync(Guid torrentId, CancellationToken cancellationToken);
    Task<TorrentActionResultDto> RemoveAsync(Guid torrentId, RemoveTorrentRequest request, CancellationToken cancellationToken);
}
