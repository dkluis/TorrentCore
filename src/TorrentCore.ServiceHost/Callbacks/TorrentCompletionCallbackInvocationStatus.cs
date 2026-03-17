namespace TorrentCore.Service.Callbacks;

public enum TorrentCompletionCallbackInvocationStatus
{
    Skipped,
    Invoked,
    Failed,
    TimedOut,
}
