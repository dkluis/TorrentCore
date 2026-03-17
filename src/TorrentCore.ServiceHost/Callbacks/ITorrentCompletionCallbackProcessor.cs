using TorrentCore.Core.Torrents;
using TorrentCore.Service.Configuration;

namespace TorrentCore.Service.Callbacks;

public interface ITorrentCompletionCallbackProcessor
{
    Task<bool> MarkPendingIfTriggeredAsync(
        DateTimeOffset? previousCompletedAtUtc,
        TorrentSnapshot snapshot,
        RuntimeSettingsSnapshot runtimeSettings,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<bool> ProcessPendingAsync(
        TorrentSnapshot snapshot,
        RuntimeSettingsSnapshot runtimeSettings,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
