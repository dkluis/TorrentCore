namespace TorrentCore.Contracts.Host;

public sealed class EngineHostStatusDto
{
    public required string ServiceName { get; init; }
    public required string ServiceVersion { get; init; }
    public required Guid ServiceInstanceId { get; init; }
    public required string EngineRuntime { get; init; }
    public required int EngineListenPort { get; init; }
    public required int EngineDhtPort { get; init; }
    public required bool EnginePortForwardingEnabled { get; init; }
    public required bool EngineLocalPeerDiscoveryEnabled { get; init; }
    public required int EngineConnectionFailureLogBurstLimit { get; init; }
    public required int EngineConnectionFailureLogWindowSeconds { get; init; }
    public required EngineHostStatus Status { get; init; }
    public required string EnvironmentName { get; init; }
    public required string DownloadRootPath { get; init; }
    public required int TorrentCount { get; init; }
    public required bool SupportsMagnetAdds { get; init; }
    public required bool SupportsPause { get; init; }
    public required bool SupportsResume { get; init; }
    public required bool SupportsRemove { get; init; }
    public required bool SupportsPersistentStorage { get; init; }
    public required bool SupportsMultiHost { get; init; }
    public required bool StartupRecoveryCompleted { get; init; }
    public required int StartupRecoveredTorrentCount { get; init; }
    public required int StartupNormalizedTorrentCount { get; init; }
    public DateTimeOffset? StartupRecoveryCompletedAtUtc { get; init; }
    public required DateTimeOffset CheckedAtUtc { get; init; }
}
