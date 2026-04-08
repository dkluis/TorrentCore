namespace TorrentCore.WebUI.Components.Pages;

public sealed record SettingHelpContent(string Label, string Summary, string Detail);

public static class SettingsHelpCatalog
{
    public static readonly SettingHelpContent SeedingStopMode = new(
        "Seeding Stop Mode",
        "Controls when a completed torrent stops seeding.",
        "This is the rule TorrentCore uses after a torrent completes. Choose unlimited seeding, stop immediately, stop after a ratio, stop after a time window, or stop when either threshold is reached. It applies live, so changing it affects future seeding decisions without restarting the service."
    );

    public static readonly SettingHelpContent SeedingStopRatio = new(
        "Seeding Stop Ratio",
        "Sets the upload ratio target used by ratio-based seeding modes.",
        "Use this when the selected seeding mode depends on ratio. A value of 1.0 means TorrentCore will try to upload an amount equal to the downloaded payload size before stopping. It is a live policy value and does not require a restart."
    );

    public static readonly SettingHelpContent SeedingStopMinutes = new(
        "Seeding Stop Minutes",
        "Sets the seeding duration target used by time-based seeding modes.",
        "Use this when the selected seeding mode depends on elapsed seeding time. TorrentCore compares this minute value against the completed torrent's seeding duration and stops when the active time-based rule is satisfied. It applies live."
    );

    public static readonly SettingHelpContent CompletedTorrentCleanupMode = new(
        "Completed Torrent Cleanup Mode",
        "Controls whether TorrentCore automatically removes completed torrents from its own tracking list.",
        "This setting only affects TorrentCore tracking state. It does not delete downloaded data. If cleanup is enabled, TorrentCore removes the torrent from the UI and internal state after the configured completion-age window is reached. It applies live."
    );

    public static readonly SettingHelpContent CompletedTorrentCleanupMinutes = new(
        "Completed Torrent Cleanup Minutes",
        "Sets the age window TorrentCore waits before running completed-torrent cleanup.",
        "This delay is measured from the completed time. TorrentCore also reuses the same window for optional completed-log deletion when that toggle is enabled, so this value controls both cleanup timing and post-completion log-pruning timing. It applies live."
    );

    public static readonly SettingHelpContent DeleteLogsForCompletedTorrents = new(
        "Delete Log Entries For Completed Torrents",
        "Deletes torrent-scoped activity logs after a successful completed torrent ages past the cleanup window.",
        "This removes only `activity_logs` rows tied to that torrent id. It does not delete downloaded data, and it does not run while the completion callback is still pending, failed, or timed out. If automatic completed-torrent removal is also enabled, TorrentCore removes the torrent from tracking first and then clears that torrent's log history in the same cleanup pass."
    );

    public static readonly SettingHelpContent MaxActiveMetadataResolutions = new(
        "Max Active Metadata Resolutions",
        "Limits how many magnets can actively resolve metadata at the same time.",
        "New magnets are still accepted and persisted immediately. When this limit is full, extra unresolved magnets wait in queue until a metadata slot opens. Raise it to resolve more magnets in parallel; lower it to reduce concurrent metadata-session load. It applies live."
    );

    public static readonly SettingHelpContent MaxActiveDownloads = new(
        "Max Active Downloads",
        "Limits how many torrents can actively download at the same time.",
        "Resolved torrents above this limit stay queued until a download slot opens. This controls execution concurrency, not API admission. Raise it for more simultaneous download activity, or lower it to constrain bandwidth, peer churn, and disk activity. It applies live."
    );

    public static readonly SettingHelpContent MetadataRefreshStaleSeconds = new(
        "Refresh After Seconds",
        "Defines how long TorrentCore waits before it considers a magnet or zero-peer download stale.",
        "When the idle window reaches this value, TorrentCore asks MonoTorrent to refresh discovery through DHT and forced tracker announces. Lower values make recovery more aggressive, while higher values give weak swarms more time before TorrentCore intervenes. It applies live."
    );

