import SwiftUI
import TorrentCoreAPI
import TorrentCoreFeatures

private struct TorrentCoreMacPeerTableItem: Identifiable {
    let peer: TorrentCorePeer

    var id: String { peer.id }
    var endpoint: String { peer.endpoint ?? "--" }
    var client: String { peer.client ?? "--" }
    var direction: String { peer.direction ?? "--" }
    var connected: Int { peer.isConnected ? 1 : 0 }
    var seeder: Int { peer.isSeeder ? 1 : 0 }
    var downloadRate: Int64 { peer.downloadRateBytesPerSecond }
    var uploadRate: Int64 { peer.uploadRateBytesPerSecond }
    var downloaded: Int64 { peer.downloadedBytes }
    var uploaded: Int64 { peer.uploadedBytes }
    var encryption: String { peer.encryption ?? "--" }
}

enum TorrentCoreMacPeerSortField: String, CaseIterable, Codable, Identifiable {
    case endpoint, client, direction, connected, seeder
    case downloadRate, uploadRate, downloaded, uploaded, encryption

    var id: Self { self }

    var title: String {
        switch self {
        case .endpoint: "Endpoint"
        case .client: "Client"
        case .direction: "Direction"
        case .connected: "Connected"
        case .seeder: "Seeder"
        case .downloadRate: "Down"
        case .uploadRate: "Up"
        case .downloaded: "Downloaded"
        case .uploaded: "Uploaded"
        case .encryption: "Encryption"
        }
    }

    fileprivate func comparator(
        descending: Bool
    ) -> KeyPathComparator<TorrentCoreMacPeerTableItem> {
        let order: SortOrder = descending ? .reverse : .forward
        return switch self {
        case .endpoint:
            KeyPathComparator(\.endpoint, comparator: .localizedStandard, order: order)
        case .client:
            KeyPathComparator(\.client, comparator: .localizedStandard, order: order)
        case .direction:
            KeyPathComparator(\.direction, comparator: .localizedStandard, order: order)
        case .connected:
            KeyPathComparator(\.connected, order: order)
        case .seeder:
            KeyPathComparator(\.seeder, order: order)
        case .downloadRate:
            KeyPathComparator(\.downloadRate, order: order)
        case .uploadRate:
            KeyPathComparator(\.uploadRate, order: order)
        case .downloaded:
            KeyPathComparator(\.downloaded, order: order)
        case .uploaded:
            KeyPathComparator(\.uploaded, order: order)
        case .encryption:
            KeyPathComparator(\.encryption, comparator: .localizedStandard, order: order)
        }
    }

    fileprivate static func field(
        for keyPath: PartialKeyPath<TorrentCoreMacPeerTableItem>
    ) -> Self? {
        switch keyPath {
        case \TorrentCoreMacPeerTableItem.endpoint: .endpoint
        case \TorrentCoreMacPeerTableItem.client: .client
        case \TorrentCoreMacPeerTableItem.direction: .direction
        case \TorrentCoreMacPeerTableItem.connected: .connected
        case \TorrentCoreMacPeerTableItem.seeder: .seeder
        case \TorrentCoreMacPeerTableItem.downloadRate: .downloadRate
        case \TorrentCoreMacPeerTableItem.uploadRate: .uploadRate
        case \TorrentCoreMacPeerTableItem.downloaded: .downloaded
        case \TorrentCoreMacPeerTableItem.uploaded: .uploaded
        case \TorrentCoreMacPeerTableItem.encryption: .encryption
        default: nil
        }
    }
}

private enum TorrentCoreMacPeerColumn: String, CaseIterable, Identifiable {
    case endpoint, client, direction, connected, seeder
    case downloadRate, uploadRate, downloaded, uploaded, encryption

    var id: String { rawValue }
    var title: String { TorrentCoreMacPeerSortField(rawValue: rawValue)?.title ?? rawValue }
    var canHide: Bool { self != .endpoint }

    var isDefaultVisible: Bool {
        switch self {
        case .endpoint, .client, .direction, .connected, .seeder, .downloadRate, .uploadRate:
            true
        case .downloaded, .uploaded, .encryption:
            false
        }
    }
}

private struct TorrentCoreMacTrackerTableItem: Identifiable {
    let tracker: TorrentCoreTracker

