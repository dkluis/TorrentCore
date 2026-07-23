import Foundation
import OpenAPIRuntime
import OpenAPIURLSession

public struct TorrentCoreTimeouts: Hashable, Sendable {
    public var health: TimeInterval
    public var read: TimeInterval
    public var mutation: TimeInterval

    public init(health: TimeInterval = 3, read: TimeInterval = 15, mutation: TimeInterval = 60) {
        self.health = health
        self.read = read
        self.mutation = mutation
    }
}

public struct TorrentCoreClient: Sendable {
    public static let supportedAPIVersion = 1

    private let healthClient: Client
    private let readClient: Client
    private let mutationClient: Client

    public init(
        baseURL: URL,
        timeouts: TorrentCoreTimeouts = .init(),
        sessionConfiguration: URLSessionConfiguration = .default
    ) throws {
        let normalizedURL = try Self.normalizedBaseURL(baseURL)
        healthClient = Client(
            serverURL: normalizedURL,
            configuration: Self.runtimeConfiguration,
            transport: Self.makeURLSessionTransport(
                configuration: sessionConfiguration,
                timeout: timeouts.health
            )
        )
        readClient = Client(
            serverURL: normalizedURL,
            configuration: Self.runtimeConfiguration,
            transport: Self.makeURLSessionTransport(
                configuration: sessionConfiguration,
                timeout: timeouts.read
            )
        )
        mutationClient = Client(
            serverURL: normalizedURL,
            configuration: Self.runtimeConfiguration,
            transport: Self.makeURLSessionTransport(
                configuration: sessionConfiguration,
                timeout: timeouts.mutation
            )
        )
    }

    init(
        baseURL: URL,
        healthTransport: any ClientTransport,
        readTransport: any ClientTransport,
        mutationTransport: any ClientTransport
    ) throws {
        let normalizedURL = try Self.normalizedBaseURL(baseURL)
        healthClient = Client(
            serverURL: normalizedURL,
            configuration: Self.runtimeConfiguration,
            transport: healthTransport
        )
        readClient = Client(
            serverURL: normalizedURL,
            configuration: Self.runtimeConfiguration,
            transport: readTransport
        )
        mutationClient = Client(
            serverURL: normalizedURL,
            configuration: Self.runtimeConfiguration,
            transport: mutationTransport
        )
    }

    public func probe() async throws -> TorrentCoreServiceHealth {
        let health = try await perform(.health) {
            let output = try await healthClient.healthGet()
            switch output {
            case let .ok(response):
                return TorrentCoreServiceHealth(try response.body.json)
            case let .undocumented(statusCode, _):
                throw TorrentCoreClientError.unexpectedResponse(statusCode: statusCode)
            }
        }

        guard health.serviceName == "TorrentCore.Service" else {
            throw TorrentCoreClientError.unexpectedService(name: health.serviceName)
        }
        if let apiVersion = health.apiVersion, apiVersion > Self.supportedAPIVersion {
            throw TorrentCoreClientError.unsupportedAPIVersion(apiVersion)
        }
        return health
    }

    public func hostStatus() async throws -> TorrentCoreHostStatus {
        try await perform(.hostStatus) {
            switch try await readClient.hostGetStatus() {
            case let .ok(response):
                return TorrentCoreHostStatus(try response.body.json)
            case let .undocumented(statusCode, _):
                throw TorrentCoreClientError.unexpectedResponse(statusCode: statusCode)
            }
        }
    }

    public func dashboardLifecycle() async throws -> TorrentCoreDashboardLifecycle {
        try await perform(.dashboardLifecycle) {
            switch try await readClient.hostGetDashboardLifecycle() {
            case let .ok(response):
                return TorrentCoreDashboardLifecycle(try response.body.json)
            case let .undocumented(statusCode, _):
                throw TorrentCoreClientError.unexpectedResponse(statusCode: statusCode)
            }
        }
    }

