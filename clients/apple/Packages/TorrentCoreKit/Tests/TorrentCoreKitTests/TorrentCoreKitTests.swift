import Foundation
import HTTPTypes
import OpenAPIRuntime
import Testing
@testable import TorrentCoreAPI
@testable import TorrentCoreFeatures
@testable import TorrentCoreSupport

@Test
func sharedTargetsExposeTheSameProductIdentity() {
    #expect(TorrentCoreProduct.displayName == "TorrentCore")
    #expect(TorrentCoreAPIFoundation.productName == TorrentCoreProduct.displayName)
    #expect(TorrentCoreFeatureFoundation.productName == TorrentCoreProduct.displayName)
}

@Test
func initialSliceBuildsDeterministicRequestsAndDecodesFixtures() async throws {
    let recorder = RequestRecorder()
    let transport = FixtureTransport(recorder: recorder)
    let client = try TorrentCoreClient(
        baseURL: #require(URL(string: "http://torrentcore.test:7033")),
        healthTransport: transport,
        readTransport: transport,
        mutationTransport: transport
    )
    let torrentID = try #require(UUID(uuidString: FixturePayloads.torrentID))

    let health = try await client.probe()
    let host = try await client.hostStatus()
    let lifecycle = try await client.dashboardLifecycle()
    let torrents = try await client.torrents()
    let detail = try await client.torrent(id: torrentID)
    let categories = try await client.categories()
    let added = try await client.addMagnet("magnet:?xt=urn:btih:ABC", categoryKey: "tv")
    let paused = try await client.pause(id: torrentID)
    let resumed = try await client.resume(id: torrentID)
    let removed = try await client.remove(id: torrentID, deleteData: true)

    #expect(health.apiVersion == 1)
    #expect(host.serviceInstanceID?.uuidString == FixturePayloads.serviceInstanceID.uppercased())
    #expect(lifecycle.recentEvents.count == 1)
    #expect(torrents.first?.state.rawValue == "FutureTorrentState")
    #expect(detail.torrentID == torrentID)
    #expect(categories.first?.key == "tv")
    #expect(added.magnetURI == "magnet:?xt=urn:btih:ABC")
    #expect(paused.state == .paused)
    #expect(resumed.state == .downloading)
    #expect(removed.dataDeleted == true)

    let requests = await recorder.entries
    #expect(requests.map(\.operationID) == [
        "Health_Get",
        "Host_GetStatus",
        "Host_GetDashboardLifecycle",
        "Torrents_GetAll",
        "Torrents_GetById",
        "Categories_GetAll",
        "Torrents_Add",
        "Torrents_Pause",
        "Torrents_Resume",
        "Torrents_Remove",
    ])
    #expect(requests.map(\.method) == [
        "GET", "GET", "GET", "GET", "GET", "GET", "POST", "POST", "POST", "POST",
    ])
    #expect(requests[4].path == "/api/torrents/\(torrentID.uuidString)")
    #expect(requests[7].path == "/api/torrents/\(torrentID.uuidString)/pause")
    #expect(requests[8].path == "/api/torrents/\(torrentID.uuidString)/resume")
    #expect(requests[9].path == "/api/torrents/\(torrentID.uuidString)/remove")

    let addBody = try #require(requests[6].body).jsonObject
    #expect(addBody["magnetUri"] as? String == "magnet:?xt=urn:btih:ABC")
    #expect(addBody["categoryKey"] as? String == "tv")
    let removeBody = try #require(requests[9].body).jsonObject
    #expect(removeBody["deleteData"] as? Bool == true)
}

