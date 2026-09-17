using TorrentCore.Contracts.Host;

namespace TorrentCore.WebUI2.Client.Services;

// The service accepts a complete runtime-settings document. Keep every field here so saving
// one visible group preserves values from the other groups.
public sealed class RuntimeSettingsDraft
{
    public string VpnRecoveryProvider { get; set; } = "ExpressVPN";
    public string SeedingStopMode { get; set; } = "Unlimited";
    public double SeedingStopRatio { get; set; }
    public int SeedingStopMinutes { get; set; }
    public string CompletedTorrentCleanupMode { get; set; } = "Never";
    public int CompletedTorrentCleanupMinutes { get; set; }
    public bool DeleteLogsForCompletedTorrents { get; set; }
    public int EngineConnectionFailureLogBurstLimit { get; set; }
    public int EngineConnectionFailureLogWindowSeconds { get; set; }
    public bool EngineAllowPeerExchange { get; set; }
    public string EngineEncryptionMode { get; set; } = "EncryptedPreferred";
    public int EngineMaximumConnections { get; set; }
    public int EngineMaximumHalfOpenConnections { get; set; }
    public int EngineMaximumDownloadRateBytesPerSecond { get; set; }
    public int EngineMaximumUploadRateBytesPerSecond { get; set; }
    public int MaxActiveMetadataResolutions { get; set; }
    public int MaxActiveDownloads { get; set; }
    public int MetadataRefreshStaleSeconds { get; set; }
    public int MetadataRefreshRestartDelaySeconds { get; set; }
    public int MetadataResolutionTimeSliceMinutes { get; set; }
    public int PriorityMetadataAttempts { get; set; }
    public int DownloadNoProgressTimeSliceMinutes { get; set; }
    public int AutomaticMetadataResetStuckThresholdSeconds { get; set; }
    public int ColdDownloadRecoveryThresholdMinutes { get; set; }
    public int ColdDownloadRecoveryIntervalMinutes { get; set; }
    public int ColdDownloadAbandonAfterHours { get; set; }
    public bool CompletionCallbackEnabled { get; set; }
    public string? CompletionCallbackCommandPath { get; set; }
    public string? CompletionCallbackArguments { get; set; }
    public string? CompletionCallbackWorkingDirectory { get; set; }
    public int CompletionCallbackTimeoutSeconds { get; set; }
    public int CompletionCallbackFinalizationTimeoutSeconds { get; set; }
    public string? CompletionCallbackApiBaseUrlOverride { get; set; }
    public string? CompletionCallbackApiKeyOverride { get; set; }
    public bool VpnEgressValidationEnabled { get; set; }
    public string VpnEgressValidationEndpoint { get; set; } = "";
    public string VpnEgressDirectIspCidrs { get; set; } = "";
    public int VpnEgressDegradedCheckIntervalSeconds { get; set; }
    public int VpnEgressReadyCheckIntervalSeconds { get; set; }
    public int VpnEgressRequestTimeoutSeconds { get; set; }
    public int VpnEgressEngineSuspensionTimeoutSeconds { get; set; }
    public string ExpressVpnAutomaticRecoveryMode { get; set; } = "Disabled";
    public int ExpressVpnRecoveryDelaySeconds { get; set; }
    public int ExpressVpnUnavailableLaunchDelaySeconds { get; set; }
    public bool RuntimeTickDurationSummaryEnabled { get; set; }

