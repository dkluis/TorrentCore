import Foundation
import Testing
import TorrentCoreAPI
@testable import TorrentCoreFeatures

@Test
func profileAddressesAndDevicePreferencesAreNormalizedAndPersisted() async throws {
    let suiteName = "TorrentCoreFeatureSessionTests.\(UUID().uuidString)"
    let defaults = try #require(UserDefaults(suiteName: suiteName))
    defer {
        defaults.removePersistentDomain(forName: suiteName)
    }

    let profile = try TorrentCoreConnectionProfile(
        name: "  CA-Desktop  ",
        address: "TorrentCore.Local:7033/"
    )
    #expect(profile.name == "CA-Desktop")
    #expect(profile.baseURL.absoluteString == "http://torrentcore.local:7033")
    #expect(TorrentCoreRefreshInterval.defaultValue == .fifteenSeconds)
    #expect(TorrentCoreRefreshInterval.allCases.map(\.rawValue) == [5, 10, 15])

    let preferences = TorrentCoreClientPreferences(
        profiles: [profile],
        activeProfileID: profile.id,
        refreshInterval: .fiveSeconds
    )
    let store = UserDefaultsTorrentCoreProfileStore(suiteName: suiteName)
    try await store.save(preferences)
    let reloaded = try await store.load()

    #expect(reloaded == preferences)
    #expect(reloaded.activeProfile == profile)
    #expect(reloaded.autoRefreshEnabled)
}

@Test
func versionOnePreferencesMigrateWithoutLosingConnections() async throws {
    let suiteName = "TorrentCoreFeatureSessionMigrationTests.\(UUID().uuidString)"
    let defaults = try #require(UserDefaults(suiteName: suiteName))
    defer {
        defaults.removePersistentDomain(forName: suiteName)
    }

    let profile = try TorrentCoreConnectionProfile(
        name: "Existing",
        address: "http://existing.test:7033"
    )
    let current = TorrentCoreClientPreferences(
        profiles: [profile],
        activeProfileID: profile.id,
        refreshInterval: .fiveSeconds
    )
    let encoded = try JSONEncoder().encode(current)
    var object = try #require(
        JSONSerialization.jsonObject(with: encoded) as? [String: Any]
    )
    object["schemaVersion"] = 1
    object.removeValue(forKey: "autoRefreshEnabled")
    defaults.set(
        try JSONSerialization.data(withJSONObject: object),
        forKey: UserDefaultsTorrentCoreProfileStore.legacyStorageKey
    )

    let store = UserDefaultsTorrentCoreProfileStore(suiteName: suiteName)
    let migrated = try await store.load()

    #expect(migrated.schemaVersion == 2)
    #expect(migrated.activeProfile == profile)
    #expect(migrated.refreshInterval == .fiveSeconds)
    #expect(migrated.autoRefreshEnabled)
    #expect(defaults.data(forKey: UserDefaultsTorrentCoreProfileStore.storageKey) != nil)
}

@Test
func profileValidationRejectsUnsafeAndDuplicateAddresses() async throws {
    #expect(throws: TorrentCoreConnectionProfileError.emptyName) {
        try TorrentCoreConnectionProfile(name: " ", address: "torrentcore.local:7033")
    }
    for address in [
        "ftp://torrentcore.local:7033",
        "http://user:password@torrentcore.local:7033",
        "http://torrentcore.local:7033/api",
        "http://torrentcore.local:7033?target=other",
    ] {
        #expect(throws: TorrentCoreConnectionProfileError.invalidAddress) {
            try TorrentCoreConnectionProfile(name: "Invalid", address: address)
        }
    }

    let session = await TorrentCoreFeatureSession(
        profileStore: MemoryProfileStore(),
        clientFactory: FakeClientFactory(clients: [:])
    )
    _ = try await session.addProfile(name: "First", address: "torrentcore.local:7033")
    await #expect(throws: TorrentCoreConnectionProfileError.duplicateAddress) {
        try await session.addProfile(name: "Second", address: "HTTP://TORRENTCORE.LOCAL:7033/")
    }
}

@Test
@MainActor
func switchingProfilesClearsAllInstallationState() async throws {
    let firstProfile = try TorrentCoreConnectionProfile(
        name: "First",
        address: "http://first.test:7033"
    )
    let secondProfile = try TorrentCoreConnectionProfile(
        name: "Second",
        address: "http://second.test:7033"
    )
    let firstTorrent = torrent(named: "First installation")
    let secondTorrent = torrent(named: "Second installation")
    let firstClient = FakeServiceClient(torrents: [firstTorrent])
    let secondClient = FakeServiceClient(torrents: [secondTorrent])
    let preferences = TorrentCoreClientPreferences(
        profiles: [firstProfile, secondProfile],
        activeProfileID: firstProfile.id
    )
    let session = TorrentCoreFeatureSession(
        profileStore: MemoryProfileStore(preferences),
        clientFactory: FakeClientFactory(clients: [
            firstProfile.baseURL: firstClient,
            secondProfile.baseURL: secondClient,
        ])
    )

    try await session.load()
    session.setContext(.torrents)
    await session.refresh()
    #expect(session.torrents.value?.first?.name == "First installation")

    try await session.selectProfile(id: secondProfile.id)
    #expect(session.torrents.value == nil)
    #expect(session.health == nil)
    #expect(session.hostStatus.value == nil)

    await session.refresh()
    #expect(session.torrents.value?.first?.name == "Second installation")
}

