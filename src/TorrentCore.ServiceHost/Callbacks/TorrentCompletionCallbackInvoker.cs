#region

using System.Diagnostics;
using System.Text.Json;
using TorrentCore.Core.Diagnostics;
using TorrentCore.Core.Torrents;
using TorrentCore.Service.Configuration;

#endregion

namespace TorrentCore.Service.Callbacks;

public sealed class TorrentCompletionCallbackInvoker(IRuntimeSettingsService runtimeSettingsService,
    ResolvedTorrentCoreServicePaths servicePaths, IActivityLogService activityLogService,
    ServiceInstanceContext serviceInstanceContext, ILogger<TorrentCompletionCallbackInvoker> logger)
        : ITorrentCompletionCallbackInvoker
{
    private const string TvmazeApiCompleteApiKeyEnvironmentVariable      = "TVMAZE_API_COMPLETE_API_KEY";
    private const string TvmazeApiCompleteUrlEnvironmentVariable         = "TVMAZE_API_COMPLETE_URL";
    private const string TorrentCoreFinalPayloadPathEnvironmentVariable  = "TORRENTCORE_FINAL_PAYLOAD_PATH";
    private const string TransmissionTorrentDirectoryEnvironmentVariable = "TR_TORRENT_DIR";
    private const string TransmissionTorrentHashEnvironmentVariable      = "TR_TORRENT_HASH";
    private const string TransmissionTorrentIdEnvironmentVariable        = "TR_TORRENT_ID";
    private const string TransmissionTorrentLabelsEnvironmentVariable    = "TR_TORRENT_LABELS";
    private const string TransmissionTorrentNameEnvironmentVariable      = "TR_TORRENT_NAME";

    public async Task<TorrentCompletionCallbackInvocationResult> InvokeAsync(
        TorrentSnapshot currentSnapshot,
        string? finalPayloadPath,
        CancellationToken cancellationToken
    )
    {
        var runtimeSettings = await runtimeSettingsService.GetEffectiveSettingsAsync(cancellationToken);
        if (!runtimeSettings.CompletionCallbackEnabled ||
            string.IsNullOrWhiteSpace(runtimeSettings.CompletionCallbackCommandPath))
        {
            return new TorrentCompletionCallbackInvocationResult
            {
                Status = TorrentCompletionCallbackInvocationStatus.Skipped,
            };
        }

        var commandPath      = runtimeSettings.CompletionCallbackCommandPath.Trim();
        var workingDirectory = ResolveWorkingDirectory(runtimeSettings, commandPath);
        using var process = new Process
        {
            StartInfo = BuildProcessStartInfo(
                runtimeSettings, currentSnapshot, finalPayloadPath, commandPath, workingDirectory
            ),
        };

        int processId;
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("The completion callback process did not start.");
            }

            processId = process.Id;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception, "Failed launching completion callback for torrent {TorrentId}", currentSnapshot.TorrentId
            );
            await WriteCallbackLogAsync(
                ActivityLogLevel.Warning, "torrent.callback.failed",
                $"Completion callback launch failed for torrent '{currentSnapshot.Name}'.", currentSnapshot,
                runtimeSettings, finalPayloadPath, null, null, workingDirectory, exception.Message,
                cancellationToken
            );
            return new TorrentCompletionCallbackInvocationResult
            {
                Status = TorrentCompletionCallbackInvocationStatus.Failed,
                Error  = exception.Message,
            };
        }

        using var timeoutCts =
                new CancellationTokenSource(TimeSpan.FromSeconds(runtimeSettings.CompletionCallbackTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                }
            }
            catch (Exception killException)
            {
                logger.LogDebug(
                    killException, "Failed killing timed-out completion callback process {ProcessId}", processId
                );
            }

            logger.LogWarning(
                "Completion callback timed out for torrent {TorrentId}. ProcessId={ProcessId} TimeoutSeconds={TimeoutSeconds}",
                currentSnapshot.TorrentId, processId, runtimeSettings.CompletionCallbackTimeoutSeconds
            );

            await WriteCallbackLogAsync(
                ActivityLogLevel.Warning, "torrent.callback.timed_out",
                $"Completion callback timed out for torrent '{currentSnapshot.Name}'.", currentSnapshot,
                runtimeSettings, finalPayloadPath, processId, null, workingDirectory,
                $"The callback exceeded the {runtimeSettings.CompletionCallbackTimeoutSeconds}-second timeout.",
                cancellationToken
            );
            return new TorrentCompletionCallbackInvocationResult
            {
                Status = TorrentCompletionCallbackInvocationStatus.TimedOut,
                Error = $"The callback exceeded the {runtimeSettings.CompletionCallbackTimeoutSeconds}-second timeout.",
            };
        }

        if (process.ExitCode != 0)
        {
            logger.LogWarning(
                "Completion callback failed for torrent {TorrentId}. ProcessId={ProcessId} ExitCode={ExitCode}",
                currentSnapshot.TorrentId, processId, process.ExitCode
            );

            await WriteCallbackLogAsync(
                ActivityLogLevel.Warning, "torrent.callback.failed",
                $"Completion callback failed for torrent '{currentSnapshot.Name}'.", currentSnapshot, runtimeSettings,
                finalPayloadPath, processId, process.ExitCode, workingDirectory,
                $"The callback exited with code {process.ExitCode}.",
                cancellationToken
            );
            return new TorrentCompletionCallbackInvocationResult
            {
                Status = TorrentCompletionCallbackInvocationStatus.Failed,
                Error  = $"The callback exited with code {process.ExitCode}.",
            };
        }

        await WriteCallbackLogAsync(
            ActivityLogLevel.Information, "torrent.callback.invoked",
            $"Invoked completion callback for torrent '{currentSnapshot.Name}'.", currentSnapshot, runtimeSettings,
            finalPayloadPath, processId, process.ExitCode, workingDirectory, null, cancellationToken
        );

        return new TorrentCompletionCallbackInvocationResult
        {
            Status = TorrentCompletionCallbackInvocationStatus.Invoked,
        };
    }

    private ProcessStartInfo BuildProcessStartInfo(RuntimeSettingsSnapshot runtimeSettings, TorrentSnapshot snapshot,
        string? finalPayloadPath, string commandPath, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName         = commandPath,
            Arguments        = runtimeSettings.CompletionCallbackArguments ?? string.Empty,
            WorkingDirectory = workingDirectory,
            UseShellExecute  = false,
        };

        startInfo.Environment[TransmissionTorrentIdEnvironmentVariable]   = "0";
        startInfo.Environment[TransmissionTorrentHashEnvironmentVariable] = snapshot.InfoHash ?? string.Empty;
        startInfo.Environment[TransmissionTorrentNameEnvironmentVariable] = snapshot.Name;
        startInfo.Environment[TransmissionTorrentDirectoryEnvironmentVariable] =
                snapshot.DownloadRootPath ?? servicePaths.DownloadRootPath;
        startInfo.Environment[TransmissionTorrentLabelsEnvironmentVariable] =
                snapshot.CompletionCallbackLabel ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(finalPayloadPath))
        {
            startInfo.Environment[TorrentCoreFinalPayloadPathEnvironmentVariable] = finalPayloadPath;
        }

        if (!string.IsNullOrWhiteSpace(runtimeSettings.CompletionCallbackApiBaseUrlOverride))
        {
            startInfo.Environment[TvmazeApiCompleteUrlEnvironmentVariable] =
                    runtimeSettings.CompletionCallbackApiBaseUrlOverride.Trim();
        }

        if (!string.IsNullOrWhiteSpace(runtimeSettings.CompletionCallbackApiKeyOverride))
        {
            startInfo.Environment[TvmazeApiCompleteApiKeyEnvironmentVariable] =
                    runtimeSettings.CompletionCallbackApiKeyOverride.Trim();
        }

        return startInfo;
    }

    private string ResolveWorkingDirectory(RuntimeSettingsSnapshot runtimeSettings, string commandPath)
    {
        if (!string.IsNullOrWhiteSpace(runtimeSettings.CompletionCallbackWorkingDirectory))
        {
            return runtimeSettings.CompletionCallbackWorkingDirectory.Trim();
        }

        var commandDirectory = Path.GetDirectoryName(commandPath);
        return string.IsNullOrWhiteSpace(commandDirectory) ? servicePaths.StorageRootPath : commandDirectory;
    }

    private async Task WriteCallbackLogAsync(ActivityLogLevel level, string eventType, string message,
        TorrentSnapshot snapshot, RuntimeSettingsSnapshot runtimeSettings, string? finalPayloadPath, int? processId, int? exitCode,
        string workingDirectory, string? error, CancellationToken cancellationToken)
    {
        await activityLogService.WriteAsync(
            new ActivityLogWriteRequest
            {
                Level             = level,
                Category          = "torrent",
                EventType         = eventType,
                Message           = message,
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
                        FinalPayloadPath = finalPayloadPath,
                        CommandPath = runtimeSettings.CompletionCallbackCommandPath,
                        runtimeSettings.CompletionCallbackArguments,
                        WorkingDirectory = workingDirectory,
                        runtimeSettings.CompletionCallbackTimeoutSeconds,
                        ProcessId = processId,
                        ExitCode  = exitCode,
                        Error     = error,
                    }
                ),
            }, cancellationToken
        );
    }
}
