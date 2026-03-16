using TorrentCore.Contracts.Torrents;

namespace TorrentCore.Core.Torrents;

public sealed class TorrentSnapshot
{
    public required Guid TorrentId { get; init; }
    public required string Name { get; set; }
    public string? CategoryKey { get; set; }
    public string? CompletionCallbackLabel { get; set; }
    public bool InvokeCompletionCallback { get; set; }
    public required TorrentState State { get; set; }
    public required TorrentDesiredState DesiredState { get; set; }
    public required string MagnetUri { get; init; }
    public string? InfoHash { get; init; }
    public string? DownloadRootPath { get; set; }
    public required string SavePath { get; set; }
    public required double ProgressPercent { get; set; }
    public required long DownloadedBytes { get; set; }
    public required long UploadedBytes { get; set; }
    public long? TotalBytes { get; set; }
    public required long DownloadRateBytesPerSecond { get; set; }
    public required long UploadRateBytesPerSecond { get; set; }
    public required int TrackerCount { get; set; }
    public required int ConnectedPeerCount { get; set; }
    public required DateTimeOffset AddedAtUtc { get; init; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public DateTimeOffset? SeedingStartedAtUtc { get; set; }
    public DateTimeOffset? LastActivityAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
}
