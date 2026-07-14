using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Core.Torrents;
using TorrentCore.Service.Configuration;
using TorrentCore.Service.Infrastructure;

namespace TorrentCore.Service.Callbacks;

public sealed class TorrentCompletionFinalizationProbeCoordinator(
    ITorrentCompletionFinalizationChecker finalizationChecker,
    IActivityLogService activityLogService,
    ServiceInstanceContext serviceInstanceContext,
    RuntimeOperationDurationDiagnostics durationDiagnostics)
{
    private readonly ConcurrentDictionary<Guid, Lazy<Task<TorrentCompletionFinalizationCheckResult>>> _probes = new();

    public bool TryTakeCompletedOrSchedule(
        TorrentSnapshot snapshot,
        RuntimeSettingsSnapshot runtimeSettings,
        IReadOnlyList<TorrentCompletionObservedFilePaths>? observedFiles,
        out TorrentCompletionFinalizationCheckResult? result)
    {
        var observedFilesSnapshot = observedFiles?.ToArray();
        var probe = _probes.GetOrAdd(
            snapshot.TorrentId,
            _ => new Lazy<Task<TorrentCompletionFinalizationCheckResult>>(
                () => Task.Run(
                    async () => await RunProbeAsync(snapshot, runtimeSettings, observedFilesSnapshot),
                    CancellationToken.None
                ),
                LazyThreadSafetyMode.ExecutionAndPublication
            )
        );
        var task = probe.Value;
        if (!task.IsCompleted)
        {
            result = null;
            return false;
        }

        _probes.TryRemove(snapshot.TorrentId, out _);
        result = task.GetAwaiter().GetResult();
        return true;
    }

    public void Remove(Guid torrentId)
    {
        _probes.TryRemove(torrentId, out _);
    }

    public static TorrentCompletionFinalizationCheckResult CreateDeferredResult(
        TorrentSnapshot snapshot,
        IReadOnlyList<TorrentCompletionObservedFilePaths>? observedFiles,
        string pendingReason = "The final payload visibility check is running in the background.",
        string? defaultDownloadRootPath = null)
    {
        var downloadRootPath = snapshot.DownloadRootPath ?? defaultDownloadRootPath ??
                               Path.GetDirectoryName(snapshot.SavePath) ?? snapshot.SavePath;
        var finalPayloadPath = observedFiles is { Count: 1 } &&
                               !string.IsNullOrWhiteSpace(observedFiles[0].CompletePath)
            ? observedFiles[0].CompletePath
            : Path.Combine(downloadRootPath, snapshot.Name);

        return new TorrentCompletionFinalizationCheckResult
        {
            IsReady = false,
            FinalPayloadPath = finalPayloadPath,
            PendingReason = pendingReason,
        };
    }

    private async Task<TorrentCompletionFinalizationCheckResult> RunProbeAsync(
        TorrentSnapshot snapshot,
        RuntimeSettingsSnapshot runtimeSettings,
        IReadOnlyList<TorrentCompletionObservedFilePaths>? observedFiles)
    {
        var stopwatch = Stopwatch.StartNew();
        var outcome = "succeeded";
        try
        {
            return finalizationChecker.Check(snapshot, runtimeSettings, observedFiles);
        }
        catch (Exception exception)
        {
            outcome = "failed";
            await activityLogService.TryWriteActivityLogAsync(
                new ActivityLogWriteRequest
                {
                    Level = ActivityLogLevel.Warning,
                    Category = "runtime",
                    EventType = "runtime.finalization.probe_failed",
                    Message = $"Background finalization visibility probe failed for torrent '{snapshot.Name}'.",
                    ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                    DetailsJson = JsonSerializer.Serialize(
                        new
                        {
                            snapshot.TorrentId,
                            TorrentName = snapshot.Name,
                            Error = exception.Message,
                        }
                    ),
                },
                CancellationToken.None
            );
            return CreateDeferredResult(
                snapshot,
                observedFiles,
                $"The final payload visibility check failed: {exception.Message}"
            );
        }
        finally
        {
            stopwatch.Stop();
            await durationDiagnostics.RecordIfSlowAsync(
                "filesystem",
                "torrent_finalization_visibility_probe",
                stopwatch.Elapsed,
                RuntimeOperationDurationDiagnostics.StorageSlowThreshold,
                outcome,
                snapshot.TorrentId,
                new { TorrentName = snapshot.Name }
            );
        }
    }
}
