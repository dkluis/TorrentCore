using TorrentCore.Core.Diagnostics;
using TorrentCore.Service.Application;

namespace TorrentCore.Service.Infrastructure;

internal static class ActivityLogServiceExtensions
{
    public static async Task TryWriteActivityLogAsync(this IActivityLogService activityLogService,
        ActivityLogWriteRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await activityLogService.WriteAsync(request, cancellationToken);
        }
        catch (Exception exception) when (ServiceBoundaryExceptionMapper.IsStorageException(exception))
        {
            TryWriteActivityLogFailureToStandardError(request, exception);
        }
    }

    private static void TryWriteActivityLogFailureToStandardError(ActivityLogWriteRequest request, Exception exception)
    {
        try
        {
            Console.Error.WriteLine(
                "[{0:O}] TorrentCore activity log write failed. EventType={1} TorrentId={2} ExceptionType={3} Message={4}",
                DateTimeOffset.UtcNow,
                request.EventType,
                request.TorrentId?.ToString("D") ?? "(none)",
                exception.GetType().FullName,
                exception.Message
            );
        }
        catch
        {
            // Last-ditch diagnostics must never interrupt engine or callback work.
        }
    }
}
