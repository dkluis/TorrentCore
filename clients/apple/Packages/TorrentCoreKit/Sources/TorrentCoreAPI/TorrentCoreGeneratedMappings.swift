import Foundation

extension TorrentCoreServiceHealth {
    init(_ value: Components.Schemas.ServiceHealthDto) {
        self.init(
            apiVersion: value.apiVersion.map(Int.init),
            serviceName: value.serviceName,
            status: value.status,
            environmentName: value.environmentName,
            checkedAt: value.checkedAtUtc
        )
    }
}

extension TorrentCoreHostStatus {
    init(_ value: Components.Schemas.EngineHostStatusDto) {
        apiVersion = value.apiVersion.map(Int.init)
        serviceName = value.serviceName
        serviceVersion = value.serviceVersion
        serviceBuild = value.serviceBuild
        serviceInstanceID = UUID(uuidString: value.serviceInstanceId)
        engineRuntime = value.engineRuntime
        engineListenPort = Int(value.engineListenPort)
        engineDHTPort = Int(value.engineDhtPort)
        enginePortForwardingEnabled = value.enginePortForwardingEnabled
        engineLocalPeerDiscoveryEnabled = value.engineLocalPeerDiscoveryEnabled
        engineAllowPeerExchange = value.engineAllowPeerExchange
        engineEncryptionMode = value.engineEncryptionMode
        engineMaximumConnections = Int(value.engineMaximumConnections)
        engineMaximumHalfOpenConnections = Int(value.engineMaximumHalfOpenConnections)
        engineMaximumDownloadRateBytesPerSecond = Int(value.engineMaximumDownloadRateBytesPerSecond)
        engineMaximumUploadRateBytesPerSecond = Int(value.engineMaximumUploadRateBytesPerSecond)
        engineConnectionFailureLogBurstLimit = Int(value.engineConnectionFailureLogBurstLimit)
        engineConnectionFailureLogWindowSeconds = Int(value.engineConnectionFailureLogWindowSeconds)
        maxActiveMetadataResolutions = Int(value.maxActiveMetadataResolutions)
        maxActiveDownloads = Int(value.maxActiveDownloads)
        availableMetadataResolutionSlots = Int(value.availableMetadataResolutionSlots)
        availableDownloadSlots = Int(value.availableDownloadSlots)
        resolvingMetadataCount = Int(value.resolvingMetadataCount)
        metadataQueueCount = Int(value.metadataQueueCount)
        downloadingCount = Int(value.downloadingCount)
        downloadQueueCount = Int(value.downloadQueueCount)
        seedingCount = Int(value.seedingCount)
        pausedCount = Int(value.pausedCount)
        completedCount = Int(value.completedCount)
        errorCount = Int(value.errorCount)
        currentConnectedPeerCount = Int(value.currentConnectedPeerCount)
        currentDownloadRateBytesPerSecond = value.currentDownloadRateBytesPerSecond
        currentUploadRateBytesPerSecond = value.currentUploadRateBytesPerSecond
        partialFilesEnabled = value.partialFilesEnabled
        partialFileSuffix = value.partialFileSuffix
        seedingStopMode = value.seedingStopMode
        seedingStopRatio = value.seedingStopRatio
        seedingStopMinutes = Int(value.seedingStopMinutes)
        completedTorrentCleanupMode = value.completedTorrentCleanupMode
        completedTorrentCleanupMinutes = Int(value.completedTorrentCleanupMinutes)
        deleteLogsForCompletedTorrents = value.deleteLogsForCompletedTorrents
        status = .init(rawValue: value.status)
        environmentName = value.environmentName
        downloadRootPath = value.downloadRootPath
        torrentCount = Int(value.torrentCount)
        supportsMagnetAdds = value.supportsMagnetAdds
        supportsPause = value.supportsPause
        supportsResume = value.supportsResume
        supportsRemove = value.supportsRemove
        supportsPersistentStorage = value.supportsPersistentStorage
        supportsMultiHost = value.supportsMultiHost
        startupRecoveryCompleted = value.startupRecoveryCompleted
        startupRecoveredTorrentCount = Int(value.startupRecoveredTorrentCount)
        startupNormalizedTorrentCount = Int(value.startupNormalizedTorrentCount)
        startupRecoveryCompletedAt = value.startupRecoveryCompletedAtUtc
        vpnValidationEnabled = value.vpnValidationEnabled
        vpnConnectionPhase = value.vpnConnectionPhase
        vpnConnectionReason = value.vpnConnectionReason
        torrentProcessingAvailable = value.torrentProcessingAvailable
        torrentProcessingMessage = value.torrentProcessingMessage
        vpnLastCheckAt = value.vpnLastCheckAtUtc
        vpnLastSuccessAt = value.vpnLastSuccessAtUtc
        vpnNextAutomaticRetryAt = value.vpnNextAutomaticRetryAtUtc
        vpnObservedPublicIPv4 = value.vpnObservedPublicIpv4
        vpnDegradedCheckIntervalSeconds = value.vpnDegradedCheckIntervalSeconds.map(Int.init)
        vpnReadyCheckIntervalSeconds = value.vpnReadyCheckIntervalSeconds.map(Int.init)
        vpnFailureSummary = value.vpnFailureSummary
        expressVPNRecoveryMode = value.expressVpnRecoveryMode
        expressVPNRecoveryPhase = value.expressVpnRecoveryPhase
        expressVPNConnectionState = value.expressVpnConnectionState
        expressVPNReconnectAttemptsUsed = value.expressVpnReconnectAttemptsUsed.map(Int.init)
        expressVPNReconnectAttemptsMaximum = value.expressVpnReconnectAttemptsMaximum.map(Int.init)
        expressVPNLaunchAttemptsUsed = value.expressVpnLaunchAttemptsUsed.map(Int.init)
        expressVPNLaunchAttemptsMaximum = value.expressVpnLaunchAttemptsMaximum.map(Int.init)
        expressVPNNextActionAt = value.expressVpnNextActionAtUtc
        expressVPNLastActionAt = value.expressVpnLastActionAtUtc
        expressVPNLastActionOutcome = value.expressVpnLastActionOutcome
        expressVPNRecoveryMessage = value.expressVpnRecoveryMessage
        checkedAt = value.checkedAtUtc
    }
}

