import Foundation

public struct TorrentCoreHelpContent: Equatable, Hashable, Sendable {
    public let label: String
    public let summary: String
    public let detail: String

    public init(label: String, summary: String, detail: String) {
        self.label = label
        self.summary = summary
        self.detail = detail
    }
}

public enum TorrentCoreHelpCatalog {
    public enum Settings {
        public static let seedingStopMode = help(
            "Seeding Stop Mode",
            "Controls when a completed torrent stops seeding.",
            "Choose unlimited seeding, stop immediately, stop after a ratio, stop after a time window, or stop when either threshold is reached. Changes apply live."
        )
        public static let seedingStopRatio = help(
            "Seeding Stop Ratio",
            "Sets the upload-ratio target used by ratio-based seeding modes.",
            "A value of 1.0 means TorrentCore will try to upload an amount equal to the downloaded payload size before stopping. Changes apply live."
        )
        public static let seedingStopMinutes = help(
            "Seeding Stop Minutes",
            "Sets the duration target used by time-based seeding modes.",
            "TorrentCore compares this value with the completed torrent's seeding duration when the selected policy uses time. Changes apply live."
        )
        public static let completedTorrentCleanupMode = help(
            "Completed Torrent Cleanup Mode",
            "Controls automatic removal of completed torrents from tracking.",
            "Cleanup removes the torrent from TorrentCore's active tracking state after the configured age. It never deletes downloaded payload data."
        )
        public static let completedTorrentCleanupMinutes = help(
            "Completed Torrent Cleanup Minutes",
            "Sets the age window before completed-torrent cleanup.",
            "The delay is measured from completion. TorrentCore also uses this window for optional completed-log deletion."
        )
        public static let deleteLogsForCompletedTorrents = help(
            "Delete Log Entries For Completed Torrents",
            "Deletes torrent-scoped logs after successful completion and the cleanup delay.",
            "This removes only activity-log rows tied to the torrent. It does not delete downloaded data and does not run while callback processing is unresolved."
        )
        public static let maxActiveMetadataResolutions = help(
            "Max Active Metadata Resolutions",
            "Limits magnets actively resolving metadata at one time.",
            "Additional magnets remain accepted and queued until a metadata slot opens. Changes apply live."
        )
        public static let maxActiveDownloads = help(
            "Max Active Downloads",
            "Limits torrents actively downloading at one time.",
            "Resolved torrents above the limit remain queued. This controls execution concurrency rather than API admission and applies live."
        )
        public static let metadataRefreshStaleSeconds = help(
            "Refresh After Seconds",
            "Sets the inactivity window before TorrentCore refreshes discovery.",
            "TorrentCore refreshes DHT and tracker discovery when a magnet or zero-peer download remains idle for this long. Changes apply live."
        )
        public static let metadataRefreshRestartDelaySeconds = help(
            "Restart Delay Seconds",
            "Sets the delay before stale recovery escalates to restart.",
            "TorrentCore first performs a non-destructive discovery refresh, then uses a stop/start recovery path if the torrent remains cold for this additional delay."
        )
        public static let coldDownloadRecoveryThresholdMinutes = help(
            "Long-Cold Threshold Minutes",
            "Sets when an inactive download enters long-cold recovery.",
            "Useful peer activity, positive transfer rate, or downloaded-byte progress resets the timer. Changes apply live."
        )
        public static let coldDownloadRecoveryIntervalMinutes = help(
            "Long-Cold Recovery Interval Minutes",
            "Limits automatic recovery frequency for long-cold downloads.",
            "TorrentCore alternates peer refresh and restart actions, with no more than one automatic action during this interval."
        )
        public static let coldDownloadAbandonAfterHours = help(
            "Abandon Cold Download After Hours",
            "Removes a continuously inactive download and its partial payload.",
            "TorrentCore retains history, deletes torrent-scoped logs, and does not invoke the completion callback. Use 0 to disable automatic abandonment."
        )
        public static let engineConnectionFailureLogBurstLimit = help(
            "Legacy Failure Burst Limit",
            "Retained for settings compatibility.",
            "TorrentCore now aggregates connection failures in minute summaries, so this value no longer controls persistent logging."
        )
        public static let engineConnectionFailureLogWindowSeconds = help(
            "Legacy Failure Window",
            "Retained for settings compatibility.",
            "Connection failures now appear in aggregate minute summaries, so this value no longer affects persistent logging."
        )
        public static let engineAllowPeerExchange = help(
            "Allow Peer Exchange (PEX)",
            "Allows connected peers to supply additional peers for the same swarm.",
            "PEX supplements trackers, DHT, and local peer discovery. It is disabled by default because MonoTorrent 3.0.2 PEX processing caused the observed unhandled Queue exception. A service restart is required."
        )
        public static let engineMaximumConnections = help(
            "Saved Max Connections",
            "Sets the host-wide cap on established peer connections.",
            "The cap is shared across all torrents. Higher values increase socket, memory, and CPU use. A service restart is required."
        )
        public static let engineEncryptionMode = help(
            "Saved Encryption Mode",
            "Controls plaintext and encrypted peer preference.",
            "Plain Text Preferred maximizes compatibility, Encrypted Preferred tries encryption first, and Encrypted Required disables plaintext. A service restart is required."
        )
        public static let engineMaximumHalfOpenConnections = help(
            "Saved Max Half-Open Connections",
            "Sets the cap on outbound peer connections still being established.",
            "Higher values allow more simultaneous attempts but can increase churn and connection-failure noise. A service restart is required."
        )
        public static let engineMaximumDownloadRateBytesPerSecond = help(
            "Saved Max Download Rate",
            "Sets the host-wide download-rate ceiling.",
            "The value applies across all torrents. Use 0 for unlimited. A service restart is required."
        )
        public static let engineMaximumUploadRateBytesPerSecond = help(
            "Saved Max Upload Rate",
            "Sets the host-wide upload-rate ceiling.",
            "The value applies across all torrents. Use 0 for unlimited. A service restart is required."
        )
        public static let completionCallbackEnabled = help(
            "Enable Completion Callback Invocation",
            "Turns the shared completion callback on or off.",
            "When enabled, TorrentCore invokes the configured callback after completion and final payload readiness are confirmed. Changes apply live."
        )
        public static let completionCallbackCommandPath = help(
            "Command Path",
            "Sets the executable or script TorrentCore launches.",
            "Use a full absolute path so callback execution does not depend on shell lookup behavior or the service's launch context."
        )
        public static let completionCallbackArguments = help(
            "Arguments",
            "Sets optional static command-line arguments.",
            "Leave this blank unless the configured callback entrypoint requires additional arguments."
        )
        public static let completionCallbackWorkingDirectory = help(
            "Working Directory",
            "Sets an optional callback working directory.",
            "Leave this blank unless the callback depends on a particular current directory. Use an absolute path when set."
        )
        public static let completionCallbackTimeoutSeconds = help(
            "Legacy Process Timeout",
            "Retained for settings compatibility.",
            "TorrentCore treats successful process start as dispatch and no longer waits for process exit, so this value does not limit callback execution."
        )
        public static let completionCallbackFinalizationTimeoutSeconds = help(
            "Finalization Wait Seconds",
            "Limits the wait for final payload readiness and callback feedback.",
            "TorrentCore waits for the downstream-visible final payload before launching the callback. The same budget covers asynchronous callback feedback."
        )
        public static let completionCallbackAPIBaseURLOverride = help(
            "API Base URL Override",
            "Optionally overrides the API address exposed to the callback.",
            "Leave this blank for the normal centrally managed setup. Set it only when the callback must contact a different TorrentCore API address."
        )
        public static let completionCallbackAPIKeyOverride = help(
            "API Key Override",
            "Optionally overrides the API key exposed to the callback.",
            "Leave this blank for the normal setup. The native app does not persist or log the value entered here."
        )
        public static let categoryEnabled = help(
            "Enabled",
            "Controls whether the category is available for future torrent adds.",
            "Disabling a category does not move or rewrite torrents that were already added with it."
        )
        public static let categoryInvokeCompletionCallback = help(
            "Invoke Callback",
            "Controls callback routing for future torrents in this category.",
            "Existing torrents keep the callback-routing values that were resolved and persisted when they were added."
        )
        public static let categoryDisplayName = help(
            "Display Name",
            "Sets the operator-facing category name.",
            "Changing the display name does not change the stable category key or routing already stored on torrents."
        )
        public static let categoryCallbackLabel = help(
            "Callback Label",
            "Sets the category label passed to the shared callback.",
            "Keep this aligned with downstream routing expectations. Changes affect future torrents only."
        )
        public static let categoryDownloadRootPath = help(
            "Download Root",
            "Sets the download root used for future torrents in this category.",
            "Keep this aligned with downstream routing for the callback label. Existing torrents retain their resolved path."
        )
        public static let categorySortOrder = help(
            "Sort Order",
            "Controls category order in operator-facing lists.",
            "Lower values appear earlier. This does not change the stable category key or routing behavior."
        )
        public static let cleanupLogEntries = help(
            "Log Entries",
            "Deletes eligible log entries older than the selected date.",
            "The Service uses local midnight at the start of the selected date as an exclusive cutoff. Logs tied to torrent ids still present in the live torrent table are protected."
        )
        public static let cleanupHistoryRecords = help(
            "History Records",
            "Deletes eligible history records older than the selected date.",
            "Eligibility uses Last Updated and the Service's local midnight at the start of the selected date. History tied to torrent ids still present in the live torrent table is protected."
        )
        public static let cleanupOrphanedTorrentLogs = help(
            "Orphaned Torrent Logs",
            "Deletes torrent-scoped logs whose torrent id is no longer live.",
            "This is the same guarded orphan-log maintenance operation available on the Logs screen. Service-level logs and logs for still-tracked torrents are kept."
        )

