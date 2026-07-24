import Foundation
import Observation
import TorrentCoreAPI

public enum TorrentCoreFeatureContext: Equatable, Sendable {
    case none
    case connection
    case dashboard
    case torrents
    case torrentDetail(UUID)
    case torrentListAndDetail(UUID)
    case addMagnet
    case history(query: TorrentCoreHistoryQuery, selectedTorrentID: UUID?)
    case logs(TorrentCoreLogQuery)
    case peers(UUID)
    case trackers(UUID)
    case serviceSettings

    var refreshesPeriodically: Bool {
        switch self {
        case .connection, .dashboard, .torrents, .torrentDetail, .torrentListAndDetail,
             .history, .logs:
            true
        case .none, .addMagnet, .peers, .trackers, .serviceSettings:
            false
        }
    }
}

public enum TorrentCoreConnectionState: Equatable, Sendable {
    case noProfile
    case notConnected(profileID: UUID)
    case connecting(profileID: UUID)
    case connected(profileID: UUID, connectedAt: Date)
    case offline(profileID: UUID, attemptedAt: Date, message: String)

    public var isConnected: Bool {
        if case .connected = self {
            return true
        }
        return false
    }
}

public enum TorrentCoreFeaturePhase: Equatable, Sendable {
    case idle
    case loading
    case current
    case stale(message: String)
}

public struct TorrentCoreFeatureSnapshot<Value: Sendable>: Sendable {
    public private(set) var value: Value?
    public private(set) var phase: TorrentCoreFeaturePhase
    public private(set) var lastSuccessfulAt: Date?

    public init(
        value: Value? = nil,
        phase: TorrentCoreFeaturePhase = .idle,
        lastSuccessfulAt: Date? = nil
    ) {
        self.value = value
        self.phase = phase
        self.lastSuccessfulAt = lastSuccessfulAt
    }

    mutating func beginLoading() {
        phase = .loading
    }

    mutating func succeed(_ value: Value, at date: Date) {
        self.value = value
        phase = .current
        lastSuccessfulAt = date
    }

    mutating func fail(message: String) {
        phase = .stale(message: message)
    }

    mutating func reset() {
        self = .init()
    }
}

public enum TorrentCoreFeatureActionError: Error, Equatable, Sendable {
    case offline
    case capabilityUnavailable
    case actionAlreadyInProgress
}

extension TorrentCoreFeatureActionError: LocalizedError {
    public var errorDescription: String? {
        switch self {
        case .offline:
            "Refresh the TorrentCore connection before performing this action."
        case .capabilityUnavailable:
            "TorrentCore does not currently allow this action."
        case .actionAlreadyInProgress:
            "Wait for the current TorrentCore action to finish."
        }
    }
}

@MainActor
@Observable
public final class TorrentCoreFeatureSession {
    public private(set) var preferences = TorrentCoreClientPreferences()
    public private(set) var connectionState: TorrentCoreConnectionState = .noProfile
    public private(set) var health: TorrentCoreServiceHealth?
    public private(set) var hostStatus = TorrentCoreFeatureSnapshot<TorrentCoreHostStatus>()
    public private(set) var dashboardLifecycle = TorrentCoreFeatureSnapshot<TorrentCoreDashboardLifecycle>()
    public private(set) var torrents = TorrentCoreFeatureSnapshot<[TorrentCoreTorrentSummary]>()
    public private(set) var torrentDetail = TorrentCoreFeatureSnapshot<TorrentCoreTorrentDetail>()
    public private(set) var categories = TorrentCoreFeatureSnapshot<[TorrentCoreCategory]>()
    public private(set) var history = TorrentCoreFeatureSnapshot<[TorrentCoreHistorySummary]>()
    public private(set) var historyDetail = TorrentCoreFeatureSnapshot<TorrentCoreHistoryDetail>()
    public private(set) var abandonedHistory = TorrentCoreFeatureSnapshot<[TorrentCoreHistorySummary]>()
    public private(set) var logs = TorrentCoreFeatureSnapshot<[TorrentCoreActivityLogEntry]>()
    public private(set) var peers = TorrentCoreFeatureSnapshot<[TorrentCorePeer]>()
    public private(set) var trackers = TorrentCoreFeatureSnapshot<[TorrentCoreTracker]>()
    public private(set) var runtimeSettings = TorrentCoreFeatureSnapshot<TorrentCoreRuntimeSettings>()
    public private(set) var activeMutation: TorrentCoreOperation?
    public private(set) var context: TorrentCoreFeatureContext = .none
    public private(set) var isApplicationActive = false

