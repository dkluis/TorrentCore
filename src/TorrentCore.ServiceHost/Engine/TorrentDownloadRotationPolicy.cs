using TorrentCore.Contracts.Torrents;
using TorrentCore.Core.Torrents;

namespace TorrentCore.Service.Engine;

internal sealed record TorrentDownloadRotationSelection(
    IReadOnlyList<Guid> YieldTorrentIds,
    IReadOnlyList<Guid> ReplacementTorrentIds);

internal static class TorrentDownloadRotationPolicy
{
    public static TorrentDownloadRotationSelection Evaluate(
        IReadOnlyList<TorrentQueuePolicyItem> items,
        int maxActiveMetadataResolutions,
        int maxActiveDownloads,
        TimeSpan noProgressInterval,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (noProgressInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(noProgressInterval));
        }

        var baseline = TorrentQueuePolicy.Evaluate(
            items, maxActiveMetadataResolutions, maxActiveDownloads);
        var baselineAdmissions = baseline.AdmissionOrder.ToHashSet();
        var selected = new List<Guid>();

        var staleCandidates = items
            .Where(item => IsStaleActiveDownload(item, noProgressInterval, now))
            .OrderBy(item => item.Snapshot.DownloadNoProgressStartedAtUtc)
            .ThenBy(item => item.Snapshot.TorrentId)
            .ToArray();

        foreach (var candidate in staleCandidates)
        {
            var simulatedYieldIds = selected.Append(candidate.Snapshot.TorrentId).ToHashSet();
            var simulatedItems = items.Select(item => simulatedYieldIds.Contains(item.Snapshot.TorrentId)
                    ? item with { IsActive = false }
                    : item)
                .ToArray();
            var simulated = TorrentQueuePolicy.Evaluate(
                simulatedItems, maxActiveMetadataResolutions, maxActiveDownloads);
            var incrementalAdmissionCount = simulated.AdmissionOrder.Count(torrentId =>
                !baselineAdmissions.Contains(torrentId) && !simulatedYieldIds.Contains(torrentId));

            if (incrementalAdmissionCount > selected.Count)
            {
                selected.Add(candidate.Snapshot.TorrentId);
            }
        }

        if (selected.Count == 0)
        {
            return new TorrentDownloadRotationSelection([], []);
        }

        var selectedSet = selected.ToHashSet();
        var finalItems = items.Select(item => selectedSet.Contains(item.Snapshot.TorrentId)
                ? item with { IsActive = false }
                : item)
            .ToArray();
        var finalPolicy = TorrentQueuePolicy.Evaluate(
            finalItems, maxActiveMetadataResolutions, maxActiveDownloads);
        var replacements = finalPolicy.AdmissionOrder
            .Where(torrentId => !baselineAdmissions.Contains(torrentId) && !selectedSet.Contains(torrentId))
            .Take(selected.Count)
            .ToArray();

        return new TorrentDownloadRotationSelection(selected, replacements);
    }

    private static bool IsStaleActiveDownload(
        TorrentQueuePolicyItem item,
        TimeSpan noProgressInterval,
        DateTimeOffset now)
    {
        return item.WorkKind == TorrentQueueWorkKind.Download && item.IsActive && !item.IsComplete &&
               item.Snapshot.DesiredState == TorrentDesiredState.Runnable && !item.Snapshot.IsQueueHeld &&
               item.Snapshot.ProgressPercent < 100 &&
               item.Snapshot.State is not TorrentState.Completed and not TorrentState.Error and
                   not TorrentState.Removed and not TorrentState.Paused and not TorrentState.Seeding and
                   not TorrentState.WaitingForFileCompletion &&
               item.Snapshot.DownloadNoProgressStartedAtUtc is { } startedAt &&
               now - startedAt >= noProgressInterval;
    }
}