        public static let all: [TorrentCoreHelpContent] = [
            seedingStopMode, seedingStopRatio, seedingStopMinutes,
            completedTorrentCleanupMode, completedTorrentCleanupMinutes,
            deleteLogsForCompletedTorrents, maxActiveMetadataResolutions,
            maxActiveDownloads, metadataRefreshStaleSeconds,
            metadataRefreshRestartDelaySeconds, coldDownloadRecoveryThresholdMinutes,
            coldDownloadRecoveryIntervalMinutes, coldDownloadAbandonAfterHours,
            engineConnectionFailureLogBurstLimit, engineConnectionFailureLogWindowSeconds,
            engineAllowPeerExchange, engineMaximumConnections, engineEncryptionMode,
            engineMaximumHalfOpenConnections, engineMaximumDownloadRateBytesPerSecond,
            engineMaximumUploadRateBytesPerSecond, completionCallbackEnabled,
            completionCallbackCommandPath, completionCallbackArguments,
            completionCallbackWorkingDirectory, completionCallbackTimeoutSeconds,
            completionCallbackFinalizationTimeoutSeconds,
            completionCallbackAPIBaseURLOverride, completionCallbackAPIKeyOverride,
            categoryEnabled, categoryInvokeCompletionCallback, categoryDisplayName,
            categoryCallbackLabel, categoryDownloadRootPath, categorySortOrder,
            cleanupLogEntries, cleanupHistoryRecords, cleanupOrphanedTorrentLogs,
        ]
    }

