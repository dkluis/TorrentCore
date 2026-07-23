using TorrentCore.Contracts.History;

namespace TorrentCore.Core.History;

public sealed class TorrentHistoryRecord
{
    public required Guid            TorrentId                          { get; init; }
    public required string          Name                               { get; set; }
    public required string          MagnetUri                          { get; set; }
    public          string?         InfoHash                           { get; set; }
    public          string?         CategoryKey                        { get; set; }
    public          string?         DownloadRootPath                   { get; set; }
    public required string          LatestTorrentState                 { get; set; }
    public          string?         LatestWaitReason                   { get; set; }
    public          string?         LatestErrorMessage                 { get; set; }
    public required double          LatestProgressPercent              { get; set; }
    public required long            LatestDownloadedBytes              { get; set; }
    public required long            LatestUploadedBytes                { get; set; }
    public          long?           LatestTotalBytes                   { get; set; }
    public required long            LatestDownloadRateBytesPerSecond   { get; set; }
    public required long            LatestUploadRateBytesPerSecond     { get; set; }
    public required int             LatestTrackerCount                 { get; set; }
    public required int             LatestConnectedPeerCount           { get; set; }
    public required DateTimeOffset  SubmittedAtUtc                     { get; set; }
    public          DateTimeOffset? MetadataResolvedAtUtc              { get; set; }
    public          DateTimeOffset? DownloadStartedAtUtc               { get; set; }
    public          DateTimeOffset? DownloadCompletedAtUtc             { get; set; }
    public          DateTimeOffset? SeedingStartedAtUtc                { get; set; }
    public          DateTimeOffset? LastActivityAtUtc                  { get; set; }
    public required DateTimeOffset  LastUpdatedAtUtc                   { get; set; }
    public          DateTimeOffset? RemovedAtUtc                       { get; set; }
    public required bool            InvokeCompletionCallback           { get; set; }
    public          string?         CompletionCallbackLabel            { get; set; }
    public          string?         LatestCallbackStatus               { get; set; }
    public          DateTimeOffset? CallbackStartedAtUtc               { get; set; }
    public          DateTimeOffset? CallbackCompletedAtUtc             { get; set; }
    public          string?         CallbackLastError                  { get; set; }
    public          DateTimeOffset? LatestCompletionCallbackFeedbackReceivedAtUtc { get; set; }
    public          string?         LatestCompletionCallbackFeedbackJson { get; set; }
    public required bool            DataDeleted                        { get; set; }
    public          string?         RemovalReason                      { get; set; }
    public          TorrentRemovalKind? RemovalKind                    { get; set; }
    public required bool            RemovedByCleanupPolicy             { get; set; }
    public          string?         FinalPayloadPath                   { get; set; }
    public          Guid?           ServiceInstanceIdLastSeen          { get; set; }
}
