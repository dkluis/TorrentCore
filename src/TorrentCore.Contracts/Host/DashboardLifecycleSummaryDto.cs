namespace TorrentCore.Contracts.Host;

public sealed class DashboardLifecycleSummaryDto
{
    public required Guid                               ServiceInstanceId               { get; init; }
    public          DateTimeOffset?                    FirstEventAtUtc                 { get; init; }
    public          DateTimeOffset?                    LastEventAtUtc                  { get; init; }
    public          DateTimeOffset?                    StartupReadyAtUtc               { get; init; }
    public          DateTimeOffset?                    RecoveryCompletedAtUtc          { get; init; }
    public required int                                StartupRecoveredTorrentCount    { get; init; }
    public required int                                StartupNormalizedTorrentCount   { get; init; }
    public required int                                TorrentsAddedCount              { get; init; }
    public required int                                TorrentsRemovedCount            { get; init; }
    public required int                                MetadataResolvedCount           { get; init; }
    public required int                                MetadataRefreshRequestedCount   { get; init; }
    public required int                                MetadataResetRequestedCount     { get; init; }
    public required int                                MetadataRestartRequestedCount   { get; init; }
    public required int                                CallbackInvokedCount            { get; init; }
    public required int                                CallbackFailedCount             { get; init; }
    public required int                                CallbackTimedOutCount           { get; init; }
    public required int                                CompletedAutoRemovedCount       { get; init; }
    public required int                                OrphanedTorrentLogsDeletedCount { get; init; }
    public required IReadOnlyList<DashboardLifecycleEventDto> RecentEvents              { get; init; }
}
