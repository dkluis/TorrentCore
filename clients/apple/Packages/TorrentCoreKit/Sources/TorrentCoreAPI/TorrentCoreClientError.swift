import Foundation
import OpenAPIRuntime

public enum TorrentCoreOperation: String, Sendable {
    case health
    case hostStatus
    case dashboardLifecycle
    case torrentList
    case torrentDetail
    case categories
    case history
    case historyDetail
    case logs
    case peers
    case trackers
    case runtimeSettings
    case addMagnet
    case pause
    case resume
    case remove
    case refreshMetadata
    case resetMetadata
    case retryCompletionCallback
    case deleteOrphanedLogs
    case updateRuntimeSettings
    case updateCategory
    case restartService

    public var isMutation: Bool {
        switch self {
        case .addMagnet,
             .pause,
             .resume,
             .remove,
             .refreshMetadata,
             .resetMetadata,
             .retryCompletionCallback,
             .deleteOrphanedLogs,
             .updateRuntimeSettings,
             .updateCategory,
             .restartService:
            true
        default:
            false
        }
    }
}

public enum TorrentCoreClientError: Error, Sendable {
    case invalidBaseURL
    case unexpectedService(name: String?)
    case unsupportedAPIVersion(Int)
    case service(TorrentCoreServiceProblem)
    case unexpectedResponse(statusCode: Int)
    case invalidPayload(String)
    case offline
    case timedOut(operation: TorrentCoreOperation, outcomeUncertain: Bool)
    case cancelled
    case transport(String)
}

extension TorrentCoreClientError: LocalizedError {
    public var errorDescription: String? {
        switch self {
        case .invalidBaseURL:
            "Enter an HTTP or HTTPS TorrentCore service address with a host."
        case let .unexpectedService(name):
            "The address responded, but it is not a TorrentCore service\(name.map { " (\($0))" } ?? "")."
        case let .unsupportedAPIVersion(version):
            "This TorrentCore service uses unsupported API version \(version)."
        case let .service(problem):
            problem.detail ?? problem.title ?? "TorrentCore rejected the request."
        case let .unexpectedResponse(statusCode):
            "TorrentCore returned an unexpected HTTP \(statusCode) response."
        case let .invalidPayload(detail):
            "TorrentCore returned data the app could not read: \(detail)"
        case .offline:
            "TorrentCore could not be reached. Check the server address and LAN or VPN connection."
        case let .timedOut(_, outcomeUncertain):
            outcomeUncertain
                ? "TorrentCore did not answer in time. The operation may still have completed; refresh before trying again."
                : "TorrentCore did not answer in time."
        case .cancelled:
            "The TorrentCore request was cancelled."
        case let .transport(detail):
            "The TorrentCore request failed: \(detail)"
        }
    }
}

extension TorrentCoreClientError {
    static func map(_ error: any Error, operation: TorrentCoreOperation) -> TorrentCoreClientError {
        if let mapped = error as? TorrentCoreClientError {
            return mapped
        }
        if error is CancellationError || Task.isCancelled {
            return .cancelled
        }

        let underlying: any Error
        if let clientError = error as? ClientError {
            underlying = clientError.underlyingError
        } else {
            underlying = error
        }

        if underlying is CancellationError {
            return .cancelled
        }
        if underlying is DecodingError {
            return .invalidPayload(String(describing: underlying))
        }
        if let urlError = underlying as? URLError {
            switch urlError.code {
            case .cancelled:
                return .cancelled
            case .timedOut:
                return .timedOut(operation: operation, outcomeUncertain: operation.isMutation)
            case .cannotFindHost,
                 .cannotConnectToHost,
                 .dataNotAllowed,
                 .dnsLookupFailed,
                 .networkConnectionLost,
                 .notConnectedToInternet:
                return .offline
            default:
                return .transport(urlError.localizedDescription)
            }
        }

        return .transport(String(describing: underlying))
    }
}
