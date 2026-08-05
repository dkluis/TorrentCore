import Foundation

public struct TorrentCoreHistoryOutcome: RawRepresentable, Codable, Hashable, Sendable {
    public let rawValue: String

    public init(rawValue: String) {
        self.rawValue = rawValue
    }

    public static let active = Self(rawValue: "Active")
    public static let removed = Self(rawValue: "Removed")
    public static let abandoned = Self(rawValue: "Abandoned")
}

public struct TorrentCoreRemovalKind: RawRepresentable, Codable, Hashable, Sendable {
    public let rawValue: String

    public init(rawValue: String) {
        self.rawValue = rawValue
    }

    public static let manualRemoval = Self(rawValue: "ManualRemoval")
    public static let manualRemovalWithData = Self(rawValue: "ManualRemovalWithData")
    public static let completedTorrentCleanup = Self(rawValue: "CompletedTorrentCleanup")
    public static let coldDownloadAbandonment = Self(rawValue: "ColdDownloadAbandonment")
}

public struct TorrentCoreHistoryQuery: Hashable, Sendable {
    public var torrentName: String?
    public var categoryKey: String?
    public var state: String?
    public var outcome: TorrentCoreHistoryOutcome?
    public var removed: Bool?
    public var fromDate: String?
    public var toDate: String?
    public var take: Int?

    public init(
        torrentName: String? = nil,
        categoryKey: String? = nil,
        state: String? = nil,
        outcome: TorrentCoreHistoryOutcome? = nil,
        removed: Bool? = nil,
        fromDate: String? = nil,
        toDate: String? = nil,
        take: Int? = nil
    ) {
        self.torrentName = torrentName
        self.categoryKey = categoryKey
        self.state = state
        self.outcome = outcome
        self.removed = removed
        self.fromDate = fromDate
        self.toDate = toDate
        self.take = take
    }
}

public struct TorrentCoreHistoryFilterOptions: Codable, Hashable, Sendable {
    public var categoryKeys: [String]
    public var states: [String]

    public init(categoryKeys: [String], states: [String]) {
        self.categoryKeys = categoryKeys
        self.states = states
    }
}

public struct TorrentCoreHistorySummary: Codable, Hashable, Sendable, Identifiable {
    public var categoryKey: String?
    public var completionCallbackFinalResult: String?
    public var dataDeleted: Bool
    public var downloadCompletedAt: Date?
    public var downloadRootPath: String?
    public var downloadStartedAt: Date?
    public var infoHash: String?
    public var lastActivityAt: Date?
    public var lastUpdatedAt: Date
    public var latestCallbackStatus: String?
    public var latestConnectedPeerCount: Int
    public var latestDownloadRateBytesPerSecond: Int64
    public var latestDownloadedBytes: Int64
    public var latestErrorMessage: String?
    public var latestProgressPercent: Double
    public var latestTorrentState: String?
    public var latestTotalBytes: Int64?
    public var latestTrackerCount: Int
    public var latestUploadRateBytesPerSecond: Int64
    public var latestUploadedBytes: Int64
    public var latestWaitReason: String?
    public var metadataResolvedAt: Date?
    public var name: String?
    public var outcome: TorrentCoreHistoryOutcome
    public var removalKind: TorrentCoreRemovalKind?
    public var removalReason: String?
    public var removedAt: Date?
    public var removedByCleanupPolicy: Bool
    public var seedingStartedAt: Date?
    public var submittedAt: Date
    public var torrentID: UUID?

    public var id: String {
        torrentID?.uuidString ?? "history|\(submittedAt.timeIntervalSince1970)|\(name ?? "")"
    }
}

