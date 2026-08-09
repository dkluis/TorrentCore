import Foundation

extension TorrentCoreHistoryFilterOptions {
    init(_ value: Components.Schemas.TorrentHistoryFilterOptionsDto) {
        categoryKeys = value.categoryKeys ?? []
        states = value.states ?? []
    }
}

extension TorrentCoreHistorySummary {
    init(_ value: Components.Schemas.TorrentHistorySummaryDto) {
        categoryKey = value.categoryKey
        completionCallbackFinalResult = value.completionCallbackFinalResult
        dataDeleted = value.dataDeleted
        downloadCompletedAt = value.downloadCompletedAt
        downloadRootPath = value.downloadRootPath
        downloadStartedAt = value.downloadStartedAt
        infoHash = value.infoHash
        lastActivityAt = value.lastActivityAt
        lastUpdatedAt = value.lastUpdatedAt
        latestCallbackStatus = value.latestCallbackStatus
        latestConnectedPeerCount = Int(value.latestConnectedPeerCount)
        latestDownloadRateBytesPerSecond = value.latestDownloadRateBytesPerSecond
        latestDownloadedBytes = value.latestDownloadedBytes
        latestErrorMessage = value.latestErrorMessage
        latestProgressPercent = value.latestProgressPercent
        latestTorrentState = value.latestTorrentState
        latestTotalBytes = value.latestTotalBytes
        latestTrackerCount = Int(value.latestTrackerCount)
        latestUploadRateBytesPerSecond = value.latestUploadRateBytesPerSecond
        latestUploadedBytes = value.latestUploadedBytes
        latestWaitReason = value.latestWaitReason
        metadataResolvedAt = value.metadataResolvedAt
        name = value.name
        outcome = .init(rawValue: value.outcome)
        removalKind = value.removalKind.map(TorrentCoreRemovalKind.init(rawValue:))
        removalReason = value.removalReason
        removedAt = value.removedAt
        removedByCleanupPolicy = value.removedByCleanupPolicy
        seedingStartedAt = value.seedingStartedAt
        submittedAt = value.submittedAt
        torrentID = UUID(uuidString: value.torrentId)
    }
}

extension TorrentCoreHistoryDetail {
    init(_ value: Components.Schemas.TorrentHistoryDetailDto) {
        callbackCompletedAt = value.callbackCompletedAt
        callbackLastError = value.callbackLastError
        callbackStartedAt = value.callbackStartedAt
        categoryKey = value.categoryKey
        completionCallbackFeedback = value.completionCallbackFeedback.map(
            TorrentCoreCompletionCallbackFeedback.init
        )
        completionCallbackLabel = value.completionCallbackLabel
        dataDeleted = value.dataDeleted
        downloadCompletedAt = value.downloadCompletedAt
        downloadRootPath = value.downloadRootPath
        downloadStartedAt = value.downloadStartedAt
        finalPayloadPath = value.finalPayloadPath
        infoHash = value.infoHash
        invokeCompletionCallback = value.invokeCompletionCallback
        lastActivityAt = value.lastActivityAt
        lastUpdatedAt = value.lastUpdatedAt
        latestCallbackStatus = value.latestCallbackStatus
        latestConnectedPeerCount = Int(value.latestConnectedPeerCount)
        latestDownloadRateBytesPerSecond = value.latestDownloadRateBytesPerSecond
        latestDownloadedBytes = value.latestDownloadedBytes
        latestErrorMessage = value.latestErrorMessage
        latestProgressPercent = value.latestProgressPercent
        latestTorrentState = value.latestTorrentState
        latestTotalBytes = value.latestTotalBytes
        latestTrackerCount = Int(value.latestTrackerCount)
        latestUploadRateBytesPerSecond = value.latestUploadRateBytesPerSecond
        latestUploadedBytes = value.latestUploadedBytes
        latestWaitReason = value.latestWaitReason
        magnetURI = value.magnetUri
        metadataResolvedAt = value.metadataResolvedAt
        name = value.name
        outcome = .init(rawValue: value.outcome)
        removalKind = value.removalKind.map(TorrentCoreRemovalKind.init(rawValue:))
        removalReason = value.removalReason
        removedAt = value.removedAt
        removedByCleanupPolicy = value.removedByCleanupPolicy
        seedingStartedAt = value.seedingStartedAt
        serviceInstanceIDLastSeen = value.serviceInstanceIdLastSeen.flatMap(UUID.init(uuidString:))
        submittedAt = value.submittedAt
        torrentID = UUID(uuidString: value.torrentId)
    }
}

