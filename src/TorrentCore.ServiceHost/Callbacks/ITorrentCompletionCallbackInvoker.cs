using TorrentCore.Core.Torrents;

namespace TorrentCore.Service.Callbacks;

public interface ITorrentCompletionCallbackInvoker
{
    Task<TorrentCompletionCallbackInvocationResult> InvokeAsync(
        TorrentSnapshot currentSnapshot,
        CancellationToken cancellationToken);
}
