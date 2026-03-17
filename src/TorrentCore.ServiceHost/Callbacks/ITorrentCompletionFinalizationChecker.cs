using TorrentCore.Core.Torrents;
using TorrentCore.Service.Configuration;

namespace TorrentCore.Service.Callbacks;

public interface ITorrentCompletionFinalizationChecker
{
    TorrentCompletionFinalizationCheckResult Check(TorrentSnapshot snapshot, RuntimeSettingsSnapshot runtimeSettings);
}
