import Foundation
import SwiftUI
import TorrentCoreAPI
import TorrentCoreFeatures

enum TorrentCoreMacTorrentSortField: String, CaseIterable, Codable, Identifiable {
    case name
    case category
    case state
    case progress
    case download
    case upload
    case peers
    case trackers
    case downloaded
    case total
    case reason
    case queue
    case priority
    case held
    case torrentID
    case added
    case completed
    case lastActivity
    case noProgressSince
    case lastYielded
    case error
    case callback
    case callbackPendingSince
    case callbackInvoked
    case callbackError
    case queueHeld
    case downloadYielded

    var id: Self { self }

    var title: String {
        switch self {
        case .name: "Name"
        case .category: "Category"
        case .state: "State"
        case .progress: "Progress"
        case .download: "Download"
        case .upload: "Upload"
        case .peers: "Peers"
        case .trackers: "Trackers"
        case .downloaded: "Downloaded"
        case .total: "Total"
        case .reason: "Reason"
        case .queue: "Queue #"
        case .priority: "Priority #"
        case .held: "Held #"
        case .torrentID: "Torrent ID"
        case .added: "Added"
        case .completed: "Completed"
        case .lastActivity: "Last Activity"
        case .noProgressSince: "No Progress Since"
        case .lastYielded: "Last Yielded"
        case .error: "Error"
        case .callback: "Callback"
        case .callbackPendingSince: "Callback Pending Since"
        case .callbackInvoked: "Callback Invoked"
        case .callbackError: "Callback Error"
        case .queueHeld: "Queue Held"
        case .downloadYielded: "Download Yielded"
        }
    }

    func comparator(descending: Bool) -> KeyPathComparator<TorrentCoreTorrentListItem> {
        let order: SortOrder = descending ? .reverse : .forward
        return switch self {
        case .name:
            KeyPathComparator(
                \TorrentCoreTorrentListItem.name,
                comparator: .localizedStandard,
                order: order
            )
        case .category:
            KeyPathComparator(
                \TorrentCoreTorrentListItem.category,
                comparator: .localizedStandard,
                order: order
            )
        case .state:
            KeyPathComparator(
                \TorrentCoreTorrentListItem.state,
                comparator: .localizedStandard,
                order: order
            )
        case .progress:
            KeyPathComparator(\TorrentCoreTorrentListItem.progress, order: order)
        case .download:
            KeyPathComparator(\TorrentCoreTorrentListItem.downloadRate, order: order)
        case .upload:
            KeyPathComparator(\TorrentCoreTorrentListItem.uploadRate, order: order)
        case .peers:
            KeyPathComparator(\TorrentCoreTorrentListItem.peers, order: order)
        case .trackers:
            KeyPathComparator(\TorrentCoreTorrentListItem.trackerCount, order: order)
        case .downloaded:
            KeyPathComparator(\TorrentCoreTorrentListItem.downloadedBytes, order: order)
        case .total:
            KeyPathComparator(\TorrentCoreTorrentListItem.totalSortValue, order: order)
        case .reason:
            KeyPathComparator(
                \TorrentCoreTorrentListItem.reason,
                comparator: .localizedStandard,
                order: order
            )
        case .queue:
            KeyPathComparator(\TorrentCoreTorrentListItem.queueSortValue, order: order)
        case .priority:
            KeyPathComparator(\TorrentCoreTorrentListItem.priorityQueueSortValue, order: order)
        case .held:
            KeyPathComparator(\TorrentCoreTorrentListItem.heldQueueSortValue, order: order)
        case .torrentID:
            KeyPathComparator(
                \TorrentCoreTorrentListItem.torrentIDText,
                comparator: .localizedStandard,
                order: order
            )
        case .added:
            KeyPathComparator(\TorrentCoreTorrentListItem.addedAt, order: order)
        case .completed:
            KeyPathComparator(\TorrentCoreTorrentListItem.completedSortValue, order: order)
        case .lastActivity:
            KeyPathComparator(\TorrentCoreTorrentListItem.lastActivitySortValue, order: order)
        case .noProgressSince:
            KeyPathComparator(\TorrentCoreTorrentListItem.noProgressSortValue, order: order)
        case .lastYielded:
            KeyPathComparator(\TorrentCoreTorrentListItem.lastYieldedSortValue, order: order)
        case .error:
            KeyPathComparator(
                \TorrentCoreTorrentListItem.errorText,
                comparator: .localizedStandard,
                order: order
            )
        case .callback:
            KeyPathComparator(
                \TorrentCoreTorrentListItem.callbackText,
                comparator: .localizedStandard,
                order: order
            )
        case .callbackPendingSince:
            KeyPathComparator(\TorrentCoreTorrentListItem.callbackPendingSortValue, order: order)
        case .callbackInvoked:
            KeyPathComparator(\TorrentCoreTorrentListItem.callbackInvokedSortValue, order: order)
        case .callbackError:
            KeyPathComparator(
                \TorrentCoreTorrentListItem.callbackErrorText,
                comparator: .localizedStandard,
                order: order
            )
        case .queueHeld:
            KeyPathComparator(\TorrentCoreTorrentListItem.queueHeldSortValue, order: order)
        case .downloadYielded:
            KeyPathComparator(\TorrentCoreTorrentListItem.downloadYieldedSortValue, order: order)
        }
    }

    static func field(
        for keyPath: PartialKeyPath<TorrentCoreTorrentListItem>
    ) -> Self? {
        switch keyPath {
        case \TorrentCoreTorrentListItem.name:
            .name
        case \TorrentCoreTorrentListItem.category:
            .category
        case \TorrentCoreTorrentListItem.state:
            .state
        case \TorrentCoreTorrentListItem.progress:
            .progress
        case \TorrentCoreTorrentListItem.downloadRate:
            .download
        case \TorrentCoreTorrentListItem.uploadRate:
            .upload
        case \TorrentCoreTorrentListItem.peers:
            .peers
        case \TorrentCoreTorrentListItem.trackerCount:
            .trackers
        case \TorrentCoreTorrentListItem.downloadedBytes:
            .downloaded
        case \TorrentCoreTorrentListItem.totalSortValue:
            .total
        case \TorrentCoreTorrentListItem.reason:
            .reason
        case \TorrentCoreTorrentListItem.queueSortValue:
            .queue
        case \TorrentCoreTorrentListItem.priorityQueueSortValue:
            .priority
        case \TorrentCoreTorrentListItem.heldQueueSortValue:
            .held
        case \TorrentCoreTorrentListItem.torrentIDText:
            .torrentID
        case \TorrentCoreTorrentListItem.addedAt:
            .added
        case \TorrentCoreTorrentListItem.completedSortValue:
            .completed
        case \TorrentCoreTorrentListItem.lastActivitySortValue:
            .lastActivity
        case \TorrentCoreTorrentListItem.noProgressSortValue:
            .noProgressSince
        case \TorrentCoreTorrentListItem.lastYieldedSortValue:
            .lastYielded
        case \TorrentCoreTorrentListItem.errorText:
            .error
        case \TorrentCoreTorrentListItem.callbackText:
            .callback
        case \TorrentCoreTorrentListItem.callbackPendingSortValue:
            .callbackPendingSince
        case \TorrentCoreTorrentListItem.callbackInvokedSortValue:
            .callbackInvoked
        case \TorrentCoreTorrentListItem.callbackErrorText:
            .callbackError
        case \TorrentCoreTorrentListItem.queueHeldSortValue:
            .queueHeld
        case \TorrentCoreTorrentListItem.downloadYieldedSortValue:
            .downloadYielded
        default:
            nil
        }
    }
}

private enum TorrentCoreMacTorrentColumn: String, CaseIterable, Identifiable {
    case name
    case state
    case progress
    case download
    case peers
    case category
    case reason
    case queue = "queuePosition"
    case priority = "priorityQueuePosition"
    case held = "heldQueuePosition"
    case torrentID
    case upload
    case trackers
    case downloaded
    case total
    case added
    case completed
    case lastActivity
    case noProgressSince
    case lastYielded
    case callback
    case callbackPendingSince
    case callbackInvoked
    case callbackError
    case error
    case queueHeld
    case downloadYielded

    var id: String { rawValue }

    var title: String {
        switch self {
        case .name: "Name"
        case .state: "State"
        case .progress: "Progress"
        case .download: "Download"
        case .peers: "Peers"
        case .category: "Category"
        case .reason: "Reason"
        case .queue: "Queue #"
        case .priority: "Priority #"
        case .held: "Held #"
        case .torrentID: "Torrent ID"
        case .upload: "Upload"
        case .trackers: "Trackers"
        case .downloaded: "Downloaded"
        case .total: "Total"
        case .added: "Added"
        case .completed: "Completed"
        case .lastActivity: "Last Activity"
        case .noProgressSince: "No Progress Since"
        case .lastYielded: "Last Yielded"
        case .callback: "Callback"
        case .callbackPendingSince: "Callback Pending Since"
        case .callbackInvoked: "Callback Invoked"
        case .callbackError: "Callback Error"
        case .error: "Error"
        case .queueHeld: "Queue Held"
        case .downloadYielded: "Download Yielded"
        }
    }

