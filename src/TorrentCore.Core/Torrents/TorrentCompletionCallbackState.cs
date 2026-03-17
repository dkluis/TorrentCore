namespace TorrentCore.Core.Torrents;

public enum TorrentCompletionCallbackState
{
    PendingFinalization,
    Invoked,
    Failed,
    TimedOut,
}
