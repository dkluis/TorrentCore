import AppKit
import Foundation
import SwiftUI
import TorrentCoreAPI
import TorrentCoreFeatures

private enum TorrentCoreMacMagnetCopyStatus {
    case idle
    case copied
    case failed

    var label: String {
        switch self {
        case .idle:
            "Copy Magnet"
        case .copied:
            "Copied"
        case .failed:
            "Copy Failed"
        }
    }

    var systemImage: String {
        switch self {
        case .idle:
            "doc.on.doc"
        case .copied:
            "checkmark"
        case .failed:
            "exclamationmark.triangle"
        }
    }
}

private struct TorrentCoreMacHistoryTableItem: Identifiable {
    let summary: TorrentCoreHistorySummary

    var id: String { summary.id }
    var lastUpdated: Date { summary.lastUpdatedAt }
    var name: String { summary.name ?? "Unnamed Torrent" }
    var category: String { TorrentCoreDisplayFormatter.category(summary.categoryKey) }
    var state: String { TorrentCoreDisplayFormatter.operatorValue(summary.latestTorrentState) }
    var outcome: String { summary.outcome.rawValue }
    var progress: Double { summary.latestProgressPercent }
    var downloaded: Int64 { summary.latestDownloadedBytes }
    var total: Int64 { summary.latestTotalBytes ?? -1 }
    var callback: String {
        TorrentCoreCompletionCallbackPresentation.state(summary.latestCallbackStatus)
    }
    var removed: Date { summary.removedAt ?? .distantPast }
    var removalReason: String {
        TorrentCoreDisplayFormatter.operatorValue(summary.removalReason)
    }
    var callbackFinalResult: String { summary.completionCallbackFinalResult ?? "" }
    var dataDeleted: Int { summary.dataDeleted ? 1 : 0 }
    var downloadCompleted: Date { summary.downloadCompletedAt ?? .distantPast }
    var downloadRootPath: String { summary.downloadRootPath ?? "" }
    var downloadStarted: Date { summary.downloadStartedAt ?? .distantPast }
    var infoHash: String { summary.infoHash ?? "" }
    var lastActivity: Date { summary.lastActivityAt ?? .distantPast }
    var connectedPeers: Int { summary.latestConnectedPeerCount }
    var downloadRate: Int64 { summary.latestDownloadRateBytesPerSecond }
    var errorMessage: String { summary.latestErrorMessage ?? "" }
    var trackerCount: Int { summary.latestTrackerCount }
    var uploadRate: Int64 { summary.latestUploadRateBytesPerSecond }
    var uploaded: Int64 { summary.latestUploadedBytes }
    var waitReason: String { summary.latestWaitReason ?? "" }
    var metadataResolved: Date { summary.metadataResolvedAt ?? .distantPast }
    var removalKind: String { summary.removalKind?.rawValue ?? "" }
    var removedByCleanup: Int { summary.removedByCleanupPolicy ? 1 : 0 }
    var seedingStarted: Date { summary.seedingStartedAt ?? .distantPast }
    var submitted: Date { summary.submittedAt }
    var torrentID: String { summary.torrentID?.uuidString ?? "" }
}

enum TorrentCoreMacHistorySortField: String, CaseIterable, Codable, Identifiable {
    case lastUpdated
    case name
    case category
    case state
    case outcome
    case progress
    case downloaded
    case total
    case callback
    case removed
    case removalReason
    case callbackFinalResult
    case dataDeleted
    case downloadCompleted
    case downloadRootPath
    case downloadStarted
    case infoHash
    case lastActivity
    case connectedPeers
    case downloadRate
    case errorMessage
    case trackerCount
    case uploadRate
    case uploaded
    case waitReason
    case metadataResolved
    case removalKind
    case removedByCleanup
    case seedingStarted
    case submitted
    case torrentID

    var id: Self { self }

    var title: String {
        switch self {
        case .lastUpdated: "Last Updated"
        case .name: "Name"
        case .category: "Category"
        case .state: "State"
        case .outcome: "Outcome"
        case .progress: "Progress"
        case .downloaded: "Downloaded"
        case .total: "Total"
        case .callback: "Callback"
        case .removed: "Removed"
        case .removalReason: "Removal Reason"
        case .callbackFinalResult: "Callback Final Result"
        case .dataDeleted: "Data Deleted"
        case .downloadCompleted: "Download Completed"
        case .downloadRootPath: "Download Root Path"
        case .downloadStarted: "Download Started"
        case .infoHash: "Info Hash"
        case .lastActivity: "Last Activity"
        case .connectedPeers: "Connected Peers"
        case .downloadRate: "Download Rate"
        case .errorMessage: "Error Message"
        case .trackerCount: "Tracker Count"
        case .uploadRate: "Upload Rate"
        case .uploaded: "Uploaded"
        case .waitReason: "Wait Reason"
        case .metadataResolved: "Metadata Resolved"
        case .removalKind: "Removal Kind"
        case .removedByCleanup: "Removed by Cleanup"
        case .seedingStarted: "Seeding Started"
        case .submitted: "Submitted"
        case .torrentID: "Torrent ID"
        }
    }

    fileprivate func comparator(descending: Bool) -> KeyPathComparator<TorrentCoreMacHistoryTableItem> {
        let order: SortOrder = descending ? .reverse : .forward
        return switch self {
        case .lastUpdated:
            KeyPathComparator(\.lastUpdated, order: order)
        case .name:
            KeyPathComparator(\.name, comparator: .localizedStandard, order: order)
        case .category:
            KeyPathComparator(\.category, comparator: .localizedStandard, order: order)
        case .state:
            KeyPathComparator(\.state, comparator: .localizedStandard, order: order)
        case .outcome:
            KeyPathComparator(\.outcome, comparator: .localizedStandard, order: order)
        case .progress:
            KeyPathComparator(\.progress, order: order)
        case .downloaded:
            KeyPathComparator(\.downloaded, order: order)
        case .total:
            KeyPathComparator(\.total, order: order)
        case .callback:
            KeyPathComparator(\.callback, comparator: .localizedStandard, order: order)
        case .removed:
            KeyPathComparator(\.removed, order: order)
        case .removalReason:
            KeyPathComparator(\.removalReason, comparator: .localizedStandard, order: order)
        case .callbackFinalResult:
            KeyPathComparator(\.callbackFinalResult, comparator: .localizedStandard, order: order)
        case .dataDeleted:
            KeyPathComparator(\.dataDeleted, order: order)
        case .downloadCompleted:
            KeyPathComparator(\.downloadCompleted, order: order)
        case .downloadRootPath:
            KeyPathComparator(\.downloadRootPath, comparator: .localizedStandard, order: order)
        case .downloadStarted:
            KeyPathComparator(\.downloadStarted, order: order)
        case .infoHash:
            KeyPathComparator(\.infoHash, comparator: .localizedStandard, order: order)
        case .lastActivity:
            KeyPathComparator(\.lastActivity, order: order)
        case .connectedPeers:
            KeyPathComparator(\.connectedPeers, order: order)
        case .downloadRate:
            KeyPathComparator(\.downloadRate, order: order)
        case .errorMessage:
            KeyPathComparator(\.errorMessage, comparator: .localizedStandard, order: order)
        case .trackerCount:
            KeyPathComparator(\.trackerCount, order: order)
        case .uploadRate:
            KeyPathComparator(\.uploadRate, order: order)
        case .uploaded:
            KeyPathComparator(\.uploaded, order: order)
        case .waitReason:
            KeyPathComparator(\.waitReason, comparator: .localizedStandard, order: order)
        case .metadataResolved:
            KeyPathComparator(\.metadataResolved, order: order)
        case .removalKind:
            KeyPathComparator(\.removalKind, comparator: .localizedStandard, order: order)
        case .removedByCleanup:
            KeyPathComparator(\.removedByCleanup, order: order)
        case .seedingStarted:
            KeyPathComparator(\.seedingStarted, order: order)
        case .submitted:
            KeyPathComparator(\.submitted, order: order)
        case .torrentID:
            KeyPathComparator(\.torrentID, comparator: .localizedStandard, order: order)
        }
    }