    var canHide: Bool { self != .name }
    var isDefaultVisible: Bool {
        switch self {
        case .name, .state, .progress, .download, .peers, .category,
             .queue, .priority, .held:
            true
        default:
            false
        }
    }
}

private extension TorrentCoreTorrentListItem {
    var torrentIDText: String { summary.torrentID?.uuidString ?? "" }
    var trackerCount: Int { summary.trackerCount }
    var downloadedBytes: Int64 { summary.downloadedBytes }
    var totalSortValue: Int64 { summary.totalBytes ?? .min }
    var completedSortValue: Date { summary.completedAt ?? .distantPast }
    var lastActivitySortValue: Date { summary.lastActivityAt ?? .distantPast }
    var noProgressSortValue: Date { summary.downloadNoProgressStartedAt ?? .distantPast }
    var lastYieldedSortValue: Date { summary.downloadLastYieldedAt ?? .distantPast }
    var errorText: String { summary.errorMessage ?? "" }
    var callbackText: String { summary.completionCallbackState ?? "" }
    var callbackPendingSortValue: Date { summary.completionCallbackPendingSince ?? .distantPast }
    var callbackInvokedSortValue: Date { summary.completionCallbackInvokedAt ?? .distantPast }
    var callbackErrorText: String { summary.completionCallbackLastError ?? "" }
    var queueHeldSortValue: Int { summary.isQueueHeld ? 1 : 0 }
    var downloadYieldedSortValue: Int { summary.isDownloadYielded ? 1 : 0 }
}

struct TorrentCoreMacTorrentsView: View {
    @AppStorage("TorrentCore.Mac.Torrents.StateFilter.v1")
    private var storedStateFilter = ""
    @AppStorage("TorrentCore.Mac.Torrents.CategoryFilter.v1")
    private var storedCategoryFilter = ""
    @AppStorage("TorrentCore.Mac.Torrents.ReasonFilter.v1")
    private var storedReasonFilter = ""
    @AppStorage("TorrentCore.Mac.Torrents.PageSize.v1")
    private var storedPageSize = TorrentCoreTorrentPageSize.defaultValue.rawValue
    @AppStorage("TorrentCore.Mac.Torrents.Sort.v2")
    private var storedSort = ""
    @AppStorage("TorrentCore.Mac.Torrents.Columns.v1")
    private var columnCustomization =
        TableColumnCustomization<TorrentCoreTorrentListItem>()
    @AppStorage("TorrentCore.Mac.Torrents.OverlayWidth.v1")
    private var overlayWidth = 390.0

    let session: TorrentCoreFeatureSession
    @Binding var selectedTorrentID: UUID?
    @Binding var isInspectorPresented: Bool
    let contextChanged: () -> Void
    let addMagnet: () -> Void
    let showHistory: (UUID) -> Void
    let showLogs: (UUID) -> Void

    @State private var searchText = ""
    @State private var pageIndex = 0
    @State private var sortDescriptors: [
        TorrentCoreMacSortDescriptor<TorrentCoreMacTorrentSortField>
    ]
    @State private var isActing = false
    @State private var actionError: String?
    @State private var actionMessage: String?
    @State private var notice: TorrentCoreMacNotice?
    @State private var pendingRemoval: PendingRemoval?
    @State private var isPeersPresented = false
    @State private var isTrackersPresented = false
    @State private var isSortEditorPresented = false
    @State private var isMetadataResetConfirmationPresented = false
    @State private var isCallbackRetryConfirmationPresented = false

    init(
        session: TorrentCoreFeatureSession,
        selectedTorrentID: Binding<UUID?>,
        isInspectorPresented: Binding<Bool>,
        contextChanged: @escaping () -> Void,
        addMagnet: @escaping () -> Void,
        showHistory: @escaping (UUID) -> Void,
        showLogs: @escaping (UUID) -> Void
    ) {
        self.session = session
        _selectedTorrentID = selectedTorrentID
        _isInspectorPresented = isInspectorPresented
        self.contextChanged = contextChanged
        self.addMagnet = addMagnet
        self.showHistory = showHistory
        self.showLogs = showLogs

        let defaults = UserDefaults.standard
        let stored = defaults.string(
            forKey: "TorrentCore.Mac.Torrents.Sort.v2"
        ) ?? ""
        let priorStored = defaults.string(
            forKey: "TorrentCore.Mac.Torrents.Sort.v1"
        ) ?? ""
        let storedField = defaults.string(
            forKey: "TorrentCore.Mac.Torrents.SortField.v1"
        )
        let field = TorrentCoreMacTorrentSortField(rawValue: storedField ?? "") ?? .name
        let descending = defaults.bool(
            forKey: "TorrentCore.Mac.Torrents.SortDescending.v1"
        )
        let orderedSort = TorrentCoreMacSortStorage.decode(
            stored,
            as: TorrentCoreMacTorrentSortField.self
        )
        let priorOrderedSort = TorrentCoreMacSortStorage.decode(
            priorStored,
            as: TorrentCoreMacTorrentSortField.self
        )
        let migratedSort: [TorrentCoreMacSortDescriptor<TorrentCoreMacTorrentSortField>]
        if let orderedSort {
            migratedSort = orderedSort
        } else if let priorOrderedSort {
            migratedSort = priorOrderedSort == Self.legacyDefaultSortDescriptors
                ? Self.defaultSortDescriptors
                : priorOrderedSort
        } else if storedField != nil {
            let legacySort = [TorrentCoreMacSortDescriptor(field: field, descending: descending)]
            migratedSort = legacySort == Self.legacyDefaultSortDescriptors
                ? Self.defaultSortDescriptors
                : legacySort
        } else {
            migratedSort = Self.defaultSortDescriptors
        }
        _sortDescriptors = State(initialValue: migratedSort)
    }