    public enum Torrents {
        public static let filters = help(
            "Filters",
            "Narrows the locally loaded torrent list.",
            "Name, state, and category filters apply locally. Clear resets the inputs without changing any torrents."
        )
        public static let name = help(
            "Name",
            "Filters torrents by name text.",
            "Matching is case-insensitive and applies to the currently loaded torrent list."
        )
        public static let state = help(
            "State",
            "Filters torrents by lifecycle state.",
            "Choose a TorrentCore state to isolate resolving, queued, downloading, seeding, paused, completed, error, or removed torrents."
        )
        public static let category = help(
            "Category",
            "Filters torrents by stored category.",
            "Choose a routing category or Uncategorized. Filtering applies to the currently loaded list."
        )
        public static let autoRefresh = help(
            "Auto Refresh",
            "Controls automatic refresh for the active view.",
            "The app refreshes only the visible context at the selected interval while it is in the foreground."
        )
        public static let currentTorrents = help(
            "Current Torrents",
            "Shows the filtered torrent table.",
            "Sort with column headers, page through results, and select one torrent to open its inspector."
        )
        public static let selectedTorrent = help(
            "Selected Torrent",
            "Shows details and actions for one torrent.",
            "The inspector contains transfer details, callback status, diagnostics, and actions for the selected torrent."
        )
        public static let pause = help(
            "Pause",
            "Stops active transfer work for the selected torrent.",
            "TorrentCore retains the torrent in tracking state so it can be resumed later."
        )
        public static let resume = help(
            "Resume",
            "Returns a paused torrent to normal processing.",
            "TorrentCore places the selected torrent back into metadata, download, or seeding flow as appropriate."
        )
        public static let refreshMetadata = help(
            "Refresh Metadata",
            "Requests a non-destructive peer and metadata refresh.",
            "Use this for a stale magnet or weak swarm before using the stronger reset action."
        )
        public static let resetMetadata = help(
            "Reset Metadata",
            "Recreates metadata discovery for the selected torrent.",
            "This is the stronger recovery step and should normally follow an unsuccessful metadata refresh."
        )
        public static let retryCallback = help(
            "Retry Callback",
            "Requeues an unsuccessful completion callback.",
            "This retries callback processing without forcing a payload download."
        )
        public static let logDetails = help(
            "Log Details",
            "Opens Logs filtered to the selected torrent.",
            "Use this to inspect the torrent's activity without copying its identifier."
        )
        public static let peers = help(
            "Peers",
            "Shows live peer diagnostics.",
            "Inspect client identities, direction, seeder status, and live transfer rates."
        )
        public static let trackers = help(
            "Trackers",
            "Shows tracker diagnostics.",
            "Inspect tracker tiers, status, announce and scrape results, and failures."
        )
        public static let remove = help(
            "Remove",
            "Removes the torrent from tracking without deleting payload data.",
            "The torrent leaves the current list, while downloaded files remain on disk."
        )
        public static let deleteData = help(
            "Delete Data",
            "Removes the torrent and deletes its payload.",
            "This is destructive and deletes both TorrentCore tracking state and downloaded files."
        )
    }

