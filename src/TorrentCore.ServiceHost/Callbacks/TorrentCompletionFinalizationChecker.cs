#region

using TorrentCore.Core.Torrents;
using TorrentCore.Service.Configuration;

#endregion

namespace TorrentCore.Service.Callbacks;

public sealed class TorrentCompletionFinalizationChecker(ResolvedTorrentCoreServicePaths servicePaths)
        : ITorrentCompletionFinalizationChecker
{
    public TorrentCompletionFinalizationCheckResult Check(TorrentSnapshot snapshot,
        RuntimeSettingsSnapshot runtimeSettings, IReadOnlyList<TorrentCompletionObservedFilePaths>? observedFiles = null)
    {
        _ = runtimeSettings;
        var downloadRootPath = snapshot.DownloadRootPath ?? servicePaths.DownloadRootPath;
        var defaultFinalPayloadPath = Path.Combine(downloadRootPath, snapshot.Name);

        if (observedFiles is { Count: > 0 })
        {
            return CheckObservedFiles(defaultFinalPayloadPath, observedFiles);
        }

        if (File.Exists(defaultFinalPayloadPath))
        {
            return Ready(defaultFinalPayloadPath);
        }

        if (Directory.Exists(defaultFinalPayloadPath))
        {
            return Ready(defaultFinalPayloadPath);
        }

        return NotReady(defaultFinalPayloadPath, "The final payload path is not visible yet.");
    }

    private static TorrentCompletionFinalizationCheckResult CheckObservedFiles(string defaultFinalPayloadPath,
        IReadOnlyList<TorrentCompletionObservedFilePaths> observedFiles)
    {
        var finalPayloadPath = observedFiles.Count == 1 &&
                !string.IsNullOrWhiteSpace(observedFiles[0].CompletePath) ? observedFiles[0].CompletePath :
                defaultFinalPayloadPath;

        foreach (var observedFile in observedFiles)
        {
            if (!File.Exists(observedFile.CompletePath))
            {
                return NotReady(
                    finalPayloadPath, $"A final payload file is not visible yet: '{observedFile.CompletePath}'."
                );
            }

        }

        return Ready(finalPayloadPath);
    }

    private static TorrentCompletionFinalizationCheckResult Ready(string finalPayloadPath)
    {
        return new TorrentCompletionFinalizationCheckResult
        {
            IsReady          = true,
            FinalPayloadPath = finalPayloadPath,
            PendingReason    = null,
        };
    }

    private static TorrentCompletionFinalizationCheckResult NotReady(string finalPayloadPath, string reason)
    {
        return new TorrentCompletionFinalizationCheckResult
        {
            IsReady          = false,
            FinalPayloadPath = finalPayloadPath,
            PendingReason    = reason,
        };
    }
}
