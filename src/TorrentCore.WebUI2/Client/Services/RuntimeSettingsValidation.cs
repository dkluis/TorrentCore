namespace TorrentCore.WebUI2.Client.Services;

public static class RuntimeSettingsValidation
{
    private static readonly string[] SeedingModes = ["Unlimited", "StopImmediately", "StopAfterRatio", "StopAfterTime", "StopAfterRatioOrTime"];
    private static readonly string[] CleanupModes = ["Never", "AfterCompletedMinutes"];
    private static readonly string[] EncryptionModes = ["PlainTextPreferred", "EncryptedPreferred", "EncryptedRequired"];
    private static readonly string[] RecoveryModes = ["Disabled", "DirectIspOnly", "AnyValidationFailure"];

    public static string? Error(RuntimeSettingsDraft x)
    {
        if (!SeedingModes.Contains(x.SeedingStopMode)) return "Choose a supported seeding stop mode.";
        if (!CleanupModes.Contains(x.CompletedTorrentCleanupMode)) return "Choose a supported completed-torrent cleanup mode.";
        if (!EncryptionModes.Contains(x.EngineEncryptionMode)) return "Choose a supported engine encryption mode.";
        if (x.SeedingStopRatio <= 0) return "Seeding stop ratio must be greater than 0.";
        if (x.SeedingStopMinutes < 1) return "Seeding stop minutes must be 1 or greater.";
        if (x.CompletedTorrentCleanupMinutes < 0) return "Completed-torrent cleanup minutes must be 0 or greater.";
        if (x.MaxActiveMetadataResolutions < 1) return "Active metadata resolutions must be 1 or greater.";
        if (x.MaxActiveDownloads < 1) return "Active downloads must be 1 or greater.";
        if (x.MetadataRefreshStaleSeconds < 1) return "Metadata refresh stale seconds must be 1 or greater.";
        if (x.MetadataRefreshRestartDelaySeconds < 1) return "Metadata refresh restart delay must be 1 or greater.";
        if (x.MetadataResolutionTimeSliceMinutes is < 1 or > 1440) return "Metadata resolution time slice must be between 1 and 1,440 minutes.";
        if (x.PriorityMetadataAttempts is < 1 or > 10) return "Priority metadata attempts must be between 1 and 10.";
        if (x.DownloadNoProgressTimeSliceMinutes is < 1 or > 60) return "Download no-progress time slice must be between 1 and 60 minutes.";
        if (x.AutomaticMetadataResetStuckThresholdSeconds is < 15 or > 300) return "Automatic metadata reset stuck threshold must be between 15 and 300 seconds.";
        if (x.ColdDownloadRecoveryThresholdMinutes < 1) return "Cold-download recovery threshold must be 1 or greater.";
        if (x.ColdDownloadRecoveryIntervalMinutes < 1) return "Cold-download recovery interval must be 1 or greater.";
        if (x.ColdDownloadAbandonAfterHours < 0) return "Cold-download abandonment hours must be 0 or greater.";
        if (x.EngineConnectionFailureLogBurstLimit < 1) return "Connection-failure burst limit must be 1 or greater.";
        if (x.EngineConnectionFailureLogWindowSeconds < 1) return "Connection-failure window must be 1 or greater.";
        if (x.EngineMaximumConnections < 1) return "Maximum connections must be 1 or greater.";
        if (x.EngineMaximumHalfOpenConnections < 1) return "Maximum half-open connections must be 1 or greater.";
        if (x.EngineMaximumDownloadRateBytesPerSecond < 0) return "Maximum download rate must be 0 or greater.";
        if (x.EngineMaximumUploadRateBytesPerSecond < 0) return "Maximum upload rate must be 0 or greater.";
        if (x.CompletionCallbackTimeoutSeconds < 1) return "Callback timeout must be 1 or greater.";
        if (x.CompletionCallbackFinalizationTimeoutSeconds < 1) return "Callback finalization timeout must be 1 or greater.";
        if (x.CompletionCallbackEnabled && string.IsNullOrWhiteSpace(x.CompletionCallbackCommandPath)) return "Command path is required when the completion callback is enabled.";
        if (!Uri.TryCreate(x.VpnEgressValidationEndpoint.Trim(), UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(endpoint.Host) || !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Fragment))
            return "VPN validation endpoint must be an absolute HTTPS URL without credentials or a fragment.";
        if (x.VpnEgressValidationEnabled && string.IsNullOrWhiteSpace(x.VpnEgressDirectIspCidrs)) return "At least one direct ISP IPv4 CIDR is required when VPN validation is enabled.";
        if (x.VpnEgressDegradedCheckIntervalSeconds < 1) return "VPN degraded check interval must be 1 or greater.";
        if (x.VpnEgressReadyCheckIntervalSeconds < 1) return "VPN ready check interval must be 1 or greater.";
        if (x.VpnEgressRequestTimeoutSeconds < 1) return "VPN request timeout must be 1 or greater.";
        if (x.VpnEgressEngineSuspensionTimeoutSeconds < 1) return "VPN engine suspension timeout must be 1 or greater.";
        if (!RecoveryModes.Contains(x.ExpressVpnAutomaticRecoveryMode)) return "VPN automatic recovery mode is invalid.";
        if (x.ExpressVpnRecoveryDelaySeconds < 1) return "VPN recovery delay must be 1 or greater.";
        if (x.ExpressVpnUnavailableLaunchDelaySeconds < 1) return "ExpressVPN unavailable launch delay must be 1 or greater.";
        if (x.VpnEgressRequestTimeoutSeconds >= x.VpnEgressDegradedCheckIntervalSeconds || x.VpnEgressRequestTimeoutSeconds >= x.VpnEgressReadyCheckIntervalSeconds)
            return "VPN request timeout must be shorter than both check intervals.";
        if (x.VpnRecoveryProvider is not ("ExpressVPN" or "PIA")) return "Choose ExpressVPN or PIA as the recovery provider.";
        return null;
    }
}