    var id: String { tracker.id }
    var tier: Int { tracker.tierNumber }
    var number: Int { tracker.trackerNumber }
    var active: Int { tracker.isActive ? 1 : 0 }
    var status: String { tracker.status ?? "--" }
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
    var failure: String { tracker.failureMessage ?? "--" }
    var warning: String { tracker.warningMessage ?? "--" }

    private static func optionalBooleanSortValue(_ value: Bool?) -> Int {
        switch value {
        case true: 2
        case false: 1
        case nil: 0
        }
    }
}

enum TorrentCoreMacTrackerSortField: String, CaseIterable, Codable, Identifiable {
    case tier, number, active, status, canAnnounce, canScrape
    case sinceAnnounce, announceSucceeded, sinceScrape, scrapeSucceeded
    case failure, warning

    var id: Self { self }

    var title: String {
        switch self {
        case .tier: "Tier"
        case .number: "Tracker"
        case .active: "Active"
        case .status: "Status"
        case .canAnnounce: "Announce"
        case .canScrape: "Scrape"
        case .sinceAnnounce: "Since Announce"
        case .announceSucceeded: "Announce OK"
        case .sinceScrape: "Since Scrape"
        case .scrapeSucceeded: "Scrape OK"
        case .failure: "Failure"
        case .warning: "Warning"
        }
    }

    fileprivate func comparator(
        descending: Bool
    ) -> KeyPathComparator<TorrentCoreMacTrackerTableItem> {
        let order: SortOrder = descending ? .reverse : .forward
        return switch self {
        case .tier:
            KeyPathComparator(\.tier, order: order)
        case .number:
            KeyPathComparator(\.number, order: order)
        case .active:
            KeyPathComparator(\.active, order: order)
        case .status:
            KeyPathComparator(\.status, comparator: .localizedStandard, order: order)
        case .canAnnounce:
            KeyPathComparator(\.canAnnounce, order: order)
        case .canScrape:
            KeyPathComparator(\.canScrape, order: order)
        case .sinceAnnounce:
            KeyPathComparator(\.sinceAnnounce, order: order)
        case .announceSucceeded:
            KeyPathComparator(\.announceSucceeded, order: order)
        case .sinceScrape:
            KeyPathComparator(\.sinceScrape, order: order)
        case .scrapeSucceeded:
            KeyPathComparator(\.scrapeSucceeded, order: order)
        case .failure:
            KeyPathComparator(\.failure, comparator: .localizedStandard, order: order)
        case .warning:
            KeyPathComparator(\.warning, comparator: .localizedStandard, order: order)
        }
    }

    fileprivate static func field(
        for keyPath: PartialKeyPath<TorrentCoreMacTrackerTableItem>
    ) -> Self? {
        switch keyPath {
        case \TorrentCoreMacTrackerTableItem.tier: .tier
        case \TorrentCoreMacTrackerTableItem.number: .number
        case \TorrentCoreMacTrackerTableItem.active: .active
        case \TorrentCoreMacTrackerTableItem.status: .status
        case \TorrentCoreMacTrackerTableItem.canAnnounce: .canAnnounce
        case \TorrentCoreMacTrackerTableItem.canScrape: .canScrape
        case \TorrentCoreMacTrackerTableItem.sinceAnnounce: .sinceAnnounce
        case \TorrentCoreMacTrackerTableItem.announceSucceeded: .announceSucceeded
        case \TorrentCoreMacTrackerTableItem.sinceScrape: .sinceScrape
        case \TorrentCoreMacTrackerTableItem.scrapeSucceeded: .scrapeSucceeded
        case \TorrentCoreMacTrackerTableItem.failure: .failure
        case \TorrentCoreMacTrackerTableItem.warning: .warning
        default: nil
        }
    }
}

private enum TorrentCoreMacTrackerColumn: String, CaseIterable, Identifiable {
    case tier, number, active, status, canAnnounce, canScrape
    case sinceAnnounce, announceSucceeded, sinceScrape, scrapeSucceeded
    case failure, warning

    var id: String { rawValue }
    var title: String { TorrentCoreMacTrackerSortField(rawValue: rawValue)?.title ?? rawValue }
    var canHide: Bool { self != .number }

    var isDefaultVisible: Bool {
        switch self {
        case .tier, .number, .active, .status, .canAnnounce, .canScrape:
            true
        case .sinceAnnounce, .announceSucceeded, .sinceScrape, .scrapeSucceeded,
             .failure, .warning:
            false
        }
    }
}