    public var activeProfile: TorrentCoreConnectionProfile? {
        preferences.activeProfile
    }

    @ObservationIgnored private let profileStore: any TorrentCoreProfilePersisting
    @ObservationIgnored private let clientFactory: any TorrentCoreServiceClientBuilding
    @ObservationIgnored private let sleeper: any TorrentCoreSleeping
    @ObservationIgnored private let now: @Sendable () -> Date
    @ObservationIgnored private let restartRecoveryDelay: TimeInterval
    @ObservationIgnored private var client: (any TorrentCoreServiceClientProtocol)?
    @ObservationIgnored private var clientProfileID: UUID?
    @ObservationIgnored private var generation = 0
    @ObservationIgnored private var refreshLoopTask: Task<Void, Never>?
    @ObservationIgnored private var refreshTask: Task<Void, Never>?
    @ObservationIgnored private var refreshOperationID: UUID?

    public init(
        profileStore: any TorrentCoreProfilePersisting = UserDefaultsTorrentCoreProfileStore(),
        clientFactory: any TorrentCoreServiceClientBuilding = LiveTorrentCoreServiceClientFactory(),
        sleeper: any TorrentCoreSleeping = ContinuousTorrentCoreSleeper(),
        now: @escaping @Sendable () -> Date = { Date() },
        restartRecoveryDelay: TimeInterval = 2
    ) {
        self.profileStore = profileStore
        self.clientFactory = clientFactory
        self.sleeper = sleeper
        self.now = now
        self.restartRecoveryDelay = restartRecoveryDelay
    }

    deinit {
        refreshLoopTask?.cancel()
        refreshTask?.cancel()
    }

    public func load() async throws {
        let loadedPreferences = try await profileStore.load()
        preferences = loadedPreferences
        resetRemoteState()
        restartRefreshLoop()
    }

    @discardableResult
    public func addProfile(
        name: String,
        address: String,
        makeActive: Bool = true
    ) async throws -> TorrentCoreConnectionProfile {
        let profile = try TorrentCoreConnectionProfile(name: name, address: address, createdAt: now())
        try ensureUniqueAddress(profile.baseURL, excluding: nil)

        var updatedPreferences = preferences
        updatedPreferences.profiles.append(profile)
        updatedPreferences.profiles.sort {
            $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending
        }
        if makeActive {
            updatedPreferences.activeProfileID = profile.id
        }
        try await profileStore.save(updatedPreferences)

        let profileChanged = preferences.activeProfileID != updatedPreferences.activeProfileID
        preferences = updatedPreferences
        if profileChanged {
            resetRemoteState()
        }
        restartRefreshLoop()
        return profile
    }

    @discardableResult
    public func updateProfile(
        id: UUID,
        name: String,
        address: String
    ) async throws -> TorrentCoreConnectionProfile {
        guard let index = preferences.profiles.firstIndex(where: { $0.id == id }) else {
            throw TorrentCoreConnectionProfileError.profileNotFound
        }

        let existing = preferences.profiles[index]
        let updatedProfile = try TorrentCoreConnectionProfile(
            id: existing.id,
            name: name,
            address: address,
            createdAt: existing.createdAt,
            updatedAt: now()
        )
        try ensureUniqueAddress(updatedProfile.baseURL, excluding: id)

        var updatedPreferences = preferences
        updatedPreferences.profiles[index] = updatedProfile
        updatedPreferences.profiles.sort {
            $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending
        }
        try await profileStore.save(updatedPreferences)
        preferences = updatedPreferences

        if preferences.activeProfileID == id {
            resetRemoteState()
            restartRefreshLoop()
        }
        return updatedProfile
    }