@Test
@MainActor
func aCancelledOldProfileResponseCannotReplaceNewProfileState() async throws {
    let firstProfile = try TorrentCoreConnectionProfile(
        name: "Slow",
        address: "http://slow.test:7033"
    )
    let secondProfile = try TorrentCoreConnectionProfile(
        name: "Current",
        address: "http://current.test:7033"
    )
    let firstClient = FakeServiceClient(torrents: [torrent(named: "Stale response")])
    let secondClient = FakeServiceClient(torrents: [torrent(named: "Current response")])
    await firstClient.suspendNextTorrentRequest()
    let session = TorrentCoreFeatureSession(
        profileStore: MemoryProfileStore(.init(
            profiles: [firstProfile, secondProfile],
            activeProfileID: firstProfile.id
        )),
        clientFactory: FakeClientFactory(clients: [
            firstProfile.baseURL: firstClient,
            secondProfile.baseURL: secondClient,
        ])
    )
    try await session.load()
    session.setContext(.torrents)

    let oldRefresh = Task { @MainActor in
        await session.refresh()
    }
    await waitUntil {
        await firstClient.hasSuspendedTorrentRequest
    }

    try await session.selectProfile(id: secondProfile.id)
    await session.refresh()
    #expect(session.torrents.value?.first?.name == "Current response")

    await firstClient.resumeTorrentRequest()
    await oldRefresh.value
    #expect(session.torrents.value?.first?.name == "Current response")
}

@Test
@MainActor
func changingFeatureContextDoesNotChangeUnrelatedSnapshotPhases() async throws {
    let profile = try TorrentCoreConnectionProfile(
        name: "Loading",
        address: "http://loading.test:7033"
    )
    let session = TorrentCoreFeatureSession(
        profileStore: MemoryProfileStore(.init(
            profiles: [profile],
            activeProfileID: profile.id
        )),
        clientFactory: FakeClientFactory(clients: [:])
    )
    try await session.load()

    #expect(session.torrents.phase == .idle)
    session.setContext(.torrents)
    #expect(session.torrents.phase == .idle)

    #expect(session.logs.phase == .idle)
    session.setContext(.logs(.init(take: 100)))
    #expect(session.logs.phase == .idle)
    #expect(session.torrents.phase == .idle)

    #expect(session.runtimeSettings.phase == .idle)
    #expect(session.categories.phase == .idle)
    session.setContext(.serviceSettings)
    #expect(session.runtimeSettings.phase == .idle)
    #expect(session.categories.phase == .idle)
}

@Test
@MainActor
func refreshOnlyLoadsTheOpenFeatureContext() async throws {
    let profile = try TorrentCoreConnectionProfile(
        name: "Context",
        address: "http://context.test:7033"
    )
    let client = FakeServiceClient(torrents: [.initPreview])
    let session = TorrentCoreFeatureSession(
        profileStore: MemoryProfileStore(.init(profiles: [profile], activeProfileID: profile.id)),
        clientFactory: FakeClientFactory(clients: [profile.baseURL: client])
    )
    try await session.load()

    session.setContext(.torrents)
    await session.refresh()
    var calls = await client.calls
    #expect(calls.probe == 1)
    #expect(calls.hostStatus == 1)
    #expect(calls.torrents == 1)
    #expect(calls.dashboardLifecycle == 0)
    #expect(calls.categories == 0)

    session.setContext(.dashboard)
    await session.refresh()
    calls = await client.calls
    #expect(calls.probe == 1)
    #expect(calls.hostStatus == 2)
    #expect(calls.dashboardLifecycle == 1)
    #expect(calls.torrents == 1)

    session.setContext(.addMagnet)
    await session.refresh()
    calls = await client.calls
    #expect(calls.categories == 1)
    #expect(calls.torrents == 1)
    #expect(calls.dashboardLifecycle == 1)
}

@Test
@MainActor
func operationalContextsLoadOnlyTheirVisibleData() async throws {
    let profile = try TorrentCoreConnectionProfile(
        name: "Operations",
        address: "http://operations.test:7033"
    )
    let client = FakeServiceClient(torrents: [.initPreview])
    let session = TorrentCoreFeatureSession(
        profileStore: MemoryProfileStore(.init(profiles: [profile], activeProfileID: profile.id)),
        clientFactory: FakeClientFactory(clients: [profile.baseURL: client])
    )
    try await session.load()

    session.setContext(.history(
        query: .init(outcome: .active, take: 100),
        selectedTorrentID: TorrentCorePreviewFixtures.torrentID
    ))
    await session.refreshHistoryFilterOptions()
    await session.refresh()
    var calls = await client.calls
    #expect(calls.history == 2)
    #expect(calls.historyFilterOptions == 1)
    #expect(calls.historyDetail == 1)
    #expect(calls.logs == 0)
    #expect(session.history.value?.isEmpty == false)
    #expect(session.abandonedHistory.value?.isEmpty == false)
    #expect(session.historyFilterOptions.value?.categoryKeys == ["Movies", "TV"])

    session.setContext(.logs(.init(take: 500, level: .warning)))
    await session.refreshActivityLogFilterOptions()
    await session.refresh()
    await session.refresh()
    calls = await client.calls
    #expect(calls.logs == 2)
    #expect(calls.activityLogFilterOptions == 1)
    #expect(calls.history == 2)
    #expect(session.logs.value?.count == TorrentCorePreviewFixtures.activityLogs.count)
    #expect(session.activityLogFilterOptions.value?.categories == ["runtime", "torrent"])

    session.setContext(.peers(TorrentCorePreviewFixtures.torrentID))
    await session.refresh()
    session.setContext(.trackers(TorrentCorePreviewFixtures.torrentID))
    await session.refresh()
    calls = await client.calls
    #expect(calls.peers == 1)
    #expect(calls.trackers == 1)
    #expect(session.peers.value == TorrentCorePreviewFixtures.peers)
    #expect(session.trackers.value == TorrentCorePreviewFixtures.trackers)

    session.setContext(.serviceSettings)
    await session.refresh()
    calls = await client.calls
    #expect(calls.runtimeSettings == 1)
    #expect(calls.categories == 1)
    #expect(session.runtimeSettings.value == TorrentCorePreviewFixtures.runtimeSettings)
}

