import Foundation
import TorrentCoreAPI

public enum TorrentCoreConnectionProfileError: Error, Equatable, Sendable {
    case emptyName
    case invalidAddress
    case duplicateAddress
    case profileNotFound
}

extension TorrentCoreConnectionProfileError: LocalizedError {
    public var errorDescription: String? {
        switch self {
        case .emptyName:
            "Enter a name for this TorrentCore installation."
        case .invalidAddress:
            "Enter an HTTP or HTTPS TorrentCore address containing only a host and optional port."
        case .duplicateAddress:
            "A connection profile already uses this TorrentCore address."
        case .profileNotFound:
            "The selected TorrentCore connection profile no longer exists."
        }
    }
}

public struct TorrentCoreConnectionProfile: Codable, Hashable, Identifiable, Sendable {
    public let id: UUID
    public var name: String
    public var baseURL: URL
    public let createdAt: Date
    public var updatedAt: Date

    public init(
        id: UUID = UUID(),
        name: String,
        address: String,
        createdAt: Date = Date(),
        updatedAt: Date? = nil
    ) throws {
        let trimmedName = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmedName.isEmpty else {
            throw TorrentCoreConnectionProfileError.emptyName
        }

        let normalizedURL: URL
        do {
            normalizedURL = try TorrentCoreClient.normalizedBaseURL(address)
        } catch {
            throw TorrentCoreConnectionProfileError.invalidAddress
        }

        self.id = id
        self.name = trimmedName
        self.baseURL = normalizedURL
        self.createdAt = createdAt
        self.updatedAt = updatedAt ?? createdAt
    }
}

public enum TorrentCoreRefreshInterval: Int, CaseIterable, Codable, Hashable, Sendable {
    case fiveSeconds = 5
    case tenSeconds = 10
    case fifteenSeconds = 15

    public static let defaultValue = Self.fifteenSeconds

    public var seconds: TimeInterval {
        TimeInterval(rawValue)
    }
}

public struct TorrentCoreClientPreferences: Codable, Equatable, Sendable {
    public static let currentSchemaVersion = 2

    public var schemaVersion: Int
    public var profiles: [TorrentCoreConnectionProfile]
    public var activeProfileID: UUID?
    public var refreshInterval: TorrentCoreRefreshInterval
    public var autoRefreshEnabled: Bool

    public init(
        schemaVersion: Int = currentSchemaVersion,
        profiles: [TorrentCoreConnectionProfile] = [],
        activeProfileID: UUID? = nil,
        refreshInterval: TorrentCoreRefreshInterval = .defaultValue,
        autoRefreshEnabled: Bool = true
    ) {
        self.schemaVersion = schemaVersion
        self.profiles = profiles
        self.activeProfileID = activeProfileID
        self.refreshInterval = refreshInterval
        self.autoRefreshEnabled = autoRefreshEnabled
    }

    public var activeProfile: TorrentCoreConnectionProfile? {
        guard let activeProfileID else {
            return nil
        }
        return profiles.first { $0.id == activeProfileID }
    }

    private enum CodingKeys: String, CodingKey {
        case schemaVersion
        case profiles
        case activeProfileID
        case refreshInterval
        case autoRefreshEnabled
    }

    public init(from decoder: any Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        let storedVersion = try container.decodeIfPresent(Int.self, forKey: .schemaVersion) ?? 1
        guard storedVersion == 1 || storedVersion == Self.currentSchemaVersion else {
            throw TorrentCoreProfileStoreError.unsupportedSchemaVersion(storedVersion)
        }

        schemaVersion = Self.currentSchemaVersion
        profiles = try container.decodeIfPresent(
            [TorrentCoreConnectionProfile].self,
            forKey: .profiles
        ) ?? []
        activeProfileID = try container.decodeIfPresent(UUID.self, forKey: .activeProfileID)
        refreshInterval = try container.decodeIfPresent(
            TorrentCoreRefreshInterval.self,
            forKey: .refreshInterval
        ) ?? .defaultValue
        autoRefreshEnabled = try container.decodeIfPresent(
            Bool.self,
            forKey: .autoRefreshEnabled
        ) ?? true
    }
}