    public static readonly SettingHelpContent MetadataRefreshRestartDelaySeconds = new(
        "Restart Delay Seconds",
        "Defines how long TorrentCore waits after a stale refresh before escalating to a stronger recovery step.",
        "TorrentCore first tries a non-destructive discovery refresh. If the torrent is still cold after this additional delay, TorrentCore escalates to a stop/start recovery path and fresh peer discovery. Lower values recover faster but can create more churn for slow swarms. It applies live."
    );

    public static readonly SettingHelpContent EngineConnectionFailureLogBurstLimit = new(
        "Connection Failure Burst Limit",
        "Caps how many repeated connection-failure events TorrentCore logs before it starts suppressing duplicates.",
        "This keeps the activity log from filling with hundreds of near-identical warnings when many peers are unreachable. Raise it if you want more repeated failure visibility; lower it if the log is too noisy. It applies live."
    );

    public static readonly SettingHelpContent EngineConnectionFailureLogWindowSeconds = new(
        "Connection Failure Window Seconds",
        "Sets the time window used with the burst limit for connection-failure log suppression.",
        "TorrentCore groups repeated identical connection failures inside this window before allowing them to appear again. A longer window suppresses noise more aggressively, while a shorter window lets repeated failures show up again sooner. It applies live."
    );

    public static readonly SettingHelpContent EngineMaximumConnections = new(
        "Saved Max Connections",
        "Sets the global cap on fully established peer connections across the engine host.",
        "This is not a torrent count. One torrent can use multiple peer sessions, and the total is shared across all torrents. Higher values can improve swarm participation but also increase socket, memory, and CPU usage. This setting is saved immediately but only applies after TorrentCore.Service restarts."
    );

    public static readonly SettingHelpContent EngineMaximumHalfOpenConnections = new(
        "Saved Max Half-Open Connections",
        "Sets the global cap on outbound peer connection attempts that are still in progress.",
        "These are sessions TorrentCore is still trying to establish and that are not fully connected yet. Higher values let the engine fan out to more new peers at once, but they can also increase churn and connection-failure noise. This setting requires a service restart to apply."
    );

    public static readonly SettingHelpContent EngineMaximumDownloadRateBytesPerSecond = new(
        "Saved Max Download Rate",
        "Sets the global download-throughput ceiling for the engine host.",
        "This is a host-wide receive cap across all torrents combined, not a per-torrent limit. Use 0 for unlimited. TorrentCore measures this as network payload throughput seen by the engine, not disk write speed or final file growth. This setting requires a service restart to apply."
    );

    public static readonly SettingHelpContent EngineMaximumUploadRateBytesPerSecond = new(
        "Saved Max Upload Rate",
        "Sets the global upload-throughput ceiling for the engine host.",
        "This is a host-wide send cap across all torrents combined, not a per-torrent limit. Use 0 for unlimited. TorrentCore measures this as network upload throughput seen by the engine, not disk read speed. This setting requires a service restart to apply."
    );

    public static readonly SettingHelpContent CompletionCallbackEnabled = new(
        "Enable Completion Callback Invocation",
        "Turns the shared TVMaze-style completion callback on or off for TorrentCore.",
        "When enabled, TorrentCore invokes the configured shared callback entrypoint after a torrent completes and downstream-visible finalization is confirmed. When disabled, TorrentCore completes the torrent lifecycle without launching the external callback process. This setting applies live."
    );

    public static readonly SettingHelpContent CompletionCallbackCommandPath = new(
        "Command Path",
        "The full executable or script path TorrentCore launches for the shared completion callback.",
        "In the normal setup this points to the shared TVMaze callback launcher script. TorrentCore uses this command together with the existing Transmission-style environment variables it prepares for the callback. Keep it as a full absolute path so service restarts and different launch contexts do not depend on shell lookup behavior."
    );