@Test
func problemDetailsAndNetworkFailuresKeepTheirMeaning() async throws {
    let serviceClient = try TorrentCoreClient(
        baseURL: #require(URL(string: "http://torrentcore.test:7033")),
        healthTransport: FailureTransport(failure: .problem),
        readTransport: FailureTransport(failure: .problem),
        mutationTransport: FailureTransport(failure: .problem)
    )

    do {
        _ = try await serviceClient.addMagnet("not-a-magnet")
        Issue.record("Expected a typed service error")
    } catch let TorrentCoreClientError.service(problem) {
        #expect(problem.code == "torrent.invalid_magnet")
        #expect(problem.target == "magnetUri")
        #expect(problem.traceID == "trace-123")
        #expect(problem.errors["magnetUri"] == ["A valid magnet URI is required."])
    }

    for (failure, expected) in [
        (FailureTransport.Failure.offline, "offline"),
        (.timeout, "timeout"),
        (.cancelled, "cancelled"),
    ] {
        let client = try TorrentCoreClient(
            baseURL: #require(URL(string: "http://torrentcore.test:7033")),
            healthTransport: FailureTransport(failure: failure),
            readTransport: FailureTransport(failure: failure),
            mutationTransport: FailureTransport(failure: failure)
        )
        do {
            _ = try await client.addMagnet("magnet:?xt=urn:btih:ABC")
            Issue.record("Expected \(expected) error")
        } catch TorrentCoreClientError.offline {
            #expect(expected == "offline")
        } catch let TorrentCoreClientError.timedOut(operation, outcomeUncertain) {
            #expect(expected == "timeout")
            #expect(operation == .addMagnet)
            #expect(outcomeUncertain)
        } catch TorrentCoreClientError.cancelled {
            #expect(expected == "cancelled")
        }
    }

    let invalidPayloadClient = try TorrentCoreClient(
        baseURL: #require(URL(string: "http://torrentcore.test:7033")),
        healthTransport: InvalidPayloadTransport(),
        readTransport: InvalidPayloadTransport(),
        mutationTransport: InvalidPayloadTransport()
    )
    do {
        _ = try await invalidPayloadClient.probe()
        Issue.record("Expected an invalid-payload error")
    } catch TorrentCoreClientError.invalidPayload {
        // Expected.
    }
}

@Test
func baseURLValidationIsNarrowAndPredictable() throws {
    #expect(
        try TorrentCoreClient.normalizedBaseURL(
            #require(URL(string: "http://ca-server.local:7033"))
        ).absoluteString == "http://ca-server.local:7033"
    )
    #expect(throws: TorrentCoreClientError.self) {
        try TorrentCoreClient.normalizedBaseURL(
            #require(URL(string: "ftp://ca-server.local:7033"))
        )
    }
}

@Test
func liveReadOnlyIntegrationProbe() async throws {
    guard let value = ProcessInfo.processInfo.environment["TORRENTCORE_INTEGRATION_BASE_URL"],
          let baseURL = URL(string: value)
    else {
        return
    }

    let client = try TorrentCoreClient(baseURL: baseURL)
    _ = try await liveStep("health") { try await client.probe() }
    _ = try await liveStep("host status") { try await client.hostStatus() }
    _ = try await liveStep("dashboard lifecycle") { try await client.dashboardLifecycle() }
    let torrents = try await liveStep("torrent list") { try await client.torrents() }
    _ = try await liveStep("categories") { try await client.categories() }
    if let torrentID = torrents.first?.torrentID {
        _ = try await liveStep("torrent detail") { try await client.torrent(id: torrentID) }
    }
}

private struct LiveProbeStepError: Error, CustomStringConvertible {
    let step: String
    let underlying: any Error

    var description: String {
        "\(step): \(underlying)"
    }
}

private func liveStep<Value>(
    _ step: String,
    operation: () async throws -> Value
) async throws -> Value {
    do {
        return try await operation()
    } catch {
        throw LiveProbeStepError(step: step, underlying: error)
    }
}

private actor RequestRecorder {
    struct Entry: Sendable {
        var operationID: String
        var method: String
        var path: String?
        var body: String?
    }

    private(set) var entries: [Entry] = []

    func append(_ entry: Entry) {
        entries.append(entry)
    }
}

private struct FixtureTransport: ClientTransport {
    let recorder: RequestRecorder

    func send(
        _ request: HTTPRequest,
        body: HTTPBody?,
        baseURL: URL,
        operationID: String
    ) async throws -> (HTTPResponse, HTTPBody?) {
        let bodyString: String?
        if let body {
            bodyString = try await String(collecting: body, upTo: 1_048_576)
        } else {
            bodyString = nil
        }
        await recorder.append(.init(
            operationID: operationID,
            method: request.method.rawValue,
            path: request.path,
            body: bodyString
        ))

        let fixture = FixturePayloads.response(for: operationID)
        let headers: HTTPFields = [.contentType: "application/json"]
        return (
            HTTPResponse(status: .init(code: fixture.statusCode), headerFields: headers),
            HTTPBody(fixture.body)
        )
    }
}

private struct FailureTransport: ClientTransport {
    enum Failure: Sendable {
        case problem
        case offline
        case timeout
        case cancelled
    }

    let failure: Failure

