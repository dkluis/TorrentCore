#region

using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Torrents;
using TorrentCore.Service.Configuration;

#endregion

namespace TorrentCore.Service.Engine;

internal static class TorrentQueueDiagnostics
{
    public static IReadOnlyDictionary<Guid, TorrentQueueDiagnostic> Create(IReadOnlyList<TorrentSnapshot> snapshots,
        RuntimeSettingsSnapshot runtimeSettings)
    {
        return TorrentQueuePolicy.EvaluateSnapshots(
            snapshots,
            runtimeSettings.MaxActiveMetadataResolutions,
            runtimeSettings.MaxActiveDownloads
        ).Diagnostics;
    }
}
internal readonly record struct TorrentQueueDiagnostic(
    TorrentWaitReason? WaitReason,
    int? QueuePosition,
    int? PriorityQueuePosition = null,
    int? HeldQueuePosition = null,
    bool IsQueueHeld = false);
