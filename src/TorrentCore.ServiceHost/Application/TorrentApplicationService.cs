using System.Reflection;
using TorrentCore.Contracts.Host;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Engine;

namespace TorrentCore.Service.Application;

public sealed class TorrentApplicationService(
    IHostEnvironment hostEnvironment,
    ResolvedTorrentCoreServicePaths servicePaths,
    ITorrentEngineAdapter torrentEngineAdapter) : ITorrentApplicationService
{
    public async Task<EngineHostStatusDto> GetHostStatusAsync(CancellationToken cancellationToken)
    {
        return new EngineHostStatusDto
        {
            ServiceName               = "TorrentCore.Service",
            ServiceVersion            = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
            Status                    = EngineHostStatus.Ready,
            EnvironmentName           = hostEnvironment.EnvironmentName,
            DownloadRootPath          = servicePaths.DownloadRootPath,
            TorrentCount              = await torrentEngineAdapter.GetTorrentCountAsync(cancellationToken),
            SupportsMagnetAdds        = true,
            SupportsPause             = true,
            SupportsResume            = true,
            SupportsRemove            = true,
            SupportsPersistentStorage = false,
            SupportsMultiHost         = false,
            CheckedAtUtc              = DateTimeOffset.UtcNow,
        };
    }

    public Task<IReadOnlyList<TorrentSummaryDto>> GetTorrentsAsync(CancellationToken cancellationToken) =>
        torrentEngineAdapter.GetTorrentsAsync(cancellationToken);

    public Task<TorrentDetailDto> GetTorrentAsync(Guid torrentId, CancellationToken cancellationToken) =>
        torrentEngineAdapter.GetTorrentAsync(torrentId, cancellationToken);

    public Task<TorrentDetailDto> AddMagnetAsync(AddMagnetRequest request, CancellationToken cancellationToken) =>
        torrentEngineAdapter.AddMagnetAsync(request, servicePaths.DownloadRootPath, cancellationToken);

    public Task<TorrentActionResultDto> PauseAsync(Guid torrentId, CancellationToken cancellationToken) =>
        torrentEngineAdapter.PauseAsync(torrentId, cancellationToken);

    public Task<TorrentActionResultDto> ResumeAsync(Guid torrentId, CancellationToken cancellationToken) =>
        torrentEngineAdapter.ResumeAsync(torrentId, cancellationToken);

    public Task<TorrentActionResultDto> RemoveAsync(Guid torrentId, RemoveTorrentRequest request, CancellationToken cancellationToken) =>
        torrentEngineAdapter.RemoveAsync(torrentId, request, cancellationToken);
}