    public func removeProfile(id: UUID) async throws {
        guard preferences.profiles.contains(where: { $0.id == id }) else {
            throw TorrentCoreConnectionProfileError.profileNotFound
        }

        var updatedPreferences = preferences
        updatedPreferences.profiles.removeAll { $0.id == id }
        if updatedPreferences.activeProfileID == id {
            updatedPreferences.activeProfileID = nil
        }
        try await profileStore.save(updatedPreferences)

        let removedActiveProfile = preferences.activeProfileID == id
        preferences = updatedPreferences
        if removedActiveProfile {
            resetRemoteState()
        }
        restartRefreshLoop()
    }

    public func selectProfile(id: UUID?) async throws {
        if let id, !preferences.profiles.contains(where: { $0.id == id }) {
            throw TorrentCoreConnectionProfileError.profileNotFound
        }
        guard preferences.activeProfileID != id else {
            return
        }

        var updatedPreferences = preferences
        updatedPreferences.activeProfileID = id
        try await profileStore.save(updatedPreferences)
        preferences = updatedPreferences
        resetRemoteState()
        restartRefreshLoop()
    }

    public func setRefreshInterval(_ interval: TorrentCoreRefreshInterval) async throws {
        guard preferences.refreshInterval != interval else {
            return
        }

        var updatedPreferences = preferences
        updatedPreferences.refreshInterval = interval
        try await profileStore.save(updatedPreferences)
        preferences = updatedPreferences
        if preferences.autoRefreshEnabled {
            restartRefreshLoop()
        }
    }

    public func setAutoRefreshEnabled(_ isEnabled: Bool) async throws {
        guard preferences.autoRefreshEnabled != isEnabled else {
            return
        }

        var updatedPreferences = preferences
        updatedPreferences.autoRefreshEnabled = isEnabled
        try await profileStore.save(updatedPreferences)
        preferences = updatedPreferences
        if isEnabled {
            restartRefreshLoop()
        } else {
            refreshLoopTask?.cancel()
            refreshLoopTask = nil
        }
    }

    public func testConnection(address: String) async throws -> TorrentCoreServiceHealth {
        let profile = try TorrentCoreConnectionProfile(name: "Connection Test", address: address)
        let testClient = try clientFactory.makeClient(baseURL: profile.baseURL)
        return try await testClient.probe()
    }

    public func setContext(_ context: TorrentCoreFeatureContext) {
        guard self.context != context else {
            return
        }
        self.context = context
        cancelCurrentRefresh()
        restartRefreshLoop()
    }

    public func setApplicationActive(_ isActive: Bool) {
        guard isApplicationActive != isActive else {
            return
        }
        isApplicationActive = isActive
        if !isActive {
            cancelCurrentRefresh()
        }
        restartRefreshLoop()
    }

    public func refresh() async {
        guard activeProfile != nil, context != .none else {
            return
        }
        if let refreshTask {
            await refreshTask.value
            return
        }

        let operationID = UUID()
        let requestGeneration = generation
        let requestedContext = context
        let requestedProfile = activeProfile
        refreshOperationID = operationID
        let operation = Task { [weak self] in
            guard let self, let requestedProfile else {
                return
            }
            await self.performRefresh(
                profile: requestedProfile,
                context: requestedContext,
                generation: requestGeneration
            )
        }
        refreshTask = operation
        await operation.value
        if refreshOperationID == operationID {
            refreshTask = nil
            refreshOperationID = nil
        }
    }

    public func canAddMagnet() -> Bool {
        connectionState.isConnected && hostStatus.value?.supportsMagnetAdds == true
    }

    public func canPause(_ torrent: TorrentCoreTorrentSummary) -> Bool {
        connectionState.isConnected
            && hostStatus.value?.supportsPause == true
            && torrent.canPause
    }

    public func canResume(_ torrent: TorrentCoreTorrentSummary) -> Bool {
        connectionState.isConnected
            && hostStatus.value?.supportsResume == true
            && torrent.canResume
    }