    public func torrents() async throws -> [TorrentCoreTorrentSummary] {
        try await perform(.torrentList) {
            switch try await readClient.torrentsGetAll() {
            case let .ok(response):
                return try response.body.json.map(TorrentCoreTorrentSummary.init)
            case let .undocumented(statusCode, _):
                throw TorrentCoreClientError.unexpectedResponse(statusCode: statusCode)
            }
        }
    }

    public func torrent(id: UUID) async throws -> TorrentCoreTorrentDetail {
        try await perform(.torrentDetail) {
            let output = try await readClient.torrentsGetById(
                path: .init(torrentId: id.uuidString)
            )
            switch output {
            case let .ok(response):
                return TorrentCoreTorrentDetail(try response.body.json)
            case let .notFound(response):
                throw TorrentCoreClientError.service(
                    TorrentCoreServiceProblem(try response.body.json)
                )
            case let .undocumented(statusCode, _):
                throw TorrentCoreClientError.unexpectedResponse(statusCode: statusCode)
            }
        }
    }

    public func categories() async throws -> [TorrentCoreCategory] {
        try await perform(.categories) {
            switch try await readClient.categoriesGetAll() {
            case let .ok(response):
                return try response.body.json.map(TorrentCoreCategory.init)
            case let .undocumented(statusCode, _):
                throw TorrentCoreClientError.unexpectedResponse(statusCode: statusCode)
            }
        }
    }

    public func addMagnet(_ magnetURI: String, categoryKey: String? = nil) async throws -> TorrentCoreTorrentDetail {
        try await perform(.addMagnet) {
            let request = Components.Schemas.AddMagnetRequest(
                categoryKey: categoryKey,
                magnetUri: magnetURI
            )
            switch try await mutationClient.torrentsAdd(body: .json(request)) {
            case let .created(response):
                return TorrentCoreTorrentDetail(try response.body.json)
            case let .badRequest(response):
                throw TorrentCoreClientError.service(
                    TorrentCoreServiceProblem(try response.body.json)
                )
            case let .conflict(response):
                throw TorrentCoreClientError.service(
                    TorrentCoreServiceProblem(try response.body.json)
                )
            case let .serviceUnavailable(response):
                throw TorrentCoreClientError.service(
                    TorrentCoreServiceProblem(try response.body.json)
                )
            case let .undocumented(statusCode, _):
                throw TorrentCoreClientError.unexpectedResponse(statusCode: statusCode)
            }
        }
    }

    public func pause(id: UUID) async throws -> TorrentCoreActionResult {
        try await perform(.pause) {
            switch try await mutationClient.torrentsPause(path: .init(torrentId: id.uuidString)) {
            case let .ok(response):
                return TorrentCoreActionResult(try response.body.json)
            case let .notFound(response):
                throw TorrentCoreClientError.service(
                    TorrentCoreServiceProblem(try response.body.json)
                )
            case let .conflict(response):
                throw TorrentCoreClientError.service(
                    TorrentCoreServiceProblem(try response.body.json)
                )
            case let .undocumented(statusCode, _):
                throw TorrentCoreClientError.unexpectedResponse(statusCode: statusCode)
            }
        }
    }

    public func resume(id: UUID) async throws -> TorrentCoreActionResult {
        try await perform(.resume) {
            switch try await mutationClient.torrentsResume(path: .init(torrentId: id.uuidString)) {
            case let .ok(response):
                return TorrentCoreActionResult(try response.body.json)
            case let .notFound(response):
                throw TorrentCoreClientError.service(
                    TorrentCoreServiceProblem(try response.body.json)
                )
            case let .conflict(response):
                throw TorrentCoreClientError.service(
                    TorrentCoreServiceProblem(try response.body.json)
                )
            case let .undocumented(statusCode, _):
                throw TorrentCoreClientError.unexpectedResponse(statusCode: statusCode)
            }
        }
    }

