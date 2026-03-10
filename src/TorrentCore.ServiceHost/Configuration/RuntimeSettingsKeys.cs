namespace TorrentCore.Service.Configuration;

public static class RuntimeSettingsKeys
{
    public const string SeedingStopMode = "seeding_stop_mode";
    public const string SeedingStopRatio = "seeding_stop_ratio";
    public const string SeedingStopMinutes = "seeding_stop_minutes";
    public const string EngineConnectionFailureLogBurstLimit = "engine_connection_failure_log_burst_limit";
    public const string EngineConnectionFailureLogWindowSeconds = "engine_connection_failure_log_window_seconds";
}
