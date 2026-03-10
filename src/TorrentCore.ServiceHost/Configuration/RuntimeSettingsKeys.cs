namespace TorrentCore.Service.Configuration;

public static class RuntimeSettingsKeys
{
    public const string SeedingStopMode = "seeding_stop_mode";
    public const string SeedingStopRatio = "seeding_stop_ratio";
    public const string SeedingStopMinutes = "seeding_stop_minutes";
    public const string CompletedTorrentCleanupMode = "completed_torrent_cleanup_mode";
    public const string CompletedTorrentCleanupMinutes = "completed_torrent_cleanup_minutes";
    public const string EngineConnectionFailureLogBurstLimit = "engine_connection_failure_log_burst_limit";
    public const string EngineConnectionFailureLogWindowSeconds = "engine_connection_failure_log_window_seconds";
    public const string EngineMaximumConnections = "engine_maximum_connections";
    public const string EngineMaximumHalfOpenConnections = "engine_maximum_half_open_connections";
    public const string EngineMaximumDownloadRateBytesPerSecond = "engine_maximum_download_rate_bytes_per_second";
    public const string EngineMaximumUploadRateBytesPerSecond = "engine_maximum_upload_rate_bytes_per_second";
    public const string MaxActiveMetadataResolutions = "max_active_metadata_resolutions";
    public const string MaxActiveDownloads = "max_active_downloads";
}
