using TorrentCore.Contracts.Maintenance;

namespace TorrentCore.Service.Application;

public interface IMaintenanceCleanupService
{
    Task<CleanupByDateResultDto> DeleteLogsAsync(CleanupByDateRequest request,
        CancellationToken cancellationToken);

    Task<CleanupByDateResultDto> DeleteHistoryAsync(CleanupByDateRequest request,
        CancellationToken cancellationToken);
}