struct TorrentCoreMacPeersSheet: View {
    @Environment(\.dismiss) private var dismiss
    @AppStorage("TorrentCore.Mac.Peers.PageSize.v1")
    private var storedPageSize = TorrentCoreMacTableSupport.defaultPageSize
    @AppStorage("TorrentCore.Mac.Peers.Sort.v1") private var storedSort = ""
    @AppStorage("TorrentCore.Mac.Peers.Columns.v1")
    private var columnCustomization =
        TableColumnCustomization<TorrentCoreMacPeerTableItem>()

    let session: TorrentCoreFeatureSession
    let torrentID: UUID
    let torrentName: String

    @State private var pageIndex = 0
    @State private var sortDescriptors: [
        TorrentCoreMacSortDescriptor<TorrentCoreMacPeerSortField>
    ]
    @State private var isSortEditorPresented = false
    @State private var notice: TorrentCoreMacNotice?

    init(session: TorrentCoreFeatureSession, torrentID: UUID, torrentName: String) {
        self.session = session
        self.torrentID = torrentID
        self.torrentName = torrentName
        let stored = UserDefaults.standard.string(
            forKey: "TorrentCore.Mac.Peers.Sort.v1"
        ) ?? ""
        _sortDescriptors = State(
            initialValue: TorrentCoreMacSortStorage.decode(
                stored,
                as: TorrentCoreMacPeerSortField.self
            ) ?? Self.defaultSortDescriptors
        )
    }