extension TorrentCoreActivityLogEntry {
    init(_ value: Components.Schemas.ActivityLogEntryDto) {
        category = value.category
        detailsJSON = value.detailsJson
        eventType = value.eventType
        level = value.level
        logEntryID = value.logEntryId
        message = value.message
        occurredAt = value.occurredAtUtc
        serviceInstanceID = value.serviceInstanceId.flatMap(UUID.init(uuidString:))
        torrentID = value.torrentId.flatMap(UUID.init(uuidString:))
        traceID = value.traceId
    }
}

extension TorrentCoreActivityLogFilterOptions {
    init(_ value: Components.Schemas.ActivityLogFilterOptionsDto) {
        categories = value.categories ?? []
        eventTypes = value.eventTypes ?? []
    }
}

extension TorrentCorePeer {
    init(_ value: Components.Schemas.TorrentPeerDto) {
        client = value.client
        direction = value.direction
        downloadRateBytesPerSecond = value.downloadRateBytesPerSecond
        downloadedBytes = value.downloadedBytes
        encryption = value.encryption
        endpoint = value.endpoint
        isConnected = value.isConnected
        isSeeder = value.isSeeder
        uploadRateBytesPerSecond = value.uploadRateBytesPerSecond
        uploadedBytes = value.uploadedBytes
    }
}

extension TorrentCoreTracker {
    init(_ value: Components.Schemas.TorrentTrackerDto) {
        canAnnounce = value.canAnnounce
        canScrape = value.canScrape
        failureMessage = value.failureMessage
        isActive = value.isActive
        lastAnnounceSucceeded = value.lastAnnounceSucceeded
        lastScrapeSucceeded = value.lastScrapeSucceeded
        status = value.status
        tierNumber = Int(value.tierNumber)
        timeSinceLastAnnounceSeconds = value.timeSinceLastAnnounceSeconds
        timeSinceLastScrapeSeconds = value.timeSinceLastScrapeSeconds
        trackerNumber = Int(value.trackerNumber)
        warningMessage = value.warningMessage
    }
}