    func send(
        _ request: HTTPRequest,
        body: HTTPBody?,
        baseURL: URL,
        operationID: String
    ) async throws -> (HTTPResponse, HTTPBody?) {
        switch failure {
        case .problem:
            let headers: HTTPFields = [.contentType: "application/json"]
            return (
                HTTPResponse(status: .badRequest, headerFields: headers),
                HTTPBody(FixturePayloads.problem)
            )
        case .offline:
            throw URLError(.cannotConnectToHost)
        case .timeout:
            throw URLError(.timedOut)
        case .cancelled:
            throw CancellationError()
        }
    }
}

private struct InvalidPayloadTransport: ClientTransport {
    func send(
        _ request: HTTPRequest,
        body: HTTPBody?,
        baseURL: URL,
        operationID: String
    ) async throws -> (HTTPResponse, HTTPBody?) {
        let headers: HTTPFields = [.contentType: "application/json"]
        return (HTTPResponse(status: .ok, headerFields: headers), HTTPBody("{}"))
    }
}

private enum FixturePayloads {
    static let torrentID = "11111111-2222-3333-4444-555555555555"
    static let serviceInstanceID = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"

    static func response(for operationID: String) -> (statusCode: Int, body: String) {
        switch operationID {
        case "Health_Get":
            (200, health)
        case "Host_GetStatus":
            (200, host)
        case "Host_GetDashboardLifecycle":
            (200, lifecycle)
        case "Torrents_GetAll":
            (200, "[\(summary)]")
        case "Torrents_GetById":
            (200, detail)
        case "Categories_GetAll":
            (200, "[\(category)]")
        case "Torrents_Add":
            (201, detail)
        case "Torrents_Pause":
            (200, action(state: "Paused", action: "pause", dataDeleted: false))
        case "Torrents_Resume":
            (200, action(state: "Downloading", action: "resume", dataDeleted: false))
        case "Torrents_Remove":
            (200, action(state: "Removed", action: "remove", dataDeleted: true))
        default:
            (500, "{}")
        }
    }

    static let health = """
    {
      "apiVersion": 1,
      "serviceName": "TorrentCore.Service",
      "status": "ok",
      "environmentName": "Integration",
      "checkedAtUtc": "2026-07-23T12:00:00Z"
    }
    """

    static let host = """
    {
      "apiVersion": 1,
      "serviceName": "TorrentCore.Service",
      "serviceVersion": "1.0.0",
      "serviceInstanceId": "\(serviceInstanceID)",
      "engineRuntime": "MonoTorrent",
      "engineListenPort": 55123,
      "engineDhtPort": 55123,
      "enginePortForwardingEnabled": true,
      "engineLocalPeerDiscoveryEnabled": true,
      "engineEncryptionMode": "EncryptedPreferred",
      "engineMaximumConnections": 200,
      "engineMaximumHalfOpenConnections": 20,
      "engineMaximumDownloadRateBytesPerSecond": 0,
      "engineMaximumUploadRateBytesPerSecond": 0,
      "engineConnectionFailureLogBurstLimit": 10,
      "engineConnectionFailureLogWindowSeconds": 60,
      "maxActiveMetadataResolutions": 2,
      "maxActiveDownloads": 3,
      "availableMetadataResolutionSlots": 1,
      "availableDownloadSlots": 2,
      "resolvingMetadataCount": 1,
      "metadataQueueCount": 0,
      "downloadingCount": 1,
      "downloadQueueCount": 0,
      "seedingCount": 1,
      "pausedCount": 0,
      "completedCount": 2,
      "errorCount": 0,
      "currentConnectedPeerCount": 8,
      "currentDownloadRateBytesPerSecond": 4096,
      "currentUploadRateBytesPerSecond": 1024,
      "partialFilesEnabled": true,
      "partialFileSuffix": ".!mt",
      "seedingStopMode": "Ratio",
      "seedingStopRatio": 2.0,
      "seedingStopMinutes": 0,
      "completedTorrentCleanupMode": "Never",
      "completedTorrentCleanupMinutes": 0,
      "deleteLogsForCompletedTorrents": false,
      "status": "Ready",
      "environmentName": "Integration",
      "downloadRootPath": "/downloads",
      "torrentCount": 5,
      "supportsMagnetAdds": true,
      "supportsPause": true,
      "supportsResume": true,
      "supportsRemove": true,
      "supportsPersistentStorage": true,
      "supportsMultiHost": false,
      "startupRecoveryCompleted": true,
      "startupRecoveredTorrentCount": 2,
      "startupNormalizedTorrentCount": 1,
      "startupRecoveryCompletedAtUtc": "2026-07-23T11:59:00Z",
      "checkedAtUtc": "2026-07-23T12:00:00Z"
    }
    """