extension TorrentCoreLifecycleEvent {
    init(_ value: Components.Schemas.DashboardLifecycleEventDto) {
        category = value.category
        eventType = value.eventType
        level = value.level
        message = value.message
        occurredAt = value.occurredAtUtc
        torrentID = value.torrentId.flatMap(UUID.init(uuidString:))
    }
}

extension TorrentCoreDashboardLifecycle {
    init(_ value: Components.Schemas.DashboardLifecycleSummaryDto) {
        callbackFailedCount = Int(value.callbackFailedCount)
        callbackInvokedCount = Int(value.callbackInvokedCount)
        callbackTimedOutCount = Int(value.callbackTimedOutCount)
        completedAutoRemovedCount = Int(value.completedAutoRemovedCount)
        firstEventAt = value.firstEventAtUtc
        lastEventAt = value.lastEventAtUtc
        metadataRefreshRequestedCount = Int(value.metadataRefreshRequestedCount)
        metadataResetRequestedCount = Int(value.metadataResetRequestedCount)
        metadataResolvedCount = Int(value.metadataResolvedCount)
        metadataRestartRequestedCount = Int(value.metadataRestartRequestedCount)
        orphanedTorrentLogsDeletedCount = Int(value.orphanedTorrentLogsDeletedCount)
        recentEvents = (value.recentEvents ?? []).map(TorrentCoreLifecycleEvent.init)
        recoveryCompletedAt = value.recoveryCompletedAtUtc
        serviceInstanceID = UUID(uuidString: value.serviceInstanceId)
        startupNormalizedTorrentCount = Int(value.startupNormalizedTorrentCount)
        startupReadyAt = value.startupReadyAtUtc
        startupRecoveredTorrentCount = Int(value.startupRecoveredTorrentCount)
        torrentsAddedCount = Int(value.torrentsAddedCount)
        torrentsRemovedCount = Int(value.torrentsRemovedCount)
    }
}

extension TorrentCoreTorrentSummary {
    init(_ value: Components.Schemas.TorrentSummaryDto) {
        addedAt = value.addedAtUtc
        canPause = value.canPause
        canRefreshMetadata = value.canRefreshMetadata
        canRemove = value.canRemove
        canResume = value.canResume
        canRetryCompletionCallback = value.canRetryCompletionCallback
        canMakeNext = value.canMakeNext
        canHold = value.canHold
        canReleaseHold = value.canReleaseHold
        canResumeNext = value.canResumeNext
        canResumeOnHold = value.canResumeOnHold
        categoryKey = value.categoryKey
        completedAt = value.completedAtUtc
        completionCallbackInvokedAt = value.completionCallbackInvokedAtUtc
        completionCallbackLastError = value.completionCallbackLastError
        completionCallbackPendingSince = value.completionCallbackPendingSinceUtc
        completionCallbackState = value.completionCallbackState
        connectedPeerCount = Int(value.connectedPeerCount)
        downloadLastYieldedAt = value.downloadLastYieldedAtUtc
        downloadNoProgressStartedAt = value.downloadNoProgressStartedAtUtc
        downloadRateBytesPerSecond = value.downloadRateBytesPerSecond
        downloadedBytes = value.downloadedBytes
        errorMessage = value.errorMessage
        lastActivityAt = value.lastActivityAtUtc
        name = value.name
        progressPercent = value.progressPercent
        queuePosition = value.queuePosition.map(Int.init)
        priorityQueuePosition = value.priorityQueuePosition.map(Int.init)
        heldQueuePosition = value.heldQueuePosition.map(Int.init)
        isQueueHeld = value.isQueueHeld
        isDownloadYielded = value.isDownloadYielded
        state = .init(rawValue: value.state)
        torrentID = UUID(uuidString: value.torrentId)
        totalBytes = value.totalBytes
        trackerCount = Int(value.trackerCount)
        uploadRateBytesPerSecond = value.uploadRateBytesPerSecond
        waitReason = value.waitReason.map(TorrentCoreWaitReason.init(rawValue:))
    }
}

