namespace TorrentCore.Contracts.Host;

public sealed class UpdateRuntimeSettingsRequest
{
    public required string  SeedingStopMode                              { get; init; }
    public required double  SeedingStopRatio                             { get; init; }
    public required int     SeedingStopMinutes                           { get; init; }
    public required string  CompletedTorrentCleanupMode                  { get; init; }
    public required int     CompletedTorrentCleanupMinutes               { get; init; }
    public required bool    DeleteLogsForCompletedTorrents               { get; init; }
    public required int     EngineConnectionFailureLogBurstLimit         { get; init; }
    public required int     EngineConnectionFailureLogWindowSeconds      { get; init; }
    public          bool?   EngineAllowPeerExchange                       { get; init; }
    public string           EngineEncryptionMode                         { get; init; } = "EncryptedPreferred";
    public required int     EngineMaximumConnections                     { get; init; }
    public required int     EngineMaximumHalfOpenConnections             { get; init; }
    public required int     EngineMaximumDownloadRateBytesPerSecond      { get; init; }
    public required int     EngineMaximumUploadRateBytesPerSecond        { get; init; }
    public required int     MaxActiveMetadataResolutions                 { get; init; }
    public required int     MaxActiveDownloads                           { get; init; }
    public required int     MetadataRefreshStaleSeconds                  { get; init; }
    public required int     MetadataRefreshRestartDelaySeconds           { get; init; }
    public          int?    MetadataResolutionTimeSliceMinutes            { get; init; }
    public          int?    PriorityMetadataAttempts                      { get; init; }
    public          int?    AutomaticMetadataResetStuckThresholdSeconds  { get; init; }
    public required int     ColdDownloadRecoveryThresholdMinutes         { get; init; }
    public required int     ColdDownloadRecoveryIntervalMinutes          { get; init; }
    public int              ColdDownloadAbandonAfterHours                { get; init; } = 72;
    public          bool?   CompletionCallbackEnabled                    { get; init; }
    public          string? CompletionCallbackCommandPath                { get; init; }
    public          string? CompletionCallbackArguments                  { get; init; }
    public          string? CompletionCallbackWorkingDirectory           { get; init; }
    public          int?    CompletionCallbackTimeoutSeconds             { get; init; }
    public          int?    CompletionCallbackFinalizationTimeoutSeconds { get; init; }
    public          string? CompletionCallbackApiBaseUrlOverride         { get; init; }
    public          string? CompletionCallbackApiKeyOverride             { get; init; }
    public          bool?   VpnEgressValidationEnabled                   { get; init; }
    public          string? VpnEgressValidationEndpoint                  { get; init; }
    public IReadOnlyList<string>? VpnEgressDirectIspCidrs                { get; init; }
    public          int?    VpnEgressDegradedCheckIntervalSeconds        { get; init; }
    public          int?    VpnEgressReadyCheckIntervalSeconds           { get; init; }
    public          int?    VpnEgressRequestTimeoutSeconds               { get; init; }
    public          int?    VpnEgressEngineSuspensionTimeoutSeconds      { get; init; }
    public          string? ExpressVpnAutomaticRecoveryMode               { get; init; }
    public          int?    ExpressVpnRecoveryDelaySeconds                 { get; init; }
    public          int?    ExpressVpnUnavailableLaunchDelaySeconds        { get; init; }
    public          bool?   RuntimeTickDurationSummaryEnabled            { get; init; }
}
