import Foundation
import SwiftUI
import TorrentCoreAPI
import TorrentCoreFeatures

private enum TorrentCoreMacTorrentSortField: String {
    case name
    case category
    case state
    case progress
    case download
    case upload
    case peers
    case wait

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
        case .wait:
            KeyPathComparator(
                \TorrentCoreTorrentListItem.wait,
                comparator: .localizedStandard,
                order: order
            )
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
        case \TorrentCoreTorrentListItem.wait:
            .wait
        default:
            nil
        }
    }
}

struct TorrentCoreMacTorrentsView: View {
    @AppStorage("TorrentCore.Mac.Torrents.StateFilter.v1")
    private var storedStateFilter = ""
    @AppStorage("TorrentCore.Mac.Torrents.CategoryFilter.v1")
    private var storedCategoryFilter = ""
    @AppStorage("TorrentCore.Mac.Torrents.PageSize.v1")
    private var storedPageSize = TorrentCoreTorrentPageSize.defaultValue.rawValue
    @AppStorage("TorrentCore.Mac.Torrents.SortField.v1")
    private var storedSortField = TorrentCoreMacTorrentSortField.name.rawValue
    @AppStorage("TorrentCore.Mac.Torrents.SortDescending.v1")
    private var storedSortDescending = false

    let session: TorrentCoreFeatureSession
    @Binding var selectedTorrentID: UUID?
    @Binding var isInspectorPresented: Bool
    let contextChanged: () -> Void
    let showHistory: (UUID) -> Void
    let showLogs: (UUID) -> Void

    @State private var searchText = ""
    @State private var pageIndex = 0
    @State private var sortOrder: [KeyPathComparator<TorrentCoreTorrentListItem>]
    @State private var isAddMagnetPresented = false
    @State private var isActing = false
    @State private var actionError: String?
    @State private var actionMessage: String?
    @State private var pendingRemoval: PendingRemoval?
    @State private var isPeersPresented = false
    @State private var isTrackersPresented = false
    @State private var isMetadataResetConfirmationPresented = false
    @State private var isCallbackRetryConfirmationPresented = false