public struct TorrentCoreHistoryDetail: Codable, Hashable, Sendable {
    public var callbackCompletedAt: Date?
    public var callbackLastError: String?
    public var callbackStartedAt: Date?
    public var categoryKey: String?
    public var completionCallbackFeedback: TorrentCoreCompletionCallbackFeedback?
    public var completionCallbackLabel: String?
    public var dataDeleted: Bool
    public var downloadCompletedAt: Date?
    public var downloadRootPath: String?
    public var downloadStartedAt: Date?
    public var finalPayloadPath: String?
    public var infoHash: String?
    public var invokeCompletionCallback: Bool
    public var lastActivityAt: Date?
    public var lastUpdatedAt: Date
    public var latestCallbackStatus: String?
    public var latestConnectedPeerCount: Int
    public var latestDownloadRateBytesPerSecond: Int64
    public var latestDownloadedBytes: Int64
    public var latestErrorMessage: String?
    public var latestProgressPercent: Double
    public var latestTorrentState: String?
    public var latestTotalBytes: Int64?
    public var latestTrackerCount: Int
    public var latestUploadRateBytesPerSecond: Int64
    public var latestUploadedBytes: Int64
    public var latestWaitReason: String?
    public var magnetURI: String?
    public var metadataResolvedAt: Date?
    public var name: String?
    public var outcome: TorrentCoreHistoryOutcome
    public var removalKind: TorrentCoreRemovalKind?
    public var removalReason: String?
    public var removedAt: Date?
    public var removedByCleanupPolicy: Bool
    public var seedingStartedAt: Date?
    public var serviceInstanceIDLastSeen: UUID?
    public var submittedAt: Date
    public var torrentID: UUID?
}

public struct TorrentCoreActivityLogLevel: RawRepresentable, Codable, Hashable, Sendable {
    public let rawValue: Int32

    public init(rawValue: Int32) {
        self.rawValue = rawValue
    }

    public static let debug = Self(rawValue: 0)
    public static let information = Self(rawValue: 1)
    public static let warning = Self(rawValue: 2)
    public static let error = Self(rawValue: 3)
    public static let critical = Self(rawValue: 4)
}

public struct TorrentCoreActivityLogFilterOptions: Codable, Hashable, Sendable {
    public var categories: [String]
    public var eventTypes: [String]

    public init(categories: [String], eventTypes: [String]) {
        self.categories = categories
        self.eventTypes = eventTypes
    }
}

public struct TorrentCoreLogQuery: Hashable, Sendable {
    public var take: Int
    public var level: TorrentCoreActivityLogLevel?
    public var category: String?
    public var eventType: String?
    public var torrentID: UUID?
    public var serviceInstanceID: UUID?
    public var fromUTC: Date?
    public var toUTC: Date?

    public init(
        take: Int = 1_000,
        level: TorrentCoreActivityLogLevel? = nil,
        category: String? = nil,
        eventType: String? = nil,
        torrentID: UUID? = nil,
        serviceInstanceID: UUID? = nil,
        fromUTC: Date? = nil,
        toUTC: Date? = nil
    ) {
        self.take = take
        self.level = level
        self.category = category
        self.eventType = eventType
        self.torrentID = torrentID
        self.serviceInstanceID = serviceInstanceID
        self.fromUTC = fromUTC
        self.toUTC = toUTC
    }
}

public struct TorrentCoreActivityLogEntry: Codable, Hashable, Sendable, Identifiable {
    public var category: String?
    public var detailsJSON: String?
    public var eventType: String?
    public var level: String?
    public var logEntryID: Int64
    public var message: String?
    public var occurredAt: Date
    public var serviceInstanceID: UUID?
    public var torrentID: UUID?
    public var traceID: String?

    public var id: Int64 { logEntryID }
}

public struct TorrentCoreDeleteOrphanedLogsResult: Codable, Hashable, Sendable {
    public var deletedLogEntryCount: Int

    public init(deletedLogEntryCount: Int) {
        self.deletedLogEntryCount = deletedLogEntryCount
    }
}

public struct TorrentCoreCleanupResult: Codable, Hashable, Sendable {
    public var upToDate: String
    public var cutoffUTC: Date
    public var deletedRecordCount: Int

    public init(upToDate: String, cutoffUTC: Date, deletedRecordCount: Int) {
        self.upToDate = upToDate
        self.cutoffUTC = cutoffUTC
        self.deletedRecordCount = deletedRecordCount
    }
}

public struct TorrentCorePeer: Codable, Hashable, Sendable, Identifiable {
    public var client: String?
    public var direction: String?
    public var downloadRateBytesPerSecond: Int64
    public var downloadedBytes: Int64
    public var encryption: String?
    public var endpoint: String?
    public var isConnected: Bool
    public var isSeeder: Bool
    public var uploadRateBytesPerSecond: Int64
    public var uploadedBytes: Int64

