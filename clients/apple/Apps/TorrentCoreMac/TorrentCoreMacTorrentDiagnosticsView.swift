import SwiftUI
import TorrentCoreAPI
import TorrentCoreFeatures

private struct TorrentCoreMacPeerTableItem: Identifiable {
    let peer: TorrentCorePeer

    var id: String { peer.id }
    var endpoint: String { peer.endpoint ?? "—" }
    var client: String { peer.client ?? "—" }
    var direction: String { peer.direction ?? "—" }
    var connected: Int { peer.isConnected ? 1 : 0 }
    var seeder: Int { peer.isSeeder ? 1 : 0 }
    var downloadRate: Int64 { peer.downloadRateBytesPerSecond }
    var uploadRate: Int64 { peer.uploadRateBytesPerSecond }
    var downloaded: Int64 { peer.downloadedBytes }
    var uploaded: Int64 { peer.uploadedBytes }
    var encryption: String { peer.encryption ?? "—" }
}

private struct TorrentCoreMacTrackerTableItem: Identifiable {
    let tracker: TorrentCoreTracker

    var id: String { tracker.id }
    var tier: Int { tracker.tierNumber }
    var number: Int { tracker.trackerNumber }
    var active: Int { tracker.isActive ? 1 : 0 }
    var status: String { tracker.status ?? "—" }
    var canAnnounce: Int { Self.optionalBooleanSortValue(tracker.canAnnounce) }
    var canScrape: Int { tracker.canScrape ? 1 : 0 }
    var sinceAnnounce: Int64 { tracker.timeSinceLastAnnounceSeconds ?? .max }
    var announceSucceeded: Int {
        Self.optionalBooleanSortValue(tracker.lastAnnounceSucceeded)
    }
    var sinceScrape: Int64 { tracker.timeSinceLastScrapeSeconds ?? .max }
    var scrapeSucceeded: Int {
        Self.optionalBooleanSortValue(tracker.lastScrapeSucceeded)
    }
    var failure: String { tracker.failureMessage ?? "—" }
    var warning: String { tracker.warningMessage ?? "—" }

    private static func optionalBooleanSortValue(_ value: Bool?) -> Int {
        switch value {
        case true: 2
        case false: 1
        case nil: 0
        }
    }
}

struct TorrentCoreMacPeersSheet: View {
    @Environment(\.dismiss) private var dismiss
    @AppStorage("TorrentCore.Mac.Peers.Columns.v1")
    private var columnCustomization =
        TableColumnCustomization<TorrentCoreMacPeerTableItem>()

    let session: TorrentCoreFeatureSession
    let torrentID: UUID
    let torrentName: String

