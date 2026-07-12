using System.Text.Json;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Service.Configuration;

namespace TorrentCore.Service.Infrastructure;

public sealed class RuntimeOperationDurationDiagnostics(
    IActivityLogService activityLogService,
    ServiceInstanceContext serviceInstanceContext)
{
    public static readonly TimeSpan CallbackSlowThreshold = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan GateWaitSlowThreshold = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan MonoTorrentSlowThreshold = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan StorageSlowThreshold = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan SynchronizationSlowThreshold = TimeSpan.FromSeconds(5);

    public async Task RecordIfSlowAsync(
        string subsystem,
        string operation,
        TimeSpan duration,
        TimeSpan threshold,
        string outcome,
        Guid? torrentId = null,
        object? details = null)
    {
        if (duration < threshold)
        {
            return;
        }

        await activityLogService.TryWriteActivityLogAsync(
            new ActivityLogWriteRequest
            {
                Level = ActivityLogLevel.Warning,
                Category = "runtime",
                EventType = "runtime.operation.slow",
                Message = $"Runtime operation '{operation}' in subsystem '{subsystem}' took {duration.TotalMilliseconds:F0} ms.",
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(
                    new
                    {
                        Subsystem = subsystem,
                        Operation = operation,
                        DurationMilliseconds = duration.TotalMilliseconds,
                        ThresholdMilliseconds = threshold.TotalMilliseconds,
                        Outcome = outcome,
                        TorrentId = torrentId,
                        Details = details,
                    }
                ),
            },
            CancellationToken.None
        );
    }

    public Task WriteRecoveryActionCompletedAsync(
        string recoveryKind,
        string action,
        int attemptNumber,
        TimeSpan duration,
        string outcome,
        Guid torrentId,
        string torrentName,
        object? details = null)
    {
        return activityLogService.TryWriteActivityLogAsync(
            new ActivityLogWriteRequest
            {
                Level = outcome == "succeeded" ? ActivityLogLevel.Information : ActivityLogLevel.Warning,
                Category = "runtime",
                EventType = "runtime.recovery.action_completed",
                Message = $"Recovery action '{action}' for torrent '{torrentName}' {outcome} in {duration.TotalMilliseconds:F0} ms.",
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(
                    new
                    {
                        RecoveryKind = recoveryKind,
                        Action = action,
                        AttemptNumber = attemptNumber,
                        DurationMilliseconds = duration.TotalMilliseconds,
                        Outcome = outcome,
                        TorrentId = torrentId,
                        TorrentName = torrentName,
                        Details = details,
                    }
                ),
            },
            CancellationToken.None
        );
    }

    public Task WriteCallbackExecutionCompletedAsync(
        TimeSpan duration,
        string outcome,
        Guid torrentId,
        string torrentName,
        string? categoryKey)
    {
        return activityLogService.TryWriteActivityLogAsync(
            new ActivityLogWriteRequest
            {
                Level = outcome == "succeeded" ? ActivityLogLevel.Information : ActivityLogLevel.Warning,
                Category = "runtime",
                EventType = "runtime.callback.execution_completed",
                Message = $"Completion callback execution for torrent '{torrentName}' {outcome} in {duration.TotalMilliseconds:F0} ms.",
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(
                    new
                    {
                        DurationMilliseconds = duration.TotalMilliseconds,
                        Outcome = outcome,
                        TorrentId = torrentId,
                        TorrentName = torrentName,
                        CategoryKey = categoryKey,
                    }
                ),
            },
            CancellationToken.None
        );
    }

    public Task WriteSynchronizationSummaryAsync(
        int sampleCount,
        TimeSpan averageDuration,
        TimeSpan maximumDuration,
        TimeSpan sampleWindow)
    {
        return activityLogService.TryWriteActivityLogAsync(
            new ActivityLogWriteRequest
            {
                Level = ActivityLogLevel.Information,
                Category = "runtime",
                EventType = "runtime.tick.duration_summary",
                Message = $"Torrent synchronization timing summary for {sampleCount} tick(s).",
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(
                    new
                    {
                        SampleCount = sampleCount,
                        AverageDurationMilliseconds = averageDuration.TotalMilliseconds,
                        MaximumDurationMilliseconds = maximumDuration.TotalMilliseconds,
                        SampleWindowSeconds = sampleWindow.TotalSeconds,
                    }
                ),
            },
            CancellationToken.None
        );
    }
}
