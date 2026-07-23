import Foundation

public enum TorrentCorePreviewFixtures {
    public static let checkedAt = Date(timeIntervalSince1970: 1_774_441_200)
    public static let torrentID = UUID(uuidString: "11111111-2222-3333-4444-555555555555")!

    public static let connectedHealth = TorrentCoreServiceHealth(
        apiVersion: 1,
        serviceName: "TorrentCore.Service",
        status: "ok",
        environmentName: "Preview",
        checkedAt: checkedAt
    )

    public static let hostStatus = TorrentCoreHostStatus(
        apiVersion: 1,
        serviceName: "TorrentCore.Service",
        serviceVersion: "1.0.0",
        serviceInstanceID: UUID(uuidString: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
        engineRuntime: "MonoTorrent",
        engineListenPort: 55_123,
        engineDHTPort: 55_124,
        enginePortForwardingEnabled: true,
        engineLocalPeerDiscoveryEnabled: true,
        engineEncryptionMode: "EncryptedPreferred",
        engineMaximumConnections: 150,
        engineMaximumHalfOpenConnections: 8,
        engineMaximumDownloadRateBytesPerSecond: 0,
        engineMaximumUploadRateBytesPerSecond: 0,
        engineConnectionFailureLogBurstLimit: 5,
        engineConnectionFailureLogWindowSeconds: 60,
        maxActiveMetadataResolutions: 4,
        maxActiveDownloads: 4,
        availableMetadataResolutionSlots: 4,
        availableDownloadSlots: 3,
        resolvingMetadataCount: 0,
        metadataQueueCount: 0,
        downloadingCount: 1,
        downloadQueueCount: 0,
        seedingCount: 0,
        pausedCount: 0,
        completedCount: 0,
        errorCount: 0,
        currentConnectedPeerCount: 5,
        currentDownloadRateBytesPerSecond: 4_096_000,
        currentUploadRateBytesPerSecond: 512_000,
        partialFilesEnabled: true,
        partialFileSuffix: ".!mt",
        seedingStopMode: "Unlimited",
        seedingStopRatio: 1,
        seedingStopMinutes: 60,
        completedTorrentCleanupMode: "Never",
        completedTorrentCleanupMinutes: 60,
        deleteLogsForCompletedTorrents: false,
        status: .ready,
        environmentName: "Preview",
        downloadRootPath: "/preview/downloads",
        torrentCount: 1,
        supportsMagnetAdds: true,
        supportsPause: true,
        supportsResume: true,
        supportsRemove: true,
        supportsPersistentStorage: true,
        supportsMultiHost: false,
        startupRecoveryCompleted: true,
        startupRecoveredTorrentCount: 0,
        startupNormalizedTorrentCount: 0,
        startupRecoveryCompletedAt: checkedAt,
        checkedAt: checkedAt
    )

    public static let dashboardLifecycle = TorrentCoreDashboardLifecycle(
        callbackFailedCount: 0,
        callbackInvokedCount: 0,
        callbackTimedOutCount: 0,
        completedAutoRemovedCount: 0,
        firstEventAt: nil,
        lastEventAt: nil,
        metadataRefreshRequestedCount: 0,
        metadataResetRequestedCount: 0,
        metadataResolvedCount: 0,
        metadataRestartRequestedCount: 0,
        orphanedTorrentLogsDeletedCount: 0,
        recentEvents: [],
        recoveryCompletedAt: checkedAt,
        serviceInstanceID: hostStatus.serviceInstanceID,
        startupNormalizedTorrentCount: 0,
        startupReadyAt: checkedAt,
        startupRecoveredTorrentCount: 0,
        torrentsAddedCount: 1,
        torrentsRemovedCount: 0
    )

    public static let downloadingTorrent = TorrentCoreTorrentSummary(
        addedAt: checkedAt.addingTimeInterval(-3_600),
        canPause: true,
        canRefreshMetadata: false,
        canRemove: true,
        canResume: false,
        canRetryCompletionCallback: false,
        categoryKey: "tv",
        completedAt: nil,
        completionCallbackInvokedAt: nil,
        completionCallbackLastError: nil,
        completionCallbackPendingSince: nil,
        completionCallbackState: nil,
        connectedPeerCount: 5,
        downloadRateBytesPerSecond: 4_096_000,
        downloadedBytes: 524_288_000,
        errorMessage: nil,
        lastActivityAt: checkedAt,
        name: "Preview Torrent",
        progressPercent: 50,
        queuePosition: nil,
        state: .downloading,
        torrentID: torrentID,
        totalBytes: 1_048_576_000,
        trackerCount: 2,
        uploadRateBytesPerSecond: 512_000,
        waitReason: nil
    )

    public static let pausedTorrent: TorrentCoreTorrentSummary = {
        var torrent = downloadingTorrent
        torrent.name = "Paused Preview Torrent"
        torrent.state = .paused
        torrent.canPause = false
        torrent.canResume = true
        torrent.downloadRateBytesPerSecond = 0
        torrent.uploadRateBytesPerSecond = 0
        torrent.connectedPeerCount = 0
        torrent.waitReason = .pausedByOperator
        torrent.torrentID = UUID(uuidString: "66666666-7777-8888-9999-aaaaaaaaaaaa")
        return torrent
    }()

    public static let torrentDetail = TorrentCoreTorrentDetail(
        addedAt: downloadingTorrent.addedAt,
        canPause: true,
        canRefreshMetadata: false,
        canRemove: true,
        canResume: false,
        canRetryCompletionCallback: false,
        categoryKey: "tv",
        completedAt: nil,
        completionCallbackFeedback: nil,
        completionCallbackFinalPayloadPath: nil,
        completionCallbackInvokedAt: nil,
        completionCallbackLastError: nil,
        completionCallbackPendingReason: nil,
        completionCallbackPendingSince: nil,
        completionCallbackState: nil,
        connectedPeerCount: 5,
        downloadRateBytesPerSecond: 4_096_000,
        downloadedBytes: 524_288_000,
        errorMessage: nil,
        infoHash: "0123456789abcdef0123456789abcdef01234567",
        lastActivityAt: checkedAt,
        magnetURI: "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567",
        name: "Preview Torrent",
        progressPercent: 50,
        queuePosition: nil,
        savePath: "/preview/downloads/Preview Torrent",
        state: .downloading,
        torrentID: torrentID,
        totalBytes: 1_048_576_000,
        trackerCount: 2,
        uploadRateBytesPerSecond: 512_000,
        waitReason: nil
    )

    public static let categories = [
        TorrentCoreCategory(
            callbackLabel: "TV Show",
            displayName: "TV (Show)",
            downloadRootPath: "/preview/downloads/tv",
            enabled: true,
            invokeCompletionCallback: true,
            key: "tv",
            sortOrder: 10
        ),
        TorrentCoreCategory(
            callbackLabel: "Movie",
            displayName: "Movie",
            downloadRootPath: "/preview/downloads/movies",
            enabled: true,
            invokeCompletionCallback: true,
            key: "movie",
            sortOrder: 20
        ),
    ]

    public static let serviceProblem = TorrentCoreServiceProblem(
        type: "https://torrentcore.local/problems/unavailable",
        title: "Service unavailable",
        status: 503,
        detail: "TorrentCore is starting.",
        instance: nil,
        code: "host.not_ready",
        target: nil,
        traceID: "preview-trace",
        errors: [:]
    )

    public static func actionResult(
        action: String,
        state: TorrentCoreTorrentState,
        dataDeleted: Bool? = nil
    ) -> TorrentCoreActionResult {
        TorrentCoreActionResult(
            action: action,
            dataDeleted: dataDeleted,
            processedAt: checkedAt,
            state: state,
            torrentID: torrentID
        )
    }
}