    @State private var pageSize = 25
    @State private var pageIndex = 0
    @State private var sortOrder = [
        KeyPathComparator(
            \TorrentCoreMacPeerTableItem.endpoint,
            comparator: .localizedStandard
        ),
    ]

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                VStack(alignment: .leading) {
                    Text("Peers")
                        .font(.title2.weight(.semibold))
                    Text(torrentName)
                        .foregroundStyle(.secondary)
                }
                Spacer()
                Button {
                    Task { await session.refresh(.peers(torrentID)) }
                } label: {
                    Label("Refresh", systemImage: "arrow.clockwise")
                }
                Button("Done") { dismiss() }
                    .keyboardShortcut(.defaultAction)
            }
            .padding()

            Divider()

            TorrentCoreMacPhaseBanner(
                phase: session.peers.phase,
                lastSuccessfulAt: session.peers.lastSuccessfulAt
            )
            .padding()

            if let peers = session.peers.value {
                if peers.isEmpty {
                    ContentUnavailableView(
                        "No Connected Peers",
                        systemImage: "person.2.slash",
                        description: Text("TorrentCore currently reports no connected peers.")
                    )
                } else {
                    Table(
                        currentPage,
                        sortOrder: $sortOrder,
                        columnCustomization: $columnCustomization
                    ) {
                        TableColumn(
                            "Endpoint",
                            value: \.endpoint,
                            comparator: .localizedStandard
                        ) {
                            Text($0.endpoint)
                        }
                        .width(min: 155, ideal: 210)
                        TableColumn(
                            "Client",
                            value: \.client,
                            comparator: .localizedStandard
                        ) {
                            Text($0.client)
                        }
                        .width(min: 145, ideal: 190)
                        TableColumn(
                            "Direction",
                            value: \.direction,
                            comparator: .localizedStandard
                        ) {
                            Text($0.direction)
                        }
                        .width(min: 105, ideal: 125)
                        TableColumn("Connected", value: \.connected) {
                            Text($0.peer.isConnected ? "Yes" : "No")
                        }
                        .width(min: 90, ideal: 105)
                        TableColumn("Seeder", value: \.seeder) {
                            Text($0.peer.isSeeder ? "Yes" : "No")
                        }
                        .width(min: 75, ideal: 90)
                        TableColumn("Down", value: \.downloadRate) {
                            Text(TorrentCoreDisplayFormatter.rate(
                                $0.downloadRate
                            ))
                            .monospacedDigit()
                        }
                        .width(min: 95, ideal: 115)
                        TableColumn("Up", value: \.uploadRate) {
                            Text(TorrentCoreDisplayFormatter.rate(
                                $0.uploadRate
                            ))
                            .monospacedDigit()
                        }
                        .width(min: 95, ideal: 115)
                        TableColumn("Downloaded", value: \.downloaded) {
                            Text(TorrentCoreDisplayFormatter.bytes($0.downloaded))
                                .monospacedDigit()
                        }
                        .width(min: 105, ideal: 125)
                        TableColumn("Uploaded", value: \.uploaded) {
                            Text(TorrentCoreDisplayFormatter.bytes($0.uploaded))
                                .monospacedDigit()
                        }
                        .width(min: 105, ideal: 125)
                        TableColumn(
                            "Encryption",
                            value: \.encryption,
                            comparator: .localizedStandard
                        ) {
                            Text($0.encryption)
                        }
                        .width(min: 115, ideal: 140)
                    }
                    Divider()
                    paginationBar
                }
            } else {
                ContentUnavailableView(
                    "Peers Unavailable",
                    systemImage: "person.2",
                    description: Text("Waiting for TorrentCore peer diagnostics.")
                )
            }
        }
        .frame(minWidth: 1_120, idealWidth: 1_420, minHeight: 580, idealHeight: 680)
        .onChange(of: pageSize) { _, _ in pageIndex = 0 }
        .onChange(of: sortOrder) { _, _ in pageIndex = 0 }
        .torrentCoreRefreshWhileVisible(
            session: session,
            context: .peers(torrentID)
        )
    }

    private var tableItems: [TorrentCoreMacPeerTableItem] {
        (session.peers.value ?? [])
            .map { TorrentCoreMacPeerTableItem(peer: $0) }
            .sorted(using: sortOrder)
    }

    private var currentPage: [TorrentCoreMacPeerTableItem] {
        page(tableItems)
    }

    private var maxPageIndex: Int {
        max(0, (tableItems.count - 1) / max(1, pageSize))
    }

    private var paginationBar: some View {
        HStack {
            Text(rangeLabel(count: tableItems.count, noun: "peers"))
                .foregroundStyle(.secondary)
            Spacer()
            Picker("Rows", selection: $pageSize) {
                ForEach([10, 25, 50, 100], id: \.self) {
                    Text("\($0)").tag($0)
                }
            }
            .frame(width: 120)
            Button {
                pageIndex = max(0, pageIndex - 1)
            } label: {
                Label("Previous", systemImage: "chevron.left")
            }
            .disabled(pageIndex == 0)
            Button {
                pageIndex = min(maxPageIndex, pageIndex + 1)
            } label: {
                Label("Next", systemImage: "chevron.right")
            }
            .disabled(pageIndex >= maxPageIndex)
        }
        .padding(10)
    }

    private func page<T>(_ values: [T]) -> [T] {
        let safeIndex = min(pageIndex, maxPageIndex)
        let start = safeIndex * pageSize
        guard start < values.count else {
            return []
        }
        return Array(values[start..<min(values.count, start + pageSize)])
    }

    private func rangeLabel(count: Int, noun: String) -> String {
        guard count > 0 else {
            return "0 \(noun)"
        }
        let start = min(pageIndex, maxPageIndex) * pageSize + 1
        let end = start + currentPage.count - 1
        return "\(start)–\(end) of \(count) \(noun)"
    }
}