    static let lifecycle = """
    {
      "callbackFailedCount": 0,
      "callbackInvokedCount": 1,
      "callbackTimedOutCount": 0,
      "completedAutoRemovedCount": 0,
      "firstEventAtUtc": "2026-07-23T11:00:00Z",
      "lastEventAtUtc": "2026-07-23T12:00:00Z",
      "metadataRefreshRequestedCount": 1,
      "metadataResetRequestedCount": 0,
      "metadataResolvedCount": 1,
      "metadataRestartRequestedCount": 0,
      "orphanedTorrentLogsDeletedCount": 0,
      "recentEvents": [{
        "category": "Runtime",
        "eventType": "runtime.ready",
        "level": "Information",
        "message": "TorrentCore is ready.",
        "occurredAtUtc": "2026-07-23T12:00:00Z",
        "torrentId": "\(torrentID)"
      }],
      "recoveryCompletedAtUtc": "2026-07-23T11:59:00Z",
      "serviceInstanceId": "\(serviceInstanceID)",
      "startupNormalizedTorrentCount": 1,
      "startupReadyAtUtc": "2026-07-23T11:59:30Z",
      "startupRecoveredTorrentCount": 2,
      "torrentsAddedCount": 4,
      "torrentsRemovedCount": 1
    }
    """

    static let summary = """
    {
      "addedAtUtc": "2026-07-23T10:00:00Z",
      "canPause": true,
      "canRefreshMetadata": false,
      "canRemove": true,
      "canResume": false,
      "canRetryCompletionCallback": false,
      "categoryKey": "tv",
      "connectedPeerCount": 5,
      "downloadRateBytesPerSecond": 4096,
      "downloadedBytes": 524288,
      "name": "Fixture Torrent",
      "progressPercent": 50.0,
      "state": "FutureTorrentState",
      "torrentId": "\(torrentID)",
      "totalBytes": 1048576,
      "trackerCount": 2,
      "uploadRateBytesPerSecond": 512,
      "waitReason": "FutureWaitReason"
    }
    """

    static let detail = """
    {
      "addedAtUtc": "2026-07-23T10:00:00Z",
      "canPause": true,
      "canRefreshMetadata": false,
      "canRemove": true,
      "canResume": false,
      "canRetryCompletionCallback": false,
      "categoryKey": "tv",
      "connectedPeerCount": 5,
      "downloadRateBytesPerSecond": 4096,
      "downloadedBytes": 524288,
      "infoHash": "ABC",
      "magnetUri": "magnet:?xt=urn:btih:ABC",
      "name": "Fixture Torrent",
      "progressPercent": 50.0,
      "savePath": "/downloads/Fixture Torrent",
      "state": "Downloading",
      "torrentId": "\(torrentID)",
      "totalBytes": 1048576,
      "trackerCount": 2,
      "uploadRateBytesPerSecond": 512
    }
    """

    static let category = """
    {
      "callbackLabel": "TV",
      "displayName": "Television",
      "downloadRootPath": "/downloads/tv",
      "enabled": true,
      "invokeCompletionCallback": true,
      "key": "tv",
      "sortOrder": 10
    }
    """

    static func action(state: String, action: String, dataDeleted: Bool) -> String {
        """
        {
          "action": "\(action)",
          "dataDeleted": \(dataDeleted),
          "processedAtUtc": "2026-07-23T12:00:00Z",
          "state": "\(state)",
          "torrentId": "\(torrentID)"
        }
        """
    }

    static let problem = """
    {
      "type": "https://torrentcore.local/problems/invalid-magnet",
      "title": "Invalid magnet",
      "status": 400,
      "detail": "The magnet URI is invalid.",
      "code": "torrent.invalid_magnet",
      "target": "magnetUri",
      "traceId": "trace-123",
      "errors": {
        "magnetUri": ["A valid magnet URI is required."]
      }
    }
    """
}

private extension String {
    var jsonObject: [String: Any] {
        get throws {
            let object = try JSONSerialization.jsonObject(with: Data(utf8))
            return try #require(object as? [String: Any])
        }
    }
}
