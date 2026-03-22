namespace TorrentCore.Contracts.Host;

public sealed class RuntimeSettingsDto
{
    public required string          EngineRuntime                                  { get; init; }
    public required bool            SupportsLiveUpdates                            { get; init; }
    public required bool            UsesPersistedOverrides                         { get; init; }
    public required bool            PartialFilesEnabled                            { get; init; }
    public required string          PartialFileSuffix                              { get; init; }
    public required string          SeedingStopMode                                { get; init; }
    public required double          SeedingStopRatio                               { get; init; }
    public required int             SeedingStopMinutes                             { get; init; }
    public required string          CompletedTorrentCleanupMode                    { get; init; }
    public required int             CompletedTorrentCleanupMinutes                 { get; init; }
    public required int             EngineConnectionFailureLogBurstLimit           { get; init; }
    public required int             EngineConnectionFailureLogWindowSeconds        { get; init; }
    public required int             EngineMaximumConnections                       { get; init; }
    public required int             EngineMaximumHalfOpenConnections               { get; init; }
    public required int             EngineMaximumDownloadRateBytesPerSecond        { get; init; }
    public required int             EngineMaximumUploadRateBytesPerSecond          { get; init; }
    public required int             MaxActiveMetadataResolutions                   { get; init; }
    public required int             MaxActiveDownloads                             { get; init; }
    public required int             MetadataRefreshStaleSeconds                    { get; init; }
    public required int             MetadataRefreshRestartDelaySeconds             { get; init; }
    public required bool            CompletionCallbackEnabled                      { get; init; }
    public          string?         CompletionCallbackCommandPath                  { get; init; }
    public          string?         CompletionCallbackArguments                    { get; init; }
    public          string?         CompletionCallbackWorkingDirectory             { get; init; }
    public required int             CompletionCallbackTimeoutSeconds               { get; init; }
    public required int             CompletionCallbackFinalizationTimeoutSeconds   { get; init; }
    public          string?         CompletionCallbackApiBaseUrlOverride           { get; init; }
    public          string?         CompletionCallbackApiKeyOverride               { get; init; }
    public required int             AppliedEngineMaximumConnections                { get; init; }
    public required int             AppliedEngineMaximumHalfOpenConnections        { get; init; }
    public required int             AppliedEngineMaximumDownloadRateBytesPerSecond { get; init; }
    public required int             AppliedEngineMaximumUploadRateBytesPerSecond   { get; init; }
    public required bool            EngineSettingsRequireRestart                   { get; init; }
    public          DateTimeOffset? UpdatedAtUtc                                   { get; init; }
    public required DateTimeOffset  RetrievedAtUtc                                 { get; init; }
}