@Test
@MainActor
func operationalMutationsRemainSingleItemAndRefreshAuthoritativeState() async throws {
    let profile = try TorrentCoreConnectionProfile(
        name: "Mutations",
        address: "http://mutations.test:7033"
    )
    var actionableTorrent = TorrentCorePreviewFixtures.downloadingTorrent
    actionableTorrent.canRefreshMetadata = true
    actionableTorrent.canRetryCompletionCallback = true
    let client = FakeServiceClient(torrents: [actionableTorrent])
    let session = TorrentCoreFeatureSession(
        profileStore: MemoryProfileStore(.init(profiles: [profile], activeProfileID: profile.id)),
        clientFactory: FakeClientFactory(clients: [profile.baseURL: client]),
        restartRecoveryDelay: 0
    )
    try await session.load()
    session.setContext(.torrents)
    await session.refresh()

    _ = try await session.refreshMetadata(actionableTorrent)
    _ = try await session.resetMetadataSession(actionableTorrent)
    _ = try await session.retryCompletionCallback(actionableTorrent)

    session.setContext(.logs(.init()))
    await session.refresh()
    _ = try await session.deleteOrphanedLogs()
    let logCleanup = try await session.cleanupLogs(upToDate: "2026-07-21")
    let historyCleanup = try await session.cleanupHistory(upToDate: "2026-06-28")
    #expect(logCleanup.deletedRecordCount == 3)
    #expect(historyCleanup.deletedRecordCount == 2)

    session.setContext(.serviceSettings)
    await session.refresh()
    var update = TorrentCoreRuntimeSettingsUpdate(
        settings: TorrentCorePreviewFixtures.runtimeSettings
    )
    update.maxActiveDownloads = 8
    update.engineAllowPeerExchange = true
    update.metadataResolutionTimeSliceMinutes = 20
    update.automaticMetadataResetStuckThresholdSeconds = 60
    update.vpnEgressValidationEnabled = true
    update.vpnEgressValidationEndpoint = "https://vpn-check.example.test/ip"
    update.vpnEgressDirectIspCidrs = ["198.51.100.0/24"]
    update.vpnEgressDegradedCheckIntervalSeconds = 30
    update.vpnEgressReadyCheckIntervalSeconds = 120
    update.vpnEgressRequestTimeoutSeconds = 5
    update.vpnEgressEngineSuspensionTimeoutSeconds = 7
    update.runtimeTickDurationSummaryEnabled = true
    let updatedSettings = try await session.updateRuntimeSettings(update)
    #expect(updatedSettings.engineAllowPeerExchange)
    #expect(updatedSettings.metadataResolutionTimeSliceMinutes == 20)
    #expect(updatedSettings.automaticMetadataResetStuckThresholdSeconds == 60)
    #expect(updatedSettings.vpnEgressValidationEnabled)
    #expect(updatedSettings.vpnEgressDirectIspCidrs == ["198.51.100.0/24"])
    #expect(updatedSettings.vpnEgressEngineSuspensionTimeoutSeconds == 7)
    #expect(updatedSettings.runtimeTickDurationSummaryEnabled)
    let category = try #require(TorrentCorePreviewFixtures.categories.first)
    _ = try await session.updateCategory(
        key: try #require(category.key),
        update: TorrentCoreCategoryUpdate(category: category)
    )
    _ = try await session.restartService()

    let calls = await client.calls
    #expect(calls.refreshMetadata == 1)
    #expect(calls.resetMetadataSession == 1)
    #expect(calls.retryCompletionCallback == 1)
    #expect(calls.deleteOrphanedLogs == 1)
    #expect(calls.cleanupLogs == 1)
    #expect(calls.cleanupHistory == 1)
    #expect(calls.updateRuntimeSettings == 1)
    #expect(calls.updateCategory == 1)
    #expect(calls.restartService == 1)
    #expect(session.activeMutation == nil)
    #expect(session.connectionState.isConnected)
}

@Test
@MainActor
func torrentInspectorContextRefreshesListAndSelectedDetail() async throws {
    let profile = try TorrentCoreConnectionProfile(
        name: "Inspector",
        address: "http://inspector.test:7033"
    )
    let client = FakeServiceClient(torrents: [.initPreview])
    let session = TorrentCoreFeatureSession(
        profileStore: MemoryProfileStore(.init(profiles: [profile], activeProfileID: profile.id)),
        clientFactory: FakeClientFactory(clients: [profile.baseURL: client])
    )
    try await session.load()

    session.setContext(.torrentListAndDetail(TorrentCorePreviewFixtures.torrentID))
    await session.refresh()

    let calls = await client.calls
    #expect(calls.torrents == 1)
    #expect(calls.torrentDetail == 1)
    #expect(session.torrents.value?.count == 1)
    #expect(session.torrentDetail.value?.torrentID == TorrentCorePreviewFixtures.torrentID)
}

