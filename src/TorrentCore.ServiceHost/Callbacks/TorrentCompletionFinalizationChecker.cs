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
        var downloadRootPath = snapshot.DownloadRootPath ?? servicePaths.DownloadRootPath;
        var defaultFinalPayloadPath = Path.Combine(downloadRootPath, snapshot.Name);
        var partialSuffix    = runtimeSettings.PartialFilesEnabled ? runtimeSettings.PartialFileSuffix : string.Empty;

        if (observedFiles is { Count: > 0 })
        {
            return CheckObservedFiles(defaultFinalPayloadPath, partialSuffix, observedFiles);
        }

        if (File.Exists(defaultFinalPayloadPath))
        {
            if (!string.IsNullOrWhiteSpace(partialSuffix) && File.Exists(defaultFinalPayloadPath + partialSuffix))
            {
                return NotReady(defaultFinalPayloadPath, "The partial-suffix sibling is still visible.");
            }

            return Ready(defaultFinalPayloadPath);
        }

        if (Directory.Exists(defaultFinalPayloadPath))
        {
            if (string.IsNullOrWhiteSpace(partialSuffix))
            {
                return Ready(defaultFinalPayloadPath);
            }

            try
            {
                foreach (var filePath in Directory.EnumerateFiles(defaultFinalPayloadPath, "*", SearchOption.AllDirectories))
                {
                    if (filePath.EndsWith(partialSuffix, StringComparison.Ordinal))
                    {
                        return NotReady(
                            defaultFinalPayloadPath, $"A partial file is still visible in the payload tree: '{filePath}'."
                        );
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return NotReady(defaultFinalPayloadPath, $"Finalization scan failed: {exception.Message}");
            }

            return Ready(defaultFinalPayloadPath);
        }

        if (!string.IsNullOrWhiteSpace(partialSuffix) && File.Exists(defaultFinalPayloadPath + partialSuffix))
        {
            return NotReady(defaultFinalPayloadPath, "Only the partial-suffix payload is visible.");
        }

        return NotReady(defaultFinalPayloadPath, "The final payload path is not visible yet.");
    }

    private static TorrentCompletionFinalizationCheckResult CheckObservedFiles(string defaultFinalPayloadPath,
        string                                                                     partialSuffix,
        IReadOnlyList<TorrentCompletionObservedFilePaths>                          observedFiles)
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

            if (string.IsNullOrWhiteSpace(partialSuffix) || string.IsNullOrWhiteSpace(observedFile.IncompletePath) ||
                !File.Exists(observedFile.IncompletePath))
            {
                continue;
            }

            if (PathEquals(observedFile.ActivePath, observedFile.CompletePath))
            {
                continue;
            }

            return observedFiles.Count == 1
                    ? NotReady(finalPayloadPath, "The partial-suffix sibling is still visible.")
                    : NotReady(
                        finalPayloadPath,
                        $"A partial file is still visible in the payload tree: '{observedFile.IncompletePath}'."
                    );
        }

        return Ready(finalPayloadPath);
    }

    private static bool PathEquals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
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