    init(
        session: TorrentCoreFeatureSession,
        selectedTorrentID: Binding<UUID?>,
        isInspectorPresented: Binding<Bool>,
        contextChanged: @escaping () -> Void,
        showHistory: @escaping (UUID) -> Void,
        showLogs: @escaping (UUID) -> Void
    ) {
        self.session = session
        _selectedTorrentID = selectedTorrentID
        _isInspectorPresented = isInspectorPresented
        self.contextChanged = contextChanged
        self.showHistory = showHistory
        self.showLogs = showLogs

        let defaults = UserDefaults.standard
        let storedField = defaults.string(
            forKey: "TorrentCore.Mac.Torrents.SortField.v1"
        )
        let field = TorrentCoreMacTorrentSortField(rawValue: storedField ?? "") ?? .name
        let descending = defaults.bool(
            forKey: "TorrentCore.Mac.Torrents.SortDescending.v1"
        )
        _sortOrder = State(initialValue: [field.comparator(descending: descending)])
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
            ToolbarItemGroup {
                Button {
                    isAddMagnetPresented = true
                } label: {
                    Label("Add Magnet", systemImage: "plus")
                }
                .disabled(!session.canAddMagnet() || isActing)
                .accessibilityIdentifier("torrents.add")

                Button {
                    pauseSelected()
                } label: {
                    Label("Pause", systemImage: "pause")
                }
                .disabled(!canPauseSelected || isActing)
                .accessibilityIdentifier("torrents.pause")

                Button {
                    resumeSelected()
                } label: {
                    Label("Resume", systemImage: "play")
                }
                .disabled(!canResumeSelected || isActing)
                .accessibilityIdentifier("torrents.resume")

                Button {
                    isInspectorPresented.toggle()
                    contextChanged()
                } label: {
                    Label("Inspector", systemImage: "sidebar.trailing")
                }
                .disabled(selectedTorrentID == nil)
                .accessibilityIdentifier("torrents.inspector")
            }
        }
        .inspector(isPresented: $isInspectorPresented) {
            TorrentCoreMacTorrentInspector(
                session: session,
                selectedItem: selectedItem,
                isActing: isActing,
                pause: pauseSelected,
                resume: resumeSelected,
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
            .inspectorColumnWidth(min: 320, ideal: 390, max: 520)
        }
        .sheet(isPresented: $isAddMagnetPresented) {
            TorrentCoreMacAddMagnetView(session: session) { newTorrentID in
                selectedTorrentID = newTorrentID
                isInspectorPresented = true
                actionMessage = "Magnet added to TorrentCore."
            }
        }
        .sheet(isPresented: $isPeersPresented) {
            if let selectedItem, let torrentID = selectedItem.summary.torrentID {
                TorrentCoreMacPeersSheet(
                    session: session,
                    torrentID: torrentID,
                    torrentName: selectedItem.name,
                    restoreContext: contextChanged
                )
            }
        }
        .sheet(isPresented: $isTrackersPresented) {
            if let selectedItem, let torrentID = selectedItem.summary.torrentID {
                TorrentCoreMacTrackersSheet(
                    session: session,
                    torrentID: torrentID,
                    torrentName: selectedItem.name,
                    restoreContext: contextChanged
                )
            }
        }
        .onChange(of: isAddMagnetPresented) { _, isPresented in
            if isPresented {
                session.setContext(.addMagnet)
            } else {
                contextChanged()
            }
        }
        .onChange(of: selectedTorrentID) { _, newValue in
            if newValue != nil {
                isInspectorPresented = true
            }
            contextChanged()
        }
        .onChange(of: isInspectorPresented) { _, _ in
            contextChanged()
        }
        .onChange(of: session.torrents.value) { _, summaries in
            guard let selectedTorrentID else {
                return
            }
            if summaries?.contains(where: { $0.torrentID == selectedTorrentID }) != true {
                self.selectedTorrentID = nil
                isInspectorPresented = false
            }
        }
        .onChange(of: sortOrder) { _, newValue in
            guard let comparator = newValue.first,
                  let field = TorrentCoreMacTorrentSortField.field(for: comparator.keyPath)
            else {
                return
            }
            storedSortField = field.rawValue
            storedSortDescending = comparator.order == .reverse
            pageIndex = 0
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
    }

    private var filterBar: some View {
        HStack(spacing: 12) {
            TextField("Search by name", text: $searchText)
                .textFieldStyle(.roundedBorder)
                .frame(minWidth: 180, idealWidth: 260, maxWidth: 340)
                .accessibilityIdentifier("torrents.search")
                .onChange(of: searchText) { _, _ in
                    pageIndex = 0
                }

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
            }

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
            }

            Button("Clear") {
                searchText = ""
                storedStateFilter = ""
                storedCategoryFilter = ""
                pageIndex = 0
            }
            .disabled(
                searchText.isEmpty
                    && storedStateFilter.isEmpty
                    && storedCategoryFilter.isEmpty
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
    }

    private var torrentTable: some View {
        Table(
            currentPage.values,
            selection: tableSelection,
            sortOrder: $sortOrder
        ) {
            TableColumn(
                "Name",
                value: \.name,
                comparator: .localizedStandard
            ) { item in
                Text(item.name)
                    .lineLimit(2)
                    .contextMenu {
                        Button("Show Details") {
                            selectedTorrentID = item.summary.torrentID
                            isInspectorPresented = true
                            contextChanged()
                        }
                        if let torrentID = item.summary.torrentID {
                            Button("Show History") {
                                showHistory(torrentID)
                            }
                            Button("Show Filtered Logs") {
                                showLogs(torrentID)
                            }
                        }
                    }
            }
            .width(min: 220, ideal: 340)

            TableColumn(
                "State",
                value: \.state,
                comparator: .localizedStandard
            ) { item in
                Text(TorrentCoreDisplayFormatter.state(item.summary.state))
            }
            .width(min: 100, ideal: 135)

            TableColumn("Progress", value: \.progress) { item in
                HStack(spacing: 6) {
                    ProgressView(value: item.progress, total: 100)
                    Text(TorrentCoreDisplayFormatter.percent(item.progress))
                        .monospacedDigit()
                        .frame(width: 48, alignment: .trailing)
                }
            }
            .width(min: 130, ideal: 170)

            TableColumn("Download", value: \.downloadRate) { item in
                Text(TorrentCoreDisplayFormatter.rate(item.downloadRate))
                    .monospacedDigit()
            }
            .width(min: 90, ideal: 110)

            TableColumn("Upload", value: \.uploadRate) { item in
                Text(TorrentCoreDisplayFormatter.rate(item.uploadRate))
                    .monospacedDigit()
            }
            .width(min: 90, ideal: 110)

            TableColumn("Peers", value: \.peers) { item in
                Text(item.peers.formatted())
                    .monospacedDigit()
            }
            .width(min: 55, ideal: 70)

            TableColumn(
                "Category",
                value: \.category,
                comparator: .localizedStandard
            ) { item in
                Text(TorrentCoreDisplayFormatter.category(item.summary.categoryKey))
            }
            .width(min: 90, ideal: 120)

            TableColumn(
                "Wait",
                value: \.wait,
                comparator: .localizedStandard
            ) { item in
                Text(item.wait)
                    .lineLimit(2)
            }
            .width(min: 120, ideal: 170)
        }
        .accessibilityIdentifier("torrents.table")
    }

    private var paginationBar: some View {
        HStack {
            Text(resultRangeLabel)
                .foregroundStyle(.secondary)

            Spacer()

            Picker("Rows", selection: pageSizeBinding) {
                ForEach(TorrentCoreTorrentPageSize.allCases, id: \.self) { size in
                    Text(size.rawValue.formatted()).tag(size)
                }
            }
            .pickerStyle(.menu)
            .fixedSize()
            .accessibilityIdentifier("torrents.pageSize")

            Button {
                pageIndex = max(0, currentPage.pageIndex - 1)
            } label: {
                Label("Previous Page", systemImage: "chevron.left")
                    .labelStyle(.iconOnly)
            }
            .disabled(currentPage.pageIndex == 0)
            .accessibilityIdentifier("torrents.previousPage")

            Text("Page \(currentPage.pageIndex + 1) of \(currentPage.pageCount)")
                .monospacedDigit()
                .frame(minWidth: 105)

            Button {
                pageIndex = min(currentPage.pageCount - 1, currentPage.pageIndex + 1)
            } label: {
                Label("Next Page", systemImage: "chevron.right")
                    .labelStyle(.iconOnly)
            }
            .disabled(currentPage.pageIndex >= currentPage.pageCount - 1)
            .accessibilityIdentifier("torrents.nextPage")
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
    }

    private var emptyTorrentList: some View {
        ContentUnavailableView {
            Label("No Torrents", systemImage: "tray")
        } description: {
            Text("The service is connected and has no torrents.")
        } actions: {
            Button("Add Magnet") {
                isAddMagnetPresented = true
            }
            .disabled(!session.canAddMagnet())
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
                    Task {
                        await session.refresh()
                    }
                }
            }
        }
        .accessibilityIdentifier("torrents.unavailable")
    }