    public var id: String {
        "\(endpoint ?? "unknown")|\(direction ?? "unknown")|\(client ?? "unknown")"
    }
}

public struct TorrentCoreTracker: Codable, Hashable, Sendable, Identifiable {
    public var canAnnounce: Bool?
    public var canScrape: Bool
    public var failureMessage: String?
    public var isActive: Bool
    public var lastAnnounceSucceeded: Bool?
    public var lastScrapeSucceeded: Bool?
    public var status: String?
    public var tierNumber: Int
    public var timeSinceLastAnnounceSeconds: Int64?
    public var timeSinceLastScrapeSeconds: Int64?
    public var trackerNumber: Int
    public var warningMessage: String?

    public var id: String { "\(tierNumber)|\(trackerNumber)" }
}

public struct TorrentCoreRuntimeSettings: Codable, Hashable, Sendable {
    public var appliedEngineAllowPeerExchange: Bool
    public var appliedEngineEncryptionMode: String?
    public var appliedEngineMaximumConnections: Int
    public var appliedEngineMaximumDownloadRateBytesPerSecond: Int
    public var appliedEngineMaximumHalfOpenConnections: Int
    public var appliedEngineMaximumUploadRateBytesPerSecond: Int
    public var automaticMetadataResetStuckThresholdSeconds: Int
    public var coldDownloadAbandonAfterHours: Int
    public var coldDownloadRecoveryIntervalMinutes: Int
    public var coldDownloadRecoveryThresholdMinutes: Int
    public var completedTorrentCleanupMinutes: Int
    public var completedTorrentCleanupMode: String?
    public var completionCallbackAPIBaseURLOverride: String?
    public var completionCallbackAPIKeyOverride: String?
    public var completionCallbackArguments: String?
    public var completionCallbackCommandPath: String?
    public var completionCallbackEnabled: Bool
    public var completionCallbackFinalizationTimeoutSeconds: Int
    public var completionCallbackTimeoutSeconds: Int
    public var completionCallbackWorkingDirectory: String?
    public var deleteLogsForCompletedTorrents: Bool
    public var engineConnectionFailureLogBurstLimit: Int
    public var engineConnectionFailureLogWindowSeconds: Int
    public var engineAllowPeerExchange: Bool
    public var engineEncryptionMode: String?
    public var engineMaximumConnections: Int
    public var engineMaximumDownloadRateBytesPerSecond: Int
    public var engineMaximumHalfOpenConnections: Int
    public var engineMaximumUploadRateBytesPerSecond: Int
    public var engineRuntime: String?
    public var engineSettingsRequireRestart: Bool
    public var maxActiveDownloads: Int
    public var maxActiveMetadataResolutions: Int
    public var metadataRefreshRestartDelaySeconds: Int
    public var metadataRefreshStaleSeconds: Int
    public var metadataResolutionTimeSliceMinutes: Int
    public var partialFileSuffix: String?
    public var partialFilesEnabled: Bool
    public var retrievedAt: Date
    public var seedingStopMinutes: Int
    public var seedingStopMode: String?
    public var seedingStopRatio: Double
    public var supportsLiveUpdates: Bool
    public var updatedAt: Date?
    public var usesPersistedOverrides: Bool
}

public struct TorrentCoreRuntimeSettingsUpdate: Codable, Hashable, Sendable {
    public var automaticMetadataResetStuckThresholdSeconds: Int
    public var coldDownloadAbandonAfterHours: Int
    public var coldDownloadRecoveryIntervalMinutes: Int
    public var coldDownloadRecoveryThresholdMinutes: Int
    public var completedTorrentCleanupMinutes: Int
    public var completedTorrentCleanupMode: String
    public var completionCallbackAPIBaseURLOverride: String?
    public var completionCallbackAPIKeyOverride: String?
    public var completionCallbackArguments: String?
    public var completionCallbackCommandPath: String?
    public var completionCallbackEnabled: Bool
    public var completionCallbackFinalizationTimeoutSeconds: Int
    public var completionCallbackTimeoutSeconds: Int
    public var completionCallbackWorkingDirectory: String?
    public var deleteLogsForCompletedTorrents: Bool
    public var engineConnectionFailureLogBurstLimit: Int
    public var engineConnectionFailureLogWindowSeconds: Int
    public var engineAllowPeerExchange: Bool
    public var engineEncryptionMode: String
    public var engineMaximumConnections: Int
    public var engineMaximumDownloadRateBytesPerSecond: Int
    public var engineMaximumHalfOpenConnections: Int
    public var engineMaximumUploadRateBytesPerSecond: Int
    public var maxActiveDownloads: Int
    public var maxActiveMetadataResolutions: Int
    public var metadataRefreshRestartDelaySeconds: Int
    public var metadataRefreshStaleSeconds: Int
    public var metadataResolutionTimeSliceMinutes: Int
    public var seedingStopMinutes: Int
    public var seedingStopMode: String
    public var seedingStopRatio: Double

