import Foundation

public enum TorrentCoreCredentialKeychainConvention {
    public static let service = "com.conadv.TorrentCore.credentials"

    public static func account(profileID: UUID) -> String {
        profileID.uuidString.lowercased()
    }
}

public protocol TorrentCoreCredentialStoring: Sendable {
    func credential(for profileID: UUID) async throws -> Data?
    func saveCredential(_ credential: Data, for profileID: UUID) async throws
    func removeCredential(for profileID: UUID) async throws
}

public enum TorrentCoreCredentialStoreError: Error, Equatable, Sendable {
    case authenticationNotConfigured
}

extension TorrentCoreCredentialStoreError: LocalizedError {
    public var errorDescription: String? {
        "This TorrentCore installation does not currently use client credentials."
    }
}

public struct UnconfiguredTorrentCoreCredentialStore: TorrentCoreCredentialStoring {
    public init() {}

    public func credential(for profileID: UUID) async throws -> Data? {
        nil
    }

    public func saveCredential(_ credential: Data, for profileID: UUID) async throws {
        throw TorrentCoreCredentialStoreError.authenticationNotConfigured
    }

    public func removeCredential(for profileID: UUID) async throws {}
}