@Test
@MainActor
func visibleRefreshUsesTheSelectedIntervalAndStopsWhenCancelled() async throws {
    let profile = try TorrentCoreConnectionProfile(
        name: "Foreground",
        address: "http://foreground.test:7033"
    )
    let client = FakeServiceClient(torrents: [.initPreview])
    let sleeper = RecordingSleeper()
    let session = TorrentCoreFeatureSession(
        profileStore: MemoryProfileStore(.init(profiles: [profile], activeProfileID: profile.id)),
        clientFactory: FakeClientFactory(clients: [profile.baseURL: client]),
        sleeper: sleeper
    )
    try await session.load()

    let initialPolling = Task { @MainActor in
        await session.refreshWhileVisible(.torrents)
    }
    await waitUntil {
        await client.calls.torrents == 1
    }
    await waitUntil {
        await sleeper.intervals == [15]
    }
    #expect(await client.calls.torrents == 1)
    initialPolling.cancel()
    await initialPolling.value

    try await session.setRefreshInterval(.fiveSeconds)
    let updatedPolling = Task { @MainActor in
        await session.refreshWhileVisible(.torrents)
    }
    await waitUntil {
        await sleeper.intervals.last == 5
    }
    #expect(session.preferences.refreshInterval == .fiveSeconds)
    updatedPolling.cancel()
    await updatedPolling.value
}

@Test
@MainActor
func peerAndTrackerDiagnosticsUseTheGlobalRefreshInterval() async throws {
    let profile = try TorrentCoreConnectionProfile(
        name: "Diagnostics polling",
        address: "http://diagnostics-polling.test:7033"
    )
    let client = FakeServiceClient(
        torrents: [.initPreview],
        peers: TorrentCorePreviewFixtures.peers,
        trackers: TorrentCorePreviewFixtures.trackers
    )
    let sleeper = ImmediateSleeper()
    let session = TorrentCoreFeatureSession(
        profileStore: MemoryProfileStore(.init(
            profiles: [profile],
            activeProfileID: profile.id,
            refreshInterval: .tenSeconds
        )),
        clientFactory: FakeClientFactory(clients: [profile.baseURL: client]),
        sleeper: sleeper
    )
    try await session.load()

    let peerPolling = Task { @MainActor in
        await session.refreshWhileVisible(.peers(TorrentCorePreviewFixtures.torrentID))
    }
    await waitUntil {
        await client.calls.peers >= 2
    }
    peerPolling.cancel()
    await peerPolling.value
    #expect(await client.calls.peers >= 2)

    let trackerPolling = Task { @MainActor in
        await session.refreshWhileVisible(.trackers(TorrentCorePreviewFixtures.torrentID))
    }
    await waitUntil {
        await client.calls.trackers >= 2
    }
    trackerPolling.cancel()
    await trackerPolling.value
    #expect(await client.calls.trackers >= 2)
    #expect(await sleeper.intervals.isEmpty == false)
    #expect(await sleeper.intervals.allSatisfy { $0 == 10 })
}

@Test
@MainActor
func masterDataContextsLoadOnceEvenWhenAutoRefreshIsEnabled() async throws {
    let profile = try TorrentCoreConnectionProfile(
        name: "Master data",
        address: "http://master-data.test:7033"
    )
    let client = FakeServiceClient(torrents: [.initPreview])
    let sleeper = ImmediateSleeper()
    let session = TorrentCoreFeatureSession(
        profileStore: MemoryProfileStore(.init(
            profiles: [profile],
            activeProfileID: profile.id,
            refreshInterval: .fiveSeconds
        )),
        clientFactory: FakeClientFactory(clients: [profile.baseURL: client]),
        sleeper: sleeper
    )
    try await session.load()

    await session.refresh(.addMagnet)
    for _ in 0..<20 {
        await Task.yield()
    }
    #expect(await client.calls.categories == 1)
    #expect(session.categories.phase == .current)
    #expect(session.categories.value?.isEmpty == false)
    #expect(session.context == .none)

    await session.refresh(.serviceSettings)
    for _ in 0..<20 {
        await Task.yield()
    }

    let calls = await client.calls
    #expect(calls.runtimeSettings == 1)
    #expect(calls.categories == 2)
    #expect(await sleeper.intervals.isEmpty)
    #expect(session.context == .none)
}

@Test
@MainActor
func disabledAutoRefreshLoadsTheContextOnceWithoutPolling() async throws {
    let profile = try TorrentCoreConnectionProfile(
        name: "Manual",
        address: "http://manual.test:7033"
    )
    let client = FakeServiceClient(torrents: [.initPreview])
    let sleeper = RecordingSleeper()
    let session = TorrentCoreFeatureSession(
        profileStore: MemoryProfileStore(.init(
            profiles: [profile],
            activeProfileID: profile.id,
            autoRefreshEnabled: false
        )),
        clientFactory: FakeClientFactory(clients: [profile.baseURL: client]),
        sleeper: sleeper
    )
    try await session.load()
    await session.refreshWhileVisible(.torrents)
    for _ in 0..<20 {
        await Task.yield()
    }

    #expect(await client.calls.torrents == 1)
    #expect(await sleeper.intervals.isEmpty)
}

@Test
func torrentFilteringAndPaginationMatchTheOperatorGridSemantics() {
    var uncategorized = TorrentCorePreviewFixtures.pausedTorrent
    uncategorized.categoryKey = nil
    let values = [TorrentCorePreviewFixtures.downloadingTorrent, uncategorized]
    let filter = TorrentCoreTorrentFilter(
        searchText: "paused",
        state: TorrentCoreTorrentState.paused.rawValue,
        category: .uncategorized
    )

    let filtered = filter.apply(to: values)
    #expect(filtered.map(\.torrentID) == [uncategorized.torrentID])

    let manyValues = Array(0..<60)
    let page = TorrentCorePagination.page(
        manyValues,
        index: 2,
        size: .twentyFive
    )
    #expect(page.values == Array(50..<60))
    #expect(page.pageIndex == 2)
    #expect(page.pageCount == 3)
    #expect(page.totalCount == 60)
}

