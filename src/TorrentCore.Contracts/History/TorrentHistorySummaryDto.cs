namespace TorrentCore.Contracts.History;

public sealed class TorrentHistorySummaryDto
{
    public required Guid            TorrentId                        { get; init; }
    public required string          Name                             { get; init; }
    public          string?         InfoHash                         { get; init; }
    public          string?         CategoryKey                      { get; init; }
    public          string?         DownloadRootPath                 { get; init; }
    public required string          LatestTorrentState               { get; init; }
    public          string?         LatestWaitReason                 { get; init; }
    public          string?         LatestErrorMessage               { get; init; }
    public required double          LatestProgressPercent            { get; init; }
    public required long            LatestDownloadedBytes            { get; init; }
    public required long            LatestUploadedBytes              { get; init; }
    public          long?           LatestTotalBytes                 { get; init; }
    public required long            LatestDownloadRateBytesPerSecond { get; init; }
    public required long            LatestUploadRateBytesPerSecond   { get; init; }
    public required int             LatestTrackerCount               { get; init; }
    public required int             LatestConnectedPeerCount         { get; init; }
    public required DateTimeOffset  SubmittedAt                      { get; init; }
    public          DateTimeOffset? MetadataResolvedAt               { get; init; }
    public          DateTimeOffset? DownloadStartedAt                { get; init; }
    public          DateTimeOffset? DownloadCompletedAt              { get; init; }
    public          DateTimeOffset? SeedingStartedAt                 { get; init; }
    public          DateTimeOffset? LastActivityAt                   { get; init; }
    public required DateTimeOffset  LastUpdatedAt                    { get; init; }
    public          DateTimeOffset? RemovedAt                        { get; init; }
    public          string?         LatestCallbackStatus             { get; init; }
    public required bool            DataDeleted                      { get; init; }
    public          string?         RemovalReason                    { get; init; }
    public required bool            RemovedByCleanupPolicy           { get; init; }
}