    var body: some View {
        VStack(spacing: 0) {
            header
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
                    peerTable
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
        .torrentCoreToast(notice: $notice)
        .onAppear {
            if storedSort.isEmpty {
                storedSort = TorrentCoreMacSortStorage.encode(sortDescriptors)
            }
            if !TorrentCoreMacTableSupport.pageSizes.contains(storedPageSize) {
                storedPageSize = TorrentCoreMacTableSupport.defaultPageSize
            }
            clampPage()
        }
        .onChange(of: session.peers.value) { _, _ in clampPage() }
        .onChange(of: sortDescriptors) { _, descriptors in
            storedSort = TorrentCoreMacSortStorage.encode(descriptors)
            pageIndex = 0
        }
        .torrentCoreRefreshWhileVisible(session: session, context: .peers(torrentID))
    }

    private var header: some View {
        HStack {
            VStack(alignment: .leading) {
                Text("Peers").font(.title2.weight(.semibold))
                Text(torrentName).foregroundStyle(.secondary)
            }
            Spacer()
            sortButton
            columnsMenu
            exportMenu
            Button {
                Task { await session.refresh(.peers(torrentID)) }
            } label: {
                Label("Refresh", systemImage: "arrow.clockwise")
            }
            Button("Done") { dismiss() }
                .keyboardShortcut(.defaultAction)
        }
        .padding()
    }

    private var peerTable: some View {
        Table(
            currentPage,
            sortOrder: tableSortOrder,
            columnCustomization: $columnCustomization
        ) {
            TableColumn(sortHeaderTitle(.endpoint), value: \.endpoint, comparator: .localizedStandard) {
                Text($0.endpoint)
            }
            .width(min: 155, ideal: 210)
            .defaultVisibility(.visible)
            .disabledCustomizationBehavior(.visibility)
            .customizationID(TorrentCoreMacPeerColumn.endpoint.id)

            TableColumn(sortHeaderTitle(.client), value: \.client, comparator: .localizedStandard) {
                Text($0.client)
            }
            .width(min: 145, ideal: 190)
            .defaultVisibility(.visible)
            .customizationID(TorrentCoreMacPeerColumn.client.id)

            TableColumn(sortHeaderTitle(.direction), value: \.direction, comparator: .localizedStandard) {
                Text($0.direction)
            }
            .width(min: 105, ideal: 125)
            .defaultVisibility(.visible)
            .customizationID(TorrentCoreMacPeerColumn.direction.id)

            TableColumn(sortHeaderTitle(.connected), value: \.connected) {
                Text($0.peer.isConnected ? "Yes" : "No")
            }
            .width(min: 90, ideal: 105)
            .defaultVisibility(.visible)
            .customizationID(TorrentCoreMacPeerColumn.connected.id)

            TableColumn(sortHeaderTitle(.seeder), value: \.seeder) {
                Text($0.peer.isSeeder ? "Yes" : "No")
            }
            .width(min: 75, ideal: 90)
            .defaultVisibility(.visible)
            .customizationID(TorrentCoreMacPeerColumn.seeder.id)

            TableColumn(sortHeaderTitle(.downloadRate), value: \.downloadRate) {
                Text(TorrentCoreDisplayFormatter.rate($0.downloadRate)).monospacedDigit()
            }
            .width(min: 95, ideal: 115)
            .defaultVisibility(.visible)
            .customizationID(TorrentCoreMacPeerColumn.downloadRate.id)

            TableColumn(sortHeaderTitle(.uploadRate), value: \.uploadRate) {
                Text(TorrentCoreDisplayFormatter.rate($0.uploadRate)).monospacedDigit()
            }
            .width(min: 95, ideal: 115)
            .defaultVisibility(.visible)
            .customizationID(TorrentCoreMacPeerColumn.uploadRate.id)

            TableColumn(sortHeaderTitle(.downloaded), value: \.downloaded) {
                Text(TorrentCoreDisplayFormatter.bytes($0.downloaded)).monospacedDigit()
            }
            .width(min: 105, ideal: 125)
            .defaultVisibility(.hidden)
            .customizationID(TorrentCoreMacPeerColumn.downloaded.id)

            TableColumn(sortHeaderTitle(.uploaded), value: \.uploaded) {
                Text(TorrentCoreDisplayFormatter.bytes($0.uploaded)).monospacedDigit()
            }
            .width(min: 105, ideal: 125)
            .defaultVisibility(.hidden)
            .customizationID(TorrentCoreMacPeerColumn.uploaded.id)

            TableColumn(sortHeaderTitle(.encryption), value: \.encryption, comparator: .localizedStandard) {
                Text($0.encryption)
            }
            .width(min: 115, ideal: 140)
            .defaultVisibility(.hidden)
            .customizationID(TorrentCoreMacPeerColumn.encryption.id)
        }
        .accessibilityIdentifier("peers.table")
    }

    private var tableItems: [TorrentCoreMacPeerTableItem] {
        (session.peers.value ?? [])
            .map { TorrentCoreMacPeerTableItem(peer: $0) }
            .sorted(using: comparatorOrder)
    }

    private var comparatorOrder: [KeyPathComparator<TorrentCoreMacPeerTableItem>] {
        sortDescriptors.map { $0.field.comparator(descending: $0.descending) }
    }

    private var tableSortOrder: Binding<[KeyPathComparator<TorrentCoreMacPeerTableItem>]> {
        Binding(
            get: { comparatorOrder },
            set: { proposed in
                guard let comparator = proposed.first,
                      let field = TorrentCoreMacPeerSortField.field(for: comparator.keyPath)
                else { return }
                sortDescriptors = [
                    .init(field: field, descending: comparator.order == .reverse),
                ]
            }
        )
    }

    private var currentPage: [TorrentCoreMacPeerTableItem] {
        TorrentCoreMacTableSupport.page(tableItems, index: pageIndex, size: pageSize)
    }

    private var pageSize: Int {
        TorrentCoreMacTableSupport.pageSizes.contains(storedPageSize)
            ? storedPageSize
            : TorrentCoreMacTableSupport.defaultPageSize
    }

    private var pageSizeBinding: Binding<Int> {
        Binding(
            get: { pageSize },
            set: { newValue in
                guard TorrentCoreMacTableSupport.pageSizes.contains(newValue) else { return }
                storedPageSize = newValue
                pageIndex = 0
            }
        )
    }

    private var paginationBar: some View {
        TorrentCoreMacPaginationBar(
            resultCount: tableItems.count,
            pageIndex: $pageIndex,
            pageSize: pageSizeBinding,
            accessibilityPrefix: "peers"
        )
    }

    private var sortButton: some View {
        Button("Sort", systemImage: "arrow.up.arrow.down") {
            isSortEditorPresented.toggle()
        }
        .popover(isPresented: $isSortEditorPresented, arrowEdge: .top) {
            TorrentCoreMacSortEditor(
                descriptors: $sortDescriptors,
                defaultDescriptors: Self.defaultSortDescriptors,
                fieldTitle: { $0.title },
                done: { isSortEditorPresented = false }
            )
        }
        .accessibilityIdentifier("peers.sort")
    }

    private var columnsMenu: some View {
        Menu("Columns", systemImage: "rectangle.3.group") {
            ForEach(TorrentCoreMacPeerColumn.allCases) { column in
                if column.canHide {
                    Toggle(column.title, isOn: columnVisibility(column))
                }
            }
            Divider()
            Button("Show All Columns") {
                for column in TorrentCoreMacPeerColumn.allCases {
                    columnCustomization[visibility: column.id] = .visible
                }
            }
            Button("Restore Default Columns") {
                for column in TorrentCoreMacPeerColumn.allCases {
                    columnCustomization[visibility: column.id] = .automatic
                }
            }
            Divider()
            Button("Reset Table Layout") { columnCustomization = .init() }
        }
        .accessibilityIdentifier("peers.columns")
    }

    private var exportMenu: some View {
        Menu("Export", systemImage: "square.and.arrow.up") {
            Button("Export All Results (\(tableItems.count.formatted()))") { exportAll() }
                .disabled(tableItems.isEmpty)
        }
        .accessibilityIdentifier("peers.export")
    }

    private func sortHeaderTitle(_ field: TorrentCoreMacPeerSortField) -> String {
        guard let index = sortDescriptors.firstIndex(where: { $0.field == field }) else {
            return field.title
        }
        return "\(field.title) \(sortDescriptors[index].descending ? "↓" : "↑")\(index + 1)"
    }

    private func columnVisibility(_ column: TorrentCoreMacPeerColumn) -> Binding<Bool> {
        Binding(
            get: {
                let visibility = columnCustomization[visibility: column.id]
                return visibility == .visible
                    || (visibility == .automatic && column.isDefaultVisible)
            },
            set: { visible in
                guard column.canHide else { return }
                columnCustomization[visibility: column.id] = visible ? .visible : .hidden
            }
        )
    }

    private func clampPage() {
        pageIndex = TorrentCoreMacTableSupport.clampedPageIndex(
            pageIndex,
            count: tableItems.count,
            size: pageSize
        )
    }

    private func exportAll() {
        do {
            let fileURL = try TorrentCoreMacTableExport.write(
                headers: Self.exportHeaders,
                rows: tableItems.map { Self.exportRow($0.peer) },
                fileName: "peers-\(torrentID.uuidString)-\(TorrentCoreMacTableExport.timestamp()).csv"
            )
            notice = .init(
                kind: .success,
                message: "Exported \(tableItems.count.formatted()) peer rows to Downloads/\(fileURL.lastPathComponent)."
            )
        } catch {
            notice = .init(
                kind: .error,
                message: "Export failed: \(TorrentCoreMacErrorPresenter.message(error))"
            )
        }
    }

    static let defaultSortDescriptors = [
        TorrentCoreMacSortDescriptor(field: TorrentCoreMacPeerSortField.endpoint, descending: false),
    ]

    static let exportHeaders = [
        "Client",
        "Direction",
        "Download Rate Bytes Per Second",
        "Downloaded Bytes",
        "Encryption",
        "Endpoint",
        "Is Connected",
        "Is Seeder",
        "Upload Rate Bytes Per Second",
        "Uploaded Bytes",
    ]

    static func exportRow(_ value: TorrentCorePeer) -> [String] {
        [
            value.client ?? "",
            value.direction ?? "",
            String(value.downloadRateBytesPerSecond),
            String(value.downloadedBytes),
            value.encryption ?? "",
            value.endpoint ?? "",
            value.isConnected ? "Yes" : "No",
            value.isSeeder ? "Yes" : "No",
            String(value.uploadRateBytesPerSecond),
            String(value.uploadedBytes),
        ]
    }
}

struct TorrentCoreMacTrackersSheet: View {
    @Environment(\.dismiss) private var dismiss
    @AppStorage("TorrentCore.Mac.Trackers.PageSize.v1")
    private var storedPageSize = TorrentCoreMacTableSupport.defaultPageSize
    @AppStorage("TorrentCore.Mac.Trackers.Sort.v1") private var storedSort = ""
    @AppStorage("TorrentCore.Mac.Trackers.Columns.v1")
    private var columnCustomization =
        TableColumnCustomization<TorrentCoreMacTrackerTableItem>()

