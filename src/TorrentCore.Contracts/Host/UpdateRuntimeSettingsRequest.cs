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
    public required int EngineMaximumConnections { get; init; }
    public required int EngineMaximumHalfOpenConnections { get; init; }
    public required int EngineMaximumDownloadRateBytesPerSecond { get; init; }
    public required int EngineMaximumUploadRateBytesPerSecond { get; init; }
    public required int MaxActiveMetadataResolutions { get; init; }
    public required int MaxActiveDownloads { get; init; }
    public bool? CompletionCallbackEnabled { get; init; }
    public string? CompletionCallbackCommandPath { get; init; }
    public string? CompletionCallbackArguments { get; init; }
    public string? CompletionCallbackWorkingDirectory { get; init; }
    public int? CompletionCallbackTimeoutSeconds { get; init; }
    public string? CompletionCallbackApiBaseUrlOverride { get; init; }
    public string? CompletionCallbackApiKeyOverride { get; init; }
}