    public static RuntimeSettingsDraft From(RuntimeSettingsDto x) => new()
    {
        VpnRecoveryProvider = x.VpnRecoveryProvider,
        SeedingStopMode = x.SeedingStopMode,
        SeedingStopRatio = x.SeedingStopRatio,
        SeedingStopMinutes = x.SeedingStopMinutes,
        CompletedTorrentCleanupMode = x.CompletedTorrentCleanupMode,
        CompletedTorrentCleanupMinutes = x.CompletedTorrentCleanupMinutes,
        DeleteLogsForCompletedTorrents = x.DeleteLogsForCompletedTorrents,
        EngineConnectionFailureLogBurstLimit = x.EngineConnectionFailureLogBurstLimit,
        EngineConnectionFailureLogWindowSeconds = x.EngineConnectionFailureLogWindowSeconds,
        EngineAllowPeerExchange = x.EngineAllowPeerExchange,
        EngineEncryptionMode = x.EngineEncryptionMode,
        EngineMaximumConnections = x.EngineMaximumConnections,
        EngineMaximumHalfOpenConnections = x.EngineMaximumHalfOpenConnections,
        EngineMaximumDownloadRateBytesPerSecond = x.EngineMaximumDownloadRateBytesPerSecond,
        EngineMaximumUploadRateBytesPerSecond = x.EngineMaximumUploadRateBytesPerSecond,
        MaxActiveMetadataResolutions = x.MaxActiveMetadataResolutions,
        MaxActiveDownloads = x.MaxActiveDownloads,
        MetadataRefreshStaleSeconds = x.MetadataRefreshStaleSeconds,
        MetadataRefreshRestartDelaySeconds = x.MetadataRefreshRestartDelaySeconds,
        MetadataResolutionTimeSliceMinutes = x.MetadataResolutionTimeSliceMinutes,
        PriorityMetadataAttempts = x.PriorityMetadataAttempts,
        DownloadNoProgressTimeSliceMinutes = x.DownloadNoProgressTimeSliceMinutes,
        AutomaticMetadataResetStuckThresholdSeconds = x.AutomaticMetadataResetStuckThresholdSeconds,
        ColdDownloadRecoveryThresholdMinutes = x.ColdDownloadRecoveryThresholdMinutes,
        ColdDownloadRecoveryIntervalMinutes = x.ColdDownloadRecoveryIntervalMinutes,
        ColdDownloadAbandonAfterHours = x.ColdDownloadAbandonAfterHours,
        CompletionCallbackEnabled = x.CompletionCallbackEnabled,
        CompletionCallbackCommandPath = x.CompletionCallbackCommandPath,
        CompletionCallbackArguments = x.CompletionCallbackArguments,
        CompletionCallbackWorkingDirectory = x.CompletionCallbackWorkingDirectory,
        CompletionCallbackTimeoutSeconds = x.CompletionCallbackTimeoutSeconds,
        CompletionCallbackFinalizationTimeoutSeconds = x.CompletionCallbackFinalizationTimeoutSeconds,
        CompletionCallbackApiBaseUrlOverride = x.CompletionCallbackApiBaseUrlOverride,
        CompletionCallbackApiKeyOverride = x.CompletionCallbackApiKeyOverride,
        VpnEgressValidationEnabled = x.VpnEgressValidationEnabled,
        VpnEgressValidationEndpoint = x.VpnEgressValidationEndpoint,
        VpnEgressDirectIspCidrs = string.Join(", ", x.VpnEgressDirectIspCidrs),
        VpnEgressDegradedCheckIntervalSeconds = x.VpnEgressDegradedCheckIntervalSeconds,
        VpnEgressReadyCheckIntervalSeconds = x.VpnEgressReadyCheckIntervalSeconds,
        VpnEgressRequestTimeoutSeconds = x.VpnEgressRequestTimeoutSeconds,
        VpnEgressEngineSuspensionTimeoutSeconds = x.VpnEgressEngineSuspensionTimeoutSeconds,
        ExpressVpnAutomaticRecoveryMode = x.ExpressVpnAutomaticRecoveryMode,
        ExpressVpnRecoveryDelaySeconds = x.ExpressVpnRecoveryDelaySeconds,
        ExpressVpnUnavailableLaunchDelaySeconds = x.ExpressVpnUnavailableLaunchDelaySeconds,
        RuntimeTickDurationSummaryEnabled = x.RuntimeTickDurationSummaryEnabled,
    };