    public func canRemove(_ torrent: TorrentCoreTorrentSummary) -> Bool {
        connectionState.isConnected
            && hostStatus.value?.supportsRemove == true
            && torrent.canRemove
    }

    public func canRefreshMetadata(_ torrent: TorrentCoreTorrentSummary) -> Bool {
        connectionState.isConnected && torrent.canRefreshMetadata
    }

    public func canResetMetadataSession(_ torrent: TorrentCoreTorrentSummary) -> Bool {
        connectionState.isConnected && torrent.torrentID != nil
    }

    public func canRetryCompletionCallback(_ torrent: TorrentCoreTorrentSummary) -> Bool {
        connectionState.isConnected && torrent.canRetryCompletionCallback
    }

    public func addMagnet(
        _ magnetURI: String,
        categoryKey: String? = nil
    ) async throws -> TorrentCoreTorrentDetail {
        guard canAddMagnet() else {
            throw connectionState.isConnected
                ? TorrentCoreFeatureActionError.capabilityUnavailable
                : TorrentCoreFeatureActionError.offline
        }
        let serviceClient = try beginMutation(.addMagnet)
        do {
            let result = try await serviceClient.addMagnet(magnetURI, categoryKey: categoryKey)
            activeMutation = nil
            torrentDetail.succeed(result, at: now())
            await refresh()
            return result
        } catch {
            activeMutation = nil
            await handleMutationFailure(error)
            throw error
        }
    }

    public func pause(_ torrent: TorrentCoreTorrentSummary) async throws -> TorrentCoreActionResult {
        guard canPause(torrent), let torrentID = torrent.torrentID else {
            throw connectionState.isConnected
                ? TorrentCoreFeatureActionError.capabilityUnavailable
                : TorrentCoreFeatureActionError.offline
        }
        let serviceClient = try beginMutation(.pause)
        do {
            let result = try await serviceClient.pause(id: torrentID)
            activeMutation = nil
            await refresh()
            return result
        } catch {
            activeMutation = nil
            await handleMutationFailure(error)
            throw error
        }
    }

    public func resume(_ torrent: TorrentCoreTorrentSummary) async throws -> TorrentCoreActionResult {
        guard canResume(torrent), let torrentID = torrent.torrentID else {
            throw connectionState.isConnected
                ? TorrentCoreFeatureActionError.capabilityUnavailable
                : TorrentCoreFeatureActionError.offline
        }
        let serviceClient = try beginMutation(.resume)
        do {
            let result = try await serviceClient.resume(id: torrentID)
            activeMutation = nil
            await refresh()
            return result
        } catch {
            activeMutation = nil
            await handleMutationFailure(error)
            throw error
        }
    }

    public func remove(
        _ torrent: TorrentCoreTorrentSummary,
        deleteData: Bool
    ) async throws -> TorrentCoreActionResult {
        guard canRemove(torrent), let torrentID = torrent.torrentID else {
            throw connectionState.isConnected
                ? TorrentCoreFeatureActionError.capabilityUnavailable
                : TorrentCoreFeatureActionError.offline
        }
        let serviceClient = try beginMutation(.remove)
        do {
            let result = try await serviceClient.remove(id: torrentID, deleteData: deleteData)
            activeMutation = nil
            if context == .torrentDetail(torrentID) {
                torrentDetail.reset()
            } else {
                await refresh()
            }
            return result
        } catch {
            activeMutation = nil
            await handleMutationFailure(error)
            throw error
        }
    }

    public func refreshMetadata(
        _ torrent: TorrentCoreTorrentSummary
    ) async throws -> TorrentCoreActionResult {
        guard canRefreshMetadata(torrent), let torrentID = torrent.torrentID else {
            throw connectionState.isConnected
                ? TorrentCoreFeatureActionError.capabilityUnavailable
                : TorrentCoreFeatureActionError.offline
        }
        return try await performTorrentMutation(.refreshMetadata) { serviceClient in
            try await serviceClient.refreshMetadata(id: torrentID)
        }
    }

