namespace TorrentCore.Service.Configuration;

public static class RuntimeSettingsKeys
{
    public const string SeedingStopMode                         = "seeding_stop_mode";
    public const string SeedingStopRatio                        = "seeding_stop_ratio";
    public const string SeedingStopMinutes                      = "seeding_stop_minutes";
    public const string CompletedTorrentCleanupMode             = "completed_torrent_cleanup_mode";
    public const string CompletedTorrentCleanupMinutes          = "completed_torrent_cleanup_minutes";
    public const string DeleteLogsForCompletedTorrents          = "delete_logs_for_completed_torrents";
    public const string EngineConnectionFailureLogBurstLimit    = "engine_connection_failure_log_burst_limit";
    public const string EngineConnectionFailureLogWindowSeconds = "engine_connection_failure_log_window_seconds";
    public const string EngineAllowPeerExchange                  = "engine_allow_peer_exchange";
    public const string EngineEncryptionMode                    = "engine_encryption_mode";
    public const string EngineMaximumConnections                = "engine_maximum_connections";
    public const string EngineMaximumHalfOpenConnections        = "engine_maximum_half_open_connections";
    public const string EngineMaximumDownloadRateBytesPerSecond = "engine_maximum_download_rate_bytes_per_second";
    public const string EngineMaximumUploadRateBytesPerSecond   = "engine_maximum_upload_rate_bytes_per_second";
    public const string MaxActiveMetadataResolutions            = "max_active_metadata_resolutions";
    public const string MaxActiveDownloads                      = "max_active_downloads";
    public const string MetadataRefreshStaleSeconds             = "metadata_refresh_stale_seconds";
    public const string MetadataRefreshRestartDelaySeconds      = "metadata_refresh_restart_delay_seconds";
    public const string MetadataResolutionTimeSliceMinutes      = "metadata_resolution_time_slice_minutes";
    public const string AutomaticMetadataResetStuckThresholdSeconds =
            "automatic_metadata_reset_stuck_threshold_seconds";
    public const string ColdDownloadRecoveryThresholdMinutes    = "cold_download_recovery_threshold_minutes";
    public const string ColdDownloadRecoveryIntervalMinutes     = "cold_download_recovery_interval_minutes";
    public const string ColdDownloadAbandonAfterHours           = "cold_download_abandon_after_hours";
    public const string CompletionCallbackEnabled               = "completion_callback_enabled";
    public const string CompletionCallbackCommandPath           = "completion_callback_command_path";
    public const string CompletionCallbackArguments             = "completion_callback_arguments";
    public const string CompletionCallbackWorkingDirectory      = "completion_callback_working_directory";
    public const string CompletionCallbackTimeoutSeconds        = "completion_callback_timeout_seconds";
    public const string CompletionCallbackFinalizationTimeoutSeconds =
            "completion_callback_finalization_timeout_seconds";
    public const string CompletionCallbackApiBaseUrlOverride = "completion_callback_api_base_url_override";
    public const string CompletionCallbackApiKeyOverride     = "completion_callback_api_key_override";
    public const string VpnEgressValidationEnabled = "vpn_egress_validation_enabled";
    public const string VpnEgressValidationEndpoint = "vpn_egress_validation_endpoint";
    public const string VpnEgressDirectIspCidrs = "vpn_egress_direct_isp_cidrs";
    public const string VpnEgressDegradedCheckIntervalSeconds = "vpn_egress_degraded_check_interval_seconds";
    public const string VpnEgressReadyCheckIntervalSeconds = "vpn_egress_ready_check_interval_seconds";
    public const string VpnEgressRequestTimeoutSeconds = "vpn_egress_request_timeout_seconds";
    public const string VpnEgressEngineSuspensionTimeoutSeconds =
            "vpn_egress_engine_suspension_timeout_seconds";
    public const string ExpressVpnAutomaticRecoveryMode = "expressvpn_automatic_recovery_mode";
    public const string ExpressVpnRecoveryDelaySeconds = "expressvpn_recovery_delay_seconds";
    public const string ExpressVpnUnavailableLaunchDelaySeconds =
            "expressvpn_unavailable_launch_delay_seconds";
    public const string RuntimeTickDurationSummaryEnabled = "runtime_tick_duration_summary_enabled";
}
