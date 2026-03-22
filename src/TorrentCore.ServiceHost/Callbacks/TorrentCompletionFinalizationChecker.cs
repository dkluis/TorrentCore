#region

using TorrentCore.Core.Torrents;
using TorrentCore.Service.Configuration;

#endregion

namespace TorrentCore.Service.Callbacks;

public sealed class TorrentCompletionFinalizationChecker(ResolvedTorrentCoreServicePaths servicePaths)
        : ITorrentCompletionFinalizationChecker
{
    public TorrentCompletionFinalizationCheckResult Check(TorrentSnapshot snapshot,
        RuntimeSettingsSnapshot                                           runtimeSettings)
    {
        var downloadRootPath = snapshot.DownloadRootPath ?? servicePaths.DownloadRootPath;
        var finalPayloadPath = Path.Combine(downloadRootPath, snapshot.Name);
        var partialSuffix    = runtimeSettings.PartialFilesEnabled ? runtimeSettings.PartialFileSuffix : string.Empty;

        if (File.Exists(finalPayloadPath))
        {
            if (!string.IsNullOrWhiteSpace(partialSuffix) && File.Exists(finalPayloadPath + partialSuffix))
            {
                return NotReady(finalPayloadPath, "The partial-suffix sibling is still visible.");
            }

            return Ready(finalPayloadPath);
        }

        if (Directory.Exists(finalPayloadPath))
        {
            if (string.IsNullOrWhiteSpace(partialSuffix))
            {
                return Ready(finalPayloadPath);
            }

            try
            {
                foreach (var filePath in Directory.EnumerateFiles(finalPayloadPath, "*", SearchOption.AllDirectories))
                {
                    if (filePath.EndsWith(partialSuffix, StringComparison.Ordinal))
                    {
                        return NotReady(
                            finalPayloadPath, $"A partial file is still visible in the payload tree: '{filePath}'."
                        );
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return NotReady(finalPayloadPath, $"Finalization scan failed: {exception.Message}");
            }

            return Ready(finalPayloadPath);
        }

        if (!string.IsNullOrWhiteSpace(partialSuffix) && File.Exists(finalPayloadPath + partialSuffix))
        {
            return NotReady(finalPayloadPath, "Only the partial-suffix payload is visible.");
        }

        return NotReady(finalPayloadPath, "The final payload path is not visible yet.");
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