@Test
@MainActor
func offlineRefreshKeepsLastKnownStateAndDisablesActions() async throws {
    let profile = try TorrentCoreConnectionProfile(
        name: "Offline",
        address: "http://offline.test:7033"
    )
    let summary = TorrentCoreTorrentSummary.initPreview
    let client = FakeServiceClient(torrents: [summary])
    let session = TorrentCoreFeatureSession(
        profileStore: MemoryProfileStore(.init(profiles: [profile], activeProfileID: profile.id)),
        clientFactory: FakeClientFactory(clients: [profile.baseURL: client])
    )
    try await session.load()
    session.setContext(.torrents)
    await session.refresh()
    #expect(session.canPause(summary))

    await client.setOffline(true)
    await session.refresh()

    #expect(session.torrents.value?.first?.torrentID == summary.torrentID)
    if case .stale = session.torrents.phase {
        // Expected.
    } else {
        Issue.record("Expected the last-known torrent state to be marked stale.")
    }
    #expect(!session.canPause(summary))
    await #expect(throws: TorrentCoreFeatureActionError.offline) {
        try await session.pause(summary)
    }
}

@Test
@MainActor
func aLateResponseFromAnOldContextCannotPopulateHiddenState() async throws {
    let profile = try TorrentCoreConnectionProfile(
        name: "Slow context",
        address: "http://slow-context.test:7033"
    )
    let client = FakeServiceClient(torrents: [torrent(named: "Late torrent")])
    await client.suspendNextTorrentRequest()
    let session = TorrentCoreFeatureSession(
        profileStore: MemoryProfileStore(.init(
            profiles: [profile],
            activeProfileID: profile.id
        )),
        clientFactory: FakeClientFactory(clients: [profile.baseURL: client])
    )
    try await session.load()
    session.setContext(.torrents)

    let oldRefresh = Task { @MainActor in
        await session.refresh()
    }
    await waitUntil {
        await client.hasSuspendedTorrentRequest
    }

    session.setContext(.logs(.init(take: 100)))
    await session.refresh()
    #expect(session.logs.value?.isEmpty == false)

    await client.resumeTorrentRequest()
    await oldRefresh.value
    #expect(session.logs.value?.isEmpty == false)
    #expect(session.torrents.value == nil)
}

@Test
@MainActor
func reconnectingToANewServiceInstanceClearsOldSnapshotsAndReloadsOpenContext() async throws {
    let profile = try TorrentCoreConnectionProfile(
        name: "Restarted host",
        address: "http://restarted-host.test:7033"
    )
    let client = FakeServiceClient(torrents: [.initPreview])
    let session = TorrentCoreFeatureSession(
        profileStore: MemoryProfileStore(.init(
            profiles: [profile],
            activeProfileID: profile.id
        )),
        clientFactory: FakeClientFactory(clients: [profile.baseURL: client])
    )
    try await session.load()

    session.setContext(.torrents)
    await session.refresh()
    #expect(session.torrents.value?.isEmpty == false)

    session.setContext(.logs(.init(take: 100)))
    await session.refresh()
    #expect(session.logs.value?.isEmpty == false)

    let replacementInstanceID = UUID()
    await client.setServiceInstanceID(replacementInstanceID)
    await client.setOffline(true)
    await session.refresh()
    #expect(!session.connectionState.isConnected)

    await client.setOffline(false)
    await session.refresh()

    #expect(session.connectionState.isConnected)
    #expect(session.hostStatus.value?.serviceInstanceID == replacementInstanceID)
    #expect(session.logs.value?.isEmpty == false)
    #expect(session.logs.phase == .current)
    #expect(session.torrents.value == nil)
    #expect(session.history.value == nil)
    #expect(session.runtimeSettings.value == nil)
    #expect(session.preferences.activeProfile == profile)
}

@Test
@MainActor
func requestedRestartRetriesThenAcceptsTheNewServiceInstance() async throws {
    let profile = try TorrentCoreConnectionProfile(
        name: "Restart recovery",
        address: "http://restart-recovery.test:7033"
    )
    let client = FakeServiceClient(torrents: [.initPreview])
    let sleeper = ImmediateSleeper()
    let session = TorrentCoreFeatureSession(
        profileStore: MemoryProfileStore(.init(
            profiles: [profile],
            activeProfileID: profile.id
        )),
        clientFactory: FakeClientFactory(clients: [profile.baseURL: client]),
        sleeper: sleeper,
        restartRecoveryDelay: 0
    )
    try await session.load()
    session.setContext(.torrents)
    await session.refresh()
    #expect(session.torrents.value?.isEmpty == false)

    session.setContext(.serviceSettings)
    await session.refresh()
    let replacementInstanceID = UUID()
    await client.setServiceInstanceID(replacementInstanceID)
    await client.failNextProbes(2)

    _ = try await session.restartService()

    #expect(session.connectionState.isConnected)
    #expect(session.activeMutation == nil)
    #expect(session.hostStatus.value?.serviceInstanceID == replacementInstanceID)
    #expect(session.runtimeSettings.phase == .current)
    #expect(session.categories.phase == .current)
    #expect(session.torrents.value == nil)
    #expect(await sleeper.intervals == [2, 2])
    let calls = await client.calls
    #expect(calls.restartService == 1)
    #expect(calls.probe == 4)
    #expect(calls.hostStatus == 2)
}

