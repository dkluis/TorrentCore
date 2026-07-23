import Foundation
import TorrentCoreAPI

public protocol TorrentCoreServiceClientProtocol: Sendable {
    func probe() async throws -> TorrentCoreServiceHealth
    func hostStatus() async throws -> TorrentCoreHostStatus
    func dashboardLifecycle() async throws -> TorrentCoreDashboardLifecycle
    func torrents() async throws -> [TorrentCoreTorrentSummary]
    func torrent(id: UUID) async throws -> TorrentCoreTorrentDetail
    func categories() async throws -> [TorrentCoreCategory]
    func addMagnet(_ magnetURI: String, categoryKey: String?) async throws -> TorrentCoreTorrentDetail
    func pause(id: UUID) async throws -> TorrentCoreActionResult
    func resume(id: UUID) async throws -> TorrentCoreActionResult
    func remove(id: UUID, deleteData: Bool) async throws -> TorrentCoreActionResult
}

extension TorrentCoreClient: TorrentCoreServiceClientProtocol {}

public protocol TorrentCoreServiceClientBuilding: Sendable {
    func makeClient(baseURL: URL) throws -> any TorrentCoreServiceClientProtocol
}

public struct LiveTorrentCoreServiceClientFactory: TorrentCoreServiceClientBuilding {
    private let timeouts: TorrentCoreTimeouts

    public init(timeouts: TorrentCoreTimeouts = .init()) {
        self.timeouts = timeouts
    }

    public func makeClient(baseURL: URL) throws -> any TorrentCoreServiceClientProtocol {
        try TorrentCoreClient(baseURL: baseURL, timeouts: timeouts)
    }
}

public protocol TorrentCoreSleeping: Sendable {
    func sleep(for interval: TimeInterval) async throws
}

public struct ContinuousTorrentCoreSleeper: TorrentCoreSleeping {
    public init() {}

    public func sleep(for interval: TimeInterval) async throws {
        let nanoseconds = UInt64(max(0, interval) * 1_000_000_000)
        try await Task.sleep(nanoseconds: nanoseconds)
    }
}