    public func remove(id: UUID, deleteData: Bool) async throws -> TorrentCoreActionResult {
        try await perform(.remove) {
            let request = Components.Schemas.RemoveTorrentRequest(deleteData: deleteData)
            switch try await mutationClient.torrentsRemove(
                path: .init(torrentId: id.uuidString),
                body: .json(request)
            ) {
            case let .ok(response):
                return TorrentCoreActionResult(try response.body.json)
            case let .notFound(response):
                throw TorrentCoreClientError.service(
                    TorrentCoreServiceProblem(try response.body.json)
                )
            case let .undocumented(statusCode, _):
                throw TorrentCoreClientError.unexpectedResponse(statusCode: statusCode)
            }
        }
    }

    public static func normalizedBaseURL(_ baseURL: URL) throws -> URL {
        guard let scheme = baseURL.scheme?.lowercased(),
              scheme == "http" || scheme == "https",
              baseURL.host?.isEmpty == false,
              baseURL.user == nil,
              baseURL.password == nil,
              baseURL.query == nil,
              baseURL.fragment == nil,
              baseURL.path.isEmpty || baseURL.path == "/"
        else {
            throw TorrentCoreClientError.invalidBaseURL
        }

        guard var components = URLComponents(url: baseURL, resolvingAgainstBaseURL: false) else {
            throw TorrentCoreClientError.invalidBaseURL
        }
        components.scheme = scheme
        components.host = components.host?.lowercased()
        components.percentEncodedPath = ""
        guard let normalizedURL = components.url else {
            throw TorrentCoreClientError.invalidBaseURL
        }
        return normalizedURL
    }

    public static func normalizedBaseURL(_ address: String) throws -> URL {
        let trimmedAddress = address.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmedAddress.isEmpty else {
            throw TorrentCoreClientError.invalidBaseURL
        }

        let addressWithScheme = trimmedAddress.contains("://")
            ? trimmedAddress
            : "http://\(trimmedAddress)"
        guard let baseURL = URL(string: addressWithScheme) else {
            throw TorrentCoreClientError.invalidBaseURL
        }
        return try normalizedBaseURL(baseURL)
    }

    private func perform<Value: Sendable>(
        _ operation: TorrentCoreOperation,
        body: () async throws -> Value
    ) async throws -> Value {
        do {
            return try await body()
        } catch {
            throw TorrentCoreClientError.map(error, operation: operation)
        }
    }

    private static func makeURLSessionTransport(
        configuration: URLSessionConfiguration,
        timeout: TimeInterval
    ) -> URLSessionTransport {
        let requestConfiguration = configuration.copy() as! URLSessionConfiguration
        requestConfiguration.timeoutIntervalForRequest = timeout
        requestConfiguration.timeoutIntervalForResource = timeout
        let session = URLSession(configuration: requestConfiguration)
        return URLSessionTransport(configuration: .init(session: session))
    }

    private static var runtimeConfiguration: OpenAPIRuntime.Configuration {
        .init(dateTranscoder: TorrentCoreDateTranscoder())
    }
}

private struct TorrentCoreDateTranscoder: DateTranscoder, @unchecked Sendable {
    private let lock = NSLock()
    private let fractionalFormatter: ISO8601DateFormatter
    private let standardFormatter: ISO8601DateFormatter

    init() {
        fractionalFormatter = ISO8601DateFormatter()
        fractionalFormatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        standardFormatter = ISO8601DateFormatter()
        standardFormatter.formatOptions = [.withInternetDateTime]
    }

    func encode(_ date: Date) throws -> String {
        lock.withLock {
            fractionalFormatter.string(from: date)
        }
    }

    func decode(_ dateString: String) throws -> Date {
        try lock.withLock {
            if let date = fractionalFormatter.date(from: dateString)
                ?? standardFormatter.date(from: dateString)
            {
                return date
            }
            throw DecodingError.dataCorrupted(
                .init(
                    codingPath: [],
                    debugDescription: "Expected an ISO-8601 timestamp compatible with TorrentCore."
                )
            )
        }
    }
}