@Test
@MainActor
func agreedMaximumFixtureCollectionsLoadWithoutClientTruncation() async throws {
    let profile = try TorrentCoreConnectionProfile(
        name: "Large fixtures",
        address: "http://large-fixtures.test:7033"
    )
    let client = FakeServiceClient(
        torrents: makeTorrents(count: 100),
        history: makeHistory(count: 500),
        logs: makeLogs(count: 5_000),
        peers: makePeers(count: 250),
        trackers: makeTrackers(count: 50)
    )
    let session = TorrentCoreFeatureSession(
        profileStore: MemoryProfileStore(.init(
            profiles: [profile],
            activeProfileID: profile.id
        )),
        clientFactory: FakeClientFactory(clients: [profile.baseURL: client])
    )
    try await session.load()

    session.setContext(.torrents)
    await session.refresh()
    #expect(session.torrents.value?.count == 100)

    session.setContext(.history(query: .init(take: 500), selectedTorrentID: nil))
    await session.refresh()
    #expect(session.history.value?.count == 500)

    session.setContext(.logs(.init(take: 5_000)))
    await session.refresh()
    #expect(session.logs.value?.count == 5_000)

    session.setContext(.peers(TorrentCorePreviewFixtures.torrentID))
    await session.refresh()
    #expect(session.peers.value?.count == 250)

    session.setContext(.trackers(TorrentCorePreviewFixtures.torrentID))
    await session.refresh()
    #expect(session.trackers.value?.count == 50)
}

@Test
@MainActor
func aSuccessfulSingleItemMutationRefreshesTheOpenContext() async throws {
    let profile = try TorrentCoreConnectionProfile(
        name: "Mutation",
        address: "http://mutation.test:7033"
    )
    let summary = TorrentCoreTorrentSummary.initPreview
    let client = FakeServiceClient(torrents: [summary])
    let session = TorrentCoreFeatureSession(
        profileStore: MemoryProfileStore(.init(profiles: [profile], activeProfileID: profile.id)),
        clientFactory: FakeClientFactory(clients: [profile.baseURL: client])
    )
    try await session.load()
    session.setContext(.torrents)
    await session.refresh()

    let result = try await session.pause(summary)
    let calls = await client.calls
    #expect(result.action == "pause")
    #expect(calls.pause == 1)
    #expect(calls.torrents == 2)
    #expect(session.activeMutation == nil)
}

private actor MemoryProfileStore: TorrentCoreProfilePersisting {
    private var preferences: TorrentCoreClientPreferences

    init(_ preferences: TorrentCoreClientPreferences = .init()) {
        self.preferences = preferences
    }

    func load() async throws -> TorrentCoreClientPreferences {
        preferences
    }

    func save(_ preferences: TorrentCoreClientPreferences) async throws {
        self.preferences = preferences
    }
}

private actor RecordingSleeper: TorrentCoreSleeping {
    private(set) var intervals: [TimeInterval] = []

    func sleep(for interval: TimeInterval) async throws {
        intervals.append(interval)
        try await Task.sleep(nanoseconds: 3_600_000_000_000)
    }
}

private actor ImmediateSleeper: TorrentCoreSleeping {
    private(set) var intervals: [TimeInterval] = []

    func sleep(for interval: TimeInterval) async throws {
        intervals.append(interval)
    }
}

private struct FakeClientFactory: TorrentCoreServiceClientBuilding {
    let clients: [URL: FakeServiceClient]

    func makeClient(baseURL: URL) throws -> any TorrentCoreServiceClientProtocol {
        guard let client = clients[baseURL] else {
            throw TorrentCoreClientError.invalidBaseURL
        }
        return client
    }
}

