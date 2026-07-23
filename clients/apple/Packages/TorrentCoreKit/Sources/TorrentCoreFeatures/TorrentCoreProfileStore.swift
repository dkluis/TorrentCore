import Foundation

public protocol TorrentCoreProfilePersisting: Sendable {
    func load() async throws -> TorrentCoreClientPreferences
    func save(_ preferences: TorrentCoreClientPreferences) async throws
}

public enum TorrentCoreProfileStoreError: Error, Equatable, Sendable {
    case unsupportedSchemaVersion(Int)
    case invalidActiveProfile
    case duplicateAddress
    case encodingFailed(String)
    case decodingFailed(String)
}

extension TorrentCoreProfileStoreError: LocalizedError {
    public var errorDescription: String? {
        switch self {
        case let .unsupportedSchemaVersion(version):
            "The saved TorrentCore connection settings use unsupported schema version \(version)."
        case .invalidActiveProfile:
            "The selected TorrentCore connection profile is missing."
        case .duplicateAddress:
            "Two saved TorrentCore profiles use the same service address."
        case let .encodingFailed(detail):
            "TorrentCore could not save its connection settings: \(detail)"
        case let .decodingFailed(detail):
            "TorrentCore could not read its connection settings: \(detail)"
        }
    }
}

public actor UserDefaultsTorrentCoreProfileStore: TorrentCoreProfilePersisting {
    public static let storageKey = "TorrentCore.ClientPreferences.v1"

    private let defaults: UserDefaults
    private let storageKey: String

    public init(suiteName: String? = nil, storageKey: String = storageKey) {
        if let suiteName, let suiteDefaults = UserDefaults(suiteName: suiteName) {
            defaults = suiteDefaults
        } else {
            defaults = .standard
        }
        self.storageKey = storageKey
    }

    public func load() async throws -> TorrentCoreClientPreferences {
        guard let data = defaults.data(forKey: storageKey) else {
            return TorrentCoreClientPreferences()
        }

        do {
            let preferences = try JSONDecoder().decode(TorrentCoreClientPreferences.self, from: data)
            try Self.validate(preferences)
            return preferences
        } catch let error as TorrentCoreProfileStoreError {
            throw error
        } catch {
            throw TorrentCoreProfileStoreError.decodingFailed(error.localizedDescription)
        }
    }

    public func save(_ preferences: TorrentCoreClientPreferences) async throws {
        try Self.validate(preferences)
        do {
            defaults.set(try JSONEncoder().encode(preferences), forKey: storageKey)
        } catch let error as TorrentCoreProfileStoreError {
            throw error
        } catch {
            throw TorrentCoreProfileStoreError.encodingFailed(error.localizedDescription)
        }
    }

    private static func validate(_ preferences: TorrentCoreClientPreferences) throws {
        guard preferences.schemaVersion == TorrentCoreClientPreferences.currentSchemaVersion else {
            throw TorrentCoreProfileStoreError.unsupportedSchemaVersion(preferences.schemaVersion)
        }
        if let activeProfileID = preferences.activeProfileID,
           !preferences.profiles.contains(where: { $0.id == activeProfileID })
        {
            throw TorrentCoreProfileStoreError.invalidActiveProfile
        }

        let addresses = preferences.profiles.map { $0.baseURL.absoluteString.lowercased() }
        guard Set(addresses).count == addresses.count else {
            throw TorrentCoreProfileStoreError.duplicateAddress
        }
    }
}
