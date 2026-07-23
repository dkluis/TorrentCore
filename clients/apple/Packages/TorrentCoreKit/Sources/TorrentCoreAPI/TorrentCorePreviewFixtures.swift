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
}