extension TorrentCoreRuntimeSettings {
    init(_ value: Components.Schemas.RuntimeSettingsDto) {
        appliedEngineAllowPeerExchange = value.appliedEngineAllowPeerExchange
        appliedEngineEncryptionMode = value.appliedEngineEncryptionMode
        appliedEngineMaximumConnections = Int(value.appliedEngineMaximumConnections)
        appliedEngineMaximumDownloadRateBytesPerSecond = Int(
            value.appliedEngineMaximumDownloadRateBytesPerSecond
        )
        appliedEngineMaximumHalfOpenConnections = Int(value.appliedEngineMaximumHalfOpenConnections)
        appliedEngineMaximumUploadRateBytesPerSecond = Int(
            value.appliedEngineMaximumUploadRateBytesPerSecond
        )
        automaticMetadataResetStuckThresholdSeconds = Int(
            value.automaticMetadataResetStuckThresholdSeconds ?? 30
        )
        coldDownloadAbandonAfterHours = Int(value.coldDownloadAbandonAfterHours ?? 72)
        coldDownloadRecoveryIntervalMinutes = Int(value.coldDownloadRecoveryIntervalMinutes)
        coldDownloadRecoveryThresholdMinutes = Int(value.coldDownloadRecoveryThresholdMinutes)
        completedTorrentCleanupMinutes = Int(value.completedTorrentCleanupMinutes)
        completedTorrentCleanupMode = value.completedTorrentCleanupMode
        completionCallbackAPIBaseURLOverride = value.completionCallbackApiBaseUrlOverride
        completionCallbackAPIKeyOverride = value.completionCallbackApiKeyOverride
        completionCallbackArguments = value.completionCallbackArguments
        completionCallbackCommandPath = value.completionCallbackCommandPath
        completionCallbackEnabled = value.completionCallbackEnabled
        completionCallbackFinalizationTimeoutSeconds = Int(
            value.completionCallbackFinalizationTimeoutSeconds
        )
        completionCallbackTimeoutSeconds = Int(value.completionCallbackTimeoutSeconds)
        completionCallbackWorkingDirectory = value.completionCallbackWorkingDirectory
        deleteLogsForCompletedTorrents = value.deleteLogsForCompletedTorrents
        engineConnectionFailureLogBurstLimit = Int(value.engineConnectionFailureLogBurstLimit)
        engineConnectionFailureLogWindowSeconds = Int(value.engineConnectionFailureLogWindowSeconds)
        engineAllowPeerExchange = value.engineAllowPeerExchange
        engineEncryptionMode = value.engineEncryptionMode
        engineMaximumConnections = Int(value.engineMaximumConnections)
        engineMaximumDownloadRateBytesPerSecond = Int(value.engineMaximumDownloadRateBytesPerSecond)
        engineMaximumHalfOpenConnections = Int(value.engineMaximumHalfOpenConnections)
        engineMaximumUploadRateBytesPerSecond = Int(value.engineMaximumUploadRateBytesPerSecond)
        engineRuntime = value.engineRuntime
        engineSettingsRequireRestart = value.engineSettingsRequireRestart
        maxActiveDownloads = Int(value.maxActiveDownloads)
        maxActiveMetadataResolutions = Int(value.maxActiveMetadataResolutions)
        metadataRefreshRestartDelaySeconds = Int(value.metadataRefreshRestartDelaySeconds)
        metadataRefreshStaleSeconds = Int(value.metadataRefreshStaleSeconds)
        metadataResolutionTimeSliceMinutes = Int(value.metadataResolutionTimeSliceMinutes ?? 15)
        partialFileSuffix = value.partialFileSuffix
        partialFilesEnabled = value.partialFilesEnabled
        retrievedAt = value.retrievedAtUtc
        seedingStopMinutes = Int(value.seedingStopMinutes)
        seedingStopMode = value.seedingStopMode
        seedingStopRatio = value.seedingStopRatio
        supportsLiveUpdates = value.supportsLiveUpdates
        updatedAt = value.updatedAtUtc
        usesPersistedOverrides = value.usesPersistedOverrides
        vpnEgressDegradedCheckIntervalSeconds = Int(
            value.vpnEgressDegradedCheckIntervalSeconds ?? 60
        )
        vpnEgressDirectIspCidrs = value.vpnEgressDirectIspCidrs ?? ["47.0.0.0/8"]
        vpnEgressReadyCheckIntervalSeconds = Int(value.vpnEgressReadyCheckIntervalSeconds ?? 240)
        vpnEgressRequestTimeoutSeconds = Int(value.vpnEgressRequestTimeoutSeconds ?? 10)
        vpnEgressValidationEnabled = value.vpnEgressValidationEnabled ?? false
        vpnEgressValidationEndpoint = value.vpnEgressValidationEndpoint
            ?? "https://api.ipify.org?format=json"
    }
}

extension TorrentCoreServiceRestartResult {
    init(_ value: Components.Schemas.ServiceRestartRequestResultDto) {
        message = value.message
        requestedAt = value.requestedAtUtc
        serviceLabel = value.serviceLabel
    }
}