private actor FakeServiceClient: TorrentCoreServiceClientProtocol {
    struct Calls: Sendable {
        var probe = 0
        var hostStatus = 0
        var dashboardLifecycle = 0
        var torrents = 0
        var torrentDetail = 0
        var categories = 0
        var history = 0
        var historyFilterOptions = 0
        var historyDetail = 0
        var logs = 0
        var activityLogFilterOptions = 0
        var peers = 0
        var trackers = 0
        var runtimeSettings = 0
        var addMagnet = 0
        var pause = 0
        var resume = 0
        var remove = 0
        var refreshMetadata = 0
        var resetMetadataSession = 0
        var retryCompletionCallback = 0
        var deleteOrphanedLogs = 0
        var cleanupLogs = 0
        var cleanupHistory = 0
        var updateRuntimeSettings = 0
        var updateCategory = 0
        var restartService = 0
    }

    private(set) var calls = Calls()
    private var isOffline = false
    private var probeFailuresRemaining = 0
    private var hostStatusValue: TorrentCoreHostStatus
    private var torrentValues: [TorrentCoreTorrentSummary]
    private var historyValues: [TorrentCoreHistorySummary]
    private var logValues: [TorrentCoreActivityLogEntry]
    private var peerValues: [TorrentCorePeer]
    private var trackerValues: [TorrentCoreTracker]
    private var shouldSuspendTorrentRequest = false
    private var suspendedTorrentContinuation: CheckedContinuation<Void, Never>?

    var hasSuspendedTorrentRequest: Bool {
        suspendedTorrentContinuation != nil
    }

    init(
        torrents: [TorrentCoreTorrentSummary],
        history: [TorrentCoreHistorySummary] = TorrentCorePreviewFixtures.history,
        logs: [TorrentCoreActivityLogEntry] = TorrentCorePreviewFixtures.activityLogs,
        peers: [TorrentCorePeer] = TorrentCorePreviewFixtures.peers,
        trackers: [TorrentCoreTracker] = TorrentCorePreviewFixtures.trackers
    ) {
        hostStatusValue = TorrentCorePreviewFixtures.hostStatus
        torrentValues = torrents
        historyValues = history
        logValues = logs
        peerValues = peers
        trackerValues = trackers
    }

    func setOffline(_ isOffline: Bool) {
        self.isOffline = isOffline
    }

    func setServiceInstanceID(_ serviceInstanceID: UUID) {
        hostStatusValue.serviceInstanceID = serviceInstanceID
    }

    func failNextProbes(_ count: Int) {
        probeFailuresRemaining = count
    }

    func suspendNextTorrentRequest() {
        shouldSuspendTorrentRequest = true
    }

    func resumeTorrentRequest() {
        suspendedTorrentContinuation?.resume()
        suspendedTorrentContinuation = nil
    }

    func probe() async throws -> TorrentCoreServiceHealth {
        calls.probe += 1
        try checkConnection()
        if probeFailuresRemaining > 0 {
            probeFailuresRemaining -= 1
            throw TorrentCoreClientError.offline
        }
        return TorrentCorePreviewFixtures.connectedHealth
    }

    func hostStatus() async throws -> TorrentCoreHostStatus {
        calls.hostStatus += 1
        try checkConnection()
        return hostStatusValue
    }

    func dashboardLifecycle() async throws -> TorrentCoreDashboardLifecycle {
        calls.dashboardLifecycle += 1
        try checkConnection()
        return TorrentCorePreviewFixtures.dashboardLifecycle
    }

    func torrents() async throws -> [TorrentCoreTorrentSummary] {
        calls.torrents += 1
        try checkConnection()
        if shouldSuspendTorrentRequest {
            shouldSuspendTorrentRequest = false
            await withCheckedContinuation { continuation in
                suspendedTorrentContinuation = continuation
            }
        }
        return torrentValues
    }

    func torrent(id: UUID) async throws -> TorrentCoreTorrentDetail {
        calls.torrentDetail += 1
        try checkConnection()
        guard torrentValues.contains(where: { $0.torrentID == id }) else {
            throw TorrentCoreClientError.unexpectedResponse(statusCode: 404)
        }
        var detail = TorrentCorePreviewFixtures.torrentDetail
        detail.torrentID = id
        detail.name = torrentValues.first(where: { $0.torrentID == id })?.name
        return detail
    }

    func categories() async throws -> [TorrentCoreCategory] {
        calls.categories += 1
        try checkConnection()
        return TorrentCorePreviewFixtures.categories
    }

    func history(query: TorrentCoreHistoryQuery) async throws -> [TorrentCoreHistorySummary] {
        calls.history += 1
        try checkConnection()
        var values = historyValues
        if let outcome = query.outcome {
            values = values.filter { $0.outcome == outcome }
        }
        if let take = query.take, take > 0 {
            values = Array(values.prefix(take))
        }
        return values
    }

    func historyFilterOptions() async throws -> TorrentCoreHistoryFilterOptions {
        calls.historyFilterOptions += 1
        try checkConnection()
        return .init(
            categoryKeys: ["Movies", "TV"],
            states: ["Completed", "Downloading"]
        )
    }

    func historyDetail(torrentID: UUID) async throws -> TorrentCoreHistoryDetail {
        calls.historyDetail += 1
        try checkConnection()
        var detail = TorrentCorePreviewFixtures.historyDetail
        detail.torrentID = torrentID
        return detail
    }

    func logs(query: TorrentCoreLogQuery) async throws -> [TorrentCoreActivityLogEntry] {
        calls.logs += 1
        try checkConnection()
        return Array(logValues.prefix(query.take))
    }

    func activityLogFilterOptions() async throws -> TorrentCoreActivityLogFilterOptions {
        calls.activityLogFilterOptions += 1
        try checkConnection()
        return .init(
            categories: ["runtime", "torrent"],
            eventTypes: ["runtime.operation.slow", "torrent.added"]
        )
    }

    func peers(torrentID: UUID) async throws -> [TorrentCorePeer] {
        calls.peers += 1
        try checkConnection()
        return peerValues
    }

    func trackers(torrentID: UUID) async throws -> [TorrentCoreTracker] {
        calls.trackers += 1
        try checkConnection()
        return trackerValues
    }

    func runtimeSettings() async throws -> TorrentCoreRuntimeSettings {
        calls.runtimeSettings += 1
        try checkConnection()
        return TorrentCorePreviewFixtures.runtimeSettings
    }

    func addMagnet(
        _ magnetURI: String,
        categoryKey: String?
    ) async throws -> TorrentCoreTorrentDetail {
        calls.addMagnet += 1
        try checkConnection()
        throw TorrentCoreClientError.unexpectedResponse(statusCode: 501)
    }

    func pause(id: UUID) async throws -> TorrentCoreActionResult {
        calls.pause += 1
        try checkConnection()
        return TorrentCorePreviewFixtures.actionResult(action: "pause", state: .paused)
    }

    func resume(id: UUID) async throws -> TorrentCoreActionResult {
        calls.resume += 1
        try checkConnection()
        return TorrentCorePreviewFixtures.actionResult(action: "resume", state: .downloading)
    }

    func remove(id: UUID, deleteData: Bool) async throws -> TorrentCoreActionResult {
        calls.remove += 1
        try checkConnection()
        return TorrentCorePreviewFixtures.actionResult(
            action: "remove",
            state: .removed,
            dataDeleted: deleteData
        )
    }

    func refreshMetadata(id: UUID) async throws -> TorrentCoreActionResult {
        calls.refreshMetadata += 1
        try checkConnection()
        return TorrentCorePreviewFixtures.actionResult(
            action: "refreshMetadata",
            state: .resolvingMetadata
        )
    }

    func resetMetadataSession(id: UUID) async throws -> TorrentCoreActionResult {
        calls.resetMetadataSession += 1
        try checkConnection()
        return TorrentCorePreviewFixtures.actionResult(
            action: "resetMetadataSession",
            state: .resolvingMetadata
        )
    }

    func retryCompletionCallback(id: UUID) async throws -> TorrentCoreActionResult {
        calls.retryCompletionCallback += 1
        try checkConnection()
        return TorrentCorePreviewFixtures.actionResult(
            action: "retryCompletionCallback",
            state: .completed
        )
    }

    func deleteOrphanedLogs() async throws -> TorrentCoreDeleteOrphanedLogsResult {
        calls.deleteOrphanedLogs += 1
        try checkConnection()
        return .init(deletedLogEntryCount: 1)
    }

    func cleanupLogs(upToDate: String) async throws -> TorrentCoreCleanupResult {
        calls.cleanupLogs += 1
        try checkConnection()
        return .init(
            upToDate: upToDate,
            cutoffUTC: Date(),
            deletedRecordCount: 3
        )
    }

    func cleanupHistory(upToDate: String) async throws -> TorrentCoreCleanupResult {
        calls.cleanupHistory += 1
        try checkConnection()
        return .init(
            upToDate: upToDate,
            cutoffUTC: Date(),
            deletedRecordCount: 2
        )
    }

    func updateRuntimeSettings(
        _ update: TorrentCoreRuntimeSettingsUpdate
    ) async throws -> TorrentCoreRuntimeSettings {
        calls.updateRuntimeSettings += 1
        try checkConnection()
        var settings = TorrentCorePreviewFixtures.runtimeSettings
        settings.engineAllowPeerExchange = update.engineAllowPeerExchange
        settings.maxActiveDownloads = update.maxActiveDownloads
        settings.metadataResolutionTimeSliceMinutes = update.metadataResolutionTimeSliceMinutes
        settings.automaticMetadataResetStuckThresholdSeconds = update.automaticMetadataResetStuckThresholdSeconds
        settings.vpnEgressValidationEnabled = update.vpnEgressValidationEnabled
        settings.vpnEgressValidationEndpoint = update.vpnEgressValidationEndpoint
        settings.vpnEgressDirectIspCidrs = update.vpnEgressDirectIspCidrs
        settings.vpnEgressDegradedCheckIntervalSeconds = update.vpnEgressDegradedCheckIntervalSeconds
        settings.vpnEgressReadyCheckIntervalSeconds = update.vpnEgressReadyCheckIntervalSeconds
        settings.vpnEgressRequestTimeoutSeconds = update.vpnEgressRequestTimeoutSeconds
        settings.vpnEgressEngineSuspensionTimeoutSeconds =
            update.vpnEgressEngineSuspensionTimeoutSeconds
        settings.runtimeTickDurationSummaryEnabled = update.runtimeTickDurationSummaryEnabled
        return settings
    }

    func updateCategory(
        key: String,
        update: TorrentCoreCategoryUpdate
    ) async throws -> TorrentCoreCategory {
        calls.updateCategory += 1
        try checkConnection()
        return try #require(
            TorrentCorePreviewFixtures.categories.first(where: { $0.key == key })
        )
    }

    func restartService() async throws -> TorrentCoreServiceRestartResult {
        calls.restartService += 1
        try checkConnection()
        return TorrentCorePreviewFixtures.restartResult
    }

    private func checkConnection() throws {
        if isOffline {
            throw TorrentCoreClientError.offline
        }
    }
}

