using Microsoft.Data.Sqlite;

namespace TorrentCore.Service.Application;

internal static class ServiceBoundaryExceptionMapper
{
    public static bool IsStorageException(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or NotSupportedException or SqliteException;
    }

    public static ServiceOperationException CreateStorageUnavailable(string message, string? target = null)
    {
        return new ServiceOperationException(
            "storage_unavailable",
            message,
            StatusCodes.Status503ServiceUnavailable,
            target
        );
    }

    public static ServiceOperationException CreateRestartUnavailable(string message, string? target = null)
    {
        return new ServiceOperationException(
            "service_restart_unavailable",
            message,
            StatusCodes.Status503ServiceUnavailable,
            target
        );
    }
}
