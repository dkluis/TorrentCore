using TorrentCore.Core.Torrents;

namespace TorrentCore.Service.Callbacks;

public static class TorrentCompletionCallbackDiagnostics
{
    private const string FinalizationTimeoutPrefix = "Timed out waiting for final payload visibility at '";
    private const string MissingAfterAttemptMarker = "The final payload is no longer visible at '";

    public static bool IsFinalizationVisibilityTimeout(string? error)
    {
        return !string.IsNullOrWhiteSpace(error) &&
               error.StartsWith(FinalizationTimeoutPrefix, StringComparison.Ordinal);
    }

    public static bool ShouldSurfaceFinalizationStatus(TorrentCompletionCallbackState? state, string? lastError)
    {
        return state == TorrentCompletionCallbackState.PendingFinalization ||
               (state == TorrentCompletionCallbackState.TimedOut && IsFinalizationVisibilityTimeout(lastError));
    }

    public static string AppendMissingAfterAttemptHint(string? error, string finalPayloadPath)
    {
        var normalizedError = string.IsNullOrWhiteSpace(error)
            ? "The callback attempt did not complete successfully."
            : error.Trim();

        if (normalizedError.Contains(MissingAfterAttemptMarker, StringComparison.Ordinal))
        {
            return normalizedError;
        }

        return
            $"{normalizedError} The final payload is no longer visible at '{finalPayloadPath}' after the callback attempt. The callback may have already moved or removed it.";
    }
}
