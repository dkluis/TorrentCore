namespace TorrentCore.Service.Callbacks;

public sealed class TorrentCompletionCallbackInvocationResult
{
    public required TorrentCompletionCallbackInvocationStatus Status { get; init; }
    public          string?                                   Error  { get; init; }
}
