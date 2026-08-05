import Foundation

public struct TorrentCoreTorrentState: RawRepresentable, Codable, Hashable, Sendable {
    public let rawValue: String

    public init(rawValue: String) {
        self.rawValue = rawValue
    }

    public static let resolvingMetadata = Self(rawValue: "ResolvingMetadata")
    public static let queued = Self(rawValue: "Queued")
    public static let downloading = Self(rawValue: "Downloading")
    public static let seeding = Self(rawValue: "Seeding")
    public static let waitingForFileCompletion = Self(rawValue: "WaitingForFileCompletion")
    public static let paused = Self(rawValue: "Paused")
    public static let completed = Self(rawValue: "Completed")
    public static let error = Self(rawValue: "Error")
    public static let removed = Self(rawValue: "Removed")
}

public struct TorrentCoreWaitReason: RawRepresentable, Codable, Hashable, Sendable {
    public let rawValue: String

    public init(rawValue: String) {
        self.rawValue = rawValue
    }

    public static let pendingMetadataDispatch = Self(rawValue: "PendingMetadataDispatch")
    public static let waitingForMetadataSlot = Self(rawValue: "WaitingForMetadataSlot")
    public static let pendingDownloadDispatch = Self(rawValue: "PendingDownloadDispatch")
    public static let waitingForDownloadSlot = Self(rawValue: "WaitingForDownloadSlot")
    public static let waitingForFileCompletion = Self(rawValue: "WaitingForFileCompletion")
    public static let pausedByOperator = Self(rawValue: "PausedByOperator")
    public static let blockedByError = Self(rawValue: "BlockedByError")
}

public struct TorrentCoreHostState: RawRepresentable, Codable, Hashable, Sendable {
    public let rawValue: String

    public init(rawValue: String) {
        self.rawValue = rawValue
    }

    public static let starting = Self(rawValue: "Starting")
    public static let ready = Self(rawValue: "Ready")
    public static let degraded = Self(rawValue: "Degraded")
    public static let stopped = Self(rawValue: "Stopped")
    public static let faulted = Self(rawValue: "Faulted")
}

public struct TorrentCoreServiceHealth: Codable, Hashable, Sendable {
    public var apiVersion: Int?
    public var serviceName: String?
    public var status: String?
    public var environmentName: String?
    public var checkedAt: Date

    public init(
        apiVersion: Int?,
        serviceName: String?,
        status: String?,
        environmentName: String?,
        checkedAt: Date
    ) {
        self.apiVersion = apiVersion
        self.serviceName = serviceName
        self.status = status
        self.environmentName = environmentName
        self.checkedAt = checkedAt
    }
}

public struct TorrentCoreHostStatus: Codable, Hashable, Sendable {
    public var apiVersion: Int?
    public var serviceName: String?
    public var serviceVersion: String?
    public var serviceBuild: String?
    public var serviceInstanceID: UUID?
    public var engineRuntime: String?
    public var engineListenPort: Int
    public var engineDHTPort: Int
    public var enginePortForwardingEnabled: Bool
    public var engineLocalPeerDiscoveryEnabled: Bool
    public var engineAllowPeerExchange: Bool
    public var engineEncryptionMode: String?
    public var engineMaximumConnections: Int
    public var engineMaximumHalfOpenConnections: Int
    public var engineMaximumDownloadRateBytesPerSecond: Int
    public var engineMaximumUploadRateBytesPerSecond: Int
    public var engineConnectionFailureLogBurstLimit: Int
    public var engineConnectionFailureLogWindowSeconds: Int
    public var maxActiveMetadataResolutions: Int
    public var maxActiveDownloads: Int
    public var availableMetadataResolutionSlots: Int
    public var availableDownloadSlots: Int
    public var resolvingMetadataCount: Int
    public var metadataQueueCount: Int
    public var downloadingCount: Int
    public var downloadQueueCount: Int
    public var seedingCount: Int
    public var pausedCount: Int
    public var completedCount: Int
    public var errorCount: Int
    public var currentConnectedPeerCount: Int
    public var currentDownloadRateBytesPerSecond: Int64
    public var currentUploadRateBytesPerSecond: Int64
    public var partialFilesEnabled: Bool
    public var partialFileSuffix: String?
    public var seedingStopMode: String?
    public var seedingStopRatio: Double
    public var seedingStopMinutes: Int
    public var completedTorrentCleanupMode: String?
    public var completedTorrentCleanupMinutes: Int
    public var deleteLogsForCompletedTorrents: Bool
    public var status: TorrentCoreHostState
    public var environmentName: String?
    public var downloadRootPath: String?
    public var torrentCount: Int
    public var supportsMagnetAdds: Bool
    public var supportsPause: Bool
    public var supportsResume: Bool
    public var supportsRemove: Bool
    public var supportsPersistentStorage: Bool
    public var supportsMultiHost: Bool
    public var startupRecoveryCompleted: Bool
    public var startupRecoveredTorrentCount: Int
    public var startupNormalizedTorrentCount: Int
    public var startupRecoveryCompletedAt: Date?
    public var checkedAt: Date
}

public struct TorrentCoreLifecycleEvent: Codable, Hashable, Sendable {
    public var category: String?
    public var eventType: String?
    public var level: String?
    public var message: String?
    public var occurredAt: Date
    public var torrentID: UUID?

