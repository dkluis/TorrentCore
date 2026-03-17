namespace TorrentCore.Contracts.Torrents;

public sealed class TorrentDetailDto
{
    public required Guid TorrentId { get; init; }
    public required string Name { get; init; }
    public string? CategoryKey { get; init; }
    public required TorrentState State { get; init; }
    public required string MagnetUri { get; init; }
    public string? InfoHash { get; init; }
    public required string SavePath { get; init; }
    public required double ProgressPercent { get; init; }
    public required long DownloadedBytes { get; init; }
    public long? TotalBytes { get; init; }
    public required long DownloadRateBytesPerSecond { get; init; }
    public required long UploadRateBytesPerSecond { get; init; }
    public required int TrackerCount { get; init; }
    public required int ConnectedPeerCount { get; init; }
    public TorrentWaitReason? WaitReason { get; init; }
    public int? QueuePosition { get; init; }
    public required DateTimeOffset AddedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; init; }
    public DateTimeOffset? LastActivityAtUtc { get; init; }
    public string? CompletionCallbackState { get; init; }
    public DateTimeOffset? CompletionCallbackPendingSinceUtc { get; init; }
    public DateTimeOffset? CompletionCallbackInvokedAtUtc { get; init; }
    public string? CompletionCallbackLastError { get; init; }
    public string? ErrorMessage { get; init; }
    public required bool CanRetryCompletionCallback { get; init; }
    public required bool CanPause { get; init; }
    public required bool CanResume { get; init; }
    public required bool CanRemove { get; init; }
}
