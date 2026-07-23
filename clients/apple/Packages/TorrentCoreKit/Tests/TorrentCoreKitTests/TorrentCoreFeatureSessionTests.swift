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
func foregroundStartsOneLoopAndBackgroundStopsIt() async throws {
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
    session.setContext(.torrents)

    session.setApplicationActive(true)
    session.setApplicationActive(true)
    await waitUntil {
        await client.calls.torrents == 1
    }
    await waitUntil {
        await sleeper.intervals == [15]
    }
    #expect(await client.calls.torrents == 1)

    try await session.setRefreshInterval(.fiveSeconds)
    await waitUntil {
        await sleeper.intervals.last == 5
    }
    #expect(session.preferences.refreshInterval == .fiveSeconds)

    session.setApplicationActive(false)
    #expect(!session.isApplicationActive)
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
        var addMagnet = 0
        var pause = 0
        var resume = 0
        var remove = 0
    }

    private(set) var calls = Calls()
    private var isOffline = false
    private var torrentValues: [TorrentCoreTorrentSummary]
    private var shouldSuspendTorrentRequest = false
    private var suspendedTorrentContinuation: CheckedContinuation<Void, Never>?

    var hasSuspendedTorrentRequest: Bool {
        suspendedTorrentContinuation != nil
    }

    init(torrents: [TorrentCoreTorrentSummary]) {
        torrentValues = torrents
    }

    func setOffline(_ isOffline: Bool) {
        self.isOffline = isOffline
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
        return TorrentCorePreviewFixtures.connectedHealth
    }

    func hostStatus() async throws -> TorrentCoreHostStatus {
        calls.hostStatus += 1
        try checkConnection()
        return TorrentCorePreviewFixtures.hostStatus
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
        throw TorrentCoreClientError.unexpectedResponse(statusCode: 404)
    }

    func categories() async throws -> [TorrentCoreCategory] {
        calls.categories += 1
        try checkConnection()
        return []
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