    private var allSummaries: [TorrentCoreTorrentSummary] {
        session.torrents.value ?? []
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
            category: categoryFilter
        )
        return filter.apply(to: allSummaries)
            .map(TorrentCoreTorrentListItem.init)
            .sorted(using: sortOrder)
    }

    private var currentPage: TorrentCoreTorrentPage<TorrentCoreTorrentListItem> {
        TorrentCorePagination.page(
            filteredAndSortedItems,
            index: pageIndex,
            size: pageSize
        )
    }

    private var pageSize: TorrentCoreTorrentPageSize {
        TorrentCoreTorrentPageSize(rawValue: storedPageSize) ?? .defaultValue
    }

    private var pageSizeBinding: Binding<TorrentCoreTorrentPageSize> {
        Binding(
            get: { pageSize },
            set: { newValue in
                storedPageSize = newValue.rawValue
                pageIndex = 0
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

    private var resultRangeLabel: String {
        guard currentPage.totalCount > 0 else {
            return "0 torrents"
        }
        let first = currentPage.pageIndex * pageSize.rawValue + 1
        let last = first + currentPage.values.count - 1
        return "\(first)–\(last) of \(currentPage.totalCount)"
    }

    private var unavailableTitle: String {
        switch session.connectionState {
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
        switch session.connectionState {
        case .offline:
            "network.slash"
        case .connecting:
            "arrow.trianglehead.2.clockwise"
        case .noProfile, .notConnected, .connected:
            "tray"
        }
    }

    private var unavailableMessage: String {
        switch session.connectionState {
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
    let pause: () -> Void
    let resume: () -> Void
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
                            Text(selectedItem.name)
                                .font(.title2.weight(.semibold))
                                .textSelection(.enabled)
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
                            Button("Resume", action: resume)
                                .disabled(!session.canResume(selectedItem.summary) || isActing)
                                .accessibilityIdentifier("inspector.resume")
                        }

                        HStack {
                            Button("Peers", action: showPeers)
                                .disabled(
                                    selectedItem.summary.torrentID == nil
                                        || !session.connectionState.isConnected
                                )
                                .accessibilityIdentifier("inspector.peers")
                            Button("Trackers", action: showTrackers)
                                .disabled(
                                    selectedItem.summary.torrentID == nil
                                        || !session.connectionState.isConnected
                                )
                                .accessibilityIdentifier("inspector.trackers")
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
                            Button("Reset Metadata Session", action: requestMetadataReset)
                                .disabled(
                                    !session.canResetMetadataSession(selectedItem.summary)
                                        || isActing
                                )
                                .accessibilityIdentifier("inspector.resetMetadata")
                            Button("Retry Completion Callback", action: requestCallbackRetry)
                                .disabled(
                                    !session.canRetryCompletionCallback(selectedItem.summary)
                                        || isActing
                                )
                                .accessibilityIdentifier("inspector.retryCallback")
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

                            Button("Remove & Delete Data", role: .destructive) {
                                requestRemoval(true)
                            }
                            .disabled(!session.canRemove(selectedItem.summary) || isActing)
                            .accessibilityIdentifier("inspector.deleteData")
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
                label: "Wait",
                value: TorrentCoreDisplayFormatter.wait(
                    detail?.waitReason ?? fallback.summary.waitReason,
                    queue: detail?.queuePosition ?? fallback.summary.queuePosition
                )
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
                label: "Info Hash",
                value: detail?.infoHash ?? "—"
            )
            TorrentCoreMacDetailRow(
                label: "Torrent ID",
                value: fallback.summary.torrentID?.uuidString ?? "—"
            )
            if let error = detail?.errorMessage ?? fallback.summary.errorMessage,
               !error.isEmpty
            {
                TorrentCoreMacDetailRow(label: "Error", value: error)
            }
        }
    }
}

private struct TorrentCoreMacAddMagnetView: View {
    @Environment(\.dismiss) private var dismiss

    let session: TorrentCoreFeatureSession
    let added: (UUID?) -> Void

    @State private var magnetURI = ""
    @State private var selectedCategoryKey: String?
    @State private var isSubmitting = false
    @State private var errorMessage: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            Text("Add Magnet")
                .font(.title2.weight(.semibold))

            TextEditor(text: $magnetURI)
                .font(.body.monospaced())
                .frame(minHeight: 130)
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

            TorrentCoreMacPhaseBanner(
                phase: session.categories.phase,
                lastSuccessfulAt: session.categories.lastSuccessfulAt
            )

            if let errorMessage {
                Label(errorMessage, systemImage: "exclamationmark.triangle")
                    .foregroundStyle(.red)
                    .accessibilityIdentifier("addMagnet.error")
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
                        || magnetURI.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
                )
                .accessibilityIdentifier("addMagnet.submit")
            }
        }
        .padding(22)
        .frame(width: 620)
        .interactiveDismissDisabled(isSubmitting)
        .onAppear {
            if session.categories.value == nil {
                Task {
                    await session.refresh()
                }
            }
        }
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
        isSubmitting = true
        errorMessage = nil
        let trimmedMagnet = magnetURI.trimmingCharacters(in: .whitespacesAndNewlines)
        Task {
            defer { isSubmitting = false }
            do {
                let detail = try await session.addMagnet(
                    trimmedMagnet,
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
