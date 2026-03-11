using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Torrents;
using TorrentCore.Service.Configuration;

namespace TorrentCore.Service.Engine;

internal static class TorrentQueueDiagnostics
{
    public static IReadOnlyDictionary<Guid, TorrentQueueDiagnostic> Create(IReadOnlyList<TorrentSnapshot> snapshots, RuntimeSettingsSnapshot runtimeSettings)
    {
        var diagnostics = snapshots.ToDictionary(
            snapshot => snapshot.TorrentId,
            _ => new TorrentQueueDiagnostic(null, null));

        var resolvingMetadataCount = snapshots.Count(snapshot => snapshot.State == TorrentState.ResolvingMetadata);
        var downloadingCount = snapshots.Count(snapshot => snapshot.State == TorrentState.Downloading);

        var availableMetadataSlots = Math.Max(0, runtimeSettings.MaxActiveMetadataResolutions - resolvingMetadataCount);
        var availableDownloadSlots = Math.Max(0, runtimeSettings.MaxActiveDownloads - downloadingCount);

        var metadataQueue = snapshots
            .Where(snapshot => snapshot.State == TorrentState.Queued && snapshot.TotalBytes is null)
            .OrderBy(snapshot => snapshot.AddedAtUtc)
            .ThenBy(snapshot => snapshot.TorrentId)
            .ToArray();

        for (var index = 0; index < metadataQueue.Length; index++)
        {
            diagnostics[metadataQueue[index].TorrentId] = new TorrentQueueDiagnostic(
                index < availableMetadataSlots ? TorrentWaitReason.PendingMetadataDispatch : TorrentWaitReason.WaitingForMetadataSlot,
                index + 1);
        }

        var downloadQueue = snapshots
            .Where(snapshot => snapshot.State == TorrentState.Queued && snapshot.TotalBytes is not null)
            .OrderBy(snapshot => snapshot.AddedAtUtc)
            .ThenBy(snapshot => snapshot.TorrentId)
            .ToArray();

        for (var index = 0; index < downloadQueue.Length; index++)
        {
            diagnostics[downloadQueue[index].TorrentId] = new TorrentQueueDiagnostic(
                index < availableDownloadSlots ? TorrentWaitReason.PendingDownloadDispatch : TorrentWaitReason.WaitingForDownloadSlot,
                index + 1);
        }

        foreach (var pausedTorrent in snapshots.Where(snapshot => snapshot.State == TorrentState.Paused))
        {
            diagnostics[pausedTorrent.TorrentId] = new TorrentQueueDiagnostic(TorrentWaitReason.PausedByOperator, null);
        }

        foreach (var errorTorrent in snapshots.Where(snapshot => snapshot.State == TorrentState.Error))
        {
            diagnostics[errorTorrent.TorrentId] = new TorrentQueueDiagnostic(TorrentWaitReason.BlockedByError, null);
        }

        return diagnostics;
    }
}

internal readonly record struct TorrentQueueDiagnostic(TorrentWaitReason? WaitReason, int? QueuePosition);