    var body: some View {
        VStack(spacing: 0) {
            filterBar
            Divider()

            TorrentCoreMacPhaseBanner(
                phase: session.torrents.phase,
                lastSuccessfulAt: session.torrents.lastSuccessfulAt
            )
            .padding(.horizontal, 12)
            .padding(.top, 8)

            if let summaries = session.torrents.value {
                if summaries.isEmpty {
                    emptyTorrentList
                } else if filteredAndSortedItems.isEmpty {
                    noFilterMatches
                } else {
                    torrentTable
                    Divider()
                    paginationBar
                }
            } else {
                unavailableTorrentList
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
            TorrentCoreMacTorrentInspector(
                session: session,
                selectedItem: selectedItem,
                isActing: isActing,
                close: { isInspectorPresented = false },
                pause: pauseSelected,
                resume: resumeSelected,
                makeNext: makeNextSelected,
                hold: holdSelected,
                releaseHold: releaseHoldSelected,
                resumeNext: resumeNextSelected,
                resumeOnHold: resumeOnHoldSelected,
                showPeers: { isPeersPresented = true },
                showTrackers: { isTrackersPresented = true },
                showHistory: {
                    if let torrentID = selectedItem?.summary.torrentID {
                        showHistory(torrentID)
                    }
                },
                showLogs: {
                    if let torrentID = selectedItem?.summary.torrentID {
                        showLogs(torrentID)
                    }
                },
                refreshMetadata: refreshSelectedMetadata,
                requestMetadataReset: {
                    isMetadataResetConfirmationPresented = true
                },
                requestCallbackRetry: {
                    isCallbackRetryConfirmationPresented = true
                },
                requestRemoval: { deleteData in
                    guard let selectedItem else {
                        return
                    }
                    pendingRemoval = PendingRemoval(
                        item: selectedItem,
                        deleteData: deleteData
                    )
                }
            )
        }
        .torrentCoreToast(notice: $notice)
        .sheet(isPresented: $isPeersPresented) {
            if let selectedItem, let torrentID = selectedItem.summary.torrentID {
                TorrentCoreMacPeersSheet(
                    session: session,
                    torrentID: torrentID,
                    torrentName: selectedItem.name
                )
            }
        }
        .sheet(isPresented: $isTrackersPresented) {
            if let selectedItem, let torrentID = selectedItem.summary.torrentID {
                TorrentCoreMacTrackersSheet(
                    session: session,
                    torrentID: torrentID,
                    torrentName: selectedItem.name
                )
            }
        }
        .onChange(of: selectedTorrentID) { _, newValue in
            isInspectorPresented = newValue != nil
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
        .onChange(of: session.torrents.value) { _, summaries in
            clampPageAndReconcileSelection()
        }
        .onChange(of: sortDescriptors) { _, descriptors in
            storedSort = TorrentCoreMacSortStorage.encode(descriptors)
            pageIndex = 0
            reconcileSelectionWithCurrentPage()
        }
        .confirmationDialog(
            pendingRemoval?.deleteData == true
                ? "Remove Torrent and Delete Data?"
                : "Remove Torrent?",
            isPresented: Binding(
                get: { pendingRemoval != nil },
                set: { if !$0 { pendingRemoval = nil } }
            ),
            titleVisibility: .visible
        ) {
            if let pendingRemoval {
                Button(
                    pendingRemoval.deleteData
                        ? "Remove & Delete Data"
                        : "Remove Torrent",
                    role: .destructive
                ) {
                    performRemoval(pendingRemoval)
                }
            }
            Button("Cancel", role: .cancel) {
                pendingRemoval = nil
            }
        } message: {
            if let pendingRemoval {
                Text(
                    pendingRemoval.deleteData
                        ? "Remove “\(pendingRemoval.item.name)” from TorrentCore and permanently delete its downloaded data from disk?"
                        : "Remove “\(pendingRemoval.item.name)” from TorrentCore tracking? Downloaded data will remain on disk."
                )
            }
        }
        .confirmationDialog(
            "Reset Metadata Session?",
            isPresented: $isMetadataResetConfirmationPresented,
            titleVisibility: .visible
        ) {
            Button("Reset Metadata Session", role: .destructive) {
                resetSelectedMetadata()
            }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text(
                "Stop the current metadata resolution session and start it again for the selected torrent?"
            )
        }
        .confirmationDialog(
            "Retry Completion Callback?",
            isPresented: $isCallbackRetryConfirmationPresented,
            titleVisibility: .visible
        ) {
            Button("Retry Callback") {
                retrySelectedCallback()
            }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text(
                "Ask TorrentCore to invoke the completion callback again for the selected torrent?"
            )
        }
        .alert(
            "Torrent Action Failed",
            isPresented: Binding(
                get: { actionError != nil },
                set: { if !$0 { actionError = nil } }
            )
        ) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(actionError ?? "TorrentCore could not complete the action.")
        }
        .torrentCoreRefreshWhileVisible(
            session: session,
            context: refreshContext,
            isEnabled: !isPeersPresented
                && !isTrackersPresented
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

    private var filterBar: some View {
        HStack(spacing: 12) {
            HStack(spacing: 4) {
                TextField("Search by name", text: $searchText)
                    .textFieldStyle(.roundedBorder)
                    .frame(minWidth: 180, idealWidth: 260, maxWidth: 340)
                    .accessibilityIdentifier("torrents.search")
                    .onChange(of: searchText) { _, _ in
                        pageIndex = 0
                        reconcileSelectionWithCurrentPage()
                    }
                TorrentCoreMacHelpButton(content: TorrentCoreHelpCatalog.Torrents.name)
            }

            HStack(spacing: 4) {
                Picker("State", selection: $storedStateFilter) {
                    Text("All States").tag("")
                    ForEach(TorrentCoreKnownTorrentState.allCases, id: \.rawValue) { state in
                        Text(TorrentCoreDisplayFormatter.splitIdentifier(state.rawValue))
                            .tag(state.rawValue)
                    }
                }
                .frame(maxWidth: 210)
                .accessibilityIdentifier("torrents.stateFilter")
                .onChange(of: storedStateFilter) { _, _ in
                    pageIndex = 0
                    reconcileSelectionWithCurrentPage()
                }
                TorrentCoreMacHelpButton(content: TorrentCoreHelpCatalog.Torrents.state)
            }

            HStack(spacing: 4) {
                Picker("Category", selection: $storedCategoryFilter) {
                    Text("All Categories").tag("")
                    Text("Uncategorized").tag(Self.uncategorizedFilter)
                    ForEach(categoryOptions, id: \.self) { category in
                        Text(category).tag(category)
                    }
                }
                .frame(maxWidth: 220)
                .accessibilityIdentifier("torrents.categoryFilter")
                .onChange(of: storedCategoryFilter) { _, _ in
                    pageIndex = 0
                    reconcileSelectionWithCurrentPage()
                }
                TorrentCoreMacHelpButton(content: TorrentCoreHelpCatalog.Torrents.category)
            }

            Picker("Reason", selection: $storedReasonFilter) {
                Text("All Reasons").tag("")
                ForEach(reasonFilterOptions, id: \.self) { reason in
                    Text(reason.isEmpty ? "Not waiting" : TorrentCoreDisplayFormatter.splitIdentifier(reason))
                        .tag(reason.isEmpty ? Self.notWaitingFilter : reason)
                }
            }
            .frame(maxWidth: 230)
            .accessibilityIdentifier("torrents.reasonFilter")
            .onChange(of: storedReasonFilter) { _, _ in
                pageIndex = 0
                reconcileSelectionWithCurrentPage()
            }

            Button("Reset Filters", action: resetFilters)
                .accessibilityIdentifier("torrents.resetFilters")
                .disabled(
                    searchText.isEmpty
                        && storedStateFilter.isEmpty
                        && storedCategoryFilter.isEmpty
                        && storedReasonFilter.isEmpty
                )

            Spacer()

            if isActing || session.activeMutation != nil {
                ProgressView()
                    .controlSize(.small)
                    .accessibilityLabel("Torrent action in progress")
            } else if let actionMessage {
                Text(actionMessage)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
        }
        .padding(12)
        .tint(.orange)
    }

    private func resetFilters() {
        searchText = ""
        storedStateFilter = ""
        storedCategoryFilter = ""
        storedReasonFilter = ""
        pageIndex = 0
        reconcileSelectionWithCurrentPage()
    }

    private var torrentTable: some View {
        Table(
            currentPage,
            selection: tableSelection,
            sortOrder: tableSortOrder,
            columnCustomization: $columnCustomization
        ) {
            torrentPrimaryColumns
            torrentQueueColumns
            torrentDiagnosticColumns
            torrentCallbackColumns
        }
        .accessibilityIdentifier("torrents.table")
    }

    @TableColumnBuilder<
        TorrentCoreTorrentListItem,
        KeyPathComparator<TorrentCoreTorrentListItem>
    >
    private var torrentPrimaryColumns: some TableColumnContent<
        TorrentCoreTorrentListItem,
        KeyPathComparator<TorrentCoreTorrentListItem>
    > {
        TableColumn(
            sortHeaderTitle(.name),
            value: \.name,
            comparator: .localizedStandard
        ) { item in
            Text(item.name)
                .lineLimit(2)
                .contextMenu { torrentContextMenu(item) }
        }
        .width(min: 220, ideal: 340)
        .defaultVisibility(.visible)
        .disabledCustomizationBehavior(.visibility)
        .customizationID(TorrentCoreMacTorrentColumn.name.id)

        TableColumn(
            sortHeaderTitle(.state),
            value: \.state,
            comparator: .localizedStandard
        ) { item in
            Text(TorrentCoreDisplayFormatter.state(item.summary.state))
        }
        .width(min: 115, ideal: 145)
        .defaultVisibility(.visible)
        .customizationID(TorrentCoreMacTorrentColumn.state.id)

        TableColumn(sortHeaderTitle(.progress), value: \.progress) { item in
            HStack(spacing: 6) {
                ProgressView(value: item.progress, total: 100)
                Text(TorrentCoreDisplayFormatter.percent(item.progress))
                    .monospacedDigit()
                    .frame(width: 48, alignment: .trailing)
            }
        }
        .width(min: 125, ideal: 160)
        .defaultVisibility(.visible)
        .customizationID(TorrentCoreMacTorrentColumn.progress.id)

        TableColumn(sortHeaderTitle(.download), value: \.downloadRate) { item in
            Text(TorrentCoreDisplayFormatter.rate(item.downloadRate))
                .monospacedDigit()
        }
        .width(min: 85, ideal: 105)
        .defaultVisibility(.visible)
        .customizationID(TorrentCoreMacTorrentColumn.download.id)

        TableColumn(sortHeaderTitle(.peers), value: \.peers) { item in
            Text(item.peers.formatted())
                .monospacedDigit()
        }
        .width(min: 50, ideal: 65)
        .defaultVisibility(.visible)
        .customizationID(TorrentCoreMacTorrentColumn.peers.id)

        TableColumn(
            sortHeaderTitle(.category),
            value: \.category,
            comparator: .localizedStandard
        ) { item in
            Text(TorrentCoreDisplayFormatter.category(item.summary.categoryKey))
        }
        .width(min: 95, ideal: 125)
        .defaultVisibility(.visible)
        .customizationID(TorrentCoreMacTorrentColumn.category.id)
    }

    @TableColumnBuilder<
        TorrentCoreTorrentListItem,
        KeyPathComparator<TorrentCoreTorrentListItem>
    >
    private var torrentQueueColumns: some TableColumnContent<
        TorrentCoreTorrentListItem,
        KeyPathComparator<TorrentCoreTorrentListItem>
    > {
        textColumn(.reason, field: .reason, value: \.reason, min: 130, ideal: 175)

        TableColumn(sortHeaderTitle(.queue), value: \.queueSortValue) { item in
            Text(item.queuePosition?.formatted() ?? "--").monospacedDigit()
        }
        .width(min: 58, ideal: 68)
        .defaultVisibility(.visible)
        .customizationID(TorrentCoreMacTorrentColumn.queue.id)

        TableColumn(sortHeaderTitle(.priority), value: \.priorityQueueSortValue) { item in
            Text(item.priorityQueuePosition?.formatted() ?? "--").monospacedDigit()
        }
        .width(min: 65, ideal: 78)
        .defaultVisibility(.visible)
        .customizationID(TorrentCoreMacTorrentColumn.priority.id)

        TableColumn(sortHeaderTitle(.held), value: \.heldQueueSortValue) { item in
            Text(item.heldQueuePosition?.formatted() ?? "--").monospacedDigit()
        }
        .width(min: 52, ideal: 64)
        .defaultVisibility(.visible)
        .customizationID(TorrentCoreMacTorrentColumn.held.id)

        textColumn(
            .torrentID,
            field: .torrentID,
            value: \.torrentIDText,
            min: 220,
            ideal: 285
        )

        TableColumn(sortHeaderTitle(.upload), value: \.uploadRate) { item in
            Text(TorrentCoreDisplayFormatter.rate(item.uploadRate)).monospacedDigit()
        }
        .width(min: 85, ideal: 105)
        .defaultVisibility(.hidden)
        .customizationID(TorrentCoreMacTorrentColumn.upload.id)

        TableColumn(sortHeaderTitle(.trackers), value: \.trackerCount) { item in
            Text(item.trackerCount.formatted()).monospacedDigit()
        }
        .width(min: 62, ideal: 76)
        .defaultVisibility(.hidden)
        .customizationID(TorrentCoreMacTorrentColumn.trackers.id)

        TableColumn(sortHeaderTitle(.downloaded), value: \.downloadedBytes) { item in
            Text(TorrentCoreDisplayFormatter.bytes(item.downloadedBytes)).monospacedDigit()
        }
        .width(min: 95, ideal: 120)
        .defaultVisibility(.hidden)
        .customizationID(TorrentCoreMacTorrentColumn.downloaded.id)

        TableColumn(sortHeaderTitle(.total), value: \.totalSortValue) { item in
            Text(TorrentCoreDisplayFormatter.bytes(item.summary.totalBytes)).monospacedDigit()
        }
        .width(min: 90, ideal: 115)
        .defaultVisibility(.hidden)
        .customizationID(TorrentCoreMacTorrentColumn.total.id)
    }

    @TableColumnBuilder<
        TorrentCoreTorrentListItem,
        KeyPathComparator<TorrentCoreTorrentListItem>
    >
    private var torrentDiagnosticColumns: some TableColumnContent<
        TorrentCoreTorrentListItem,
        KeyPathComparator<TorrentCoreTorrentListItem>
    > {
        dateColumn(.added, field: .added, value: \.summary.addedAt)
        sortableOptionalDateColumn(
            .completed,
            field: .completed,
            sortValue: \.completedSortValue,
            value: \.summary.completedAt
        )
        sortableOptionalDateColumn(
            .lastActivity,
            field: .lastActivity,
            sortValue: \.lastActivitySortValue,
            value: \.summary.lastActivityAt
        )
        sortableOptionalDateColumn(
            .noProgressSince,
            field: .noProgressSince,
            sortValue: \.noProgressSortValue,
            value: \.summary.downloadNoProgressStartedAt
        )
        sortableOptionalDateColumn(
            .lastYielded,
            field: .lastYielded,
            sortValue: \.lastYieldedSortValue,
            value: \.summary.downloadLastYieldedAt
        )
        textColumn(
            .error,
            field: .error,
            value: \.errorText,
            min: 180,
            ideal: 300
        )
        TableColumn(sortHeaderTitle(.queueHeld), value: \.queueHeldSortValue) { item in
            Text(yesNo(item.summary.isQueueHeld))
        }
        .width(min: 85, ideal: 100)
        .defaultVisibility(.hidden)
        .customizationID(TorrentCoreMacTorrentColumn.queueHeld.id)
        TableColumn(sortHeaderTitle(.downloadYielded), value: \.downloadYieldedSortValue) { item in
            Text(yesNo(item.summary.isDownloadYielded))
        }
        .width(min: 110, ideal: 125)
        .defaultVisibility(.hidden)
        .customizationID(TorrentCoreMacTorrentColumn.downloadYielded.id)
    }

    @TableColumnBuilder<
        TorrentCoreTorrentListItem,
        KeyPathComparator<TorrentCoreTorrentListItem>
    >
    private var torrentCallbackColumns: some TableColumnContent<
        TorrentCoreTorrentListItem,
        KeyPathComparator<TorrentCoreTorrentListItem>
    > {
        textColumn(
            .callback,
            field: .callback,
            value: \.callbackText,
            min: 125,
            ideal: 190
        )
        sortableOptionalDateColumn(
            .callbackPendingSince,
            field: .callbackPendingSince,
            sortValue: \.callbackPendingSortValue,
            value: \.summary.completionCallbackPendingSince
        )
        sortableOptionalDateColumn(
            .callbackInvoked,
            field: .callbackInvoked,
            sortValue: \.callbackInvokedSortValue,
            value: \.summary.completionCallbackInvokedAt
        )
        TableColumn(
            sortHeaderTitle(.callbackError),
            value: \.callbackErrorText,
            comparator: .localizedStandard
        ) { item in
            Text(TorrentCoreDisplayFormatter.operatorValue(
                item.summary.completionCallbackLastError
            ))
            .lineLimit(2)
        }
        .width(min: 180, ideal: 300)
        .defaultVisibility(.hidden)
        .customizationID(TorrentCoreMacTorrentColumn.callbackError.id)
    }

    @TableColumnBuilder<
        TorrentCoreTorrentListItem,
        KeyPathComparator<TorrentCoreTorrentListItem>
    >
    private func textColumn(
        _ column: TorrentCoreMacTorrentColumn,
        field: TorrentCoreMacTorrentSortField,
        value: KeyPath<TorrentCoreTorrentListItem, String>,
        min: CGFloat,
        ideal: CGFloat
    ) -> some TableColumnContent<
        TorrentCoreTorrentListItem,
        KeyPathComparator<TorrentCoreTorrentListItem>
    > {
        TableColumn(
            sortHeaderTitle(field),
            value: value,
            comparator: .localizedStandard
        ) { item in
            Text(TorrentCoreDisplayFormatter.operatorValue(item[keyPath: value]))
                .lineLimit(2)
        }
        .width(min: min, ideal: ideal)
        .defaultVisibility(.hidden)
        .customizationID(column.id)
    }

    @TableColumnBuilder<
        TorrentCoreTorrentListItem,
        KeyPathComparator<TorrentCoreTorrentListItem>
    >
    private func dateColumn(
        _ column: TorrentCoreMacTorrentColumn,
        field: TorrentCoreMacTorrentSortField,
        value: KeyPath<TorrentCoreTorrentListItem, Date>
    ) -> some TableColumnContent<
        TorrentCoreTorrentListItem,
        KeyPathComparator<TorrentCoreTorrentListItem>
    > {
        TableColumn(sortHeaderTitle(field), value: value) { item in
            Text(TorrentCoreDisplayFormatter.timestamp(item[keyPath: value]))
                .monospacedDigit()
        }
        .width(min: 135, ideal: 165)
        .defaultVisibility(.hidden)
        .customizationID(column.id)
    }

    @TableColumnBuilder<
        TorrentCoreTorrentListItem,
        KeyPathComparator<TorrentCoreTorrentListItem>
    >
    private func sortableOptionalDateColumn(
        _ column: TorrentCoreMacTorrentColumn,
        field: TorrentCoreMacTorrentSortField,
        sortValue: KeyPath<TorrentCoreTorrentListItem, Date>,
        value: KeyPath<TorrentCoreTorrentListItem, Date?>
    ) -> some TableColumnContent<
        TorrentCoreTorrentListItem,
        KeyPathComparator<TorrentCoreTorrentListItem>
    > {
        TableColumn(sortHeaderTitle(field), value: sortValue) { item in
            Text(TorrentCoreDisplayFormatter.timestamp(item[keyPath: value]))
                .monospacedDigit()
        }
        .width(min: 135, ideal: 165)
        .defaultVisibility(.hidden)
        .customizationID(column.id)
    }

    private var paginationBar: some View {
        TorrentCoreMacPaginationBar(
            resultCount: filteredAndSortedItems.count,
            pageIndex: $pageIndex,
            pageSize: pageSizeBinding,
            accessibilityPrefix: "torrents"
        )
    }

    private var emptyTorrentList: some View {
        ContentUnavailableView {
            Label("No Torrents", systemImage: "tray")
        } description: {
            Text("The service is connected and has no torrents.")
        } actions: {
            Button("Add Magnet") {
                addMagnet()
            }
            .disabled(!session.connectionState.isConnected)
        }
        .accessibilityIdentifier("torrents.empty")
    }

    private var noFilterMatches: some View {
        ContentUnavailableView(
            "No Torrents Match",
            systemImage: "line.3.horizontal.decrease.circle",
            description: Text("Clear or change the current torrent filters.")
        )
            .accessibilityIdentifier("torrents.noMatches")
    }

    private var unavailableTorrentList: some View {
        ContentUnavailableView {
            Label(unavailableTitle, systemImage: unavailableSystemImage)
        } description: {
            Text(unavailableMessage)
        } actions: {
            if case .loading = session.torrents.phase {
                ProgressView()
                    .controlSize(.small)
            }
            if session.activeProfile != nil {
                Button("Refresh") {
                    Task { await session.refresh(refreshContext) }
                }
            }
        }
        .accessibilityIdentifier("torrents.unavailable")
    }

    private var allSummaries: [TorrentCoreTorrentSummary] {
        session.torrents.value ?? []
    }

    private var refreshContext: TorrentCoreFeatureContext {
        if isInspectorPresented, let selectedTorrentID {
            return .torrentListAndDetail(selectedTorrentID)
        }
        return .torrents
    }

    private var filteredAndSortedItems: [TorrentCoreTorrentListItem] {
        let categoryFilter: TorrentCoreTorrentCategoryFilter
        if storedCategoryFilter.isEmpty {
            categoryFilter = .all
        } else if storedCategoryFilter == Self.uncategorizedFilter {
            categoryFilter = .uncategorized
        } else {
            categoryFilter = .category(storedCategoryFilter)
        }
        let filter = TorrentCoreTorrentFilter(
            searchText: searchText,
            state: storedStateFilter.isEmpty ? nil : storedStateFilter,
            category: categoryFilter,
            waitReason: storedReasonFilter.isEmpty ? nil : storedReasonFilter
        )
        let items = filter.apply(to: allSummaries).map(TorrentCoreTorrentListItem.init)
        return items.sorted { left, right in
            for descriptor in sortDescriptors {
                let comparison = compare(left, right, using: descriptor)
                if comparison != .orderedSame {
                    return comparison == .orderedAscending
                }
            }
            if sortDescriptors.contains(where: {
                $0.field == .queue || $0.field == .priority || $0.field == .held
            }) {
                return left.name.localizedStandardCompare(right.name) == .orderedAscending
            }
            return false
        }
    }

    private var comparatorOrder: [KeyPathComparator<TorrentCoreTorrentListItem>] {
        sortDescriptors.map { $0.field.comparator(descending: $0.descending) }
    }

    private var tableSortOrder: Binding<[KeyPathComparator<TorrentCoreTorrentListItem>]> {
        Binding(
            get: { comparatorOrder },
            set: { proposed in
                guard let comparator = proposed.first,
                      let field = TorrentCoreMacTorrentSortField.field(
                          for: comparator.keyPath
                      )
                else { return }
                sortDescriptors = [
                    .init(field: field, descending: comparator.order == .reverse),
                ]
            }
        )
    }

    private var currentPage: [TorrentCoreTorrentListItem] {
        TorrentCoreMacTableSupport.page(
            filteredAndSortedItems,
            index: pageIndex,
            size: pageSize
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
                guard TorrentCoreMacTableSupport.pageSizes.contains(newValue) else {
                    return
                }
                storedPageSize = newValue
                pageIndex = 0
                reconcileSelectionWithCurrentPage()
            }
        )
    }

    private var tableSelection: Binding<String?> {
        Binding(
            get: { selectedTorrentID?.uuidString },
            set: { value in
                selectedTorrentID = value.flatMap(UUID.init(uuidString:))
            }
        )
    }

    private var selectedItem: TorrentCoreTorrentListItem? {
        guard let selectedTorrentID,
              let summary = allSummaries.first(where: { $0.torrentID == selectedTorrentID })
        else {
            return nil
        }
        return TorrentCoreTorrentListItem(summary: summary)
    }

    private var canPauseSelected: Bool {
        guard let selectedItem else {
            return false
        }
        return session.canPause(selectedItem.summary)
    }

    private var canResumeSelected: Bool {
        guard let selectedItem else {
            return false
        }
        return session.canResume(selectedItem.summary)
    }

    private var categoryOptions: [String] {
        allSummaries.compactMap(\.categoryKey)
            .filter { !$0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
            .reduce(into: [String]()) { result, category in
                if !result.contains(where: {
                    $0.localizedCaseInsensitiveCompare(category) == .orderedSame
                }) {
                    result.append(category)
                }
            }
            .sorted { $0.localizedCaseInsensitiveCompare($1) == .orderedAscending }
    }

    private var reasonFilterOptions: [String] {
        Set(allSummaries.map { $0.waitReason?.rawValue ?? "" })
            .sorted { left, right in
                let leftLabel = left.isEmpty ? "Not waiting" : left
                let rightLabel = right.isEmpty ? "Not waiting" : right
                return leftLabel.localizedStandardCompare(rightLabel) == .orderedAscending
            }
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
        .accessibilityIdentifier("torrents.sort")
    }

    private var columnsMenu: some View {
        Menu("Columns", systemImage: "rectangle.3.group") {
            ForEach(TorrentCoreMacTorrentColumn.allCases) { column in
                if column.canHide {
                    Toggle(column.title, isOn: columnVisibility(column))
                }
            }
            Divider()
            Button("Show All Columns") {
                for column in TorrentCoreMacTorrentColumn.allCases {
                    columnCustomization[visibility: column.id] = .visible
                }
            }
            Button("Restore Default Columns") {
                for column in TorrentCoreMacTorrentColumn.allCases {
                    columnCustomization[visibility: column.id] = .automatic
                }
            }
            Divider()
            Button("Reset Table Layout") {
                columnCustomization = .init()
            }
        }
        .accessibilityIdentifier("torrents.columns")
    }

    private var exportMenu: some View {
        Menu("Export", systemImage: "square.and.arrow.up") {
            Button(selectedItem == nil ? "Export Selected Row" : "Export Selected Row (1)") {
                export(.selected)
            }
            .disabled(selectedItem == nil)
            Button("Export All Results (\(filteredAndSortedItems.count.formatted()))") {
                export(.all)
            }
            .disabled(filteredAndSortedItems.isEmpty)
        }
        .accessibilityIdentifier("torrents.export")
    }

    private func sortHeaderTitle(_ field: TorrentCoreMacTorrentSortField) -> String {
        guard let index = sortDescriptors.firstIndex(where: { $0.field == field }) else {
            return field.title
        }
        return "\(field.title) \(sortDescriptors[index].descending ? "↓" : "↑")\(index + 1)"
    }

    private func columnVisibility(_ column: TorrentCoreMacTorrentColumn) -> Binding<Bool> {
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

    private func compare(
        _ left: TorrentCoreTorrentListItem,
        _ right: TorrentCoreTorrentListItem,
        using descriptor: TorrentCoreMacSortDescriptor<TorrentCoreMacTorrentSortField>
    ) -> ComparisonResult {
        switch descriptor.field {
        case .queue, .priority, .held:
            let reasonComparison = left.reason.localizedStandardCompare(right.reason)
            if reasonComparison != .orderedSame {
                return reasonComparison
            }
            let leftValue = queueValue(left, field: descriptor.field)
            let rightValue = queueValue(right, field: descriptor.field)
            return comparableResult(
                leftValue,
                rightValue,
                descending: descriptor.descending
            )
        default:
            return descriptor.field
                .comparator(descending: descriptor.descending)
                .compare(left, right)
        }
    }

    private func queueValue(
        _ item: TorrentCoreTorrentListItem,
        field: TorrentCoreMacTorrentSortField
    ) -> Int {
        switch field {
        case .queue: item.queueSortValue
        case .priority: item.priorityQueueSortValue
        case .held: item.heldQueueSortValue
        default: .min
        }
    }

    private func comparableResult<Value: Comparable>(
        _ left: Value,
        _ right: Value,
        descending: Bool
    ) -> ComparisonResult {
        guard left != right else { return .orderedSame }
        let ascending = left < right
        return ascending != descending ? .orderedAscending : .orderedDescending
    }

    @ViewBuilder
    private func torrentContextMenu(_ item: TorrentCoreTorrentListItem) -> some View {
        Button("Show Details") {
            selectedTorrentID = item.summary.torrentID
            isInspectorPresented = true
            contextChanged()
        }
        if let torrentID = item.summary.torrentID {
            Button("Show History") { showHistory(torrentID) }
            Button("Show Filtered Logs") { showLogs(torrentID) }
        }
        Divider()
        if session.canMakeNext(item.summary) {
            Button("Make Next") { makeNext(item) }
        }
        if session.canHold(item.summary) {
            Button("Hold") { hold(item) }
        }
        if session.canReleaseHold(item.summary) {
            Button("Release Hold") { releaseHold(item) }
        }
        if session.canResumeNext(item.summary) {
            Button("Resume Next") { resumeNext(item) }
        }
        if session.canResumeOnHold(item.summary) {
            Button("Resume on Hold") { resumeOnHold(item) }
        }
    }

    private func clampPageAndReconcileSelection() {
        pageIndex = TorrentCoreMacTableSupport.clampedPageIndex(
            pageIndex,
            count: filteredAndSortedItems.count,
            size: pageSize
        )
        reconcileSelectionWithCurrentPage()
    }

    private func reconcileSelectionWithCurrentPage() {
        guard let selectedTorrentID else { return }
        guard currentPage.contains(where: {
            $0.summary.torrentID == selectedTorrentID
        }) else {
            self.selectedTorrentID = nil
            isInspectorPresented = false
            return
        }
    }

    private func export(_ scope: TorrentCoreMacExportScope) {
        let rows = scope.rows(selected: selectedItem, all: filteredAndSortedItems)
        guard !rows.isEmpty else { return }
        do {
            let fileURL = try TorrentCoreMacTableExport.write(
                headers: Self.exportHeaders,
                rows: rows.map { Self.exportRow($0.summary) },
                fileName: "torrents-\(scope.rawValue)-\(TorrentCoreMacTableExport.timestamp()).csv"
            )
            notice = .init(
                kind: .success,
                message: "Exported \(rows.count.formatted()) row\(rows.count == 1 ? "" : "s") to Downloads/\(fileURL.lastPathComponent)."
            )
        } catch {
            notice = .init(
                kind: .error,
                message: "Export failed: \(TorrentCoreMacErrorPresenter.message(error))"
            )
        }
    }

    static func exportRow(_ value: TorrentCoreTorrentSummary) -> [String] {
        func yesNo(_ value: Bool) -> String { value ? "Yes" : "No" }
        return [
            value.torrentID?.uuidString ?? "",
            value.name ?? "",
            value.categoryKey ?? "",
            value.state.rawValue,
            String(format: "%.1f", locale: Locale(identifier: "en_US_POSIX"), value.progressPercent),
            String(value.downloadRateBytesPerSecond),
            String(value.uploadRateBytesPerSecond),
            String(value.connectedPeerCount),
            String(value.trackerCount),
            String(value.downloadedBytes),
            value.totalBytes.map(String.init) ?? "",
            TorrentCoreMacTableExport.isoTimestamp(value.addedAt),
            TorrentCoreMacTableExport.isoTimestamp(value.lastActivityAt),
            TorrentCoreMacTableExport.isoTimestamp(value.completedAt),
            value.errorMessage ?? "",
            value.waitReason?.rawValue ?? "",
            value.queuePosition.map(String.init) ?? "",
            value.priorityQueuePosition.map(String.init) ?? "",
            value.heldQueuePosition.map(String.init) ?? "",
            yesNo(value.isQueueHeld),
            yesNo(value.isDownloadYielded),
            TorrentCoreMacTableExport.isoTimestamp(value.downloadNoProgressStartedAt),
            TorrentCoreMacTableExport.isoTimestamp(value.downloadLastYieldedAt),
            value.completionCallbackState ?? "",
            TorrentCoreMacTableExport.isoTimestamp(value.completionCallbackPendingSince),
            TorrentCoreMacTableExport.isoTimestamp(value.completionCallbackInvokedAt),
            value.completionCallbackLastError ?? "",
            yesNo(value.canPause),
            yesNo(value.canResume),
            yesNo(value.canRemove),
            yesNo(value.canRefreshMetadata),
            yesNo(value.canRetryCompletionCallback),
            yesNo(value.canMakeNext),
            yesNo(value.canHold),
            yesNo(value.canReleaseHold),
            yesNo(value.canResumeNext),
            yesNo(value.canResumeOnHold),
        ]
    }

    private func yesNo(_ value: Bool) -> String {
        value ? "Yes" : "No"
    }

    private var unavailableTitle: String {
        if case .loading = session.torrents.phase {
            return "Loading Torrents"
        }
        return switch session.connectionState {
        case .noProfile:
            "No TorrentCore Connection"
        case .offline:
            "TorrentCore Offline"
        case .connecting:
            "Connecting"
        case .notConnected, .connected:
            "Torrents Unavailable"
        }
    }

    private var unavailableSystemImage: String {
        if case .loading = session.torrents.phase {
            return "arrow.trianglehead.2.clockwise"
        }
        return switch session.connectionState {
        case .offline:
            "network.slash"
        case .connecting:
            "arrow.trianglehead.2.clockwise"
        case .noProfile, .notConnected, .connected:
            "tray"
        }
    }

    private var unavailableMessage: String {
        if case .loading = session.torrents.phase {
            return "Requesting the current torrent list from TorrentCore."
        }
        return switch session.connectionState {
        case .noProfile:
            "Create or select a connection before loading torrents."
        case let .offline(_, _, message):
            message
        case .connecting:
            "Checking TorrentCore.Service…"
        case .notConnected:
            "Refresh to connect to the selected TorrentCore installation."
        case .connected:
            "TorrentCore did not return a torrent list."
        }
    }

    private func pauseSelected() {
        guard let selectedItem else {
            return
        }
        performAction(successMessage: "Paused \(selectedItem.name).") {
            _ = try await session.pause(selectedItem.summary)
        }
    }

    private func resumeSelected() {
        guard let selectedItem else {
            return
        }
        performAction(successMessage: "Resumed \(selectedItem.name).") {
            _ = try await session.resume(selectedItem.summary)
        }
    }

    private func makeNextSelected() { selectedItem.map(makeNext) }
    private func holdSelected() { selectedItem.map(hold) }
    private func releaseHoldSelected() { selectedItem.map(releaseHold) }
    private func resumeNextSelected() { selectedItem.map(resumeNext) }
    private func resumeOnHoldSelected() { selectedItem.map(resumeOnHold) }

    private func makeNext(_ item: TorrentCoreTorrentListItem) {
        performAction(successMessage: "Made \(item.name) next.") {
            _ = try await session.makeNext(item.summary)
        }
    }

    private func hold(_ item: TorrentCoreTorrentListItem) {
        performAction(successMessage: "Placed \(item.name) on hold.") {
            _ = try await session.hold(item.summary)
        }
    }

    private func releaseHold(_ item: TorrentCoreTorrentListItem) {
        performAction(successMessage: "Released hold for \(item.name).") {
            _ = try await session.releaseHold(item.summary)
        }
    }

    private func resumeNext(_ item: TorrentCoreTorrentListItem) {
        performAction(successMessage: "Resumed \(item.name) next.") {
            _ = try await session.resumeNext(item.summary)
        }
    }

    private func resumeOnHold(_ item: TorrentCoreTorrentListItem) {
        performAction(successMessage: "Resumed \(item.name) on hold.") {
            _ = try await session.resumeOnHold(item.summary)
        }
    }

    private func refreshSelectedMetadata() {
        guard let selectedItem else {
            return
        }
        performAction(successMessage: "Requested metadata refresh for \(selectedItem.name).") {
            _ = try await session.refreshMetadata(selectedItem.summary)
        }
    }

    private func resetSelectedMetadata() {
        guard let selectedItem else {
            return
        }
        performAction(successMessage: "Reset metadata session for \(selectedItem.name).") {
            _ = try await session.resetMetadataSession(selectedItem.summary)
        }
    }

    private func retrySelectedCallback() {
        guard let selectedItem else {
            return
        }
        performAction(successMessage: "Requested callback retry for \(selectedItem.name).") {
            _ = try await session.retryCompletionCallback(selectedItem.summary)
        }
    }

    private func performRemoval(_ removal: PendingRemoval) {
        pendingRemoval = nil
        performAction(
            successMessage: removal.deleteData
                ? "Removed \(removal.item.name) and deleted its data."
                : "Removed \(removal.item.name) from TorrentCore."
        ) {
            _ = try await session.remove(
                removal.item.summary,
                deleteData: removal.deleteData
            )
            selectedTorrentID = nil
            isInspectorPresented = false
            contextChanged()
        }
    }

    private func performAction(
        successMessage: String,
        operation: @escaping @MainActor () async throws -> Void
    ) {
        guard !isActing else {
            return
        }
        isActing = true
        actionError = nil
        actionMessage = nil
        Task {
            defer { isActing = false }
            do {
                try await operation()
                actionMessage = successMessage
            } catch {
                actionError = TorrentCoreMacErrorPresenter.message(error)
            }
        }
    }

    private static let uncategorizedFilter = "__uncategorized__"
    private static let notWaitingFilter = "__not_waiting"
    static let defaultSortDescriptors = [
        TorrentCoreMacSortDescriptor(field: TorrentCoreMacTorrentSortField.state, descending: false),
        TorrentCoreMacSortDescriptor(field: TorrentCoreMacTorrentSortField.progress, descending: true),
    ]
    private static let legacyDefaultSortDescriptors = [
        TorrentCoreMacSortDescriptor(field: TorrentCoreMacTorrentSortField.name, descending: false),
    ]
    static let exportHeaders = [
        "Torrent ID",
        "Name",
        "Category",
        "State",
        "Progress Percent",
        "Download Rate Bytes Per Second",
        "Upload Rate Bytes Per Second",
        "Connected Peer Count",
        "Tracker Count",
        "Downloaded Bytes",
        "Total Bytes",
        "Added At",
        "Last Activity At",
        "Completed At",
        "Error Message",
        "Wait Reason",
        "Queue Position",
        "Priority Queue Position",
        "Held Queue Position",
        "Is Queue Held",
        "Is Download Yielded",
        "Download No Progress Started At",
        "Download Last Yielded At",
        "Completion Callback State",
        "Completion Callback Pending Since",
        "Completion Callback Invoked At",
        "Completion Callback Last Error",
        "Can Pause",
        "Can Resume",
        "Can Remove",
        "Can Refresh Metadata",
        "Can Retry Completion Callback",
        "Can Make Next",
        "Can Hold",
        "Can Release Hold",
        "Can Resume Next",
        "Can Resume On Hold",
    ]
}

private extension TorrentCoreMacTorrentsView {
    struct PendingRemoval {
        let item: TorrentCoreTorrentListItem
        let deleteData: Bool
    }
}

private struct TorrentCoreMacTorrentInspector: View {
    let session: TorrentCoreFeatureSession
    let selectedItem: TorrentCoreTorrentListItem?
    let isActing: Bool
    let close: () -> Void
    let pause: () -> Void
    let resume: () -> Void
    let makeNext: () -> Void
    let hold: () -> Void
    let releaseHold: () -> Void
    let resumeNext: () -> Void
    let resumeOnHold: () -> Void
    let showPeers: () -> Void
    let showTrackers: () -> Void
    let showHistory: () -> Void
    let showLogs: () -> Void
    let refreshMetadata: () -> Void
    let requestMetadataReset: () -> Void
    let requestCallbackRetry: () -> Void
    let requestRemoval: (Bool) -> Void

    var body: some View {
        Group {
            if let selectedItem {
                ScrollView {
                    VStack(alignment: .leading, spacing: 16) {
                        VStack(alignment: .leading, spacing: 6) {
                            HStack {
                                Text(selectedItem.name)
                                    .font(.title2.weight(.semibold))
                                    .textSelection(.enabled)
                                TorrentCoreMacHelpButton(
                                    content: TorrentCoreHelpCatalog.Torrents.selectedTorrent
                                )
                                Spacer()
                                Button("Close", systemImage: "xmark", action: close)
                                    .labelStyle(.iconOnly)
                                    .accessibilityIdentifier("inspector.close")
                            }
                            HStack {
                                Text(TorrentCoreDisplayFormatter.state(
                                    selectedItem.summary.state
                                ))
                                Text("•")
                                Text(TorrentCoreDisplayFormatter.category(
                                    selectedItem.summary.categoryKey
                                ))
                            }
                            .foregroundStyle(.secondary)
                        }

                        ProgressView(
                            value: selectedItem.summary.progressPercent,
                            total: 100
                        )
                        Text(TorrentCoreDisplayFormatter.percent(
                            selectedItem.summary.progressPercent
                        ))
                        .font(.headline)
                        .monospacedDigit()

                        HStack {
                            Button("Pause", action: pause)
                                .disabled(!session.canPause(selectedItem.summary) || isActing)
                                .accessibilityIdentifier("inspector.pause")
                                .help(TorrentCoreHelpCatalog.Torrents.pause.summary)
                            Button("Resume", action: resume)
                                .disabled(!session.canResume(selectedItem.summary) || isActing)
                                .accessibilityIdentifier("inspector.resume")
                                .help(TorrentCoreHelpCatalog.Torrents.resume.summary)
                        }

                        HStack {
                            if session.canMakeNext(selectedItem.summary) {
                                Button("Make Next", action: makeNext).disabled(isActing)
                            }
                            if session.canHold(selectedItem.summary) {
                                Button("Hold", action: hold).disabled(isActing)
                            }
                            if session.canReleaseHold(selectedItem.summary) {
                                Button("Release Hold", action: releaseHold).disabled(isActing)
                            }
                        }
                        HStack {
                            if session.canResumeNext(selectedItem.summary) {
                                Button("Resume Next", action: resumeNext).disabled(isActing)
                            }
                            if session.canResumeOnHold(selectedItem.summary) {
                                Button("Resume on Hold", action: resumeOnHold).disabled(isActing)
                            }
                        }

                        HStack {
                            Button("Peers", action: showPeers)
                                .disabled(
                                    selectedItem.summary.torrentID == nil
                                        || !session.connectionState.isConnected
                                )
                                .accessibilityIdentifier("inspector.peers")
                                .help(TorrentCoreHelpCatalog.Torrents.peers.summary)
                            Button("Trackers", action: showTrackers)
                                .disabled(
                                    selectedItem.summary.torrentID == nil
                                        || !session.connectionState.isConnected
                                )
                                .accessibilityIdentifier("inspector.trackers")
                                .help(TorrentCoreHelpCatalog.Torrents.trackers.summary)
                        }
                        HStack {
                            Button("History", action: showHistory)
                                .disabled(selectedItem.summary.torrentID == nil)
                                .accessibilityIdentifier("inspector.history")
                            Button("Logs", action: showLogs)
                                .disabled(selectedItem.summary.torrentID == nil)
                                .accessibilityIdentifier("inspector.logs")
                        }

                        Divider()

                        TorrentCoreMacPhaseBanner(
                            phase: session.torrentDetail.phase,
                            lastSuccessfulAt: session.torrentDetail.lastSuccessfulAt
                        )

                        detailRows(fallback: selectedItem)

                        Divider()

                        VStack(alignment: .leading, spacing: 8) {
                            Text("Recovery Actions")
                                .font(.headline)
                            Button("Refresh Metadata", action: refreshMetadata)
                                .disabled(
                                    !session.canRefreshMetadata(selectedItem.summary)
                                        || isActing
                                )
                                .accessibilityIdentifier("inspector.refreshMetadata")
                                .help(TorrentCoreHelpCatalog.Torrents.refreshMetadata.summary)
                            Button("Reset Metadata Session", action: requestMetadataReset)
                                .disabled(
                                    !session.canResetMetadataSession(selectedItem.summary)
                                        || isActing
                                )
                                .accessibilityIdentifier("inspector.resetMetadata")
                                .help(TorrentCoreHelpCatalog.Torrents.resetMetadata.summary)
                            Button("Retry Completion Callback", action: requestCallbackRetry)
                                .disabled(
                                    !session.canRetryCompletionCallback(selectedItem.summary)
                                        || isActing
                                )
                                .accessibilityIdentifier("inspector.retryCallback")
                                .help(TorrentCoreHelpCatalog.Torrents.retryCallback.summary)
                        }

                        Divider()

                        VStack(alignment: .leading, spacing: 8) {
                            Text("Remove")
                                .font(.headline)
                            Button("Remove from TorrentCore", role: .destructive) {
                                requestRemoval(false)
                            }
                            .disabled(!session.canRemove(selectedItem.summary) || isActing)
                            .accessibilityIdentifier("inspector.remove")
                            .help(TorrentCoreHelpCatalog.Torrents.remove.summary)

                            Button("Remove & Delete Data", role: .destructive) {
                                requestRemoval(true)
                            }
                            .disabled(!session.canRemove(selectedItem.summary) || isActing)
                            .accessibilityIdentifier("inspector.deleteData")
                            .help(TorrentCoreHelpCatalog.Torrents.deleteData.summary)
                        }
                    }
                    .padding(16)
                }
                .accessibilityIdentifier("torrents.inspector.content")
            } else {
                ContentUnavailableView(
                    "No Torrent Selected",
                    systemImage: "sidebar.trailing",
                    description: Text("Select one torrent to inspect its details.")
                )
            }
        }
    }

    @ViewBuilder
    private func detailRows(fallback: TorrentCoreTorrentListItem) -> some View {
        let detail = session.torrentDetail.value
        VStack(spacing: 9) {
            TorrentCoreMacDetailRow(
                label: "Downloaded",
                value: TorrentCoreDisplayFormatter.bytes(
                    detail?.downloadedBytes ?? fallback.summary.downloadedBytes
                )
            )
            TorrentCoreMacDetailRow(
                label: "Total Size",
                value: TorrentCoreDisplayFormatter.bytes(
                    detail?.totalBytes ?? fallback.summary.totalBytes
                )
            )
            TorrentCoreMacDetailRow(
                label: "Download",
                value: TorrentCoreDisplayFormatter.rate(
                    detail?.downloadRateBytesPerSecond
                        ?? fallback.summary.downloadRateBytesPerSecond
                )
            )
            TorrentCoreMacDetailRow(
                label: "Upload",
                value: TorrentCoreDisplayFormatter.rate(
                    detail?.uploadRateBytesPerSecond
                        ?? fallback.summary.uploadRateBytesPerSecond
                )
            )
            TorrentCoreMacDetailRow(
                label: "Peers",
                value: (detail?.connectedPeerCount
                    ?? fallback.summary.connectedPeerCount).formatted()
            )
            TorrentCoreMacDetailRow(
                label: "Trackers",
                value: (detail?.trackerCount ?? fallback.summary.trackerCount).formatted()
            )
            TorrentCoreMacDetailRow(
                label: "Reason",
                value: TorrentCoreDisplayFormatter.waitReason(
                    detail?.waitReason ?? fallback.summary.waitReason
                )
            )
            TorrentCoreMacDetailRow(
                label: "Queue #",
                value: (detail?.queuePosition ?? fallback.summary.queuePosition)?.formatted()
                    ?? "--"
            )
            TorrentCoreMacDetailRow(
                label: "Priority #",
                value: (detail?.priorityQueuePosition
                    ?? fallback.summary.priorityQueuePosition)?.formatted() ?? "--"
            )
            TorrentCoreMacDetailRow(
                label: "Held #",
                value: (detail?.heldQueuePosition
                    ?? fallback.summary.heldQueuePosition)?.formatted() ?? "--"
            )
            TorrentCoreMacDetailRow(
                label: "Added",
                value: TorrentCoreDisplayFormatter.timestamp(
                    detail?.addedAt ?? fallback.summary.addedAt
                )
            )
            TorrentCoreMacDetailRow(
                label: "Completed",
                value: TorrentCoreDisplayFormatter.timestamp(
                    detail?.completedAt ?? fallback.summary.completedAt
                )
            )
            TorrentCoreMacDetailRow(
                label: "Last Activity",
                value: TorrentCoreDisplayFormatter.timestamp(
                    detail?.lastActivityAt ?? fallback.summary.lastActivityAt
                )
            )
            TorrentCoreMacDetailRow(
                label: "No Progress Since",
                value: TorrentCoreDisplayFormatter.timestamp(
                    detail?.downloadNoProgressStartedAt
                        ?? fallback.summary.downloadNoProgressStartedAt
                )
            )
            TorrentCoreMacDetailRow(
                label: "Last Yielded",
                value: TorrentCoreDisplayFormatter.timestamp(
                    detail?.downloadLastYieldedAt
                        ?? fallback.summary.downloadLastYieldedAt
                )
            )
            TorrentCoreMacDetailRow(
                label: "Download Yielded",
                value: (detail?.isDownloadYielded
                    ?? fallback.summary.isDownloadYielded) ? "Yes" : "No"
            )
            TorrentCoreMacDetailRow(
                label: "Queue Held",
                value: (detail?.isQueueHeld
                    ?? fallback.summary.isQueueHeld) ? "Yes" : "No"
            )
            TorrentCoreMacDetailRow(
                label: "Callback",
                value: TorrentCoreDisplayFormatter.operatorValue(
                    detail?.completionCallbackState
                        ?? fallback.summary.completionCallbackState
                )
            )
            TorrentCoreMacDetailRow(
                label: "Callback Pending Since",
                value: TorrentCoreDisplayFormatter.timestamp(
                    detail?.completionCallbackPendingSince
                        ?? fallback.summary.completionCallbackPendingSince
                )
            )
            TorrentCoreMacDetailRow(
                label: "Callback Invoked",
                value: TorrentCoreDisplayFormatter.timestamp(
                    detail?.completionCallbackInvokedAt
                        ?? fallback.summary.completionCallbackInvokedAt
                )
            )
            TorrentCoreMacDetailRow(
                label: "Callback Error",
                value: TorrentCoreDisplayFormatter.operatorValue(
                    detail?.completionCallbackLastError
                        ?? fallback.summary.completionCallbackLastError
                )
            )
            TorrentCoreMacDetailRow(
                label: "Info Hash",
                value: detail?.infoHash ?? "--"
            )
            TorrentCoreMacCopyableDetailRow(
                label: "Torrent ID",
                value: fallback.summary.torrentID?.uuidString,
                accessibilityIdentifier: "torrents.copyTorrentID"
            )
            TorrentCoreMacDetailRow(
                label: "Error",
                value: TorrentCoreDisplayFormatter.operatorValue(
                    detail?.errorMessage ?? fallback.summary.errorMessage
                )
            )
        }
    }
}

enum TorrentCoreMacMagnetValidation {
    static let guidance = "Enter a valid magnet link that begins with magnet:? and includes an xt value."

    static func isValid(_ value: String) -> Bool {
        let trimmedValue = value.trimmingCharacters(in: .whitespacesAndNewlines)
        guard trimmedValue.lowercased().hasPrefix("magnet:?"),
              let components = URLComponents(string: trimmedValue),
              components.scheme?.lowercased() == "magnet"
        else {
            return false
        }

        return components.queryItems?.contains { queryItem in
            queryItem.name.lowercased() == "xt"
                && queryItem.value?.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty == false
        } == true
    }
}

struct TorrentCoreMacAddMagnetView: View {
    @Environment(\.dismiss) private var dismiss

    let session: TorrentCoreFeatureSession
    let added: (UUID?) -> Void

    @State private var magnetURI = ""
    @State private var selectedCategoryKey: String?
    @State private var isSubmitting = false
    @State private var errorMessage: String?
    @FocusState private var isMagnetURIFieldFocused: Bool

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            Text("Add Magnet")
                .font(.title2.weight(.semibold))

            TextEditor(text: $magnetURI)
                .font(.body.monospaced())
                .frame(minHeight: 130)
                .focused($isMagnetURIFieldFocused)
                .overlay {
                    RoundedRectangle(cornerRadius: 6)
                        .stroke(.separator)
                }
                .accessibilityLabel("Magnet URI")
                .accessibilityIdentifier("addMagnet.uri")

            Picker("Category", selection: $selectedCategoryKey) {
                Text("Uncategorized").tag(String?.none)
                ForEach(enabledCategories, id: \.key) { category in
                    Text(category.displayName ?? category.key ?? "Category")
                        .tag(category.key)
                }
            }
            .accessibilityIdentifier("addMagnet.category")

            categoryStatus

            if let errorMessage {
                Label(errorMessage, systemImage: "exclamationmark.triangle")
                    .foregroundStyle(.red)
                    .accessibilityIdentifier("addMagnet.error")
            } else if showsMagnetGuidance {
                Label(TorrentCoreMacMagnetValidation.guidance, systemImage: "exclamationmark.triangle")
                    .foregroundStyle(.red)
                    .accessibilityIdentifier("addMagnet.validation")
            }

            HStack {
                Button("Cancel", role: .cancel) {
                    dismiss()
                }
                .keyboardShortcut(.cancelAction)

                Spacer()

                Button("Add Magnet") {
                    submit()
                }
                .buttonStyle(.borderedProminent)
                .keyboardShortcut(.defaultAction)
                .disabled(
                    isSubmitting
                        || !session.canAddMagnet()
                        || validatedMagnetURI == nil
                )
                .accessibilityIdentifier("addMagnet.submit")
            }
        }
        .padding(22)
        .frame(width: 620)
        .interactiveDismissDisabled(isSubmitting)
        .onAppear {
            isMagnetURIFieldFocused = true
        }
        .onChange(of: magnetURI) {
            errorMessage = nil
        }
        .task(id: session.activeProfile?.id) {
            guard session.activeProfile != nil else {
                return
            }
            await session.refresh(.addMagnet)
        }
    }

    @ViewBuilder
    private var categoryStatus: some View {
        switch session.categories.phase {
        case .loading where session.categories.value == nil:
            HStack(spacing: 8) {
                ProgressView()
                    .controlSize(.small)
                Text("Loading categories…")
            }
            .foregroundStyle(.secondary)
            .accessibilityIdentifier("addMagnet.categories.loading")
        case .stale where session.categories.value == nil:
            Label(
                "Categories could not be loaded. You can still add the torrent as Uncategorized.",
                systemImage: "exclamationmark.triangle"
            )
            .foregroundStyle(.orange)
            .accessibilityIdentifier("addMagnet.categories.unavailable")
        case .stale:
            Label(
                "Categories could not be updated. Showing the last available values.",
                systemImage: "exclamationmark.triangle"
            )
            .foregroundStyle(.orange)
            .accessibilityIdentifier("addMagnet.categories.stale")
        case .idle, .loading, .current:
            EmptyView()
        }
    }

    private var validatedMagnetURI: String? {
        let trimmedMagnet = magnetURI.trimmingCharacters(in: .whitespacesAndNewlines)
        return TorrentCoreMacMagnetValidation.isValid(trimmedMagnet) ? trimmedMagnet : nil
    }

    private var showsMagnetGuidance: Bool {
        !magnetURI.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
            && validatedMagnetURI == nil
    }

    private var enabledCategories: [TorrentCoreCategory] {
        (session.categories.value ?? [])
            .filter { $0.enabled && $0.key?.isEmpty == false }
            .sorted {
                if $0.sortOrder == $1.sortOrder {
                    return ($0.displayName ?? $0.key ?? "")
                        .localizedCaseInsensitiveCompare(
                            $1.displayName ?? $1.key ?? ""
                        ) == .orderedAscending
                }
                return $0.sortOrder < $1.sortOrder
            }
    }

    private func submit() {
        guard let validatedMagnetURI else {
            errorMessage = TorrentCoreMacMagnetValidation.guidance
            return
        }

        isSubmitting = true
        errorMessage = nil
        Task {
            defer { isSubmitting = false }
            do {
                let detail = try await session.addMagnet(
                    validatedMagnetURI,
                    categoryKey: selectedCategoryKey
                )
                added(detail.torrentID)
                dismiss()
            } catch {
                errorMessage = TorrentCoreMacErrorPresenter.message(error)
            }
        }
    }
}
