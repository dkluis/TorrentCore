#region

using TorrentCore.Core.Torrents;
using TorrentCore.Service.Configuration;

#endregion

namespace TorrentCore.Service.Callbacks;

public interface ITorrentCompletionFinalizationChecker
{
    TorrentCompletionFinalizationCheckResult Check(TorrentSnapshot snapshot, RuntimeSettingsSnapshot runtimeSettings);
}
