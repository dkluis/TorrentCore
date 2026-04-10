#region

using TorrentCore.Core.Torrents;

#endregion

namespace TorrentCore.Service.Callbacks;

public interface ITorrentCompletionCallbackInvoker
{
    Task<TorrentCompletionCallbackInvocationResult> InvokeAsync(
        TorrentSnapshot currentSnapshot,
        string? finalPayloadPath,
        CancellationToken cancellationToken
    );
}
