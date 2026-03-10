namespace TorrentCore.Service.Configuration;

public sealed class RuntimeSettingsSnapshot
{
    public required bool UsesPersistedOverrides { get; init; }
    public required bool PartialFilesEnabled { get; init; }
    public required string PartialFileSuffix { get; init; }
    public required SeedingStopMode SeedingStopMode { get; init; }
    public required double SeedingStopRatio { get; init; }
    public required int SeedingStopMinutes { get; init; }
    public required int EngineConnectionFailureLogBurstLimit { get; init; }
    public required int EngineConnectionFailureLogWindowSeconds { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }
}