    public UpdateRuntimeSettingsRequest ToRequest() => new()
    {
        VpnRecoveryProvider = VpnRecoveryProvider,
        SeedingStopMode = SeedingStopMode,
        SeedingStopRatio = SeedingStopRatio,
        SeedingStopMinutes = SeedingStopMinutes,
        CompletedTorrentCleanupMode = CompletedTorrentCleanupMode,
        CompletedTorrentCleanupMinutes = CompletedTorrentCleanupMinutes,
        DeleteLogsForCompletedTorrents = DeleteLogsForCompletedTorrents,
        EngineConnectionFailureLogBurstLimit = EngineConnectionFailureLogBurstLimit,
        EngineConnectionFailureLogWindowSeconds = EngineConnectionFailureLogWindowSeconds,
        EngineAllowPeerExchange = EngineAllowPeerExchange,
        EngineEncryptionMode = EngineEncryptionMode,
        EngineMaximumConnections = EngineMaximumConnections,
        EngineMaximumHalfOpenConnections = EngineMaximumHalfOpenConnections,
        EngineMaximumDownloadRateBytesPerSecond = EngineMaximumDownloadRateBytesPerSecond,
        EngineMaximumUploadRateBytesPerSecond = EngineMaximumUploadRateBytesPerSecond,
        MaxActiveMetadataResolutions = MaxActiveMetadataResolutions,
        MaxActiveDownloads = MaxActiveDownloads,
        MetadataRefreshStaleSeconds = MetadataRefreshStaleSeconds,
        MetadataRefreshRestartDelaySeconds = MetadataRefreshRestartDelaySeconds,
        MetadataResolutionTimeSliceMinutes = MetadataResolutionTimeSliceMinutes,
        PriorityMetadataAttempts = PriorityMetadataAttempts,
        DownloadNoProgressTimeSliceMinutes = DownloadNoProgressTimeSliceMinutes,
        AutomaticMetadataResetStuckThresholdSeconds = AutomaticMetadataResetStuckThresholdSeconds,
        ColdDownloadRecoveryThresholdMinutes = ColdDownloadRecoveryThresholdMinutes,
        ColdDownloadRecoveryIntervalMinutes = ColdDownloadRecoveryIntervalMinutes,
        ColdDownloadAbandonAfterHours = ColdDownloadAbandonAfterHours,
        CompletionCallbackEnabled = CompletionCallbackEnabled,
        CompletionCallbackCommandPath = CompletionCallbackCommandPath,
        CompletionCallbackArguments = CompletionCallbackArguments,
        CompletionCallbackWorkingDirectory = CompletionCallbackWorkingDirectory,
        CompletionCallbackTimeoutSeconds = CompletionCallbackTimeoutSeconds,
        CompletionCallbackFinalizationTimeoutSeconds = CompletionCallbackFinalizationTimeoutSeconds,
        CompletionCallbackApiBaseUrlOverride = CompletionCallbackApiBaseUrlOverride,
        CompletionCallbackApiKeyOverride = CompletionCallbackApiKeyOverride,
        VpnEgressValidationEnabled = VpnEgressValidationEnabled,
        VpnEgressValidationEndpoint = VpnEgressValidationEndpoint,
        VpnEgressDirectIspCidrs = VpnEgressDirectIspCidrs.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
        VpnEgressDegradedCheckIntervalSeconds = VpnEgressDegradedCheckIntervalSeconds,
        VpnEgressReadyCheckIntervalSeconds = VpnEgressReadyCheckIntervalSeconds,
        VpnEgressRequestTimeoutSeconds = VpnEgressRequestTimeoutSeconds,
        VpnEgressEngineSuspensionTimeoutSeconds = VpnEgressEngineSuspensionTimeoutSeconds,
        ExpressVpnAutomaticRecoveryMode = ExpressVpnAutomaticRecoveryMode,
        ExpressVpnRecoveryDelaySeconds = ExpressVpnRecoveryDelaySeconds,
        ExpressVpnUnavailableLaunchDelaySeconds = ExpressVpnUnavailableLaunchDelaySeconds,
        RuntimeTickDurationSummaryEnabled = RuntimeTickDurationSummaryEnabled,
    };
}