    public func resetMetadataSession(
        _ torrent: TorrentCoreTorrentSummary
    ) async throws -> TorrentCoreActionResult {
        guard canResetMetadataSession(torrent), let torrentID = torrent.torrentID else {
            throw connectionState.isConnected
                ? TorrentCoreFeatureActionError.capabilityUnavailable
                : TorrentCoreFeatureActionError.offline
        }
        return try await performTorrentMutation(.resetMetadata) { serviceClient in
            try await serviceClient.resetMetadataSession(id: torrentID)
        }
    }

    public func retryCompletionCallback(
        _ torrent: TorrentCoreTorrentSummary
    ) async throws -> TorrentCoreActionResult {
        guard canRetryCompletionCallback(torrent), let torrentID = torrent.torrentID else {
            throw connectionState.isConnected
                ? TorrentCoreFeatureActionError.capabilityUnavailable
                : TorrentCoreFeatureActionError.offline
        }
        return try await performTorrentMutation(.retryCompletionCallback) { serviceClient in
            try await serviceClient.retryCompletionCallback(id: torrentID)
        }
    }

    public func deleteOrphanedLogs() async throws -> TorrentCoreDeleteOrphanedLogsResult {
        let serviceClient = try beginMutation(.deleteOrphanedLogs)
        do {
            let result = try await serviceClient.deleteOrphanedLogs()
            activeMutation = nil
            await refresh()
            return result
        } catch {
            activeMutation = nil
            await handleMutationFailure(error)
            throw error
        }
    }

    public func updateRuntimeSettings(
        _ update: TorrentCoreRuntimeSettingsUpdate
    ) async throws -> TorrentCoreRuntimeSettings {
        let serviceClient = try beginMutation(.updateRuntimeSettings)
        do {
            let result = try await serviceClient.updateRuntimeSettings(update)
            activeMutation = nil
            runtimeSettings.succeed(result, at: now())
            return result
        } catch {
            activeMutation = nil
            await handleMutationFailure(error)
            throw error
        }
    }

    public func updateCategory(
        key: String,
        update: TorrentCoreCategoryUpdate
    ) async throws -> TorrentCoreCategory {
        let serviceClient = try beginMutation(.updateCategory)
        do {
            let result = try await serviceClient.updateCategory(key: key, update: update)
            activeMutation = nil
            var values = categories.value ?? []
            if let index = values.firstIndex(where: { $0.key == key }) {
                values[index] = result
            } else {
                values.append(result)
            }
            values.sort { $0.sortOrder < $1.sortOrder }
            categories.succeed(values, at: now())
            return result
        } catch {
            activeMutation = nil
            await handleMutationFailure(error)
            throw error
        }
    }

    public func restartService() async throws -> TorrentCoreServiceRestartResult {
        let serviceClient = try beginMutation(.restartService)
        do {
            let result = try await serviceClient.restartService()
            if let profileID = activeProfile?.id {
                connectionState = .notConnected(profileID: profileID)
            }

            if restartRecoveryDelay > 0 {
                try await Task.sleep(
                    nanoseconds: UInt64(restartRecoveryDelay * 1_000_000_000)
                )
            }
            for attempt in 0..<15 {
                do {
                    let serviceHealth = try await serviceClient.probe()
                    health = serviceHealth
                    if let profileID = activeProfile?.id {
                        connectionState = .connected(profileID: profileID, connectedAt: now())
                    }
                    activeMutation = nil
                    await refresh()
                    return result
                } catch where attempt < 14 {
                    try await Task.sleep(for: .seconds(2))
                }
            }

            throw TorrentCoreClientError.offline
        } catch {
            activeMutation = nil
            await handleMutationFailure(error)
            throw error
        }
    }

    private func performTorrentMutation(
        _ operation: TorrentCoreOperation,
        request: (any TorrentCoreServiceClientProtocol) async throws -> TorrentCoreActionResult
    ) async throws -> TorrentCoreActionResult {
        let serviceClient = try beginMutation(operation)
        do {
            let result = try await request(serviceClient)
            activeMutation = nil
            await refresh()
            return result
        } catch {
            activeMutation = nil
            await handleMutationFailure(error)
            throw error
        }
    }

