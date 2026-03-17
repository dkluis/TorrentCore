using TorrentCore.Core.Torrents;
using TorrentCore.Service.Configuration;

namespace TorrentCore.Service.Callbacks;

public interface ITorrentCompletionCallbackProcessor
{
    bool MarkPendingIfTriggered(DateTimeOffset? previousCompletedAtUtc, TorrentSnapshot snapshot, DateTimeOffset now);
    Task<bool> ProcessPendingAsync(
        TorrentSnapshot snapshot,
        RuntimeSettingsSnapshot runtimeSettings,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
