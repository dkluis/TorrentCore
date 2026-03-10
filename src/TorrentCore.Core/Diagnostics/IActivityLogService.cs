namespace TorrentCore.Core.Diagnostics;

public interface IActivityLogService
{
    Task EnsureInitializedAsync(CancellationToken cancellationToken);
    Task WriteAsync(ActivityLogWriteRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<ActivityLogEntry>> GetRecentAsync(ActivityLogQuery query, CancellationToken cancellationToken);
}
