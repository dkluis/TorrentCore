#region

using System.Text.Json;
using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Core.Torrents;
using TorrentCore.Service.Configuration;

#endregion

namespace TorrentCore.Service.Callbacks;

public sealed class TorrentCompletionCallbackProcessor(ITorrentCompletionFinalizationChecker finalizationChecker,
    ITorrentCompletionCallbackInvoker completionCallbackInvoker, IActivityLogService activityLogService,
    ServiceInstanceContext serviceInstanceContext) : ITorrentCompletionCallbackProcessor
{
    public async Task<bool> MarkPendingIfTriggeredAsync(DateTimeOffset? previousCompletedAtUtc,
        TorrentSnapshot snapshot, RuntimeSettingsSnapshot runtimeSettings, DateTimeOffset now,
        CancellationToken cancellationToken, TorrentCompletionFinalizationCheckResult? finalizationResult = null)
    {
        if (previousCompletedAtUtc is not null || snapshot.CompletedAtUtc is null ||
            snapshot.State is not TorrentState.Completed and not TorrentState.Seeding ||
            !snapshot.InvokeCompletionCallback || string.IsNullOrWhiteSpace(snapshot.CompletionCallbackLabel) ||
            snapshot.CompletionCallbackState is not null)
        {
            return false;
        }

        snapshot.CompletionCallbackState           = TorrentCompletionCallbackState.PendingFinalization;
        snapshot.CompletionCallbackPendingSinceUtc = now;
        snapshot.CompletionCallbackInvokedAtUtc    = null;
        snapshot.CompletionCallbackLastError       = null;
        var resolvedFinalizationResult = finalizationResult ?? finalizationChecker.Check(snapshot, runtimeSettings);
        await WritePendingFinalizationLogAsync(
            snapshot, runtimeSettings, resolvedFinalizationResult, cancellationToken
        );
        return true;
    }

    public async Task<bool> ProcessPendingAsync(TorrentSnapshot snapshot, RuntimeSettingsSnapshot runtimeSettings,
        DateTimeOffset now, CancellationToken cancellationToken,
        TorrentCompletionFinalizationCheckResult? finalizationResult = null)
    {
        if (snapshot.CompletionCallbackState != TorrentCompletionCallbackState.PendingFinalization)
        {
            return false;
        }

        var pendingSinceUtc = snapshot.CompletionCallbackPendingSinceUtc ?? snapshot.CompletedAtUtc ?? now;
        var changed         = false;
        if (snapshot.CompletionCallbackPendingSinceUtc is null)
        {
            snapshot.CompletionCallbackPendingSinceUtc = pendingSinceUtc;
            changed                                    = true;
        }

        var resolvedFinalizationResult = finalizationResult ?? finalizationChecker.Check(snapshot, runtimeSettings);
        if (!resolvedFinalizationResult.IsReady)
        {
            if (now - pendingSinceUtc <
                TimeSpan.FromSeconds(runtimeSettings.CompletionCallbackFinalizationTimeoutSeconds))
            {
                return changed;
            }

            snapshot.CompletionCallbackState = TorrentCompletionCallbackState.TimedOut;
            snapshot.CompletionCallbackLastError =
                    $"Timed out waiting for final payload visibility at '{resolvedFinalizationResult.FinalPayloadPath}'. {resolvedFinalizationResult.PendingReason}";
            await WriteFinalizationTimeoutLogAsync(
                snapshot, runtimeSettings, resolvedFinalizationResult, cancellationToken
            );
            return true;
        }

        var invocationResult = await completionCallbackInvoker.InvokeAsync(
            snapshot,
            resolvedFinalizationResult.FinalPayloadPath,
            cancellationToken
        );
        switch (invocationResult.Status)
        {
            case TorrentCompletionCallbackInvocationStatus.Skipped:
                return changed;
            case TorrentCompletionCallbackInvocationStatus.Invoked:
                snapshot.CompletionCallbackState        = TorrentCompletionCallbackState.Invoked;
                snapshot.CompletionCallbackInvokedAtUtc = now;
                snapshot.CompletionCallbackLastError    = null;
                return true;
            case TorrentCompletionCallbackInvocationStatus.Failed:
                snapshot.CompletionCallbackState     = TorrentCompletionCallbackState.Failed;
                snapshot.CompletionCallbackLastError = BuildPostAttemptError(
                    invocationResult.Error,
                    resolvedFinalizationResult.FinalPayloadPath
                );
                return true;
            case TorrentCompletionCallbackInvocationStatus.TimedOut:
                snapshot.CompletionCallbackState     = TorrentCompletionCallbackState.TimedOut;
                snapshot.CompletionCallbackLastError = BuildPostAttemptError(
                    invocationResult.Error,
                    resolvedFinalizationResult.FinalPayloadPath
                );
                return true;
            default:
                return changed;
        }
    }

    private static string? BuildPostAttemptError(string? error, string finalPayloadPath)
    {
        if (string.IsNullOrWhiteSpace(finalPayloadPath) ||
            File.Exists(finalPayloadPath) ||
            Directory.Exists(finalPayloadPath))
        {
            return error;
        }

        return TorrentCompletionCallbackDiagnostics.AppendMissingAfterAttemptHint(error, finalPayloadPath);
    }

    private async Task WriteFinalizationTimeoutLogAsync(TorrentSnapshot snapshot,
        RuntimeSettingsSnapshot runtimeSettings, TorrentCompletionFinalizationCheckResult finalizationResult,
        CancellationToken cancellationToken)
    {
        await activityLogService.WriteAsync(
            new ActivityLogWriteRequest
            {
                Level             = ActivityLogLevel.Warning,
                Category          = "torrent",
                EventType         = "torrent.callback.finalization_timed_out",
                Message           = $"Completion callback finalization timed out for torrent '{snapshot.Name}'.",
                TorrentId         = snapshot.TorrentId,
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(
                    new
                    {
                        snapshot.Name,
                        snapshot.CategoryKey,
                        snapshot.InfoHash,
                        snapshot.DownloadRootPath,
                        snapshot.CompletionCallbackLabel,
                        snapshot.CompletionCallbackPendingSinceUtc,
                        runtimeSettings.PartialFilesEnabled,
                        runtimeSettings.PartialFileSuffix,
                        runtimeSettings.CompletionCallbackFinalizationTimeoutSeconds,
                        finalizationResult.FinalPayloadPath,
                        finalizationResult.PendingReason,
                    }
                ),
            }, cancellationToken
        );
    }

    private async Task WritePendingFinalizationLogAsync(TorrentSnapshot snapshot,
        RuntimeSettingsSnapshot runtimeSettings, TorrentCompletionFinalizationCheckResult finalizationResult,
        CancellationToken cancellationToken)
    {
        await activityLogService.WriteAsync(
            new ActivityLogWriteRequest
            {
                Level     = ActivityLogLevel.Information,
                Category  = "torrent",
                EventType = "torrent.callback.pending_finalization",
                Message =
                        $"Waiting for final payload visibility before invoking completion callback for torrent '{snapshot.Name}'.",
                TorrentId         = snapshot.TorrentId,
                ServiceInstanceId = serviceInstanceContext.ServiceInstanceId,
                DetailsJson = JsonSerializer.Serialize(
                    new
                    {
                        snapshot.Name,
                        snapshot.CategoryKey,
                        snapshot.InfoHash,
                        snapshot.DownloadRootPath,
                        snapshot.CompletionCallbackLabel,
                        snapshot.CompletionCallbackPendingSinceUtc,
                        runtimeSettings.PartialFilesEnabled,
                        runtimeSettings.PartialFileSuffix,
                        runtimeSettings.CompletionCallbackFinalizationTimeoutSeconds,
                        finalizationResult.FinalPayloadPath,
                        finalizationResult.PendingReason,
                    }
                ),
            }, cancellationToken
        );
    }
}