    fileprivate static func field(
        for keyPath: PartialKeyPath<TorrentCoreMacHistoryTableItem>
    ) -> Self? {
        switch keyPath {
        case \TorrentCoreMacHistoryTableItem.lastUpdated: .lastUpdated
        case \TorrentCoreMacHistoryTableItem.name: .name
        case \TorrentCoreMacHistoryTableItem.category: .category
        case \TorrentCoreMacHistoryTableItem.state: .state
        case \TorrentCoreMacHistoryTableItem.outcome: .outcome
        case \TorrentCoreMacHistoryTableItem.progress: .progress
        case \TorrentCoreMacHistoryTableItem.downloaded: .downloaded
        case \TorrentCoreMacHistoryTableItem.total: .total
        case \TorrentCoreMacHistoryTableItem.callback: .callback
        case \TorrentCoreMacHistoryTableItem.removed: .removed
        case \TorrentCoreMacHistoryTableItem.removalReason: .removalReason
        case \TorrentCoreMacHistoryTableItem.callbackFinalResult: .callbackFinalResult
        case \TorrentCoreMacHistoryTableItem.dataDeleted: .dataDeleted
        case \TorrentCoreMacHistoryTableItem.downloadCompleted: .downloadCompleted
        case \TorrentCoreMacHistoryTableItem.downloadRootPath: .downloadRootPath
        case \TorrentCoreMacHistoryTableItem.downloadStarted: .downloadStarted
        case \TorrentCoreMacHistoryTableItem.infoHash: .infoHash
        case \TorrentCoreMacHistoryTableItem.lastActivity: .lastActivity
        case \TorrentCoreMacHistoryTableItem.connectedPeers: .connectedPeers
        case \TorrentCoreMacHistoryTableItem.downloadRate: .downloadRate
        case \TorrentCoreMacHistoryTableItem.errorMessage: .errorMessage
        case \TorrentCoreMacHistoryTableItem.trackerCount: .trackerCount
        case \TorrentCoreMacHistoryTableItem.uploadRate: .uploadRate
        case \TorrentCoreMacHistoryTableItem.uploaded: .uploaded
        case \TorrentCoreMacHistoryTableItem.waitReason: .waitReason
        case \TorrentCoreMacHistoryTableItem.metadataResolved: .metadataResolved
        case \TorrentCoreMacHistoryTableItem.removalKind: .removalKind
        case \TorrentCoreMacHistoryTableItem.removedByCleanup: .removedByCleanup
        case \TorrentCoreMacHistoryTableItem.seedingStarted: .seedingStarted
        case \TorrentCoreMacHistoryTableItem.submitted: .submitted
        case \TorrentCoreMacHistoryTableItem.torrentID: .torrentID
        default: nil
        }
    }
}

private enum TorrentCoreMacHistoryColumn: String, CaseIterable, Identifiable {
    case lastUpdated, name, category, state, outcome, progress, downloaded, total, callback
    case removed, removalReason, callbackFinalResult, dataDeleted, downloadCompleted
    case downloadRootPath, downloadStarted, infoHash, lastActivity, connectedPeers
    case downloadRate, errorMessage, trackerCount, uploadRate, uploaded, waitReason
    case metadataResolved, removalKind, removedByCleanup, seedingStarted, submitted, torrentID

    var id: String { rawValue }
    var title: String { TorrentCoreMacHistorySortField(rawValue: rawValue)?.title ?? rawValue }
    var canHide: Bool { self != .name }
    var isDefaultVisible: Bool {
        switch self {
        case .lastUpdated, .name, .category, .state, .outcome, .progress,
             .downloaded, .total, .callback, .removed, .removalReason:
            true
        default:
            false
        }
    }
}

struct TorrentCoreMacHistoryView: View {
    static var defaultQuery: TorrentCoreHistoryQuery {
        let calendar = Calendar.current
        let today = calendar.startOfDay(for: Date())
        return TorrentCoreHistoryQuery(
            fromDate: dateOnlyFormatter.string(from: today),
            toDate: dateOnlyFormatter.string(from: today),
            take: 500
        )
    }

    let session: TorrentCoreFeatureSession
    @Binding var query: TorrentCoreHistoryQuery
    @Binding var selectedTorrentID: UUID?
    @Binding var isInspectorPresented: Bool
    let contextChanged: () -> Void
    let showTorrent: (UUID) -> Void

    @AppStorage("TorrentCore.Mac.History.PageSize.v1")
    private var storedPageSize = TorrentCoreMacTableSupport.defaultPageSize
    @AppStorage("TorrentCore.Mac.History.Sort.v4") private var storedSort = ""
    @AppStorage("TorrentCore.Mac.History.Columns.v1")
    private var columnCustomization =
        TableColumnCustomization<TorrentCoreMacHistoryTableItem>()
    @AppStorage("TorrentCore.Mac.History.OverlayWidth.v1") private var overlayWidth = 400.0