    private func beginMutation(
        _ operation: TorrentCoreOperation
    ) throws -> any TorrentCoreServiceClientProtocol {
        guard connectionState.isConnected, let client else {
            throw TorrentCoreFeatureActionError.offline
        }
        guard activeMutation == nil else {
            throw TorrentCoreFeatureActionError.actionAlreadyInProgress
        }
        activeMutation = operation
        return client
    }

    private func handleMutationFailure(_ error: any Error) async {
        if isConnectivityFailure(error), let profileID = activeProfile?.id {
            connectionState = .offline(
                profileID: profileID,
                attemptedAt: now(),
                message: errorMessage(error)
            )
        }
        if case let TorrentCoreClientError.timedOut(_, outcomeUncertain) = error, outcomeUncertain {
            await refresh()
        }
    }

    private func restartRefreshLoop() {
        refreshLoopTask?.cancel()
        refreshLoopTask = nil

        guard isApplicationActive, activeProfile != nil, context != .none else {
            return
        }

        let interval = preferences.refreshInterval.seconds
        let repeats = preferences.autoRefreshEnabled && context.refreshesPeriodically
        refreshLoopTask = Task { [weak self, sleeper] in
            guard let self else {
                return
            }
            await self.refresh()
            guard repeats else {
                return
            }

            while !Task.isCancelled {
                do {
                    try await sleeper.sleep(for: interval)
                } catch {
                    return
                }
                guard !Task.isCancelled else {
                    return
                }
                await self.refresh()
            }
        }
    }

    private func cancelCurrentRefresh() {
        generation += 1
        refreshTask?.cancel()
        refreshTask = nil
        refreshOperationID = nil
    }

    private func resetRemoteState() {
        cancelCurrentRefresh()
        client = nil
        clientProfileID = nil
        health = nil
        hostStatus.reset()
        dashboardLifecycle.reset()
        torrents.reset()
        torrentDetail.reset()
        categories.reset()
        history.reset()
        historyDetail.reset()
        abandonedHistory.reset()
        logs.reset()
        peers.reset()
        trackers.reset()
        runtimeSettings.reset()
        activeMutation = nil

        if let profileID = preferences.activeProfileID {
            connectionState = .notConnected(profileID: profileID)
        } else {
            connectionState = .noProfile
        }
    }

    private func ensureUniqueAddress(_ baseURL: URL, excluding profileID: UUID?) throws {
        let normalizedAddress = baseURL.absoluteString.lowercased()
        if preferences.profiles.contains(where: {
            $0.id != profileID && $0.baseURL.absoluteString.lowercased() == normalizedAddress
        }) {
            throw TorrentCoreConnectionProfileError.duplicateAddress
        }
    }

    private func serviceClient(
        for profile: TorrentCoreConnectionProfile
    ) throws -> any TorrentCoreServiceClientProtocol {
        if clientProfileID == profile.id, let client {
            return client
        }
        let newClient = try clientFactory.makeClient(baseURL: profile.baseURL)
        client = newClient
        clientProfileID = profile.id
        return newClient
    }

