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
    public var waitReason: String?

    public init(
        searchText: String = "",
        state: String? = nil,
        category: TorrentCoreTorrentCategoryFilter = .all,
        waitReason: String? = nil
    ) {
        self.searchText = searchText
        self.state = state
        self.category = category
        self.waitReason = waitReason
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
            if let waitReason, !waitReason.isEmpty {
                if waitReason == "__not_waiting" {
                    guard torrent.waitReason == nil else { return false }
                } else if torrent.waitReason?.rawValue != waitReason {
                    return false
                }
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
    public var reason: String { TorrentCoreDisplayFormatter.waitReason(summary.waitReason) }
    public var queuePosition: Int? { summary.queuePosition }
    public var priorityQueuePosition: Int? { summary.priorityQueuePosition }
    public var heldQueuePosition: Int? { summary.heldQueuePosition }
    public var queueSortValue: Int { summary.queuePosition ?? .min }
    public var priorityQueueSortValue: Int { summary.priorityQueuePosition ?? .min }
    public var heldQueueSortValue: Int { summary.heldQueuePosition ?? .min }
    public var wait: String {
        TorrentCoreDisplayFormatter.wait(
            summary.waitReason,
            queue: summary.queuePosition,
            priority: summary.priorityQueuePosition,
            held: summary.heldQueuePosition
        )
    }
    public var addedAt: Date { summary.addedAt }
}

public enum TorrentCoreDisplayFormatter {
    public static func bytes(_ value: Int64?) -> String {
        guard let value else {
            return "--"
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
            return "--"
        }
        return value.formatted(
            Date.FormatStyle()
                .locale(Locale(identifier: "en_US_POSIX"))
                .year()
                .month(.defaultDigits)
                .day(.defaultDigits)
                .hour(.defaultDigits(amPM: .abbreviated))
                .minute(.twoDigits)
        )
        .replacingOccurrences(of: ",", with: "")
        .replacingOccurrences(of: "\u{202F}", with: " ")
    }

    public static func state(_ value: TorrentCoreTorrentState) -> String {
        splitIdentifier(value.rawValue)
    }

    public static func wait(
        _ value: TorrentCoreWaitReason?,
        queue: Int?,
        priority: Int? = nil,
        held: Int? = nil
    ) -> String {
        var parts: [String] = []
        if let value {
            parts.append(splitIdentifier(value.rawValue))
        }
        if let priority {
            parts.append("Priority #\(priority)")
        }
        if let queue {
            parts.append(priority == nil ? "#\(queue)" : "Queue #\(queue)")
        }
        if let held {
            parts.append("Held #\(held)")
        }
        return parts.isEmpty ? "--" : parts.joined(separator: " · ")
    }

    public static func waitReason(_ value: TorrentCoreWaitReason?) -> String {
        value.map { splitIdentifier($0.rawValue) } ?? "Not waiting"
    }

    public static func category(_ key: String?) -> String {
        let value = (key ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        return value.isEmpty ? "Uncategorized" : value
    }

    public static func splitIdentifier(_ value: String) -> String {
        guard !value.isEmpty else {
            return "--"
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

    public static func operatorValue(_ value: String?) -> String {
        guard let value else {
            return "--"
        }
        let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty || trimmed.caseInsensitiveCompare("Unknown") == .orderedSame
            ? "--"
            : value
    }
}

public enum TorrentCoreCompletionCallbackPresentation {
    public static func state(_ value: String?) -> String {
        switch TorrentCoreDisplayFormatter.operatorValue(value) {
        case "PendingFinalization":
            return "Waiting For Final Payload"
        case "WaitingForFeedback":
            return "Waiting For TVMaze"
        case "Invoked":
            return "Final Feedback Received"
        case "Failed":
            return "Callback Failed"
        case "TimedOut":
            return "Callback Timed Out"
        case let state:
            return state
        }
    }

    public static func feedbackSummary(
        _ feedback: TorrentCoreCompletionCallbackFeedback?
    ) -> String? {
        guard let feedback else {
            return nil
        }

        let displayMessage = (feedback.displayMessage ?? "")
            .trimmingCharacters(in: .whitespacesAndNewlines)
        if !displayMessage.isEmpty {
            return displayMessage
        }

        let finalResult = (feedback.finalState ?? "")
            .trimmingCharacters(in: .whitespacesAndNewlines)
        return finalResult.isEmpty ? nil : finalResult
    }
}