    @State private var nameFilter: String
    @State private var categoryFilter: String
    @State private var stateFilter: String
    @State private var outcomeFilter: String
    @State private var torrentIDFilterInput: String
    @State private var fromDate: Date
    @State private var toDate: Date
    @State private var includesFromDate: Bool
    @State private var includesToDate: Bool
    @State private var selectedHistoryKey: String?
    @State private var pageIndex = 0
    @State private var sortDescriptors: [
        TorrentCoreMacSortDescriptor<TorrentCoreMacHistorySortField>
    ]
    @State private var isSortEditorPresented = false
    @State private var notice: TorrentCoreMacNotice?
    @State private var magnetCopyStatus = TorrentCoreMacMagnetCopyStatus.idle
    @State private var magnetCopyResetTask: Task<Void, Never>?

    init(
        session: TorrentCoreFeatureSession,
        query: Binding<TorrentCoreHistoryQuery>,
        selectedTorrentID: Binding<UUID?>,
        isInspectorPresented: Binding<Bool>,
        contextChanged: @escaping () -> Void,
        showTorrent: @escaping (UUID) -> Void
    ) {
        self.session = session
        _query = query
        _selectedTorrentID = selectedTorrentID
        _isInspectorPresented = isInspectorPresented
        self.contextChanged = contextChanged
        self.showTorrent = showTorrent

        let initial = query.wrappedValue
        _nameFilter = State(initialValue: initial.torrentName ?? "")
        _categoryFilter = State(initialValue: initial.categoryKey ?? "")
        _stateFilter = State(initialValue: initial.state ?? "")
        _outcomeFilter = State(initialValue: initial.outcome?.rawValue ?? "")
        _torrentIDFilterInput = State(initialValue: initial.torrentID?.uuidString ?? "")
        _selectedHistoryKey = State(
            initialValue: isInspectorPresented.wrappedValue
                ? selectedTorrentID.wrappedValue?.uuidString
                : nil
        )
        _includesFromDate = State(initialValue: initial.fromDate != nil)
        _includesToDate = State(initialValue: initial.toDate != nil)
        _fromDate = State(
            initialValue: Self.dateOnlyFormatter.date(from: initial.fromDate ?? "") ?? Date()
        )
        _toDate = State(
            initialValue: Self.dateOnlyFormatter.date(from: initial.toDate ?? "") ?? Date()
        )
        let defaults = UserDefaults.standard
        let stored = defaults.string(
            forKey: "TorrentCore.Mac.History.Sort.v4"
        ) ?? ""
        let storedField = defaults.string(
            forKey: "TorrentCore.Mac.History.Sort.v3"
        )
        let field = TorrentCoreMacHistorySortField(rawValue: storedField ?? "")
            ?? .lastUpdated
        let descending = defaults.object(
            forKey: "TorrentCore.Mac.History.SortDescending.v1"
        ) as? Bool ?? true
        _sortDescriptors = State(
            initialValue: TorrentCoreMacSortStorage.decode(
                stored,
                as: TorrentCoreMacHistorySortField.self
            ) ?? [.init(field: field, descending: descending)]
        )
    }

    var body: some View {
        VStack(spacing: 0) {
            filterBar
            Divider()

            TorrentCoreMacPhaseBanner(
                phase: session.history.phase,
                lastSuccessfulAt: session.history.lastSuccessfulAt
            )
            .padding(.horizontal, 12)
            .padding(.top, 8)

            abandonmentSummary

            if session.history.value == nil {
                ContentUnavailableView {
                    Label(unavailableTitle, systemImage: unavailableSystemImage)
                } description: {
                    Text(unavailableMessage)
                } actions: {
                    if case .loading = session.history.phase {
                        ProgressView()
                            .controlSize(.small)
                    }
                }
            } else if sortedItems.isEmpty {
                ContentUnavailableView(
                    "No History Matches",
                    systemImage: "line.3.horizontal.decrease.circle",
                    description: Text("Adjust the history filters and search again.")
                )
            } else {
                historyTable
                Divider()
                paginationBar
            }
        }
        .toolbar {
            ToolbarItem { sortButton }
            ToolbarItem { columnsMenu }
            ToolbarItem { exportMenu }
        }
        .torrentCoreTrailingOverlay(
            isPresented: isInspectorPresented,
            width: $overlayWidth
        ) {
            historyInspector
        }
        .torrentCoreToast(notice: $notice)
        .onChange(of: selectedHistoryKey) { _, key in
            magnetCopyResetTask?.cancel()
            magnetCopyStatus = .idle
            selectedTorrentID = sortedItems.first(where: { $0.id == key })?.summary.torrentID
            isInspectorPresented = selectedTorrentID != nil
            contextChanged()
        }
        .onChange(of: isInspectorPresented) { _, _ in
            contextChanged()
        }
        .onAppear {
            if storedSort.isEmpty {
                storedSort = TorrentCoreMacSortStorage.encode(sortDescriptors)
            }
            if !TorrentCoreMacTableSupport.pageSizes.contains(storedPageSize) {
                storedPageSize = TorrentCoreMacTableSupport.defaultPageSize
            }
            overlayWidth = TorrentCoreMacTableSupport.clampedOverlayWidth(overlayWidth)
            clampPageAndReconcileSelection()
        }
        .onChange(of: pageIndex) { _, _ in
            reconcileSelectionWithCurrentPage()
        }
        .onChange(of: session.history.value) { _, _ in
            clampPageAndReconcileSelection()
        }
        .onChange(of: sortDescriptors) { _, descriptors in
            storedSort = TorrentCoreMacSortStorage.encode(descriptors)
            pageIndex = 0
            reconcileSelectionWithCurrentPage()
        }
        .onDisappear {
            magnetCopyResetTask?.cancel()
        }
        .task(id: session.activeProfile?.id) {
            await session.refreshHistoryFilterOptions()
        }
        .torrentCoreRefreshWhileVisible(
            session: session,
            context: refreshContext
        )
        .allowsHitTesting(!isTorrentProcessingPaused)
        .overlay {
            if isTorrentProcessingPaused, let hostStatus = session.hostStatus.value {
                TorrentCoreMacProcessingPausedOverlay(hostStatus: hostStatus) {
                    Task { await session.refresh(refreshContext) }
                }
            }
        }
    }

    private var isTorrentProcessingPaused: Bool {
        session.hostStatus.value?.torrentProcessingAvailable == false
    }

    private var refreshContext: TorrentCoreFeatureContext {
        .history(
            query: query,
            selectedTorrentID: isInspectorPresented ? selectedTorrentID : nil
        )
    }

