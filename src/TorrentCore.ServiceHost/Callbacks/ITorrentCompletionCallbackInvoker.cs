using TorrentCore.Core.Torrents;

namespace TorrentCore.Service.Callbacks;

public interface ITorrentCompletionCallbackInvoker
{
    Task InvokeIfTriggeredAsync(
        DateTimeOffset? previousCompletedAtUtc,
        TorrentSnapshot currentSnapshot,
        CancellationToken cancellationToken);
}