    public init(settings: TorrentCoreRuntimeSettings) {
        automaticMetadataResetStuckThresholdSeconds = settings.automaticMetadataResetStuckThresholdSeconds
        engineAllowPeerExchange = settings.engineAllowPeerExchange
        coldDownloadAbandonAfterHours = settings.coldDownloadAbandonAfterHours
        coldDownloadRecoveryIntervalMinutes = settings.coldDownloadRecoveryIntervalMinutes
        coldDownloadRecoveryThresholdMinutes = settings.coldDownloadRecoveryThresholdMinutes
        completedTorrentCleanupMinutes = settings.completedTorrentCleanupMinutes
        completedTorrentCleanupMode = settings.completedTorrentCleanupMode ?? ""
        completionCallbackAPIBaseURLOverride = settings.completionCallbackAPIBaseURLOverride
        completionCallbackAPIKeyOverride = settings.completionCallbackAPIKeyOverride
        completionCallbackArguments = settings.completionCallbackArguments
        completionCallbackCommandPath = settings.completionCallbackCommandPath
        completionCallbackEnabled = settings.completionCallbackEnabled
        completionCallbackFinalizationTimeoutSeconds = settings.completionCallbackFinalizationTimeoutSeconds
        completionCallbackTimeoutSeconds = settings.completionCallbackTimeoutSeconds
        completionCallbackWorkingDirectory = settings.completionCallbackWorkingDirectory
        deleteLogsForCompletedTorrents = settings.deleteLogsForCompletedTorrents
        engineConnectionFailureLogBurstLimit = settings.engineConnectionFailureLogBurstLimit
        engineConnectionFailureLogWindowSeconds = settings.engineConnectionFailureLogWindowSeconds
        engineEncryptionMode = settings.engineEncryptionMode ?? ""
        engineMaximumConnections = settings.engineMaximumConnections
        engineMaximumDownloadRateBytesPerSecond = settings.engineMaximumDownloadRateBytesPerSecond
        engineMaximumHalfOpenConnections = settings.engineMaximumHalfOpenConnections
        engineMaximumUploadRateBytesPerSecond = settings.engineMaximumUploadRateBytesPerSecond
        maxActiveDownloads = settings.maxActiveDownloads
        maxActiveMetadataResolutions = settings.maxActiveMetadataResolutions
        metadataRefreshRestartDelaySeconds = settings.metadataRefreshRestartDelaySeconds
        metadataRefreshStaleSeconds = settings.metadataRefreshStaleSeconds
        metadataResolutionTimeSliceMinutes = settings.metadataResolutionTimeSliceMinutes
        seedingStopMinutes = settings.seedingStopMinutes
        seedingStopMode = settings.seedingStopMode ?? ""
        seedingStopRatio = settings.seedingStopRatio
    }
}

public struct TorrentCoreCategoryUpdate: Codable, Hashable, Sendable {
    public var callbackLabel: String
    public var displayName: String
    public var downloadRootPath: String
    public var enabled: Bool
    public var invokeCompletionCallback: Bool
    public var sortOrder: Int

    public init(category: TorrentCoreCategory) {
        callbackLabel = category.callbackLabel ?? ""
        displayName = category.displayName ?? ""
        downloadRootPath = category.downloadRootPath ?? ""
        enabled = category.enabled
        invokeCompletionCallback = category.invokeCompletionCallback
        sortOrder = category.sortOrder
    }
}

public struct TorrentCoreServiceRestartResult: Codable, Hashable, Sendable {
    public var message: String?
    public var requestedAt: Date
    public var serviceLabel: String?
}
