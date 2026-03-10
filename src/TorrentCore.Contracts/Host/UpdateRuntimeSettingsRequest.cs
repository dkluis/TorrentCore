namespace TorrentCore.Contracts.Host;

public sealed class UpdateRuntimeSettingsRequest
{
    public required string SeedingStopMode { get; init; }
    public required double SeedingStopRatio { get; init; }
    public required int SeedingStopMinutes { get; init; }
    public required string CompletedTorrentCleanupMode { get; init; }
    public required int CompletedTorrentCleanupMinutes { get; init; }
    public required int EngineConnectionFailureLogBurstLimit { get; init; }
    public required int EngineConnectionFailureLogWindowSeconds { get; init; }
}
