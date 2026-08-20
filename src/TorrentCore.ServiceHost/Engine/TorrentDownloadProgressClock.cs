using TorrentCore.Core.Torrents;

namespace TorrentCore.Service.Engine;

internal enum TorrentDownloadActivityState
{
    Active,
    Suspended,
    Queued,
    Inactive,
}

internal readonly record struct TorrentDownloadProgressClockTransition(
    DateTimeOffset? NoProgressStartedAtUtc,
    bool IsDownloadYielded);

internal static class TorrentDownloadProgressClock
{
    public static TorrentDownloadProgressClockTransition Evaluate(
        TorrentSnapshot priorSnapshot,
        long observedDownloadedBytes,
        TorrentDownloadActivityState activityState,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(priorSnapshot);
        ArgumentOutOfRangeException.ThrowIfNegative(observedDownloadedBytes);

        return activityState switch
        {
            TorrentDownloadActivityState.Active when priorSnapshot.IsDownloadYielded =>
                new TorrentDownloadProgressClockTransition(now, false),
            TorrentDownloadActivityState.Active when
                priorSnapshot.DownloadNoProgressStartedAtUtc is null ||
                observedDownloadedBytes > priorSnapshot.DownloadedBytes =>
                new TorrentDownloadProgressClockTransition(now, false),
            TorrentDownloadActivityState.Active =>
                new TorrentDownloadProgressClockTransition(
                    priorSnapshot.DownloadNoProgressStartedAtUtc,
                    false),
            TorrentDownloadActivityState.Suspended =>
                new TorrentDownloadProgressClockTransition(
                    priorSnapshot.DownloadNoProgressStartedAtUtc,
                    priorSnapshot.IsDownloadYielded),
            TorrentDownloadActivityState.Queued =>
                new TorrentDownloadProgressClockTransition(null, priorSnapshot.IsDownloadYielded),
            _ => new TorrentDownloadProgressClockTransition(null, false),
        };
    }
}