extension TorrentCoreCompletionCallbackFeedback {
    init(_ value: Components.Schemas.CompletionCallbackFeedbackDto) {
        allowResubmit = value.allowResubmit
        attemptCount = Int(value.attemptCount)
        callbackFinished = value.callbackFinished
        callbackLocalTimestamp = value.callbackLocalTimestamp
        callbackMachine = value.callbackMachine
        callbackSource = value.callbackSource
        completionTimestamp = value.completionTimestamp
        contractVersion = value.contractVersion
        correlationID = value.correlationId
        detailMessage = value.detailMessage
        displayMessage = value.displayMessage
        finalState = value.finalState
        mediaConsideredDone = value.mediaConsideredDone
        needsManualIntervention = value.needsManualIntervention
        rawResponseJSON = value.rawResponseJson
        reasonCode = value.reasonCode
        receivedAt = value.receivedAtUtc
        recommendedAction = value.recommendedAction
        resubmitAdvice = value.resubmitAdvice
        sourceState = value.sourceState
        torrentHash = value.torrentHash
        torrentID = UUID(uuidString: value.torrentId)
    }
}

extension TorrentCoreTorrentDetail {
    init(_ value: Components.Schemas.TorrentDetailDto) {
        addedAt = value.addedAtUtc
        canPause = value.canPause
        canRefreshMetadata = value.canRefreshMetadata
        canRemove = value.canRemove
        canResume = value.canResume
        canRetryCompletionCallback = value.canRetryCompletionCallback
        canMakeNext = value.canMakeNext
        canHold = value.canHold
        canReleaseHold = value.canReleaseHold
        canResumeNext = value.canResumeNext
        canResumeOnHold = value.canResumeOnHold
        categoryKey = value.categoryKey
        completedAt = value.completedAtUtc
        completionCallbackFeedback = value.completionCallbackFeedback.map(TorrentCoreCompletionCallbackFeedback.init)
        completionCallbackFinalPayloadPath = value.completionCallbackFinalPayloadPath
        completionCallbackInvokedAt = value.completionCallbackInvokedAtUtc
        completionCallbackLastError = value.completionCallbackLastError
        completionCallbackPendingReason = value.completionCallbackPendingReason
        completionCallbackPendingSince = value.completionCallbackPendingSinceUtc
        completionCallbackState = value.completionCallbackState
        connectedPeerCount = Int(value.connectedPeerCount)
        downloadLastYieldedAt = value.downloadLastYieldedAtUtc
        downloadNoProgressStartedAt = value.downloadNoProgressStartedAtUtc
        downloadRateBytesPerSecond = value.downloadRateBytesPerSecond
        downloadedBytes = value.downloadedBytes
        errorMessage = value.errorMessage
        infoHash = value.infoHash
        lastActivityAt = value.lastActivityAtUtc
        magnetURI = value.magnetUri
        name = value.name
        progressPercent = value.progressPercent
        queuePosition = value.queuePosition.map(Int.init)
        priorityQueuePosition = value.priorityQueuePosition.map(Int.init)
        heldQueuePosition = value.heldQueuePosition.map(Int.init)
        isQueueHeld = value.isQueueHeld
        isDownloadYielded = value.isDownloadYielded
        savePath = value.savePath
        state = .init(rawValue: value.state)
        torrentID = UUID(uuidString: value.torrentId)
        totalBytes = value.totalBytes
        trackerCount = Int(value.trackerCount)
        uploadRateBytesPerSecond = value.uploadRateBytesPerSecond
        waitReason = value.waitReason.map(TorrentCoreWaitReason.init(rawValue:))
    }
}

extension TorrentCoreCategory {
    init(_ value: Components.Schemas.TorrentCategoryDto) {
        callbackLabel = value.callbackLabel
        displayName = value.displayName
        downloadRootPath = value.downloadRootPath
        enabled = value.enabled
        invokeCompletionCallback = value.invokeCompletionCallback
        key = value.key
        sortOrder = Int(value.sortOrder)
    }
}

extension TorrentCoreActionResult {
    init(_ value: Components.Schemas.TorrentActionResultDto) {
        action = value.action
        dataDeleted = value.dataDeleted
        processedAt = value.processedAtUtc
        state = .init(rawValue: value.state)
        torrentID = UUID(uuidString: value.torrentId)
    }
}

extension TorrentCoreServiceProblem {
    init(_ value: Components.Schemas.ServiceProblemDetailsDto) {
        type = value._type
        title = value.title
        status = value.status.map(Int.init)
        detail = value.detail
        instance = value.instance
        code = value.code
        target = value.target
        traceID = value.traceId
        errors = value.errors?.additionalProperties ?? [:]
    }
}