    public enum History {
        public static let filters = help(
            "Filters",
            "Controls which history rows TorrentCore requests.",
            "Search applies the submitted date, torrent name, category, state, and outcome fields. Clear reloads unfiltered history."
        )
        public static let fromDate = help(
            "Submitted From Date",
            "Includes torrents submitted on or after this date.",
            "The start date is inclusive. Abandoned-outcome searches intentionally ignore submitted-date bounds."
        )
        public static let toDate = help(
            "Submitted To Date",
            "Includes torrents submitted on or before this date.",
            "The end date is inclusive. Abandoned-outcome searches intentionally ignore submitted-date bounds."
        )
        public static let torrentName = help(
            "Torrent Name",
            "Filters history by torrent name.",
            "Matching is case-insensitive and uses contains semantics."
        )
        public static let category = help(
            "Category",
            "Filters history by category text.",
            "Matching is case-insensitive and uses contains semantics."
        )
        public static let state = help(
            "State",
            "Filters history by its last recorded torrent lifecycle state.",
            "States such as Downloading, Completed, or Seeding describe what the torrent was doing. Use Outcome to find records that were removed or abandoned."
        )
        public static let outcome = help(
            "Outcome",
            "Filters active, removed, or abandoned history.",
            "Use Removed for manually removed history regardless of its last lifecycle state. Abandoned identifies cold downloads removed automatically and ignores submitted-date bounds."
        )
        public static let results = help(
            "History Results",
            "Shows the current history result set.",
            "Sort with table headers, page through results, and select one row to inspect its stored lifecycle record."
        )
        public static let selectedEntry = help(
            "Selected History Entry",
            "Shows the full stored history record.",
            "The inspector includes lifecycle timestamps, callback state, removal outcome, paths, and the latest stored torrent summary."
        )
    }