struct TorrentCoreMacTrackersSheet: View {
    @Environment(\.dismiss) private var dismiss
    @AppStorage("TorrentCore.Mac.Trackers.Columns.v1")
    private var columnCustomization =
        TableColumnCustomization<TorrentCoreMacTrackerTableItem>()

    let session: TorrentCoreFeatureSession
    let torrentID: UUID
    let torrentName: String

    @State private var pageSize = 25
    @State private var pageIndex = 0
    @State private var sortOrder = [
        KeyPathComparator(\TorrentCoreMacTrackerTableItem.tier),
    ]

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                VStack(alignment: .leading) {
                    Text("Trackers")
                        .font(.title2.weight(.semibold))
                    Text(torrentName)
                        .foregroundStyle(.secondary)
                }
                Spacer()
                Button {
                    Task { await session.refresh(.trackers(torrentID)) }
                } label: {
                    Label("Refresh", systemImage: "arrow.clockwise")
                }
                Button("Done") { dismiss() }
                    .keyboardShortcut(.defaultAction)
            }
            .padding()

            Divider()

            TorrentCoreMacPhaseBanner(
                phase: session.trackers.phase,
                lastSuccessfulAt: session.trackers.lastSuccessfulAt
            )
            .padding()

            if let trackers = session.trackers.value {
                if trackers.isEmpty {
                    ContentUnavailableView(
                        "No Trackers",
                        systemImage: "antenna.radiowaves.left.and.right.slash",
                        description: Text("TorrentCore reports no trackers for this torrent.")
                    )
                } else {
                    Table(
                        currentPage,
                        sortOrder: $sortOrder,
                        columnCustomization: $columnCustomization
                    ) {
                        TableColumn("Tier", value: \.tier) {
                            Text($0.tier.formatted())
                        }
                        .width(min: 45, ideal: 55)
                        TableColumn("Tracker", value: \.number) {
                            Text("\($0.tier).\($0.number)")
                        }
                        .width(min: 65, ideal: 80)
                        TableColumn("Active", value: \.active) {
                            Text($0.tracker.isActive ? "Yes" : "No")
                        }
                        .width(min: 70, ideal: 85)
                        TableColumn(
                            "Status",
                            value: \.status,
                            comparator: .localizedStandard
                        ) {
                            Text($0.status)
                        }
                        .width(min: 130, ideal: 165)
                        TableColumn("Announce", value: \.canAnnounce) {
                            Text(nullableBoolean($0.tracker.canAnnounce))
                        }
                        .width(min: 85, ideal: 105)
                        TableColumn("Scrape", value: \.canScrape) {
                            Text($0.tracker.canScrape ? "Yes" : "No")
                        }
                        .width(min: 75, ideal: 90)
                        TableColumn("Since Announce", value: \.sinceAnnounce) {
                            Text(duration($0.tracker.timeSinceLastAnnounceSeconds))
                                .monospacedDigit()
                        }
                        .width(min: 110, ideal: 135)
                        TableColumn("Announce OK", value: \.announceSucceeded) {
                            Text(nullableBoolean($0.tracker.lastAnnounceSucceeded))
                        }
                        .width(min: 100, ideal: 120)
                        TableColumn("Since Scrape", value: \.sinceScrape) {
                            Text(duration($0.tracker.timeSinceLastScrapeSeconds))
                                .monospacedDigit()
                        }
                        .width(min: 105, ideal: 130)
                        Group {
                            TableColumn(
                                "Scrape OK",
                                value: \TorrentCoreMacTrackerTableItem.scrapeSucceeded
                            ) {
                                Text(nullableBoolean($0.tracker.lastScrapeSucceeded))
                            }
                            .width(min: 90, ideal: 110)
                            TableColumn(
                                "Failure",
                                value: \TorrentCoreMacTrackerTableItem.failure,
                                comparator: .localizedStandard
                            ) {
                                Text($0.failure)
                                    .lineLimit(2)
                            }
                            .width(min: 180, ideal: 260)
                            TableColumn(
                                "Warning",
                                value: \TorrentCoreMacTrackerTableItem.warning,
                                comparator: .localizedStandard
                            ) {
                                Text($0.warning)
                                    .lineLimit(2)
                            }
                            .width(min: 180, ideal: 260)
                        }
                    }
                    Divider()
                    paginationBar
                }
            } else {
                ContentUnavailableView(
                    "Trackers Unavailable",
                    systemImage: "antenna.radiowaves.left.and.right",
                    description: Text("Waiting for TorrentCore tracker diagnostics.")
                )
            }
        }
        .frame(minWidth: 1_180, idealWidth: 1_560, minHeight: 580, idealHeight: 680)
        .onChange(of: pageSize) { _, _ in pageIndex = 0 }
        .onChange(of: sortOrder) { _, _ in pageIndex = 0 }
        .torrentCoreRefreshWhileVisible(
            session: session,
            context: .trackers(torrentID)
        )
    }

    private var tableItems: [TorrentCoreMacTrackerTableItem] {
        (session.trackers.value ?? [])
            .map { TorrentCoreMacTrackerTableItem(tracker: $0) }
            .sorted(using: sortOrder)
    }

    private var currentPage: [TorrentCoreMacTrackerTableItem] {
        let safeIndex = min(pageIndex, maxPageIndex)
        let start = safeIndex * pageSize
        guard start < tableItems.count else {
            return []
        }
        return Array(tableItems[start..<min(tableItems.count, start + pageSize)])
    }

    private var maxPageIndex: Int {
        max(0, (tableItems.count - 1) / max(1, pageSize))
    }

    private var paginationBar: some View {
        HStack {
            Text(rangeLabel)
                .foregroundStyle(.secondary)
            Spacer()
            Picker("Rows", selection: $pageSize) {
                ForEach([10, 25, 50, 100], id: \.self) {
                    Text("\($0)").tag($0)
                }
            }
            .frame(width: 120)
            Button {
                pageIndex = max(0, pageIndex - 1)
            } label: {
                Label("Previous", systemImage: "chevron.left")
            }
            .disabled(pageIndex == 0)
            Button {
                pageIndex = min(maxPageIndex, pageIndex + 1)
            } label: {
                Label("Next", systemImage: "chevron.right")
            }
            .disabled(pageIndex >= maxPageIndex)
        }
        .padding(10)
    }

    private var rangeLabel: String {
        guard !tableItems.isEmpty else {
            return "0 trackers"
        }
        let start = min(pageIndex, maxPageIndex) * pageSize + 1
        let end = start + currentPage.count - 1
        return "\(start)–\(end) of \(tableItems.count) trackers"
    }

    private func duration(_ seconds: Int64?) -> String {
        guard let seconds else { return "Never" }
        return Duration.seconds(seconds).formatted(.units(
            allowed: [.hours, .minutes, .seconds],
            width: .abbreviated,
            maximumUnitCount: 2
        ))
    }

    private func nullableBoolean(_ value: Bool?) -> String {
        switch value {
        case true: "Yes"
        case false: "No"
        case nil: "N/A"
        }
    }
}
