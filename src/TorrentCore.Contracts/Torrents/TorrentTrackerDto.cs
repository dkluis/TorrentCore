namespace TorrentCore.Contracts.Torrents;

public sealed class TorrentTrackerDto
{
    public required int     TierNumber                   { get; init; }
    public required int     TrackerNumber                { get; init; }
    public required bool    IsActive                     { get; init; }
    public required string  Status                       { get; init; }
    public          bool?   CanAnnounce                  { get; init; }
    public required bool    CanScrape                    { get; init; }
    public          long?   TimeSinceLastAnnounceSeconds { get; init; }
    public          bool?   LastAnnounceSucceeded        { get; init; }
    public          long?   TimeSinceLastScrapeSeconds   { get; init; }
    public          bool?   LastScrapeSucceeded          { get; init; }
    public          string? FailureMessage               { get; init; }
    public          string? WarningMessage               { get; init; }
}
