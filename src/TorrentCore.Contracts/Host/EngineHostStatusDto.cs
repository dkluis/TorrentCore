namespace TorrentCore.Contracts.Host;

public sealed class EngineHostStatusDto
{
    public required string           ServiceName                             { get; init; }
    public required string           ServiceVersion                          { get; init; }
    public required Guid             ServiceInstanceId                       { get; init; }
    public required string           EngineRuntime                           { get; init; }
    public required int              EngineListenPort                        { get; init; }
    public required int              EngineDhtPort                           { get; init; }
    public required bool             EnginePortForwardingEnabled             { get; init; }
    public required bool             EngineLocalPeerDiscoveryEnabled         { get; init; }
    public required int              EngineMaximumConnections                { get; init; }
    public required int              EngineMaximumHalfOpenConnections        { get; init; }
    public required int              EngineMaximumDownloadRateBytesPerSecond { get; init; }
    public required int              EngineMaximumUploadRateBytesPerSecond   { get; init; }
    public required int              EngineConnectionFailureLogBurstLimit    { get; init; }
    public required int              EngineConnectionFailureLogWindowSeconds { get; init; }
    public required int              MaxActiveMetadataResolutions            { get; init; }
    public required int              MaxActiveDownloads                      { get; init; }
    public required int              AvailableMetadataResolutionSlots        { get; init; }
    public required int              AvailableDownloadSlots                  { get; init; }
    public required int              ResolvingMetadataCount                  { get; init; }
    public required int              MetadataQueueCount                      { get; init; }
    public required int              DownloadingCount                        { get; init; }
    public required int              DownloadQueueCount                      { get; init; }
    public required int              SeedingCount                            { get; init; }
    public required int              PausedCount                             { get; init; }
    public required int              CompletedCount                          { get; init; }
    public required int              ErrorCount                              { get; init; }
    public required int              CurrentConnectedPeerCount               { get; init; }
    public required long             CurrentDownloadRateBytesPerSecond       { get; init; }
    public required long             CurrentUploadRateBytesPerSecond         { get; init; }
    public required bool             PartialFilesEnabled                     { get; init; }
    public required string           PartialFileSuffix                       { get; init; }
    public required string           SeedingStopMode                         { get; init; }
    public required double           SeedingStopRatio                        { get; init; }
    public required int              SeedingStopMinutes                      { get; init; }
    public required string           CompletedTorrentCleanupMode             { get; init; }
    public required int              CompletedTorrentCleanupMinutes          { get; init; }
    public required bool             DeleteLogsForCompletedTorrents         { get; init; }
    public required EngineHostStatus Status                                  { get; init; }
    public required string           EnvironmentName                         { get; init; }
    public required string           DownloadRootPath                        { get; init; }
    public required int              TorrentCount                            { get; init; }
    public required bool             SupportsMagnetAdds                      { get; init; }
    public required bool             SupportsPause                           { get; init; }
    public required bool             SupportsResume                          { get; init; }
    public required bool             SupportsRemove                          { get; init; }
    public required bool             SupportsPersistentStorage               { get; init; }
    public required bool             SupportsMultiHost                       { get; init; }
    public required bool             StartupRecoveryCompleted                { get; init; }
    public required int              StartupRecoveredTorrentCount            { get; init; }
    public required int              StartupNormalizedTorrentCount           { get; init; }
    public          DateTimeOffset?  StartupRecoveryCompletedAtUtc           { get; init; }
    public required DateTimeOffset   CheckedAtUtc                            { get; init; }
}
