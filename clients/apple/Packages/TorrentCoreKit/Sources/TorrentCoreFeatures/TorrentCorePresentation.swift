import Foundation
import TorrentCoreAPI

public enum TorrentCoreKnownTorrentState: String, CaseIterable, Codable, Hashable, Sendable {
    case resolvingMetadata = "ResolvingMetadata"
    case queued = "Queued"
    case downloading = "Downloading"
    case seeding = "Seeding"
    case waitingForFileCompletion = "WaitingForFileCompletion"
    case paused = "Paused"
    case completed = "Completed"
    case error = "Error"
    case removed = "Removed"
}

public enum TorrentCoreTorrentCategoryFilter: Equatable, Hashable, Sendable {
    case all
    case uncategorized
    case category(String)
}

public struct TorrentCoreTorrentFilter: Equatable, Hashable, Sendable {
    public var searchText: String
    public var state: String?
    public var category: TorrentCoreTorrentCategoryFilter

    public init(
        searchText: String = "",
        state: String? = nil,
        category: TorrentCoreTorrentCategoryFilter = .all
    ) {
        self.searchText = searchText
        self.state = state
        self.category = category
    }

    public func apply(
        to torrents: [TorrentCoreTorrentSummary]
    ) -> [TorrentCoreTorrentSummary] {
        let search = searchText.trimmingCharacters(in: .whitespacesAndNewlines)
        return torrents.filter { torrent in
            if !search.isEmpty,
               !(torrent.name ?? "").localizedCaseInsensitiveContains(search)
            {
                return false
            }
            if let state, !state.isEmpty, torrent.state.rawValue != state {
                return false
            }
            switch category {
            case .all:
                return true
            case .uncategorized:
                return (torrent.categoryKey ?? "")
                    .trimmingCharacters(in: .whitespacesAndNewlines)
                    .isEmpty
            case let .category(category):
                return (torrent.categoryKey ?? "")
                    .localizedCaseInsensitiveCompare(category) == .orderedSame
            }
        }
    }
}

public enum TorrentCoreTorrentPageSize: Int, CaseIterable, Codable, Hashable, Sendable {
    case twentyFive = 25
    case fifty = 50
    case oneHundred = 100
    case twoHundredFifty = 250

    public static let defaultValue = Self.twentyFive
}

public struct TorrentCoreTorrentPage<Value: Sendable>: Sendable {
    public let values: [Value]
    public let pageIndex: Int
    public let pageCount: Int
    public let totalCount: Int

    public init(
        values: [Value],
        pageIndex: Int,
        pageCount: Int,
        totalCount: Int
    ) {
        self.values = values
        self.pageIndex = pageIndex
        self.pageCount = pageCount
        self.totalCount = totalCount
    }
}

public enum TorrentCorePagination {
    public static func page<Value: Sendable>(
        _ values: [Value],
        index requestedIndex: Int,
        size: TorrentCoreTorrentPageSize
    ) -> TorrentCoreTorrentPage<Value> {
        let pageCount = max(1, Int(ceil(Double(values.count) / Double(size.rawValue))))
        let pageIndex = min(max(0, requestedIndex), pageCount - 1)
        let lowerBound = min(pageIndex * size.rawValue, values.count)
        let upperBound = min(lowerBound + size.rawValue, values.count)
        return TorrentCoreTorrentPage(
            values: Array(values[lowerBound..<upperBound]),
            pageIndex: pageIndex,
            pageCount: pageCount,
            totalCount: values.count
        )
    }
}

public struct TorrentCoreTorrentListItem: Identifiable, Hashable, Sendable {
    public let summary: TorrentCoreTorrentSummary

    public init(summary: TorrentCoreTorrentSummary) {
        self.summary = summary
    }

    public var id: String {
        if let torrentID = summary.torrentID {
            return torrentID.uuidString
        }
        return "missing-id|\(summary.addedAt.timeIntervalSince1970)|\(summary.name ?? "")"
    }

    public var torrentID: UUID? { summary.torrentID }
    public var name: String { summary.name ?? "Unnamed Torrent" }
    public var category: String { summary.categoryKey ?? "" }
    public var state: String { summary.state.rawValue }
    public var progress: Double { summary.progressPercent }
    public var downloadRate: Int64 { summary.downloadRateBytesPerSecond }
    public var uploadRate: Int64 { summary.uploadRateBytesPerSecond }
    public var peers: Int { summary.connectedPeerCount }
    public var wait: String { TorrentCoreDisplayFormatter.wait(summary.waitReason, queue: summary.queuePosition) }
    public var addedAt: Date { summary.addedAt }
}

public enum TorrentCoreDisplayFormatter {
    public static func bytes(_ value: Int64?) -> String {
        guard let value else {
            return "—"
        }
        return ByteCountFormatter.string(fromByteCount: value, countStyle: .file)
    }

    public static func rate(_ value: Int64) -> String {
        "\(ByteCountFormatter.string(fromByteCount: value, countStyle: .file))/s"
    }

    public static func percent(_ value: Double) -> String {
        value.formatted(.number.precision(.fractionLength(1))) + "%"
    }

    public static func timestamp(_ value: Date?) -> String {
        guard let value else {
            return "—"
        }
        return value.formatted(date: .abbreviated, time: .shortened)
    }

    public static func state(_ value: TorrentCoreTorrentState) -> String {
        splitIdentifier(value.rawValue)
    }

    public static func wait(_ value: TorrentCoreWaitReason?, queue: Int?) -> String {
        guard let value else {
            return "—"
        }
        let label = splitIdentifier(value.rawValue)
        if let queue {
            return "\(label) · #\(queue)"
        }
        return label
    }

    public static func category(_ key: String?) -> String {
        let value = (key ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        return value.isEmpty ? "Uncategorized" : value
    }

    public static func splitIdentifier(_ value: String) -> String {
        guard !value.isEmpty else {
            return "Unknown"
        }
        var output = ""
        for character in value {
            if character.isUppercase, !output.isEmpty {
                output.append(" ")
            }
            output.append(character)
        }
        return output
    }
}