extension Components.Schemas.UpdateRuntimeSettingsRequest {
    init(_ value: TorrentCoreRuntimeSettingsUpdate) {
        self.init(
            automaticMetadataResetStuckThresholdSeconds: Int32(
                value.automaticMetadataResetStuckThresholdSeconds
            ),
            coldDownloadAbandonAfterHours: Int32(value.coldDownloadAbandonAfterHours),
            coldDownloadRecoveryIntervalMinutes: Int32(value.coldDownloadRecoveryIntervalMinutes),
            coldDownloadRecoveryThresholdMinutes: Int32(value.coldDownloadRecoveryThresholdMinutes),
            completedTorrentCleanupMinutes: Int32(value.completedTorrentCleanupMinutes),
            completedTorrentCleanupMode: value.completedTorrentCleanupMode,
            completionCallbackApiBaseUrlOverride: value.completionCallbackAPIBaseURLOverride,
            completionCallbackApiKeyOverride: value.completionCallbackAPIKeyOverride,
            completionCallbackArguments: value.completionCallbackArguments,
            completionCallbackCommandPath: value.completionCallbackCommandPath,
            completionCallbackEnabled: value.completionCallbackEnabled,
            completionCallbackFinalizationTimeoutSeconds: Int32(
                value.completionCallbackFinalizationTimeoutSeconds
            ),
            completionCallbackTimeoutSeconds: Int32(value.completionCallbackTimeoutSeconds),
            completionCallbackWorkingDirectory: value.completionCallbackWorkingDirectory,
            deleteLogsForCompletedTorrents: value.deleteLogsForCompletedTorrents,
            engineAllowPeerExchange: value.engineAllowPeerExchange,
            engineConnectionFailureLogBurstLimit: Int32(value.engineConnectionFailureLogBurstLimit),
            engineConnectionFailureLogWindowSeconds: Int32(value.engineConnectionFailureLogWindowSeconds),
            engineEncryptionMode: value.engineEncryptionMode,
            engineMaximumConnections: Int32(value.engineMaximumConnections),
            engineMaximumDownloadRateBytesPerSecond: Int32(value.engineMaximumDownloadRateBytesPerSecond),
            engineMaximumHalfOpenConnections: Int32(value.engineMaximumHalfOpenConnections),
            engineMaximumUploadRateBytesPerSecond: Int32(value.engineMaximumUploadRateBytesPerSecond),
            maxActiveDownloads: Int32(value.maxActiveDownloads),
            maxActiveMetadataResolutions: Int32(value.maxActiveMetadataResolutions),
            metadataRefreshRestartDelaySeconds: Int32(value.metadataRefreshRestartDelaySeconds),
            metadataRefreshStaleSeconds: Int32(value.metadataRefreshStaleSeconds),
            metadataResolutionTimeSliceMinutes: Int32(value.metadataResolutionTimeSliceMinutes),
            seedingStopMinutes: Int32(value.seedingStopMinutes),
            seedingStopMode: value.seedingStopMode,
            seedingStopRatio: value.seedingStopRatio,
            vpnEgressDegradedCheckIntervalSeconds: Int32(
                value.vpnEgressDegradedCheckIntervalSeconds
            ),
            vpnEgressDirectIspCidrs: value.vpnEgressDirectIspCidrs,
            vpnEgressReadyCheckIntervalSeconds: Int32(value.vpnEgressReadyCheckIntervalSeconds),
            vpnEgressRequestTimeoutSeconds: Int32(value.vpnEgressRequestTimeoutSeconds),
            vpnEgressValidationEnabled: value.vpnEgressValidationEnabled,
            vpnEgressValidationEndpoint: value.vpnEgressValidationEndpoint
        )
    }
}

extension Components.Schemas.UpdateTorrentCategoryRequest {
    init(_ value: TorrentCoreCategoryUpdate) {
        self.init(
            callbackLabel: value.callbackLabel,
            displayName: value.displayName,
            downloadRootPath: value.downloadRootPath,
            enabled: value.enabled,
            invokeCompletionCallback: value.invokeCompletionCallback,
            sortOrder: Int32(value.sortOrder)
        )
    }
}
