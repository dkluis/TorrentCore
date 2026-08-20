import Foundation
import TorrentCoreAPI

public enum TorrentCoreFixtureEnvironment {
    @MainActor
    public static func makeSession(
        largeCollections: Bool = false
    ) throws -> TorrentCoreFeatureSession {
        let profile = try TorrentCoreConnectionProfile(
            name: "Fixture Service",
            address: "http://fixture.torrentcore.test:7033"
        )
        let client = TorrentCoreFixtureServiceClient(
            largeCollections: largeCollections
        )
        return TorrentCoreFeatureSession(
            profileStore: TorrentCoreFixtureProfileStore(.init(
                profiles: [profile],
                activeProfileID: profile.id
            )),
            clientFactory: TorrentCoreFixtureClientFactory(
                baseURL: profile.baseURL,
                client: client
            )
        )
    }
}

private actor TorrentCoreFixtureProfileStore: TorrentCoreProfilePersisting {
    private var preferences: TorrentCoreClientPreferences

    init(_ preferences: TorrentCoreClientPreferences) {
        self.preferences = preferences
    }

    func load() async throws -> TorrentCoreClientPreferences {
        preferences
    }

    func save(_ preferences: TorrentCoreClientPreferences) async throws {
        self.preferences = preferences
    }
}

private struct TorrentCoreFixtureClientFactory: TorrentCoreServiceClientBuilding {
    let baseURL: URL
    let client: TorrentCoreFixtureServiceClient

    func makeClient(baseURL: URL) throws -> any TorrentCoreServiceClientProtocol {
        guard baseURL == self.baseURL else {
            throw TorrentCoreClientError.invalidBaseURL
        }
        return client
    }
}