    private func performRefresh(
        profile: TorrentCoreConnectionProfile,
        context: TorrentCoreFeatureContext,
        generation: Int
    ) async {
        beginLoading(context)
        do {
            let serviceClient = try serviceClient(for: profile)
            let newlyLoadedHost = try await ensureConnected(
                serviceClient,
                profile: profile,
                forceProbe: context == .connection,
                generation: generation,
                context: context
            )
            guard isCurrent(profile: profile, context: context, generation: generation) else {
                return
            }

            switch context {
            case .none:
                return
            case .connection:
                return
            case .dashboard:
                let status: TorrentCoreHostStatus
                let lifecycle: TorrentCoreDashboardLifecycle
                if let newlyLoadedHost {
                    status = newlyLoadedHost
                    lifecycle = try await serviceClient.dashboardLifecycle()
                } else {
                    async let statusRequest = serviceClient.hostStatus()
                    async let lifecycleRequest = serviceClient.dashboardLifecycle()
                    (status, lifecycle) = try await (statusRequest, lifecycleRequest)
                }
                guard isCurrent(profile: profile, context: context, generation: generation) else {
                    return
                }
                let completedAt = now()
                hostStatus.succeed(status, at: completedAt)
                dashboardLifecycle.succeed(lifecycle, at: completedAt)
            case .torrents:
                let summaries = try await serviceClient.torrents()
                guard isCurrent(profile: profile, context: context, generation: generation) else {
                    return
                }
                torrents.succeed(summaries, at: now())
            case let .torrentDetail(torrentID):
                let detail = try await serviceClient.torrent(id: torrentID)
                guard isCurrent(profile: profile, context: context, generation: generation) else {
                    return
                }
                torrentDetail.succeed(detail, at: now())
            case let .torrentListAndDetail(torrentID):
                let summaries = try await serviceClient.torrents()
                guard isCurrent(profile: profile, context: context, generation: generation) else {
                    return
                }
                torrents.succeed(summaries, at: now())

                guard summaries.contains(where: { $0.torrentID == torrentID }) else {
                    torrentDetail.reset()
                    return
                }

                do {
                    let detail = try await serviceClient.torrent(id: torrentID)
                    guard isCurrent(profile: profile, context: context, generation: generation) else {
                        return
                    }
                    torrentDetail.succeed(detail, at: now())
                } catch {
                    guard isCurrent(profile: profile, context: context, generation: generation),
                          !(error is CancellationError)
                    else {
                        return
                    }
                    let message = errorMessage(error)
                    torrentDetail.fail(message: message)
                    if isConnectivityFailure(error) {
                        connectionState = .offline(
                            profileID: profile.id,
                            attemptedAt: now(),
                            message: message
                        )
                    }
                }
            case .addMagnet:
                let availableCategories = try await serviceClient.categories()
                guard isCurrent(profile: profile, context: context, generation: generation) else {
                    return
                }
                categories.succeed(availableCategories, at: now())
            case let .history(query, selectedTorrentID):
                async let historyRequest = serviceClient.history(query: query)
                async let abandonedRequest = serviceClient.history(
                    query: .init(outcome: .abandoned, take: 500)
                )
                let (historyValues, abandonedValues) = try await (
                    historyRequest,
                    abandonedRequest
                )
                guard isCurrent(profile: profile, context: context, generation: generation) else {
                    return
                }
                let completedAt = now()
                history.succeed(historyValues, at: completedAt)
                abandonedHistory.succeed(abandonedValues, at: completedAt)

                if let selectedTorrentID {
                    let detail = try await serviceClient.historyDetail(torrentID: selectedTorrentID)
                    guard isCurrent(profile: profile, context: context, generation: generation) else {
                        return
                    }
                    historyDetail.succeed(detail, at: now())
                } else {
                    historyDetail.reset()
                }
            case let .logs(query):
                let values = try await serviceClient.logs(query: query)
                guard isCurrent(profile: profile, context: context, generation: generation) else {
                    return
                }
                logs.succeed(values, at: now())
            case let .peers(torrentID):
                let values = try await serviceClient.peers(torrentID: torrentID)
                guard isCurrent(profile: profile, context: context, generation: generation) else {
                    return
                }
                peers.succeed(values, at: now())
            case let .trackers(torrentID):
                let values = try await serviceClient.trackers(torrentID: torrentID)
                guard isCurrent(profile: profile, context: context, generation: generation) else {
                    return
                }
                trackers.succeed(values, at: now())
            case .serviceSettings:
                async let settingsRequest = serviceClient.runtimeSettings()
                async let categoriesRequest = serviceClient.categories()
                let (settings, availableCategories) = try await (
                    settingsRequest,
                    categoriesRequest
                )
                guard isCurrent(profile: profile, context: context, generation: generation) else {
                    return
                }
                let completedAt = now()
                runtimeSettings.succeed(settings, at: completedAt)
                categories.succeed(availableCategories, at: completedAt)
            }
        } catch {
            guard isCurrent(profile: profile, context: context, generation: generation),
                  !(error is CancellationError)
            else {
                return
            }
            let message = errorMessage(error)
            markContextFailure(context, message: message)
            if isConnectivityFailure(error) {
                connectionState = .offline(profileID: profile.id, attemptedAt: now(), message: message)
            }
        }
    }

