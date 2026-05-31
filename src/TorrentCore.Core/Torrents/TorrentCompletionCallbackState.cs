namespace TorrentCore.Core.Torrents;

public enum TorrentCompletionCallbackState
{
    PendingFinalization,
    WaitingForFeedback,
    Invoked,
    Failed,
    TimedOut,
}
