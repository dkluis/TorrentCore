#region

using System.Text.Json;
using TorrentCore.Contracts.Maintenance;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Core.History;
using TorrentCore.Service.Configuration;

#endregion

namespace TorrentCore.Service.Application;

public sealed class MaintenanceCleanupService(IActivityLogService activityLogService,
    ITorrentHistoryStore torrentHistoryStore, ServiceInstanceContext serviceInstanceContext)
    : IMaintenanceCleanupService
{
    private static readonly TimeZoneInfo LocalTimeZone = TimeZoneInfo.Local;

    public async Task<CleanupByDateResultDto> DeleteLogsAsync(CleanupByDateRequest request,
        CancellationToken cancellationToken)
    {
        var cutoffUtc = ValidateAndConvertCutoff(request);

        int deletedCount;
        try
        {
            deletedCount = await activityLogService.DeleteInactiveBeforeAsync(cutoffUtc, cancellationToken);
        }
        catch (Exception exception) when (ServiceBoundaryExceptionMapper.IsStorageException(exception))
        {
            throw ServiceBoundaryExceptionMapper.CreateStorageUnavailable(
                "TorrentCore activity log storage is unavailable."
            );
        }

        await TryWriteAuditLogAsync(
            "service.logs.cleanup_completed",
            deletedCount == 0
                ? $"No eligible log entries existed before {request.UpToDate:yyyy-MM-dd}."
                : $"Deleted {deletedCount} eligible log entry or entries before {request.UpToDate:yyyy-MM-dd}.",
            request.UpToDate,
            cutoffUtc,
            deletedCount,
            cancellationToken
        );

        return CreateResult(request.UpToDate, cutoffUtc, deletedCount);
    }

    public async Task<CleanupByDateResultDto> DeleteHistoryAsync(CleanupByDateRequest request,
        CancellationToken cancellationToken)
    {
        var cutoffUtc = ValidateAndConvertCutoff(request);

        int deletedCount;
        try
        {
            deletedCount = await torrentHistoryStore.DeleteInactiveBeforeAsync(cutoffUtc, cancellationToken);
        }
        catch (Exception exception) when (ServiceBoundaryExceptionMapper.IsStorageException(exception))
        {
            throw ServiceBoundaryExceptionMapper.CreateStorageUnavailable(
                "TorrentCore history storage is unavailable."
            );
        }

        await TryWriteAuditLogAsync(
            "service.history.cleanup_completed",
            deletedCount == 0
                ? $"No eligible history records existed before {request.UpToDate:yyyy-MM-dd}."
                : $"Deleted {deletedCount} eligible history record or records before {request.UpToDate:yyyy-MM-dd}.",
            request.UpToDate,
            cutoffUtc,
            deletedCount,
            cancellationToken
        );

        return CreateResult(request.UpToDate, cutoffUtc, deletedCount);
    }

    private static DateTimeOffset ValidateAndConvertCutoff(CleanupByDateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.UpToDate == DateOnly.MinValue)
        {
            throw new ServiceOperationException(
                "invalid_cleanup_date",
                "Up To Date is required.",
                StatusCodes.Status400BadRequest,
                nameof(request.UpToDate)
            );
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        if (request.UpToDate > today)
        {
            throw new ServiceOperationException(
                "invalid_cleanup_date",
                "Up To Date cannot be in the future.",
                StatusCodes.Status400BadRequest,
                nameof(request.UpToDate)
            );
        }

        var localMidnight = DateTime.SpecifyKind(
            request.UpToDate.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified
        );
        return new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(localMidnight, LocalTimeZone),
            TimeSpan.Zero
        );
    }

    private static CleanupByDateResultDto CreateResult(DateOnly upToDate, DateTimeOffset cutoffUtc,
        int deletedCount)
    {
        return new CleanupByDateResultDto
        {
            UpToDate = upToDate,
            CutoffUtc = cutoffUtc,
            DeletedRecordCount = deletedCount,
        };
    }

    private async Task TryWriteAuditLogAsync(string eventType, string message, DateOnly upToDate,
        DateTimeOffset cutoffUtc, int deletedCount, CancellationToken cancellationToken)
    {
        try
        {
            await activityLogService.WriteAsync(
                new ActivityLogWriteRequest
                {
                    Level = ActivityLogLevel.Information,
                    Category = "service",
                    EventType = eventType,
                    Message = message,
                    ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                    DetailsJson = JsonSerializer.Serialize(
                        new
                        {
                            UpToDate = upToDate,
                            CutoffUtc = cutoffUtc,
                            DeletedRecordCount = deletedCount,
                            ProtectedLiveTorrents = true,
                        }
                    ),
                },
                cancellationToken
            );
        }
        catch (Exception exception) when (ServiceBoundaryExceptionMapper.IsStorageException(exception))
        {
            // The cleanup has already completed. Do not turn a successful delete into a 500.
        }
    }
}
