namespace TorrentCore.Contracts.Host;

public sealed class RuntimeSettingsDto
{
    public required string EngineRuntime { get; init; }
    public required bool SupportsLiveUpdates { get; init; }
    public required bool UsesPersistedOverrides { get; init; }
    public required bool PartialFilesEnabled { get; init; }
    public required string PartialFileSuffix { get; init; }
    public required string SeedingStopMode { get; init; }
    public required double SeedingStopRatio { get; init; }
    public required int SeedingStopMinutes { get; init; }
    public required int EngineConnectionFailureLogBurstLimit { get; init; }
    public required int EngineConnectionFailureLogWindowSeconds { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }
    public required DateTimeOffset RetrievedAtUtc { get; init; }
}