    let session: TorrentCoreFeatureSession
    let torrentID: UUID
    let torrentName: String

    @State private var pageIndex = 0
    @State private var sortDescriptors: [
        TorrentCoreMacSortDescriptor<TorrentCoreMacTrackerSortField>
    ]
    @State private var isSortEditorPresented = false
    @State private var notice: TorrentCoreMacNotice?

    init(session: TorrentCoreFeatureSession, torrentID: UUID, torrentName: String) {
        self.session = session
        self.torrentID = torrentID
        self.torrentName = torrentName
        let stored = UserDefaults.standard.string(
            forKey: "TorrentCore.Mac.Trackers.Sort.v1"
        ) ?? ""
        _sortDescriptors = State(
            initialValue: TorrentCoreMacSortStorage.decode(
                stored,
                as: TorrentCoreMacTrackerSortField.self
            ) ?? Self.defaultSortDescriptors
        )
    }

    var body: some View {
        VStack(spacing: 0) {
            header
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
                    trackerTable
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
        .torrentCoreToast(notice: $notice)
        .onAppear {
            if storedSort.isEmpty {
                storedSort = TorrentCoreMacSortStorage.encode(sortDescriptors)
            }
            if !TorrentCoreMacTableSupport.pageSizes.contains(storedPageSize) {
                storedPageSize = TorrentCoreMacTableSupport.defaultPageSize
            }
            clampPage()
        }
        .onChange(of: session.trackers.value) { _, _ in clampPage() }
        .onChange(of: sortDescriptors) { _, descriptors in
            storedSort = TorrentCoreMacSortStorage.encode(descriptors)
            pageIndex = 0
        }
        .torrentCoreRefreshWhileVisible(session: session, context: .trackers(torrentID))
    }

    private var header: some View {
        HStack {
            VStack(alignment: .leading) {
                Text("Trackers").font(.title2.weight(.semibold))
                Text(torrentName).foregroundStyle(.secondary)
            }
            Spacer()
            sortButton
            columnsMenu
            exportMenu
            Button {
                Task { await session.refresh(.trackers(torrentID)) }
            } label: {
                Label("Refresh", systemImage: "arrow.clockwise")
            }
            Button("Done") { dismiss() }
                .keyboardShortcut(.defaultAction)
        }
        .padding()
    }

    private var trackerTable: some View {
        Table(
            currentPage,
            sortOrder: tableSortOrder,
            columnCustomization: $columnCustomization
        ) {
            TableColumn(sortHeaderTitle(.tier), value: \.tier) {
                Text($0.tier.formatted())
            }
            .width(min: 45, ideal: 55)
            .defaultVisibility(.visible)
            .customizationID(TorrentCoreMacTrackerColumn.tier.id)

            TableColumn(sortHeaderTitle(.number), value: \.number) {
                Text("\($0.tier).\($0.number)")
            }
            .width(min: 65, ideal: 80)
            .defaultVisibility(.visible)
            .disabledCustomizationBehavior(.visibility)
            .customizationID(TorrentCoreMacTrackerColumn.number.id)

            TableColumn(sortHeaderTitle(.active), value: \.active) {
                Text($0.tracker.isActive ? "Yes" : "No")
            }
            .width(min: 70, ideal: 85)
            .defaultVisibility(.visible)
            .customizationID(TorrentCoreMacTrackerColumn.active.id)

            TableColumn(sortHeaderTitle(.status), value: \.status, comparator: .localizedStandard) {
                Text($0.status)
            }
            .width(min: 130, ideal: 165)
            .defaultVisibility(.visible)
            .customizationID(TorrentCoreMacTrackerColumn.status.id)

            TableColumn(sortHeaderTitle(.canAnnounce), value: \.canAnnounce) {
                Text(nullableBoolean($0.tracker.canAnnounce))
            }
            .width(min: 85, ideal: 105)
            .defaultVisibility(.visible)
            .customizationID(TorrentCoreMacTrackerColumn.canAnnounce.id)

            TableColumn(sortHeaderTitle(.canScrape), value: \.canScrape) {
                Text($0.tracker.canScrape ? "Yes" : "No")
            }
            .width(min: 75, ideal: 90)
            .defaultVisibility(.visible)
            .customizationID(TorrentCoreMacTrackerColumn.canScrape.id)

            TableColumn(sortHeaderTitle(.sinceAnnounce), value: \.sinceAnnounce) {
                Text(duration($0.tracker.timeSinceLastAnnounceSeconds)).monospacedDigit()
            }
            .width(min: 110, ideal: 135)
            .defaultVisibility(.hidden)
            .customizationID(TorrentCoreMacTrackerColumn.sinceAnnounce.id)

            TableColumn(sortHeaderTitle(.announceSucceeded), value: \.announceSucceeded) {
                Text(nullableBoolean($0.tracker.lastAnnounceSucceeded))
            }
            .width(min: 100, ideal: 120)
            .defaultVisibility(.hidden)
            .customizationID(TorrentCoreMacTrackerColumn.announceSucceeded.id)

            TableColumn(sortHeaderTitle(.sinceScrape), value: \.sinceScrape) {
                Text(duration($0.tracker.timeSinceLastScrapeSeconds)).monospacedDigit()
            }
            .width(min: 105, ideal: 130)
            .defaultVisibility(.hidden)
            .customizationID(TorrentCoreMacTrackerColumn.sinceScrape.id)

            Group {
                TableColumn(
                    sortHeaderTitle(.scrapeSucceeded),
                    value: \TorrentCoreMacTrackerTableItem.scrapeSucceeded
                ) {
                    Text(nullableBoolean($0.tracker.lastScrapeSucceeded))
                }
                .width(min: 90, ideal: 110)
                .defaultVisibility(.hidden)
                .customizationID(TorrentCoreMacTrackerColumn.scrapeSucceeded.id)

                TableColumn(
                    sortHeaderTitle(.failure),
                    value: \TorrentCoreMacTrackerTableItem.failure,
                    comparator: .localizedStandard
                ) {
                    Text($0.failure).lineLimit(2)
                }
                .width(min: 180, ideal: 260)
                .defaultVisibility(.hidden)
                .customizationID(TorrentCoreMacTrackerColumn.failure.id)

                TableColumn(
                    sortHeaderTitle(.warning),
                    value: \TorrentCoreMacTrackerTableItem.warning,
                    comparator: .localizedStandard
                ) {
                    Text($0.warning).lineLimit(2)
                }
                .width(min: 180, ideal: 260)
                .defaultVisibility(.hidden)
                .customizationID(TorrentCoreMacTrackerColumn.warning.id)
            }
        }
        .accessibilityIdentifier("trackers.table")
    }

    private var tableItems: [TorrentCoreMacTrackerTableItem] {
        (session.trackers.value ?? [])
            .map { TorrentCoreMacTrackerTableItem(tracker: $0) }
            .sorted(using: comparatorOrder)
    }

    private var comparatorOrder: [KeyPathComparator<TorrentCoreMacTrackerTableItem>] {
        sortDescriptors.map { $0.field.comparator(descending: $0.descending) }
    }

    private var tableSortOrder: Binding<[KeyPathComparator<TorrentCoreMacTrackerTableItem>]> {
        Binding(
            get: { comparatorOrder },
            set: { proposed in
                guard let comparator = proposed.first,
                      let field = TorrentCoreMacTrackerSortField.field(for: comparator.keyPath)
                else { return }
                sortDescriptors = [
                    .init(field: field, descending: comparator.order == .reverse),
                ]
            }
        )
    }

    private var currentPage: [TorrentCoreMacTrackerTableItem] {
        TorrentCoreMacTableSupport.page(tableItems, index: pageIndex, size: pageSize)
    }

    private var pageSize: Int {
        TorrentCoreMacTableSupport.pageSizes.contains(storedPageSize)
            ? storedPageSize
            : TorrentCoreMacTableSupport.defaultPageSize
    }

    private var pageSizeBinding: Binding<Int> {
        Binding(
            get: { pageSize },
            set: { newValue in
                guard TorrentCoreMacTableSupport.pageSizes.contains(newValue) else { return }
                storedPageSize = newValue
                pageIndex = 0
            }
        )
    }

    private var paginationBar: some View {
        TorrentCoreMacPaginationBar(
            resultCount: tableItems.count,
            pageIndex: $pageIndex,
            pageSize: pageSizeBinding,
            accessibilityPrefix: "trackers"
        )
    }

    private var sortButton: some View {
        Button("Sort", systemImage: "arrow.up.arrow.down") {
            isSortEditorPresented.toggle()
        }
        .popover(isPresented: $isSortEditorPresented, arrowEdge: .top) {
            TorrentCoreMacSortEditor(
                descriptors: $sortDescriptors,
                defaultDescriptors: Self.defaultSortDescriptors,
                fieldTitle: { $0.title },
                done: { isSortEditorPresented = false }
            )
        }
        .accessibilityIdentifier("trackers.sort")
    }

    private var columnsMenu: some View {
        Menu("Columns", systemImage: "rectangle.3.group") {
            ForEach(TorrentCoreMacTrackerColumn.allCases) { column in
                if column.canHide {
                    Toggle(column.title, isOn: columnVisibility(column))
                }
            }
            Divider()
            Button("Show All Columns") {
                for column in TorrentCoreMacTrackerColumn.allCases {
                    columnCustomization[visibility: column.id] = .visible
                }
            }
            Button("Restore Default Columns") {
                for column in TorrentCoreMacTrackerColumn.allCases {
                    columnCustomization[visibility: column.id] = .automatic
                }
            }
            Divider()
            Button("Reset Table Layout") { columnCustomization = .init() }
        }
        .accessibilityIdentifier("trackers.columns")
    }

    private var exportMenu: some View {
        Menu("Export", systemImage: "square.and.arrow.up") {
            Button("Export All Results (\(tableItems.count.formatted()))") { exportAll() }
                .disabled(tableItems.isEmpty)
        }
        .accessibilityIdentifier("trackers.export")
    }

    private func sortHeaderTitle(_ field: TorrentCoreMacTrackerSortField) -> String {
        guard let index = sortDescriptors.firstIndex(where: { $0.field == field }) else {
            return field.title
        }
        return "\(field.title) \(sortDescriptors[index].descending ? "↓" : "↑")\(index + 1)"
    }

    private func columnVisibility(_ column: TorrentCoreMacTrackerColumn) -> Binding<Bool> {
        Binding(
            get: {
                let visibility = columnCustomization[visibility: column.id]
                return visibility == .visible
                    || (visibility == .automatic && column.isDefaultVisible)
            },
            set: { visible in
                guard column.canHide else { return }
                columnCustomization[visibility: column.id] = visible ? .visible : .hidden
            }
        )
    }

    private func clampPage() {
        pageIndex = TorrentCoreMacTableSupport.clampedPageIndex(
            pageIndex,
            count: tableItems.count,
            size: pageSize
        )
    }

    private func exportAll() {
        do {
            let fileURL = try TorrentCoreMacTableExport.write(
                headers: Self.exportHeaders,
                rows: tableItems.map { Self.exportRow($0.tracker) },
                fileName: "trackers-\(torrentID.uuidString)-\(TorrentCoreMacTableExport.timestamp()).csv"
            )
            notice = .init(
                kind: .success,
                message: "Exported \(tableItems.count.formatted()) tracker rows to Downloads/\(fileURL.lastPathComponent)."
            )
        } catch {
            notice = .init(
                kind: .error,
                message: "Export failed: \(TorrentCoreMacErrorPresenter.message(error))"
            )
        }
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

    private static func exportBoolean(_ value: Bool?) -> String {
        guard let value else { return "" }
        return value ? "Yes" : "No"
    }

    static let defaultSortDescriptors = [
        TorrentCoreMacSortDescriptor(field: TorrentCoreMacTrackerSortField.tier, descending: false),
        TorrentCoreMacSortDescriptor(field: TorrentCoreMacTrackerSortField.number, descending: false),
    ]

    static let exportHeaders = [
        "Can Announce",
        "Can Scrape",
        "Failure Message",
        "Is Active",
        "Last Announce Succeeded",
        "Last Scrape Succeeded",
        "Status",
        "Tier Number",
        "Time Since Last Announce Seconds",
        "Time Since Last Scrape Seconds",
        "Tracker Number",
        "Warning Message",
    ]

    static func exportRow(_ value: TorrentCoreTracker) -> [String] {
        [
            exportBoolean(value.canAnnounce),
            value.canScrape ? "Yes" : "No",
            value.failureMessage ?? "",
            value.isActive ? "Yes" : "No",
            exportBoolean(value.lastAnnounceSucceeded),
            exportBoolean(value.lastScrapeSucceeded),
            value.status ?? "",
            String(value.tierNumber),
            value.timeSinceLastAnnounceSeconds.map(String.init) ?? "",
            value.timeSinceLastScrapeSeconds.map(String.init) ?? "",
            String(value.trackerNumber),
            value.warningMessage ?? "",
        ]
    }
}