    private func ensureConnected(
        _ serviceClient: any TorrentCoreServiceClientProtocol,
        profile: TorrentCoreConnectionProfile,
        forceProbe: Bool,
        generation: Int,
        context: TorrentCoreFeatureContext
    ) async throws -> TorrentCoreHostStatus? {
        if connectionState.isConnected && !forceProbe {
            return nil
        }

        connectionState = .connecting(profileID: profile.id)
        let serviceHealth = try await serviceClient.probe()
        guard isCurrent(profile: profile, context: context, generation: generation) else {
            throw CancellationError()
        }

        let loadedHost: TorrentCoreHostStatus?
        if hostStatus.value == nil {
            loadedHost = try await serviceClient.hostStatus()
        } else {
            loadedHost = nil
        }
        guard isCurrent(profile: profile, context: context, generation: generation) else {
            throw CancellationError()
        }

        health = serviceHealth
        if let loadedHost {
            hostStatus.succeed(loadedHost, at: now())
        }
        connectionState = .connected(profileID: profile.id, connectedAt: now())
        return loadedHost
    }

    private func beginLoading(_ context: TorrentCoreFeatureContext) {
        switch context {
        case .dashboard:
            hostStatus.beginLoading()
            dashboardLifecycle.beginLoading()
        case .torrents:
            torrents.beginLoading()
        case .torrentDetail:
            torrentDetail.beginLoading()
        case .torrentListAndDetail:
            torrents.beginLoading()
            torrentDetail.beginLoading()
        case .addMagnet:
            categories.beginLoading()
        case .history:
            history.beginLoading()
            abandonedHistory.beginLoading()
            historyDetail.beginLoading()
        case .logs:
            logs.beginLoading()
        case .peers:
            peers.beginLoading()
        case .trackers:
            trackers.beginLoading()
        case .serviceSettings:
            runtimeSettings.beginLoading()
            categories.beginLoading()
        case .none, .connection:
            break
        }
    }

    private func markContextFailure(_ context: TorrentCoreFeatureContext, message: String) {
        switch context {
        case .dashboard:
            hostStatus.fail(message: message)
            dashboardLifecycle.fail(message: message)
        case .torrents:
            torrents.fail(message: message)
        case .torrentDetail:
            torrentDetail.fail(message: message)
        case .torrentListAndDetail:
            torrents.fail(message: message)
            torrentDetail.fail(message: message)
        case .addMagnet:
            categories.fail(message: message)
        case .history:
            history.fail(message: message)
            abandonedHistory.fail(message: message)
            historyDetail.fail(message: message)
        case .logs:
            logs.fail(message: message)
        case .peers:
            peers.fail(message: message)
        case .trackers:
            trackers.fail(message: message)
        case .serviceSettings:
            runtimeSettings.fail(message: message)
            categories.fail(message: message)
        case .none, .connection:
            break
        }
    }

    private func isCurrent(
        profile: TorrentCoreConnectionProfile,
        context: TorrentCoreFeatureContext,
        generation: Int
    ) -> Bool {
        self.generation == generation
            && activeProfile?.id == profile.id
            && self.context == context
            && !Task.isCancelled
    }

    private func isConnectivityFailure(_ error: any Error) -> Bool {
        switch error {
        case TorrentCoreClientError.offline,
             TorrentCoreClientError.timedOut,
             TorrentCoreClientError.transport,
             TorrentCoreClientError.unexpectedService,
             TorrentCoreClientError.unsupportedAPIVersion,
             TorrentCoreClientError.invalidPayload:
            true
        default:
            false
        }
    }

    private func errorMessage(_ error: any Error) -> String {
        if let localizedError = error as? LocalizedError,
           let description = localizedError.errorDescription
        {
            return description
        }
        return error.localizedDescription
    }
}