    public init(
        category: String?,
        eventType: String?,
        level: String?,
        message: String?,
        occurredAt: Date,
        torrentID: UUID?
    ) {
        self.category = category
        self.eventType = eventType
        self.level = level
        self.message = message
        self.occurredAt = occurredAt
        self.torrentID = torrentID
    }
}

public struct TorrentCoreDashboardLifecycle: Codable, Hashable, Sendable {
    public var callbackFailedCount: Int
    public var callbackInvokedCount: Int
    public var callbackTimedOutCount: Int
    public var completedAutoRemovedCount: Int
    public var firstEventAt: Date?
    public var lastEventAt: Date?
    public var metadataRefreshRequestedCount: Int
    public var metadataResetRequestedCount: Int
    public var metadataResolvedCount: Int
    public var metadataRestartRequestedCount: Int
    public var orphanedTorrentLogsDeletedCount: Int
    public var recentEvents: [TorrentCoreLifecycleEvent]
    public var recoveryCompletedAt: Date?
    public var serviceInstanceID: UUID?
    public var startupNormalizedTorrentCount: Int
    public var startupReadyAt: Date?
    public var startupRecoveredTorrentCount: Int
    public var torrentsAddedCount: Int
    public var torrentsRemovedCount: Int
}

public struct TorrentCoreTorrentSummary: Codable, Hashable, Sendable {
    public var addedAt: Date
    public var canPause: Bool
    public var canRefreshMetadata: Bool
    public var canRemove: Bool
    public var canResume: Bool
    public var canRetryCompletionCallback: Bool
    public var categoryKey: String?
    public var completedAt: Date?
    public var completionCallbackInvokedAt: Date?
    public var completionCallbackLastError: String?
    public var completionCallbackPendingSince: Date?
    public var completionCallbackState: String?
    public var connectedPeerCount: Int
    public var downloadRateBytesPerSecond: Int64
    public var downloadedBytes: Int64
    public var errorMessage: String?
    public var lastActivityAt: Date?
    public var name: String?
    public var progressPercent: Double
    public var queuePosition: Int?
    public var state: TorrentCoreTorrentState
    public var torrentID: UUID?
    public var totalBytes: Int64?
    public var trackerCount: Int
    public var uploadRateBytesPerSecond: Int64
    public var waitReason: TorrentCoreWaitReason?
}

public struct TorrentCoreCompletionCallbackFeedback: Codable, Hashable, Sendable {
    public var allowResubmit: Bool
    public var attemptCount: Int
    public var callbackFinished: Bool
    public var callbackLocalTimestamp: Date?
    public var callbackMachine: String?
    public var callbackSource: String?
    public var completionTimestamp: Date?
    public var contractVersion: String?
    public var correlationID: String?
    public var detailMessage: String?
    public var displayMessage: String?
    public var finalState: String?
    public var mediaConsideredDone: Bool
    public var needsManualIntervention: Bool
    public var rawResponseJSON: String?
    public var reasonCode: String?
    public var receivedAt: Date
    public var recommendedAction: String?
    public var resubmitAdvice: String?
    public var sourceState: String?
    public var torrentHash: String?
    public var torrentID: UUID?
}

public struct TorrentCoreTorrentDetail: Codable, Hashable, Sendable {
    public var addedAt: Date
    public var canPause: Bool
    public var canRefreshMetadata: Bool
    public var canRemove: Bool
    public var canResume: Bool
    public var canRetryCompletionCallback: Bool
    public var categoryKey: String?
    public var completedAt: Date?
    public var completionCallbackFeedback: TorrentCoreCompletionCallbackFeedback?
    public var completionCallbackFinalPayloadPath: String?
    public var completionCallbackInvokedAt: Date?
    public var completionCallbackLastError: String?
    public var completionCallbackPendingReason: String?
    public var completionCallbackPendingSince: Date?
    public var completionCallbackState: String?
    public var connectedPeerCount: Int
    public var downloadRateBytesPerSecond: Int64
    public var downloadedBytes: Int64
    public var errorMessage: String?
    public var infoHash: String?
    public var lastActivityAt: Date?
    public var magnetURI: String?
    public var name: String?
    public var progressPercent: Double
    public var queuePosition: Int?
    public var savePath: String?
    public var state: TorrentCoreTorrentState
    public var torrentID: UUID?
    public var totalBytes: Int64?
    public var trackerCount: Int
    public var uploadRateBytesPerSecond: Int64
    public var waitReason: TorrentCoreWaitReason?
}

public struct TorrentCoreCategory: Codable, Hashable, Sendable {
    public var callbackLabel: String?
    public var displayName: String?
    public var downloadRootPath: String?
    public var enabled: Bool
    public var invokeCompletionCallback: Bool
    public var key: String?
    public var sortOrder: Int
}

public struct TorrentCoreActionResult: Codable, Hashable, Sendable {
    public var action: String?
    public var dataDeleted: Bool?
    public var processedAt: Date
    public var state: TorrentCoreTorrentState
    public var torrentID: UUID?

    public init(
        action: String?,
        dataDeleted: Bool?,
        processedAt: Date,
        state: TorrentCoreTorrentState,
        torrentID: UUID?
    ) {
        self.action = action
        self.dataDeleted = dataDeleted
        self.processedAt = processedAt
        self.state = state
        self.torrentID = torrentID
    }
}

public struct TorrentCoreServiceProblem: Codable, Hashable, Sendable {
    public var type: String?
    public var title: String?
    public var status: Int?
    public var detail: String?
    public var instance: String?
    public var code: String?
    public var target: String?
    public var traceID: String?
    public var errors: [String: [String]]
}