private actor TorrentCoreFixtureServiceClient: TorrentCoreServiceClientProtocol {
    private var torrentValues: [TorrentCoreTorrentSummary]
    private var historyValues: [TorrentCoreHistorySummary]
    private var logValues: [TorrentCoreActivityLogEntry]
    private var peerValues: [TorrentCorePeer]
    private var trackerValues: [TorrentCoreTracker]
    private var categoryValues = TorrentCorePreviewFixtures.categories
    private var runtimeSettingsValue = TorrentCorePreviewFixtures.runtimeSettings

    init(largeCollections: Bool) {
        if largeCollections {
            torrentValues = Self.makeTorrents(count: 100)
            historyValues = Self.makeHistory(count: 500)
            logValues = Self.makeLogs(count: 5_000)
            peerValues = Self.makePeers(count: 250)
            trackerValues = Self.makeTrackers(count: 50)
        } else {
            torrentValues = [
                TorrentCorePreviewFixtures.downloadingTorrent,
                TorrentCorePreviewFixtures.pausedTorrent,
            ]
            historyValues = TorrentCorePreviewFixtures.history
            logValues = TorrentCorePreviewFixtures.activityLogs
            peerValues = TorrentCorePreviewFixtures.peers
            trackerValues = TorrentCorePreviewFixtures.trackers
        }
    }

    func probe() async throws -> TorrentCoreServiceHealth {
        TorrentCorePreviewFixtures.connectedHealth
    }

    func hostStatus() async throws -> TorrentCoreHostStatus {
        var status = TorrentCorePreviewFixtures.hostStatus
        status.torrentCount = torrentValues.count
        status.downloadingCount = torrentValues.filter { $0.state == .downloading }.count
        status.pausedCount = torrentValues.filter { $0.state == .paused }.count
        return status
    }

    func dashboardLifecycle() async throws -> TorrentCoreDashboardLifecycle {
        var lifecycle = TorrentCorePreviewFixtures.dashboardLifecycle
        lifecycle.recentEvents = [
            TorrentCoreLifecycleEvent(
                category: "Host",
                eventType: "StartupReady",
                level: "Information",
                message: "Fixture TorrentCore service is ready.",
                occurredAt: TorrentCorePreviewFixtures.checkedAt,
                torrentID: nil
            ),
            TorrentCoreLifecycleEvent(
                category: "Torrent",
                eventType: "TorrentAdded",
                level: "Information",
                message: "Preview Torrent was added.",
                occurredAt: TorrentCorePreviewFixtures.checkedAt.addingTimeInterval(-300),
                torrentID: TorrentCorePreviewFixtures.torrentID
            ),
        ]
        return lifecycle
    }

    func torrents() async throws -> [TorrentCoreTorrentSummary] {
        torrentValues
    }

    func torrent(id: UUID) async throws -> TorrentCoreTorrentDetail {
        guard let summary = torrentValues.first(where: { $0.torrentID == id }) else {
            throw TorrentCoreClientError.unexpectedResponse(statusCode: 404)
        }
        var detail = TorrentCorePreviewFixtures.torrentDetail
        detail.torrentID = id
        detail.name = summary.name
        detail.categoryKey = summary.categoryKey
        detail.state = summary.state
        detail.canPause = summary.canPause
        detail.canResume = summary.canResume
        detail.canRemove = summary.canRemove
        detail.canMakeNext = summary.canMakeNext
        detail.canHold = summary.canHold
        detail.canReleaseHold = summary.canReleaseHold
        detail.canResumeNext = summary.canResumeNext
        detail.canResumeOnHold = summary.canResumeOnHold
        detail.progressPercent = summary.progressPercent
        detail.downloadRateBytesPerSecond = summary.downloadRateBytesPerSecond
        detail.uploadRateBytesPerSecond = summary.uploadRateBytesPerSecond
        detail.connectedPeerCount = summary.connectedPeerCount
        detail.downloadLastYieldedAt = summary.downloadLastYieldedAt
        detail.downloadNoProgressStartedAt = summary.downloadNoProgressStartedAt
        detail.waitReason = summary.waitReason
        detail.queuePosition = summary.queuePosition
        detail.priorityQueuePosition = summary.priorityQueuePosition
        detail.heldQueuePosition = summary.heldQueuePosition
        detail.isQueueHeld = summary.isQueueHeld
        detail.isDownloadYielded = summary.isDownloadYielded
        return detail
    }

    func categories() async throws -> [TorrentCoreCategory] {
        categoryValues
    }

    func history(query: TorrentCoreHistoryQuery) async throws -> [TorrentCoreHistorySummary] {
        var values = historyValues
        if let name = query.torrentName, !name.isEmpty {
            values = values.filter {
                $0.name?.localizedCaseInsensitiveContains(name) == true
            }
        }
        if let categoryKey = query.categoryKey, !categoryKey.isEmpty {
            values = values.filter { $0.categoryKey == categoryKey }
        }
        if let state = query.state, !state.isEmpty {
            values = values.filter { $0.latestTorrentState == state }
        }
        if let outcome = query.outcome {
            values = values.filter { $0.outcome == outcome }
        }
        if let removed = query.removed {
            values = values.filter { ($0.removedAt != nil) == removed }
        }
        let dateFormatter = DateFormatter()
        dateFormatter.calendar = Calendar(identifier: .gregorian)
        dateFormatter.locale = Locale(identifier: "en_US_POSIX")
        dateFormatter.dateFormat = "yyyy-MM-dd"
        if let fromDate = query.fromDate.flatMap(dateFormatter.date(from:)) {
            values = values.filter { $0.lastUpdatedAt >= fromDate }
        }
        if let toDate = query.toDate.flatMap(dateFormatter.date(from:)),
           let exclusiveEnd = dateFormatter.calendar.date(byAdding: .day, value: 1, to: toDate)
        {
            values = values.filter { $0.lastUpdatedAt < exclusiveEnd }
        }
        values.sort { $0.lastUpdatedAt > $1.lastUpdatedAt }
        if let take = query.take {
            values = Array(values.prefix(max(0, take)))
        }
        return values
    }

    func historyFilterOptions() async throws -> TorrentCoreHistoryFilterOptions {
        .init(
            categoryKeys: distinctValues(historyValues.compactMap(\.categoryKey)),
            states: distinctValues(historyValues.compactMap(\.latestTorrentState))
        )
    }

    func historyDetail(torrentID: UUID) async throws -> TorrentCoreHistoryDetail {
        guard let history = historyValues.first(where: {
            $0.torrentID == torrentID
        }) else {
            throw TorrentCoreClientError.unexpectedResponse(statusCode: 404)
        }
        var detail = TorrentCorePreviewFixtures.historyDetail
        detail.torrentID = history.torrentID
        detail.name = history.name
        detail.categoryKey = history.categoryKey
        detail.outcome = history.outcome
        detail.latestTorrentState = history.latestTorrentState
        detail.latestProgressPercent = history.latestProgressPercent
        detail.removalKind = history.removalKind
        detail.removalReason = history.removalReason
        detail.removedAt = history.removedAt
        detail.dataDeleted = history.dataDeleted
        return detail
    }

    func logs(query: TorrentCoreLogQuery) async throws -> [TorrentCoreActivityLogEntry] {
        var values = logValues
        if let level = query.level {
            values = values.filter { logLevel($0.level) == level }
        }
        if let category = query.category, !category.isEmpty {
            values = values.filter { $0.category == category }
        }
        if let eventType = query.eventType, !eventType.isEmpty {
            values = values.filter { $0.eventType == eventType }
        }
        if let torrentID = query.torrentID {
            values = values.filter { $0.torrentID == torrentID }
        }
        if let serviceInstanceID = query.serviceInstanceID {
            values = values.filter { $0.serviceInstanceID == serviceInstanceID }
        }
        if let fromUTC = query.fromUTC {
            values = values.filter { $0.occurredAt >= fromUTC }
        }
        if let toUTC = query.toUTC {
            values = values.filter { $0.occurredAt <= toUTC }
        }
        return Array(values.prefix(max(0, query.take)))
    }

    func activityLogFilterOptions() async throws -> TorrentCoreActivityLogFilterOptions {
        .init(
            categories: distinctValues(logValues.compactMap(\.category)),
            eventTypes: distinctValues(logValues.compactMap(\.eventType))
        )
    }

    func peers(torrentID: UUID) async throws -> [TorrentCorePeer] {
        guard torrentValues.contains(where: { $0.torrentID == torrentID }) else {
            throw TorrentCoreClientError.unexpectedResponse(statusCode: 404)
        }
        return peerValues
    }

    func trackers(torrentID: UUID) async throws -> [TorrentCoreTracker] {
        guard torrentValues.contains(where: { $0.torrentID == torrentID }) else {
            throw TorrentCoreClientError.unexpectedResponse(statusCode: 404)
        }
        return trackerValues
    }

    func runtimeSettings() async throws -> TorrentCoreRuntimeSettings {
        runtimeSettingsValue
    }

    func addMagnet(
        _ magnetURI: String,
        categoryKey: String?
    ) async throws -> TorrentCoreTorrentDetail {
        let newID = UUID()
        var summary = TorrentCorePreviewFixtures.downloadingTorrent
        summary.torrentID = newID
        summary.name = "New Fixture Magnet"
        summary.categoryKey = categoryKey
        summary.progressPercent = 0
        summary.downloadedBytes = 0
        summary.addedAt = Date()
        torrentValues.insert(summary, at: 0)

        var detail = TorrentCorePreviewFixtures.torrentDetail
        detail.torrentID = newID
        detail.name = summary.name
        detail.categoryKey = categoryKey
        detail.magnetURI = magnetURI
        detail.progressPercent = 0
        detail.downloadedBytes = 0
        detail.addedAt = summary.addedAt
        return detail
    }

    func pause(id: UUID) async throws -> TorrentCoreActionResult {
        try update(id: id) { torrent in
            torrent.state = .paused
            torrent.canPause = false
            torrent.canResume = true
            torrent.downloadRateBytesPerSecond = 0
            torrent.uploadRateBytesPerSecond = 0
            torrent.waitReason = .pausedByOperator
        }
        return TorrentCoreActionResult(
            action: "pause",
            dataDeleted: nil,
            processedAt: Date(),
            state: .paused,
            torrentID: id
        )
    }

    func resume(id: UUID) async throws -> TorrentCoreActionResult {
        try update(id: id) { torrent in
            torrent.state = .downloading
            torrent.canPause = true
            torrent.canResume = false
            torrent.downloadRateBytesPerSecond = 4_096_000
            torrent.waitReason = nil
        }
        return TorrentCoreActionResult(
            action: "resume",
            dataDeleted: nil,
            processedAt: Date(),
            state: .downloading,
            torrentID: id
        )
    }

    func makeNext(id: UUID) async throws -> TorrentCoreActionResult {
        try update(id: id) { torrent in
            torrent.isQueueHeld = false
            torrent.heldQueuePosition = nil
            torrent.priorityQueuePosition = 1
            torrent.canMakeNext = false
            torrent.canHold = true
            torrent.canReleaseHold = false
        }
        return queueResult("make_next", id: id, state: .queued)
    }

    func hold(id: UUID) async throws -> TorrentCoreActionResult {
        try update(id: id) { torrent in
            torrent.isQueueHeld = true
            torrent.priorityQueuePosition = nil
            torrent.heldQueuePosition = 1
            torrent.canMakeNext = true
            torrent.canHold = false
            torrent.canReleaseHold = true
            torrent.waitReason = .heldByOperator
        }
        return queueResult("hold", id: id, state: .queued)
    }

    func releaseHold(id: UUID) async throws -> TorrentCoreActionResult {
        try update(id: id) { torrent in
            torrent.isQueueHeld = false
            torrent.heldQueuePosition = nil
            torrent.canHold = true
            torrent.canReleaseHold = false
            torrent.waitReason = .waitingForDownloadSlot
        }
        return queueResult("release_hold", id: id, state: .queued)
    }

    func resumeNext(id: UUID) async throws -> TorrentCoreActionResult {
        try update(id: id) { torrent in
            torrent.state = .queued
            torrent.canResume = false
            torrent.canResumeNext = false
            torrent.canResumeOnHold = false
            torrent.canPause = true
            torrent.priorityQueuePosition = 1
            torrent.waitReason = .waitingForDownloadSlot
        }
        return queueResult("resume_next", id: id, state: .queued)
    }

    func resumeOnHold(id: UUID) async throws -> TorrentCoreActionResult {
        try update(id: id) { torrent in
            torrent.state = .queued
            torrent.canResume = false
            torrent.canResumeNext = false
            torrent.canResumeOnHold = false
            torrent.canPause = true
            torrent.isQueueHeld = true
            torrent.heldQueuePosition = 1
            torrent.waitReason = .heldByOperator
        }
        return queueResult("resume_on_hold", id: id, state: .queued)
    }

    private func queueResult(_ action: String, id: UUID,
        state: TorrentCoreTorrentState) -> TorrentCoreActionResult {
        TorrentCoreActionResult(action: action, dataDeleted: nil, processedAt: Date(), state: state, torrentID: id)
    }

    func remove(id: UUID, deleteData: Bool) async throws -> TorrentCoreActionResult {
        guard torrentValues.contains(where: { $0.torrentID == id }) else {
            throw TorrentCoreClientError.unexpectedResponse(statusCode: 404)
        }
        torrentValues.removeAll { $0.torrentID == id }
        return TorrentCoreActionResult(
            action: "remove",
            dataDeleted: deleteData,
            processedAt: Date(),
            state: .removed,
            torrentID: id
        )
    }

    func refreshMetadata(id: UUID) async throws -> TorrentCoreActionResult {
        guard torrentValues.contains(where: { $0.torrentID == id }) else {
            throw TorrentCoreClientError.unexpectedResponse(statusCode: 404)
        }
        return TorrentCoreActionResult(
            action: "refreshMetadata",
            dataDeleted: nil,
            processedAt: Date(),
            state: .downloading,
            torrentID: id
        )
    }

    func resetMetadataSession(id: UUID) async throws -> TorrentCoreActionResult {
        guard torrentValues.contains(where: { $0.torrentID == id }) else {
            throw TorrentCoreClientError.unexpectedResponse(statusCode: 404)
        }
        return TorrentCoreActionResult(
            action: "resetMetadataSession",
            dataDeleted: nil,
            processedAt: Date(),
            state: .resolvingMetadata,
            torrentID: id
        )
    }

    func retryCompletionCallback(id: UUID) async throws -> TorrentCoreActionResult {
        guard torrentValues.contains(where: { $0.torrentID == id }) else {
            throw TorrentCoreClientError.unexpectedResponse(statusCode: 404)
        }
        return TorrentCoreActionResult(
            action: "retryCompletionCallback",
            dataDeleted: nil,
            processedAt: Date(),
            state: torrentValues.first(where: { $0.torrentID == id })?.state ?? .completed,
            torrentID: id
        )
    }

    func deleteOrphanedLogs() async throws -> TorrentCoreDeleteOrphanedLogsResult {
        TorrentCoreDeleteOrphanedLogsResult(deletedLogEntryCount: 2)
    }

    func cleanupLogs(upToDate: String) async throws -> TorrentCoreCleanupResult {
        TorrentCoreCleanupResult(
            upToDate: upToDate,
            cutoffUTC: Date(),
            deletedRecordCount: 12
        )
    }

    func cleanupHistory(upToDate: String) async throws -> TorrentCoreCleanupResult {
        TorrentCoreCleanupResult(
            upToDate: upToDate,
            cutoffUTC: Date(),
            deletedRecordCount: 4
        )
    }

    func updateRuntimeSettings(
        _ update: TorrentCoreRuntimeSettingsUpdate
    ) async throws -> TorrentCoreRuntimeSettings {
        runtimeSettingsValue.automaticMetadataResetStuckThresholdSeconds = update.automaticMetadataResetStuckThresholdSeconds
        runtimeSettingsValue.coldDownloadAbandonAfterHours = update.coldDownloadAbandonAfterHours
        runtimeSettingsValue.coldDownloadRecoveryIntervalMinutes = update.coldDownloadRecoveryIntervalMinutes
        runtimeSettingsValue.coldDownloadRecoveryThresholdMinutes = update.coldDownloadRecoveryThresholdMinutes
        runtimeSettingsValue.completedTorrentCleanupMinutes = update.completedTorrentCleanupMinutes
        runtimeSettingsValue.completedTorrentCleanupMode = update.completedTorrentCleanupMode
        runtimeSettingsValue.completionCallbackAPIBaseURLOverride = update.completionCallbackAPIBaseURLOverride
        runtimeSettingsValue.completionCallbackAPIKeyOverride = update.completionCallbackAPIKeyOverride
        runtimeSettingsValue.completionCallbackArguments = update.completionCallbackArguments
        runtimeSettingsValue.completionCallbackCommandPath = update.completionCallbackCommandPath
        runtimeSettingsValue.completionCallbackEnabled = update.completionCallbackEnabled
        runtimeSettingsValue.completionCallbackFinalizationTimeoutSeconds = update.completionCallbackFinalizationTimeoutSeconds
        runtimeSettingsValue.completionCallbackTimeoutSeconds = update.completionCallbackTimeoutSeconds
        runtimeSettingsValue.completionCallbackWorkingDirectory = update.completionCallbackWorkingDirectory
        runtimeSettingsValue.deleteLogsForCompletedTorrents = update.deleteLogsForCompletedTorrents
        runtimeSettingsValue.engineConnectionFailureLogBurstLimit = update.engineConnectionFailureLogBurstLimit
        runtimeSettingsValue.engineConnectionFailureLogWindowSeconds = update.engineConnectionFailureLogWindowSeconds
        runtimeSettingsValue.engineAllowPeerExchange = update.engineAllowPeerExchange
        runtimeSettingsValue.engineEncryptionMode = update.engineEncryptionMode
        runtimeSettingsValue.engineMaximumConnections = update.engineMaximumConnections
        runtimeSettingsValue.engineMaximumDownloadRateBytesPerSecond = update.engineMaximumDownloadRateBytesPerSecond
        runtimeSettingsValue.engineMaximumHalfOpenConnections = update.engineMaximumHalfOpenConnections
        runtimeSettingsValue.engineMaximumUploadRateBytesPerSecond = update.engineMaximumUploadRateBytesPerSecond
        runtimeSettingsValue.maxActiveDownloads = update.maxActiveDownloads
        runtimeSettingsValue.maxActiveMetadataResolutions = update.maxActiveMetadataResolutions
        runtimeSettingsValue.metadataRefreshRestartDelaySeconds = update.metadataRefreshRestartDelaySeconds
        runtimeSettingsValue.metadataRefreshStaleSeconds = update.metadataRefreshStaleSeconds
        runtimeSettingsValue.metadataResolutionTimeSliceMinutes = update.metadataResolutionTimeSliceMinutes
        runtimeSettingsValue.seedingStopMinutes = update.seedingStopMinutes
        runtimeSettingsValue.seedingStopMode = update.seedingStopMode
        runtimeSettingsValue.seedingStopRatio = update.seedingStopRatio
        runtimeSettingsValue.vpnEgressDegradedCheckIntervalSeconds = update.vpnEgressDegradedCheckIntervalSeconds
        runtimeSettingsValue.vpnEgressDirectIspCidrs = update.vpnEgressDirectIspCidrs
        runtimeSettingsValue.vpnEgressReadyCheckIntervalSeconds = update.vpnEgressReadyCheckIntervalSeconds
        runtimeSettingsValue.vpnEgressRequestTimeoutSeconds = update.vpnEgressRequestTimeoutSeconds
        runtimeSettingsValue.vpnEgressEngineSuspensionTimeoutSeconds =
            update.vpnEgressEngineSuspensionTimeoutSeconds
        runtimeSettingsValue.vpnEgressValidationEnabled = update.vpnEgressValidationEnabled
        runtimeSettingsValue.vpnEgressValidationEndpoint = update.vpnEgressValidationEndpoint
        runtimeSettingsValue.runtimeTickDurationSummaryEnabled =
            update.runtimeTickDurationSummaryEnabled
        runtimeSettingsValue.updatedAt = Date()
        return runtimeSettingsValue
    }

    func updateCategory(
        key: String,
        update: TorrentCoreCategoryUpdate
    ) async throws -> TorrentCoreCategory {
        guard let index = categoryValues.firstIndex(where: { $0.key == key }) else {
            throw TorrentCoreClientError.unexpectedResponse(statusCode: 404)
        }
        categoryValues[index].callbackLabel = update.callbackLabel
        categoryValues[index].displayName = update.displayName
        categoryValues[index].downloadRootPath = update.downloadRootPath
        categoryValues[index].enabled = update.enabled
        categoryValues[index].invokeCompletionCallback = update.invokeCompletionCallback
        categoryValues[index].sortOrder = update.sortOrder
        return categoryValues[index]
    }

    func restartService() async throws -> TorrentCoreServiceRestartResult {
        TorrentCorePreviewFixtures.restartResult
    }

    private func update(
        id: UUID,
        _ mutation: (inout TorrentCoreTorrentSummary) -> Void
    ) throws {
        guard let index = torrentValues.firstIndex(where: { $0.torrentID == id }) else {
            throw TorrentCoreClientError.unexpectedResponse(statusCode: 404)
        }
        mutation(&torrentValues[index])
    }

    private func logLevel(_ level: String?) -> TorrentCoreActivityLogLevel? {
        switch level?.lowercased() {
        case "debug": .debug
        case "information": .information
        case "warning": .warning
        case "error": .error
        case "critical": .critical
        default: nil
        }
    }

    private func distinctValues(_ values: [String]) -> [String] {
        values
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            .filter { !$0.isEmpty }
            .reduce(into: [String]()) { result, value in
                if !result.contains(where: {
                    $0.localizedCaseInsensitiveCompare(value) == .orderedSame
                }) {
                    result.append(value)
                }
            }
            .sorted { $0.localizedCaseInsensitiveCompare($1) == .orderedAscending }
    }

    private static func makeTorrents(
        count: Int
    ) -> [TorrentCoreTorrentSummary] {
        (0..<count).map { index in
            var torrent = TorrentCorePreviewFixtures.downloadingTorrent
            torrent.torrentID = UUID()
            torrent.name = "Fixture Torrent \(index + 1)"
            switch index {
            case 0..<10:
                torrent.state = .downloading
                torrent.canPause = true
                torrent.canResume = false
            case 10..<20:
                torrent.state = .resolvingMetadata
                torrent.canPause = true
                torrent.canResume = false
            default:
                torrent.state = index.isMultiple(of: 2) ? .paused : .completed
                torrent.canPause = false
                torrent.canResume = torrent.state == .paused
            }
            return torrent
        }
    }

    private static func makeHistory(
        count: Int
    ) -> [TorrentCoreHistorySummary] {
        let template = TorrentCorePreviewFixtures.history[0]
        return (0..<count).map { index in
            var history = template
            history.torrentID = UUID()
            history.name = "Fixture History \(index + 1)"
            history.submittedAt = template.submittedAt.addingTimeInterval(
                -TimeInterval(index)
            )
            history.lastUpdatedAt = history.submittedAt
            return history
        }
    }

    private static func makeLogs(
        count: Int
    ) -> [TorrentCoreActivityLogEntry] {
        let template = TorrentCorePreviewFixtures.activityLogs[0]
        return (0..<count).map { index in
            var log = template
            log.logEntryID = Int64(index + 1)
            log.message = "Fixture log \(index + 1)"
            log.occurredAt = template.occurredAt.addingTimeInterval(
                -TimeInterval(index)
            )
            return log
        }
    }

    private static func makePeers(count: Int) -> [TorrentCorePeer] {
        let template = TorrentCorePreviewFixtures.peers[0]
        return (0..<count).map { index in
            var peer = template
            peer.endpoint = "192.0.2.\((index % 250) + 1):\(10_000 + index)"
            return peer
        }
    }

    private static func makeTrackers(count: Int) -> [TorrentCoreTracker] {
        let template = TorrentCorePreviewFixtures.trackers[0]
        return (0..<count).map { index in
            var tracker = template
            tracker.tierNumber = index / 10
            tracker.trackerNumber = index
            return tracker
        }
    }
}
