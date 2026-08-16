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

    public static readonly SettingHelpContent MetadataResolutionTimeSliceMinutes = new(
        "Metadata Resolution Time Slice Minutes",
        "Limits one unresolved magnet's turn when another magnet is waiting.",
        "After this many minutes, TorrentCore yields the metadata slot to a waiting unresolved magnet. A lone resolver keeps running, never-tried magnets run first, and yielded magnets retry oldest first. The allowed range is 1 through 1,440 minutes and changes apply live."
    );

    public static readonly SettingHelpContent AutomaticMetadataResetStuckThresholdSeconds = new(
        "Automatic Reset Stuck Threshold Seconds",
        "Limits how long an automatic metadata reset may run before isolation.",
        "TorrentCore quarantines a reset that exceeds this threshold so it cannot hold normal synchronization, then applies its reset circuit-breaker rules. The allowed range is 15 through 300 seconds and changes apply live."
    );

    public static readonly SettingHelpContent ColdDownloadRecoveryThresholdMinutes = new(
        "Long-Cold Threshold Minutes",
        "Defines when a continuously inactive download switches from progressive recovery to long-cold recovery.",
        "The default is 120 minutes. The timer resets when TorrentCore observes a connected peer, positive download rate, or downloaded-byte progress. It applies live."
    );

    public static readonly SettingHelpContent ColdDownloadRecoveryIntervalMinutes = new(
        "Long-Cold Recovery Interval Minutes",
        "Limits long-cold downloads to one automatic recovery action per interval.",
        "The default is 60 minutes. TorrentCore alternates a peer refresh and a restart, so the more expensive restart normally occurs every two intervals. Useful download activity immediately returns the torrent to normal recovery cadence. It applies live."
    );

    public static readonly SettingHelpContent ColdDownloadAbandonAfterHours = new(
        "Abandon Cold Download After Hours",
        "Removes a download and deletes its partial payload after continuous inactivity.",
        "The default is 72 hours. TorrentCore retains a history record, deletes torrent-scoped logs, and does not invoke the completion callback. Set this to 0 to disable automatic abandonment. It applies live."
    );

    public static readonly SettingHelpContent EngineConnectionFailureLogBurstLimit = new(
        "Legacy Failure Burst Limit",
        "Retained for settings compatibility; TorrentCore no longer persists individual peer connection failures.",
        "Connection failures are aggregated by torrent and reason in the minute activity summary, so this value no longer controls persistent logging."
    );

    public static readonly SettingHelpContent EngineConnectionFailureLogWindowSeconds = new(
        "Legacy Failure Window",
        "Retained for settings compatibility with the former individual-failure throttle.",
        "Connection failures now appear only in aggregate minute summaries, so this value no longer affects persistent logging."
    );

    public static readonly SettingHelpContent EngineAllowPeerExchange = new(
        "Allow Peer Exchange (PEX)",
        "Allows peers to tell TorrentCore about additional peers in the same swarm.",
        "PEX supplements trackers, DHT, and local peer discovery; disabling it does not disable those other discovery sources. It is disabled by default because MonoTorrent 3.0.2 peer-exchange processing caused the observed unhandled Queue exception. This setting is saved immediately but requires a TorrentCore.Service restart."
    );

    public static readonly SettingHelpContent EngineMaximumConnections = new(
        "Saved Max Connections",
        "Sets the global cap on fully established peer connections across the engine host.",
        "This is not a torrent count. One torrent can use multiple peer sessions, and the total is shared across all torrents. Higher values can improve swarm participation but also increase socket, memory, and CPU usage. This setting is saved immediately but only applies after TorrentCore.Service restarts."
    );

    public static readonly SettingHelpContent EngineEncryptionMode = new(
        "Saved Encryption Mode",
        "Controls whether TorrentCore prefers plaintext, prefers encrypted peers, or requires encryption.",
        "Use PlainTextPreferred for maximum compatibility when unencrypted peers are acceptable. Use EncryptedPreferred to behave more like Transmission's encryption-preferred mode, where TorrentCore tries RC4 first and only falls back to plaintext if needed. Use EncryptedRequired to disable plaintext entirely. This is a MonoTorrent engine setting and requires a service restart to apply."
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

    public static readonly SettingHelpContent VpnEgressValidationEnabled = new(
        "Enable VPN Egress Validation",
        "Controls whether TorrentCore requires verified VPN egress before torrent processing.",
        "When enabled, TorrentCore accepts new magnets while paused but starts torrent processing only after the VPN connection is confirmed. This setting applies live."
    );

    public static readonly SettingHelpContent VpnEgressValidationEndpoint = new(
        "Validation Endpoint",
        "Sets the HTTPS endpoint used to discover TorrentCore's public IPv4 address.",
        "Use an absolute HTTPS URL without embedded credentials or a fragment. Changes apply live without an operator restart."
    );

    public static readonly SettingHelpContent VpnEgressDirectIspCidrs = new(
        "Direct ISP IPv4 CIDRs",
        "Lists public IPv4 ranges that identify direct, non-VPN egress.",
        "Enter one or more comma-separated IPv4 CIDRs. IPv6 ranges are rejected because this installation uses IPv4 for public egress validation. TorrentCore canonicalizes and deduplicates the saved ranges."
    );

    public static readonly SettingHelpContent VpnEgressDegradedCheckIntervalSeconds = new(
        "Degraded Check Interval Seconds",
        "Sets how often TorrentCore rechecks egress while validation is degraded.",
        "The value must be at least one second, and the validation request timeout must be shorter than this interval. Changes apply live."
    );

    public static readonly SettingHelpContent VpnEgressReadyCheckIntervalSeconds = new(
        "Ready Check Interval Seconds",
        "Sets how often TorrentCore revalidates egress while processing is ready.",
        "The value must be at least one second, and the validation request timeout must be shorter than this interval. Changes apply live."
    );

    public static readonly SettingHelpContent VpnEgressRequestTimeoutSeconds = new(
        "Validation Request Timeout Seconds",
        "Limits one public-IP validation request.",
        "The value must be positive and shorter than both validation intervals. Changes apply live."
    );

    public static readonly SettingHelpContent VpnEgressEngineSuspensionTimeoutSeconds = new(
        "Engine Suspension Timeout Seconds",
        "Limits local MonoTorrent draining and teardown after VPN validation fails.",
        "The value must be at least one second. It applies live and does not limit activation or recovery."
    );

    public static readonly SettingHelpContent ExpressVpnAutomaticRecoveryMode = new(
        "ExpressVPN Automatic Recovery",
        "Selects when TorrentCore may ask ExpressVPN to reconnect after VPN egress validation fails.",
        "Disabled performs no ExpressVPN commands. Direct ISP Only requires two consecutive direct-ISP detections. Any Validation Failure allows two consecutive failed validation outcomes. MonoTorrent must be suspended first, and public-IP validation must succeed before processing resumes."
    );

    public static readonly SettingHelpContent ExpressVpnRecoveryDelaySeconds = new(
        "ExpressVPN Recovery Delay Seconds",
        "Sets the shared startup grace period and minimum interval between automatic reconnect cycles.",
        "The default is 180 seconds. This delay does not permit torrent processing while egress is degraded."
    );

    public static readonly SettingHelpContent ExpressVpnUnavailableLaunchDelaySeconds = new(
        "ExpressVPN Unavailable Launch Delay Seconds",
        "Sets how long TorrentCore waits before asking macOS to launch an unavailable ExpressVPN application.",
        "The default is 300 seconds. TorrentCore makes at most two launch requests per degradation episode and keeps MonoTorrent suspended until public egress validates."
    );

    public static readonly SettingHelpContent RuntimeTickDurationSummaryEnabled = new(
        "Performance Timing Summaries",
        "Controls one-minute synchronization timing summaries in the Service log.",
        "This changes only runtime.tick.duration_summary writes. Synchronization, slow-operation logging, failure logging, and torrent work remain unchanged. Summaries are suppressed while torrent processing is paused for the VPN connection."
    );

    public static readonly SettingHelpContent CleanupLogEntries = new(
        "Log Entries",
        "Deletes eligible log entries older than the selected date.",
        "The Service uses local midnight at the start of the selected date as an exclusive cutoff. Logs tied to torrent ids still present in the live torrent table are protected."
    );

    public static readonly SettingHelpContent CleanupHistoryRecords = new(
        "History Records",
        "Deletes eligible history records older than the selected date.",
        "Eligibility uses Last Updated and the Service's local midnight at the start of the selected date. History tied to torrent ids still present in the live torrent table is protected."
    );

    public static readonly SettingHelpContent CleanupOrphanedTorrentLogs = new(
        "Orphaned Torrent Logs",
        "Deletes torrent-scoped logs whose torrent id is no longer live.",
        "This is the same guarded orphan-log maintenance operation available on the Logs screen. Service-level logs and logs for still-tracked torrents are kept."
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
        "Legacy Process Timeout",
        "Retained for settings compatibility; TorrentCore no longer waits for the callback process to finish.",
        "TorrentCore now treats a successful process start as dispatch and immediately waits for independent API feedback. This value no longer limits callback execution and can remain at its existing value."
    );

    public static readonly SettingHelpContent CompletionCallbackFinalizationTimeoutSeconds = new(
        "Finalization Wait Seconds",
        "Limits how long TorrentCore waits for the final visible payload path before giving up on callback finalization.",
        "TorrentCore does not fire the shared callback the moment the engine first reports completion. It waits until the final payload is visible and incomplete-suffix files are no longer the active payload. In the current async TVMaze callback flow, this same timeout budget also covers the follow-up wait for TVMaze to report the final callback result back to TorrentCore."
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