    public static readonly SettingHelpContent CompletionCallbackArguments = new(
        "Arguments",
        "Optional command-line arguments passed to the callback process.",
        "Most operators can leave this blank in the standard shared TVMaze callback setup. Use it only when the launcher script or executable requires additional static arguments. This changes how TorrentCore starts the callback process, so keep it aligned with the actual callback entrypoint."
    );

    public static readonly SettingHelpContent CompletionCallbackWorkingDirectory = new(
        "Working Directory",
        "Optional working directory used when TorrentCore launches the callback process.",
        "Leave this blank for the normal setup unless the callback script depends on a specific current directory to resolve relative files or additional tools. Use an absolute path if you set it so callback execution is stable regardless of service startup context."
    );

    public static readonly SettingHelpContent CompletionCallbackTimeoutSeconds = new(
        "Process Timeout Seconds",
        "Limits how long TorrentCore waits for the callback process itself to finish.",
        "If the external callback process runs longer than this, TorrentCore marks the callback attempt as timed out. This timeout is about the launched process duration, not the time TorrentCore waits for file finalization before it starts the callback."
    );

    public static readonly SettingHelpContent CompletionCallbackFinalizationTimeoutSeconds = new(
        "Finalization Wait Seconds",
        "Limits how long TorrentCore waits for the final visible payload path before giving up on callback finalization.",
        "TorrentCore does not fire the shared callback the moment the engine first reports completion. It waits until the final payload is visible and incomplete-suffix files are no longer the active payload. If that finalization window exceeds this value, TorrentCore marks the callback path as timed out."
    );

    public static readonly SettingHelpContent CompletionCallbackApiBaseUrlOverride = new(
        "API Base URL Override",
        "Optional override for the API base URL exposed to the callback environment.",
        "Leave this blank for the normal centrally managed setup. Use it only if the shared callback needs to target a different API base URL than the default runtime context would provide."
    );

    public static readonly SettingHelpContent CompletionCallbackApiKeyOverride = new(
        "API Key Override",
        "Optional override for the API key exposed to the callback environment.",
        "Leave this blank for the normal centrally managed setup. Use it only when the shared callback must authenticate with a different API key than the default runtime context."
    );

    public static readonly SettingHelpContent CategoryEnabled = new(
        "Enabled",
        "Controls whether the category is available for future torrent adds.",
        "Disabled categories remain in configuration for reference, but operators should not use them for new intake. Changing this does not move or rewrite existing torrents that were already added with that category."
    );

    public static readonly SettingHelpContent CategoryInvokeCompletionCallback = new(
        "Invoke Callback",
        "Controls whether torrents added under this category are configured to invoke the shared completion callback.",
        "This only affects future torrents that resolve their routing from this category. Existing torrents keep the callback-routing values that were resolved and persisted when they were added."
    );

    public static readonly SettingHelpContent CategoryDisplayName = new(
        "Display Name",
        "The operator-facing name shown for the category in the UI.",
        "Use this to make the category readable in add dialogs, filters, and lists. Changing it affects how the category is presented to operators but does not change the category key used by clients or saved torrent routing."
    );

    public static readonly SettingHelpContent CategoryCallbackLabel = new(
        "Callback Label",
        "The stable category label TorrentCore passes to the shared callback boundary for future torrents in this category.",
        "This should stay aligned with the downstream TVMaze or shared-callback route expectations. Changing it affects future torrents only, because the resolved callback label is persisted on each torrent when it is added."
    );

    public static readonly SettingHelpContent CategoryDownloadRootPath = new(
        "Download Root",
        "The root directory TorrentCore resolves for future torrents added under this category.",
        "This path should stay aligned with the downstream route expectations for the same callback label/category. Changing it affects future torrents only; existing torrents keep the resolved download root that was persisted when they were added."
    );

    public static readonly SettingHelpContent CategorySortOrder = new(
        "Sort Order",
        "Controls how the category is ordered in operator-facing lists and selectors.",
        "Lower values appear earlier. Use this to keep the most common intake categories near the top without changing their stable keys or routing behavior."
    );
}
