using TorrentCore.Contracts.Torrents;

namespace TorrentCore.WebUI.Components.Pages;

internal static class CompletionCallbackDisplayText
{
    public static string FormatState(string? state)
    {
        return state switch
        {
            null or "" => "Not available",
            "PendingFinalization" => "Waiting For Final Payload",
            "WaitingForFeedback" => "Waiting For TVMaze",
            "Invoked" => "Final Feedback Received",
            "Failed" => "Callback Failed",
            "TimedOut" => "Callback Timed Out",
            _ => state,
        };
    }

    public static string BuildWaitingDetail(string? state, DateTimeOffset? pendingSinceUtc, DateTimeOffset? submittedAtUtc,
        string? lastError, Func<DateTimeOffset?, string> formatTimestamp)
    {
        if (string.Equals(state, "PendingFinalization", StringComparison.OrdinalIgnoreCase))
        {
            return pendingSinceUtc is null
                ? "Waiting for final payload visibility."
                : $"Waiting for final payload since {formatTimestamp(pendingSinceUtc)}";
        }

        if (string.Equals(state, "WaitingForFeedback", StringComparison.OrdinalIgnoreCase))
        {
            return submittedAtUtc is null
                ? "Waiting for TVMaze to report the final callback result."
                : $"Waiting for TVMaze since {formatTimestamp(submittedAtUtc)}";
        }

        return string.IsNullOrWhiteSpace(lastError)
            ? "Review torrent detail or logs for more callback context."
            : lastError;
    }

    public static string BuildFeedbackSummary(CompletionCallbackFeedbackDto? feedback)
    {
        if (feedback is null)
        {
            return "No final callback feedback stored yet.";
        }

        if (!string.IsNullOrWhiteSpace(feedback.DisplayMessage))
        {
            return feedback.DisplayMessage;
        }

        if (!string.IsNullOrWhiteSpace(feedback.FinalState))
        {
            return feedback.FinalState;
        }

        return "Final callback feedback stored.";
    }
}