private extension TorrentCoreTorrentSummary {
    static var initPreview: Self {
        TorrentCorePreviewFixtures.downloadingTorrent
    }
}

private func torrent(named name: String) -> TorrentCoreTorrentSummary {
    var torrent = TorrentCoreTorrentSummary.initPreview
    torrent.name = name
    return torrent
}

private func makeTorrents(count: Int) -> [TorrentCoreTorrentSummary] {
    (0..<count).map { index in
        var torrent = TorrentCoreTorrentSummary.initPreview
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

private func makeHistory(count: Int) -> [TorrentCoreHistorySummary] {
    let template = TorrentCorePreviewFixtures.history.first {
        $0.outcome == .active
    }!
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

private func makeLogs(count: Int) -> [TorrentCoreActivityLogEntry] {
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

private func makePeers(count: Int) -> [TorrentCorePeer] {
    let template = TorrentCorePreviewFixtures.peers[0]
    return (0..<count).map { index in
        var peer = template
        peer.endpoint = "192.0.2.\((index % 250) + 1):\(10_000 + index)"
        return peer
    }
}

private func makeTrackers(count: Int) -> [TorrentCoreTracker] {
    let template = TorrentCorePreviewFixtures.trackers[0]
    return (0..<count).map { index in
        var tracker = template
        tracker.tierNumber = index / 10
        tracker.trackerNumber = index
        return tracker
    }
}

@MainActor
private func waitUntil(
    _ condition: @escaping @MainActor () async -> Bool
) async {
    for _ in 0..<200 {
        if await condition() {
            return
        }
        await Task.yield()
    }
    Issue.record("Timed out waiting for an asynchronous condition.")
}