    private var filterBar: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 10) {
                HStack(spacing: 4) {
                    TextField("Torrent name", text: $nameFilter)
                        .textFieldStyle(.roundedBorder)
                        .frame(width: 180)
                    TorrentCoreMacHelpButton(
                        content: TorrentCoreHelpCatalog.History.torrentName
                    )
                }
                HStack(spacing: 4) {
                    Picker("Category", selection: $categoryFilter) {
                        Text("All Categories").tag("")
                        ForEach(historyCategoryOptions, id: \.self) {
                            Text($0).tag($0)
                        }
                    }
                    .frame(width: 180)
                    TorrentCoreMacHelpButton(
                        content: TorrentCoreHelpCatalog.History.category
                    )
                }
                HStack(spacing: 4) {
                    Picker("State", selection: $stateFilter) {
                        Text("All States").tag("")
                        ForEach(historyStateOptions, id: \.self) {
                            Text(TorrentCoreDisplayFormatter.splitIdentifier($0)).tag($0)
                        }
                    }
                    .frame(width: 180)
                    TorrentCoreMacHelpButton(content: TorrentCoreHelpCatalog.History.state)
                }
                HStack(spacing: 4) {
                    Picker("Outcome", selection: $outcomeFilter) {
                        Text("All Outcomes").tag("")
                        Text("Active").tag(TorrentCoreHistoryOutcome.active.rawValue)
                        Text("Removed").tag(TorrentCoreHistoryOutcome.removed.rawValue)
                        Text("Abandoned").tag(TorrentCoreHistoryOutcome.abandoned.rawValue)
                    }
                    .frame(width: 190)
                    TorrentCoreMacHelpButton(
                        content: TorrentCoreHelpCatalog.History.outcome
                    )
                }

                Button("Search", action: applyFilters)
                    .buttonStyle(.borderedProminent)
                    .keyboardShortcut(.return, modifiers: [])
                    .disabled(!isTorrentIDFilterValid)
                    .accessibilityIdentifier("history.search")
                Button("Clear Filters", action: clearFilters)
                    .accessibilityIdentifier("history.clearFilters")
                Button("Reset Filters", action: resetFilters)
                    .accessibilityIdentifier("history.resetFilters")

                Spacer(minLength: 0)
            }

            HStack(spacing: 10) {
                TextField("Torrent ID", text: $torrentIDFilterInput)
                    .textFieldStyle(.roundedBorder)
                    .frame(width: 270)
                    .accessibilityIdentifier("history.torrentIDFilter")

                HStack(spacing: 4) {
                    Toggle("From", isOn: $includesFromDate)
                        .toggleStyle(.checkbox)
                    TorrentCoreMacHelpButton(
                        content: TorrentCoreHelpCatalog.History.fromDate
                    )
                }
                DatePicker("", selection: $fromDate, displayedComponents: .date)
                    .labelsHidden()
                    .disabled(!includesFromDate)

                HStack(spacing: 4) {
                    Toggle("Through", isOn: $includesToDate)
                        .toggleStyle(.checkbox)
                    TorrentCoreMacHelpButton(
                        content: TorrentCoreHelpCatalog.History.toDate
                    )
                }
                DatePicker("", selection: $toDate, displayedComponents: .date)
                    .labelsHidden()
                    .disabled(!includesToDate)

                Spacer(minLength: 0)
            }

            if !isTorrentIDFilterValid {
                Text("Enter a valid Torrent ID.")
                    .font(.caption)
                    .foregroundStyle(.red)
                    .accessibilityIdentifier("history.torrentIDFilter.error")
            }
        }
        .padding(12)
        .tint(.orange)
    }

    private var abandonmentSummary: some View {
        HStack {
            Label(
                "\(session.abandonedHistory.value?.count ?? 0) abandoned torrent records",
                systemImage: "exclamationmark.arrow.trianglehead.2.clockwise.rotate.90"
            )
            .foregroundStyle(.secondary)
            Spacer()
            Button("Show Abandoned") {
                outcomeFilter = TorrentCoreHistoryOutcome.abandoned.rawValue
                applyFilters()
            }
            .buttonStyle(.link)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
    }

    private var historyTable: some View {
        Table(
            currentPage,
            selection: $selectedHistoryKey,
            sortOrder: tableSortOrder,
            columnCustomization: $columnCustomization
        ) {
            Group {
            TableColumn(
                sortHeaderTitle(.lastUpdated),
                value: \TorrentCoreMacHistoryTableItem.lastUpdated
            ) {
                Text(TorrentCoreDisplayFormatter.timestamp($0.summary.lastUpdatedAt))
                    .monospacedDigit()
            }
            .width(min: 85, ideal: 165)
            .defaultVisibility(.visible)
            .customizationID("lastUpdated")

            TableColumn(
                sortHeaderTitle(.name),
                value: \.name,
                comparator: .localizedStandard
            ) { item in
                Text(item.name)
                    .lineLimit(2)
                    .contextMenu {
                        Button("Inspect History") {
                            selectedHistoryKey = item.id
                        }
                        if item.summary.outcome == .active,
                           let torrentID = item.summary.torrentID
                        {
                            Button("Show in Torrents") {
                                showTorrent(torrentID)
                            }
                        }
                    }
                    .accessibilityIdentifier("history.row")
            }
            .width(min: 130, ideal: 340)
            .defaultVisibility(.visible)
            .disabledCustomizationBehavior(.visibility)
            .customizationID("name")

            TableColumn(sortHeaderTitle(.category), value: \.category, comparator: .localizedStandard) {
                Text($0.category)
            }
            .width(min: 55, ideal: 135)
            .defaultVisibility(.visible)
            .customizationID("category")

            TableColumn(sortHeaderTitle(.state), value: \.state, comparator: .localizedStandard) {
                Text(TorrentCoreDisplayFormatter.splitIdentifier($0.state))
            }
            .width(min: 65, ideal: 160)
            .defaultVisibility(.visible)
            .customizationID("state")

            TableColumn(sortHeaderTitle(.outcome), value: \.outcome, comparator: .localizedStandard) {
                Text(TorrentCoreDisplayFormatter.splitIdentifier($0.outcome))
            }
            .width(min: 55, ideal: 125)
            .defaultVisibility(.visible)
            .customizationID("outcome")

            TableColumn(sortHeaderTitle(.progress), value: \.progress) {
                Text(TorrentCoreDisplayFormatter.percent($0.progress))
                    .monospacedDigit()
            }
            .width(min: 50, ideal: 95)
            .defaultVisibility(.visible)
            .customizationID("progress")

            TableColumn(sortHeaderTitle(.downloaded), value: \.downloaded) {
                Text(TorrentCoreDisplayFormatter.bytes($0.downloaded))
                    .monospacedDigit()
            }
            .width(min: 65, ideal: 120)
            .defaultVisibility(.visible)
            .customizationID("downloaded")

            TableColumn(sortHeaderTitle(.total), value: \.total) {
                Text(TorrentCoreDisplayFormatter.bytes($0.summary.latestTotalBytes))
                    .monospacedDigit()
            }
            .width(min: 65, ideal: 120)
            .defaultVisibility(.visible)
            .customizationID("total")

            TableColumn(sortHeaderTitle(.callback), value: \.callback, comparator: .localizedStandard) {
                Text(TorrentCoreDisplayFormatter.splitIdentifier($0.callback))
            }
            .width(min: 75, ideal: 190)
            .defaultVisibility(.visible)
            .customizationID("callback")
            }

            Group {
                TableColumn(
                    sortHeaderTitle(.removed),
                    value: \TorrentCoreMacHistoryTableItem.removed
                ) {
                    Text(TorrentCoreDisplayFormatter.timestamp($0.summary.removedAt))
                        .monospacedDigit()
                }
                .width(min: 85, ideal: 165)
                .defaultVisibility(.visible)
                .customizationID("removed")

                TableColumn(
                    sortHeaderTitle(.removalReason),
                    value: \TorrentCoreMacHistoryTableItem.removalReason,
                    comparator: .localizedStandard
                ) {
                    Text($0.removalReason)
                        .lineLimit(2)
                }
                .width(min: 70, ideal: 280)
                .defaultVisibility(.visible)
                .customizationID("removalReason")

                TableColumn(sortHeaderTitle(.callbackFinalResult), value: \.callbackFinalResult, comparator: .localizedStandard) {
                    Text(TorrentCoreDisplayFormatter.operatorValue($0.summary.completionCallbackFinalResult))
                }
                .width(min: 135, ideal: 200)
                .defaultVisibility(.hidden)
                .customizationID("callbackFinalResult")

                TableColumn(sortHeaderTitle(.dataDeleted), value: \.dataDeleted) {
                    Text(yesNo($0.summary.dataDeleted))
                }
                .width(min: 90, ideal: 110)
                .defaultVisibility(.hidden)
                .customizationID("dataDeleted")

                historyDateColumn(
                    .downloadCompleted,
                    sortValue: \.downloadCompleted,
                    displayValue: \.summary.downloadCompletedAt
                )

                TableColumn(sortHeaderTitle(.downloadRootPath), value: \.downloadRootPath, comparator: .localizedStandard) {
                    Text(TorrentCoreDisplayFormatter.operatorValue($0.summary.downloadRootPath))
                        .lineLimit(2)
                }
                .width(min: 180, ideal: 320)
                .defaultVisibility(.hidden)
                .customizationID("downloadRootPath")

                historyDateColumn(
                    .downloadStarted,
                    sortValue: \.downloadStarted,
                    displayValue: \.summary.downloadStartedAt
                )

                TableColumn(sortHeaderTitle(.infoHash), value: \.infoHash, comparator: .localizedStandard) {
                    Text(TorrentCoreDisplayFormatter.operatorValue($0.summary.infoHash))
                }
                .width(min: 220, ideal: 300)
                .defaultVisibility(.hidden)
                .customizationID("infoHash")
            }

            Group {
                historyDateColumn(
                    .lastActivity,
                    sortValue: \.lastActivity,
                    displayValue: \.summary.lastActivityAt
                )

                TableColumn(sortHeaderTitle(.connectedPeers), value: \.connectedPeers) {
                    Text($0.connectedPeers.formatted()).monospacedDigit()
                }
                .width(min: 100, ideal: 120)
                .defaultVisibility(.hidden)
                .customizationID("connectedPeers")

                TableColumn(sortHeaderTitle(.downloadRate), value: \.downloadRate) {
                    Text(TorrentCoreDisplayFormatter.rate($0.downloadRate)).monospacedDigit()
                }
                .width(min: 100, ideal: 125)
                .defaultVisibility(.hidden)
                .customizationID("downloadRate")

                TableColumn(sortHeaderTitle(.errorMessage), value: \.errorMessage, comparator: .localizedStandard) {
                    Text(TorrentCoreDisplayFormatter.operatorValue($0.summary.latestErrorMessage))
                        .lineLimit(2)
                }
                .width(min: 180, ideal: 300)
                .defaultVisibility(.hidden)
                .customizationID("errorMessage")

                TableColumn(sortHeaderTitle(.trackerCount), value: \.trackerCount) {
                    Text($0.trackerCount.formatted()).monospacedDigit()
                }
                .width(min: 85, ideal: 105)
                .defaultVisibility(.hidden)
                .customizationID("trackerCount")

                TableColumn(sortHeaderTitle(.uploadRate), value: \.uploadRate) {
                    Text(TorrentCoreDisplayFormatter.rate($0.uploadRate)).monospacedDigit()
                }
                .width(min: 90, ideal: 115)
                .defaultVisibility(.hidden)
                .customizationID("uploadRate")

                TableColumn(sortHeaderTitle(.uploaded), value: \.uploaded) {
                    Text(TorrentCoreDisplayFormatter.bytes($0.uploaded)).monospacedDigit()
                }
                .width(min: 85, ideal: 115)
                .defaultVisibility(.hidden)
                .customizationID("uploaded")

                TableColumn(sortHeaderTitle(.waitReason), value: \.waitReason, comparator: .localizedStandard) {
                    Text(TorrentCoreDisplayFormatter.operatorValue($0.summary.latestWaitReason))
                }
                .width(min: 130, ideal: 200)
                .defaultVisibility(.hidden)
                .customizationID("waitReason")

                historyDateColumn(
                    .metadataResolved,
                    sortValue: \.metadataResolved,
                    displayValue: \.summary.metadataResolvedAt
                )
            }

            Group {
                TableColumn(
                    sortHeaderTitle(.removalKind),
                    value: \TorrentCoreMacHistoryTableItem.removalKind,
                    comparator: .localizedStandard
                ) {
                    Text(TorrentCoreDisplayFormatter.operatorValue($0.summary.removalKind?.rawValue))
                }
                .width(min: 130, ideal: 220)
                .defaultVisibility(.hidden)
                .customizationID("removalKind")

                TableColumn(sortHeaderTitle(.removedByCleanup), value: \.removedByCleanup) {
                    Text(yesNo($0.summary.removedByCleanupPolicy))
                }
                .width(min: 125, ideal: 150)
                .defaultVisibility(.hidden)
                .customizationID("removedByCleanup")

                historyDateColumn(
                    .seedingStarted,
                    sortValue: \.seedingStarted,
                    displayValue: \.summary.seedingStartedAt
                )

                TableColumn(sortHeaderTitle(.submitted), value: \.submitted) {
                    Text(TorrentCoreDisplayFormatter.timestamp($0.summary.submittedAt))
                        .monospacedDigit()
                }
                .width(min: 135, ideal: 165)
                .defaultVisibility(.hidden)
                .customizationID("submitted")

                TableColumn(sortHeaderTitle(.torrentID), value: \.torrentID, comparator: .localizedStandard) {
                    Text(TorrentCoreDisplayFormatter.operatorValue($0.summary.torrentID?.uuidString))
                }
                .width(min: 220, ideal: 285)
                .defaultVisibility(.hidden)
                .customizationID("torrentID")
            }
        }
        .accessibilityIdentifier("history.table")
    }

    @TableColumnBuilder<
        TorrentCoreMacHistoryTableItem,
        KeyPathComparator<TorrentCoreMacHistoryTableItem>
    >
    private func historyDateColumn(
        _ column: TorrentCoreMacHistoryColumn,
        sortValue: KeyPath<TorrentCoreMacHistoryTableItem, Date>,
        displayValue: KeyPath<TorrentCoreMacHistoryTableItem, Date?>
    ) -> some TableColumnContent<
        TorrentCoreMacHistoryTableItem,
        KeyPathComparator<TorrentCoreMacHistoryTableItem>
    > {
        let title = TorrentCoreMacHistorySortField.field(for: sortValue)
            .map(sortHeaderTitle) ?? column.title
        TableColumn(title, value: sortValue) {
            Text(TorrentCoreDisplayFormatter.timestamp($0[keyPath: displayValue]))
                .monospacedDigit()
        }
        .width(min: 135, ideal: 165)
        .defaultVisibility(.hidden)
        .customizationID(column.id)
    }

    private var paginationBar: some View {
        TorrentCoreMacPaginationBar(
            resultCount: sortedItems.count,
            pageIndex: $pageIndex,
            pageSize: pageSizeBinding,
            accessibilityPrefix: "history"
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
        .accessibilityIdentifier("history.sort")
    }

    private var columnsMenu: some View {
        Menu("Columns", systemImage: "rectangle.3.group") {
            ForEach(TorrentCoreMacHistoryColumn.allCases) { column in
                if column.canHide {
                    Toggle(column.title, isOn: columnVisibility(column))
                }
            }
            Divider()
            Button("Show All Columns") {
                for column in TorrentCoreMacHistoryColumn.allCases {
                    columnCustomization[visibility: column.id] = .visible
                }
            }
            Button("Restore Default Columns") {
                for column in TorrentCoreMacHistoryColumn.allCases {
                    columnCustomization[visibility: column.id] = .automatic
                }
            }
            Divider()
            Button("Reset Table Layout") {
                columnCustomization = .init()
            }
        }
        .accessibilityIdentifier("history.columns")
    }

    private var exportMenu: some View {
        Menu("Export", systemImage: "square.and.arrow.up") {
            Button(selectedHistoryItem == nil ? "Export Selected Row" : "Export Selected Row (1)") {
                export(.selected)
            }
            .disabled(selectedHistoryItem == nil)
            Button("Export All Results (\(sortedItems.count.formatted()))") {
                export(.all)
            }
            .disabled(sortedItems.isEmpty)
        }
        .accessibilityIdentifier("history.export")
    }

    private func sortHeaderTitle(_ field: TorrentCoreMacHistorySortField) -> String {
        guard let index = sortDescriptors.firstIndex(where: { $0.field == field }) else {
            return field.title
        }
        return "\(field.title) \(sortDescriptors[index].descending ? "↓" : "↑")\(index + 1)"
    }

    private func columnVisibility(_ column: TorrentCoreMacHistoryColumn) -> Binding<Bool> {
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

    private func export(_ scope: TorrentCoreMacExportScope) {
        let summaries = scope.rows(
            selected: selectedHistoryItem?.summary,
            all: sortedItems.map(\.summary)
        )
        guard !summaries.isEmpty else { return }
        do {
            let fileURL = try TorrentCoreMacTableExport.write(
                headers: Self.exportHeaders,
                rows: summaries.map(Self.exportRow),
                fileName: "history-\(scope.rawValue)-\(TorrentCoreMacTableExport.timestamp()).csv"
            )
            notice = .init(
                kind: .success,
                message: "Exported \(summaries.count.formatted()) row\(summaries.count == 1 ? "" : "s") to Downloads/\(fileURL.lastPathComponent)."
            )
        } catch {
            notice = .init(
                kind: .error,
                message: "Export failed: \(TorrentCoreMacErrorPresenter.message(error))"
            )
        }
    }

    private var historyInspector: some View {
        ScrollView {
            if let detail = session.historyDetail.value {
                VStack(alignment: .leading, spacing: 12) {
                    HStack {
                        Text(detail.name ?? "History Detail")
                            .font(.title2.weight(.semibold))
                        TorrentCoreMacHelpButton(
                            content: TorrentCoreHelpCatalog.History.selectedEntry
                        )
                    }
                    .padding(.trailing, 36)
                    .textSelection(.enabled)
                    TorrentCoreMacPhaseBanner(
                        phase: session.historyDetail.phase,
                        lastSuccessfulAt: session.historyDetail.lastSuccessfulAt
                    )
                    TorrentCoreMacDetailRow(label: "Outcome", value: detail.outcome.rawValue)
                    TorrentCoreMacDetailRow(
                        label: "State",
                        value: TorrentCoreDisplayFormatter.operatorValue(
                            detail.latestTorrentState
                        )
                    )
                    TorrentCoreMacDetailRow(
                        label: "Progress",
                        value: TorrentCoreDisplayFormatter.percent(
                            detail.latestProgressPercent
                        )
                    )
                    TorrentCoreMacDetailRow(
                        label: "Category",
                        value: TorrentCoreDisplayFormatter.operatorValue(detail.categoryKey)
                    )
                    TorrentCoreMacDetailRow(
                        label: "Submitted",
                        value: TorrentCoreDisplayFormatter.timestamp(detail.submittedAt)
                    )
                    TorrentCoreMacDetailRow(
                        label: "Last Updated",
                        value: TorrentCoreDisplayFormatter.timestamp(detail.lastUpdatedAt)
                    )
                    TorrentCoreMacDetailRow(
                        label: "Removed",
                        value: TorrentCoreDisplayFormatter.timestamp(detail.removedAt)
                    )
                    TorrentCoreMacDetailRow(
                        label: "Removal Kind",
                        value: TorrentCoreDisplayFormatter.operatorValue(
                            detail.removalKind?.rawValue
                        )
                    )
                    TorrentCoreMacDetailRow(
                        label: "Removal Reason",
                        value: TorrentCoreDisplayFormatter.operatorValue(detail.removalReason)
                    )
                    Divider()
                    Text("Callback Feedback")
                        .font(.headline)
                    TorrentCoreMacDetailRow(
                        label: "Summary",
                        value: TorrentCoreCompletionCallbackPresentation.feedbackSummary(
                            detail.completionCallbackFeedback
                        ) ?? "--"
                    )
                    .accessibilityIdentifier("history.feedback.summary")
                    TorrentCoreMacDetailRow(
                        label: "Received",
                        value: TorrentCoreDisplayFormatter.timestamp(
                            detail.completionCallbackFeedback?.receivedAt
                        )
                    )
                    .accessibilityIdentifier("history.feedback.received")
                    TorrentCoreMacDetailRow(
                        label: "Final Result",
                        value: TorrentCoreDisplayFormatter.operatorValue(
                            detail.completionCallbackFeedback?.finalState
                        )
                    )
                    .accessibilityIdentifier("history.feedback.finalResult")
                    TorrentCoreMacDetailRow(
                        label: "Reason",
                        value: TorrentCoreDisplayFormatter.operatorValue(
                            detail.completionCallbackFeedback?.reasonCode
                        )
                    )
                    .accessibilityIdentifier("history.feedback.reason")
                    historyMagnetRow(detail.magnetURI)
                    TorrentCoreMacDetailRow(
                        label: "Info Hash",
                        value: TorrentCoreDisplayFormatter.operatorValue(detail.infoHash)
                    )
                    TorrentCoreMacCopyableDetailRow(
                        label: "Torrent ID",
                        value: detail.torrentID?.uuidString,
                        accessibilityIdentifier: "history.copyTorrentID"
                    )
                    TorrentCoreMacCopyableDetailRow(
                        label: "Service Instance ID",
                        value: detail.serviceInstanceIDLastSeen?.uuidString,
                        accessibilityIdentifier: "history.copyServiceInstanceID"
                    )
                    if detail.outcome == .active, let torrentID = detail.torrentID {
                        Divider()
                        Button {
                            showTorrent(torrentID)
                        } label: {
                            Label("Show in Torrents", systemImage: "arrow.down.circle")
                        }
                        .disabled(!session.connectionState.isConnected)
                    }
                }
                .padding(16)
            } else {
                ContentUnavailableView(
                    "Select a History Record",
                    systemImage: "clock",
                    description: Text("Select a record with a torrent identifier to inspect it.")
                )
            }
        }
        .accessibilityIdentifier("history.inspector.content")
        .overlay(alignment: .topTrailing) {
            Button("Close", systemImage: "xmark") {
                isInspectorPresented = false
            }
            .labelStyle(.iconOnly)
            .padding(12)
            .accessibilityIdentifier("history.inspector.close")
        }
    }

    private func historyMagnetRow(_ magnetURI: String?) -> some View {
        LabeledContent("Magnet") {
            VStack(alignment: .trailing, spacing: 6) {
                Text(copyableMagnetURI(magnetURI) ?? "--")
                    .multilineTextAlignment(.trailing)
                    .textSelection(.enabled)

                Button {
                    copyMagnet(magnetURI)
                } label: {
                    Label(magnetCopyStatus.label, systemImage: magnetCopyStatus.systemImage)
                }
                .disabled(copyableMagnetURI(magnetURI) == nil)
                .accessibilityIdentifier("history.copyMagnet")
            }
        }
    }

    private func copyMagnet(_ magnetURI: String?) {
        guard let magnetURI = copyableMagnetURI(magnetURI) else {
            return
        }

        magnetCopyResetTask?.cancel()
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        magnetCopyStatus = pasteboard.setString(magnetURI, forType: .string) ? .copied : .failed
        magnetCopyResetTask = Task {
            try? await Task.sleep(for: .seconds(2))
            guard !Task.isCancelled else {
                return
            }
            magnetCopyStatus = .idle
        }
    }

    private func copyableMagnetURI(_ value: String?) -> String? {
        let magnetURI = (value ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        return magnetURI.isEmpty ? nil : magnetURI
    }

    private var sortedItems: [TorrentCoreMacHistoryTableItem] {
        (session.history.value ?? [])
            .map { TorrentCoreMacHistoryTableItem(summary: $0) }
            .sorted(using: comparatorOrder)
    }

    private var currentPage: [TorrentCoreMacHistoryTableItem] {
        TorrentCoreMacTableSupport.page(
            sortedItems,
            index: pageIndex,
            size: pageSize
        )
    }

    private var comparatorOrder: [KeyPathComparator<TorrentCoreMacHistoryTableItem>] {
        sortDescriptors.map { $0.field.comparator(descending: $0.descending) }
    }

    private var tableSortOrder: Binding<[KeyPathComparator<TorrentCoreMacHistoryTableItem>]> {
        Binding(
            get: { comparatorOrder },
            set: { proposed in
                guard let comparator = proposed.first,
                      let field = TorrentCoreMacHistorySortField.field(for: comparator.keyPath)
                else { return }
                sortDescriptors = [
                    .init(field: field, descending: comparator.order == .reverse),
                ]
            }
        )
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
                reconcileSelectionWithCurrentPage()
            }
        )
    }

    private var selectedHistoryItem: TorrentCoreMacHistoryTableItem? {
        guard let selectedHistoryKey else { return nil }
        return sortedItems.first(where: { $0.id == selectedHistoryKey })
    }

    private func clampPageAndReconcileSelection() {
        pageIndex = TorrentCoreMacTableSupport.clampedPageIndex(
            pageIndex,
            count: sortedItems.count,
            size: pageSize
        )
        reconcileSelectionWithCurrentPage()
    }

    private func reconcileSelectionWithCurrentPage() {
        guard let selectedHistoryKey else { return }
        guard currentPage.contains(where: { $0.id == selectedHistoryKey }) else {
            self.selectedHistoryKey = nil
            selectedTorrentID = nil
            isInspectorPresented = false
            return
        }
    }

    private var historyCategoryOptions: [String] {
        uniqueOptions(
            (session.historyFilterOptions.value?.categoryKeys ?? [])
                + (session.history.value ?? []).compactMap(\.categoryKey),
            preserving: categoryFilter
        )
    }

    private var historyStateOptions: [String] {
        uniqueOptions(
            (session.historyFilterOptions.value?.states ?? [])
                + (session.history.value ?? []).compactMap(\.latestTorrentState),
            preserving: stateFilter
        )
    }

    private func uniqueOptions(
        _ values: [String],
        preserving selectedValue: String
    ) -> [String] {
        (values + [selectedValue])
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            .filter { !$0.isEmpty }
            .reduce(into: [String]()) { result, value in
                if !result.contains(where: {
                    $0.localizedCaseInsensitiveCompare(value) == .orderedSame
                }) {
                    result.append(value)
                }
            }
            .sorted { $0.localizedStandardCompare($1) == .orderedAscending }
    }

    private var unavailableMessage: String {
        if case .loading = session.history.phase {
            return "Requesting torrent history from TorrentCore."
        }
        return switch session.connectionState {
        case .noProfile:
            "Create or select a connection before loading history."
        case let .offline(_, _, message):
            message
        case .connecting:
            "Checking TorrentCore.Service…"
        case .notConnected:
            "Refresh to connect to the selected TorrentCore installation."
        case .connected:
            "TorrentCore did not return history."
        }
    }

    private var unavailableTitle: String {
        if case .loading = session.history.phase {
            return "Loading History"
        }
        return "History Unavailable"
    }

    private var unavailableSystemImage: String {
        if case .loading = session.history.phase {
            return "arrow.trianglehead.2.clockwise"
        }
        return "clock.arrow.trianglehead.counterclockwise.rotate.90"
    }

    private func applyFilters() {
        guard isTorrentIDFilterValid else { return }
        let outcome = outcomeFilter.isEmpty
            ? nil
            : TorrentCoreHistoryOutcome(rawValue: outcomeFilter)
        query = TorrentCoreHistoryQuery(
            torrentID: UUID(uuidString: normalizedTorrentIDFilter),
            torrentName: nameFilter.nilIfBlank,
            categoryKey: categoryFilter.nilIfBlank,
            state: stateFilter.nilIfBlank,
            outcome: outcome,
            fromDate: includesFromDate ? Self.dateOnlyFormatter.string(from: fromDate) : nil,
            toDate: includesToDate ? Self.dateOnlyFormatter.string(from: toDate) : nil,
            take: 500
        )
        pageIndex = 0
        selectedHistoryKey = nil
        selectedTorrentID = nil
        isInspectorPresented = false
        contextChanged()
    }

    private var normalizedTorrentIDFilter: String {
        torrentIDFilterInput.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private var isTorrentIDFilterValid: Bool {
        normalizedTorrentIDFilter.isEmpty || UUID(uuidString: normalizedTorrentIDFilter) != nil
    }

    private func clearFilters() {
        nameFilter = ""
        categoryFilter = ""
        stateFilter = ""
        outcomeFilter = ""
        torrentIDFilterInput = ""
        includesFromDate = false
        includesToDate = false
        applyFilters()
    }

    private func resetFilters() {
        let defaults = Self.defaultQuery
        nameFilter = defaults.torrentName ?? ""
        categoryFilter = defaults.categoryKey ?? ""
        stateFilter = defaults.state ?? ""
        outcomeFilter = defaults.outcome?.rawValue ?? ""
        torrentIDFilterInput = ""
        includesFromDate = defaults.fromDate != nil
        includesToDate = defaults.toDate != nil
        fromDate = Self.dateOnlyFormatter.date(from: defaults.fromDate ?? "") ?? Date()
        toDate = Self.dateOnlyFormatter.date(from: defaults.toDate ?? "") ?? Date()
        applyFilters()
    }

    static let defaultSortDescriptors = [
        TorrentCoreMacSortDescriptor(
            field: TorrentCoreMacHistorySortField.lastUpdated,
            descending: true
        ),
    ]

    static let exportHeaders = [
        "Category Key",
        "Completion Callback Final Result",
        "Data Deleted",
        "Download Completed At",
        "Download Root Path",
        "Download Started At",
        "Info Hash",
        "Last Activity At",
        "Last Updated At",
        "Latest Callback Status",
        "Latest Connected Peer Count",
        "Latest Download Rate Bytes Per Second",
        "Latest Downloaded Bytes",
        "Latest Error Message",
        "Latest Progress Percent",
        "Latest Torrent State",
        "Latest Total Bytes",
        "Latest Tracker Count",
        "Latest Upload Rate Bytes Per Second",
        "Latest Uploaded Bytes",
        "Latest Wait Reason",
        "Metadata Resolved At",
        "Name",
        "Outcome",
        "Removal Kind",
        "Removal Reason",
        "Removed At",
        "Removed by Cleanup Policy",
        "Seeding Started At",
        "Submitted At",
        "Torrent ID",
    ]

    static func exportRow(_ value: TorrentCoreHistorySummary) -> [String] {
        func yesNo(_ value: Bool) -> String { value ? "Yes" : "No" }
        return [
            value.categoryKey ?? "",
            value.completionCallbackFinalResult ?? "",
            yesNo(value.dataDeleted),
            TorrentCoreMacTableExport.isoTimestamp(value.downloadCompletedAt),
            value.downloadRootPath ?? "",
            TorrentCoreMacTableExport.isoTimestamp(value.downloadStartedAt),
            value.infoHash ?? "",
            TorrentCoreMacTableExport.isoTimestamp(value.lastActivityAt),
            TorrentCoreMacTableExport.isoTimestamp(value.lastUpdatedAt),
            value.latestCallbackStatus ?? "",
            String(value.latestConnectedPeerCount),
            String(value.latestDownloadRateBytesPerSecond),
            String(value.latestDownloadedBytes),
            value.latestErrorMessage ?? "",
            String(
                format: "%.1f",
                locale: Locale(identifier: "en_US_POSIX"),
                value.latestProgressPercent
            ),
            value.latestTorrentState ?? "",
            value.latestTotalBytes.map(String.init) ?? "",
            String(value.latestTrackerCount),
            String(value.latestUploadRateBytesPerSecond),
            String(value.latestUploadedBytes),
            value.latestWaitReason ?? "",
            TorrentCoreMacTableExport.isoTimestamp(value.metadataResolvedAt),
            value.name ?? "",
            value.outcome.rawValue,
            value.removalKind?.rawValue ?? "",
            value.removalReason ?? "",
            TorrentCoreMacTableExport.isoTimestamp(value.removedAt),
            yesNo(value.removedByCleanupPolicy),
            TorrentCoreMacTableExport.isoTimestamp(value.seedingStartedAt),
            TorrentCoreMacTableExport.isoTimestamp(value.submittedAt),
            value.torrentID?.uuidString ?? "",
        ]
    }

    private func yesNo(_ value: Bool) -> String {
        value ? "Yes" : "No"
    }

    private static let dateOnlyFormatter: DateFormatter = {
        let formatter = DateFormatter()
        formatter.calendar = Calendar(identifier: .gregorian)
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.dateFormat = "yyyy-MM-dd"
        return formatter
    }()
}

private extension String {
    var nilIfBlank: String? {
        let value = trimmingCharacters(in: .whitespacesAndNewlines)
        return value.isEmpty ? nil : value
    }
}