    public enum Logs {
        public static let filters = help(
            "Filters",
            "Narrows the displayed log set.",
            "Level, category, event, torrent, service-instance, and date filters are sent to TorrentCore.Service. Search text applies locally to the returned rows."
        )
        public static let searchMessage = help(
            "Search Message",
            "Filters log messages and event names.",
            "Matching is case-insensitive and applies to the currently loaded rows."
        )
        public static let level = help(
            "Level",
            "Filters logs to one severity.",
            "Choose Debug, Information, Warning, Error, or Critical, or use All to include every level."
        )
        public static let category = help(
            "Category",
            "Filters logs by category text.",
            "Matching is case-insensitive and uses contains semantics."
        )
        public static let eventType = help(
            "Event Type",
            "Filters logs by the stored event name.",
            "Use this server-side filter when you know the event type you want to isolate. Matching uses the service's event-type filter."
        )
        public static let torrentID = help(
            "Torrent ID",
            "Filters logs to one torrent GUID.",
            "Use a valid torrent identifier to follow one torrent through metadata, download, callback, and cleanup activity."
        )
        public static let serviceInstanceID = help(
            "Service Instance ID",
            "Filters logs to one TorrentCore.Service process instance.",
            "Use the instance GUID to isolate activity recorded by one service lifetime. Leave it blank to include every instance."
        )
        public static let recentLimit = help(
            "Recent Rows",
            "Sets how many recent log rows the service returns.",
            "Raise the limit when older activity is needed, or narrow the other server filters to keep the result focused."
        )
        public static let fromDateTime = help(
            "From (Local Date/Time)",
            "Sets the inclusive lower time bound.",
            "The app converts the local date and time to UTC before filtering."
        )
        public static let toDateTime = help(
            "To (Local Date/Time)",
            "Sets the inclusive upper time bound.",
            "The app converts the local date and time to UTC before filtering."
        )
        public static let deleteOrphaned = help(
            "Delete Orphan Logs",
            "Deletes logs for torrents no longer tracked.",
            "Service-level logs and logs belonging to currently tracked torrents are retained."
        )
        public static let recentActivity = help(
            "Recent Activity",
            "Shows the filtered log table.",
            "Sort with table headers, page through results, and select one row to inspect its complete details."
        )
        public static let selectedEntry = help(
            "Selected Log Entry",
            "Shows all fields for the selected log row.",
            "The inspector contains identifiers, timestamps, category, event, message, and details JSON."
        )
    }

    public enum Connection {
        public static let currentEndpoint = help(
            "Current Connection",
            "Shows the active native-client connection and latest status.",
            "The active connection is stored on this device. Reachability reflects the latest request and can change when the service or network path changes."
        )
        public static let serviceBaseURL = help(
            "Service Address",
            "Sets the TorrentCore.Service HTTP base address.",
            "Enter the full HTTP address, including the service port. Use a host name or LAN/VPN IP address this device can reach."
        )
        public static let test = help(
            "Test Connection",
            "Tests the entered address without saving it.",
            "Use this to verify reachability before replacing or activating a saved connection."
        )
        public static let save = help(
            "Save & Connect",
            "Saves the connection on this device and makes it active.",
            "This changes only the native client's saved profiles. TorrentCore.Service and TorrentCore.WebUI configuration are not changed."
        )
        public static let restartService = help(
            "Restart Service",
            "Requests a service restart through the TorrentCore API.",
            "The service may be unavailable briefly while launchd restarts it. The app waits for recovery."
        )
        public static let recheck = help(
            "Refresh",
            "Retests the active saved connection.",
            "Use Refresh after a service restart or network recovery. It does not change the saved address."
        )
    }

    private static func help(
        _ label: String,
        _ summary: String,
        _ detail: String
    ) -> TorrentCoreHelpContent {
        TorrentCoreHelpContent(label: label, summary: summary, detail: detail)
    }
}
