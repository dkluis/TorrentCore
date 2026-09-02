import Foundation
import SwiftUI
import TorrentCoreAPI
import TorrentCoreFeatures

private struct TorrentCoreMacLogTableItem: Identifiable {
    let log: TorrentCoreActivityLogEntry

    var id: Int64 { log.logEntryID }
    var occurredAt: Date { log.occurredAt }
    var level: String { log.level ?? "--" }
    var category: String { log.category ?? "--" }
    var eventType: String { log.eventType ?? "--" }
    var message: String { log.message ?? "--" }
    var logEntryID: Int64 { log.logEntryID }
    var torrentID: String { log.torrentID?.uuidString ?? "" }
    var serviceInstanceID: String { log.serviceInstanceID?.uuidString ?? "" }
    var traceID: String { log.traceID ?? "" }
    var detailsJSON: String { log.detailsJSON ?? "" }
}

enum TorrentCoreMacLogSortField: String, CaseIterable, Codable, Identifiable {
    case occurredAt
    case level
    case category
    case eventType
    case message
    case logEntryID
    case torrentID
    case serviceInstanceID
    case traceID
    case detailsJSON

    var id: Self { self }

    var title: String {
        switch self {
        case .occurredAt: "When"
        case .level: "Level"
        case .category: "Category"
        case .eventType: "Event"
        case .message: "Message"
        case .logEntryID: "Log ID"
        case .torrentID: "Torrent ID"
        case .serviceInstanceID: "Service Instance ID"
        case .traceID: "Trace ID"
        case .detailsJSON: "Details JSON"
        }
    }

    fileprivate func comparator(descending: Bool) -> KeyPathComparator<TorrentCoreMacLogTableItem> {
        let order: SortOrder = descending ? .reverse : .forward
        return switch self {
        case .occurredAt:
            KeyPathComparator(\.occurredAt, order: order)
        case .level:
            KeyPathComparator(\.level, comparator: .localizedStandard, order: order)
        case .category:
            KeyPathComparator(\.category, comparator: .localizedStandard, order: order)
        case .eventType:
            KeyPathComparator(\.eventType, comparator: .localizedStandard, order: order)
        case .message:
            KeyPathComparator(\.message, comparator: .localizedStandard, order: order)
        case .logEntryID:
            KeyPathComparator(\.logEntryID, order: order)
        case .torrentID:
            KeyPathComparator(\.torrentID, comparator: .localizedStandard, order: order)
        case .serviceInstanceID:
            KeyPathComparator(\.serviceInstanceID, comparator: .localizedStandard, order: order)
        case .traceID:
            KeyPathComparator(\.traceID, comparator: .localizedStandard, order: order)
        case .detailsJSON:
            KeyPathComparator(\.detailsJSON, comparator: .localizedStandard, order: order)
        }
    }

    fileprivate static func field(for keyPath: PartialKeyPath<TorrentCoreMacLogTableItem>) -> Self? {
        switch keyPath {
        case \TorrentCoreMacLogTableItem.occurredAt: .occurredAt
        case \TorrentCoreMacLogTableItem.level: .level
        case \TorrentCoreMacLogTableItem.category: .category
        case \TorrentCoreMacLogTableItem.eventType: .eventType
        case \TorrentCoreMacLogTableItem.message: .message
        case \TorrentCoreMacLogTableItem.logEntryID: .logEntryID
        case \TorrentCoreMacLogTableItem.torrentID: .torrentID
        case \TorrentCoreMacLogTableItem.serviceInstanceID: .serviceInstanceID
        case \TorrentCoreMacLogTableItem.traceID: .traceID
        case \TorrentCoreMacLogTableItem.detailsJSON: .detailsJSON
        default: nil
        }
    }
}

private enum TorrentCoreMacLogColumn: String, CaseIterable, Identifiable {
    case when, level, category, event, message
    case logEntryID, torrentID, serviceInstanceID, traceID, detailsJSON

    var id: String { rawValue }
    var title: String {
        switch self {
        case .when: "When"
        case .level: "Level"
        case .category: "Category"
        case .event: "Event"
        case .message: "Message"
        case .logEntryID: "Log ID"
        case .torrentID: "Torrent ID"
        case .serviceInstanceID: "Service Instance ID"
        case .traceID: "Trace ID"
        case .detailsJSON: "Details JSON"
        }
    }
    var canHide: Bool { self != .message }
    var isDefaultVisible: Bool {
        switch self {
        case .when, .level, .category, .event, .message:
            true
        default:
            false
        }
    }
}

struct TorrentCoreMacLogsView: View {
    static let defaultQuery = TorrentCoreLogQuery(take: 1_000)

    let session: TorrentCoreFeatureSession
    @Binding var query: TorrentCoreLogQuery
    @Binding var selectedLogID: Int64?
    @Binding var isInspectorPresented: Bool
    let contextChanged: () -> Void
    let showTorrent: (UUID) -> Void
    let showHistory: (UUID) -> Void

    @AppStorage("TorrentCore.Mac.Logs.PageSize.v1")
    private var storedPageSize = TorrentCoreMacTableSupport.defaultPageSize
    @AppStorage("TorrentCore.Mac.Logs.Sort.v2") private var storedSort = ""
    @AppStorage("TorrentCore.Mac.Logs.Columns.v1")
    private var columnCustomization =
        TableColumnCustomization<TorrentCoreMacLogTableItem>()
    @AppStorage("TorrentCore.Mac.Logs.OverlayWidth.v1") private var overlayWidth = 420.0

    @State private var searchText = ""
    @State private var levelFilter: TorrentCoreActivityLogLevel?
    @State private var categoryFilter: String
    @State private var eventTypeFilter: String
    @State private var torrentIDFilter: String
    @State private var serviceInstanceIDFilter: String
    @State private var includesFromDate: Bool
    @State private var includesToDate: Bool
    @State private var fromDate: Date
    @State private var toDate: Date
    @State private var isDeleteConfirmationPresented = false
    @State private var actionMessage: String?
    @State private var actionError: String?
    @State private var pageIndex = 0
    @State private var sortDescriptors: [
        TorrentCoreMacSortDescriptor<TorrentCoreMacLogSortField>
    ]
    @State private var isSortEditorPresented = false
    @State private var notice: TorrentCoreMacNotice?

    init(
        session: TorrentCoreFeatureSession,
        query: Binding<TorrentCoreLogQuery>,
        selectedLogID: Binding<Int64?>,
        isInspectorPresented: Binding<Bool>,
        contextChanged: @escaping () -> Void,
        showTorrent: @escaping (UUID) -> Void,
        showHistory: @escaping (UUID) -> Void
    ) {
        self.session = session
        _query = query
        _selectedLogID = selectedLogID
        _isInspectorPresented = isInspectorPresented
        self.contextChanged = contextChanged
        self.showTorrent = showTorrent
        self.showHistory = showHistory
        let initial = query.wrappedValue
        _levelFilter = State(initialValue: initial.level)
        _categoryFilter = State(initialValue: initial.category ?? "")
        _eventTypeFilter = State(initialValue: initial.eventType ?? "")
        _torrentIDFilter = State(initialValue: initial.torrentID?.uuidString ?? "")
        _serviceInstanceIDFilter = State(
            initialValue: initial.serviceInstanceID?.uuidString ?? ""
        )
        _includesFromDate = State(initialValue: initial.fromUTC != nil)
        _includesToDate = State(initialValue: initial.toUTC != nil)
        _fromDate = State(initialValue: initial.fromUTC ?? Date())
        _toDate = State(initialValue: initial.toUTC ?? Date())
        let defaults = UserDefaults.standard
        let stored = defaults.string(forKey: "TorrentCore.Mac.Logs.Sort.v2") ?? ""
        let storedField = defaults.string(forKey: "TorrentCore.Mac.Logs.SortField.v1")
        let field = TorrentCoreMacLogSortField(rawValue: storedField ?? "")
            ?? .occurredAt
        let descending = defaults.object(
            forKey: "TorrentCore.Mac.Logs.SortDescending.v1"
        ) as? Bool ?? true
        _sortDescriptors = State(
            initialValue: TorrentCoreMacSortStorage.decode(
                stored,
                as: TorrentCoreMacLogSortField.self
            ) ?? [.init(field: field, descending: descending)]
        )
    }

    var body: some View {
        VStack(spacing: 0) {
            filterBar
            Divider()

            TorrentCoreMacPhaseBanner(
                phase: session.logs.phase,
                lastSuccessfulAt: session.logs.lastSuccessfulAt
            )
            .padding(.horizontal, 12)
            .padding(.top, 8)

            if isLimited {
                Label(
                    "Showing the most recent \(query.take.formatted()) log entries. Narrow the server filters to inspect older activity.",
                    systemImage: "ellipsis.rectangle"
                )
                .font(.caption)
                .foregroundStyle(.secondary)
                .padding(.horizontal, 12)
                .padding(.vertical, 8)
                .frame(maxWidth: .infinity, alignment: .leading)
                .accessibilityIdentifier("logs.limitNotice")
            }

            if session.logs.value == nil {
                ContentUnavailableView {
                    Label(unavailableTitle, systemImage: unavailableSystemImage)
                } description: {
                    Text(unavailableMessage)
                } actions: {
                    if case .loading = session.logs.phase {
                        ProgressView()
                            .controlSize(.small)
                    }
                }
            } else if sortedLogItems.isEmpty {
                ContentUnavailableView(
                    "No Logs Match",
                    systemImage: "line.3.horizontal.decrease.circle",
                    description: Text("Adjust the server filters or local search.")
                )
            } else {
                logTable
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
            logInspector
        }
        .torrentCoreToast(notice: $notice)
        .onChange(of: selectedLogID) { _, value in
            isInspectorPresented = value != nil
        }
        .onChange(of: searchText) { _, _ in
            pageIndex = 0
            reconcileSelectionWithCurrentPage()
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
        .onChange(of: session.logs.value) { _, _ in
            clampPageAndReconcileSelection()
        }
        .onChange(of: sortDescriptors) { _, descriptors in
            storedSort = TorrentCoreMacSortStorage.encode(descriptors)
            pageIndex = 0
            reconcileSelectionWithCurrentPage()
        }
        .confirmationDialog(
            "Delete Orphaned Torrent Logs?",
            isPresented: $isDeleteConfirmationPresented,
            titleVisibility: .visible
        ) {
            Button("Delete Orphaned Logs", role: .destructive) {
                deleteOrphanedLogs()
            }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text(
                "Delete log entries associated with torrents that no longer have a current or historical torrent record? Other service logs are kept."
            )
        }
        .alert(
            "Log Action Failed",
            isPresented: Binding(
                get: { actionError != nil },
                set: { if !$0 { actionError = nil } }
            )
        ) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(actionError ?? "TorrentCore could not complete the log action.")
        }
        .overlay(alignment: .bottom) {
            if let actionMessage {
                Text(actionMessage)
                    .padding(10)
                    .background(.regularMaterial, in: Capsule())
                    .padding()
                    .onTapGesture { self.actionMessage = nil }
            }
        }
        .task(id: session.activeProfile?.id) {
            await session.refreshActivityLogFilterOptions()
        }
        .torrentCoreRefreshWhileVisible(
            session: session,
            context: .logs(query)
        )
    }

    private var filterBar: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(alignment: .bottom, spacing: 10) {
                VStack(alignment: .leading, spacing: 3) {
                    TorrentCoreMacHelpLabel(
                        "Search",
                        content: TorrentCoreHelpCatalog.Logs.searchMessage
                    )
                    TextField("Search loaded logs", text: $searchText)
                        .textFieldStyle(.roundedBorder)
                        .frame(width: 180)
                }
                VStack(alignment: .leading, spacing: 3) {
                    TorrentCoreMacHelpLabel(content: TorrentCoreHelpCatalog.Logs.level)
                    Picker("Level", selection: $levelFilter) {
                        Text("All Levels").tag(TorrentCoreActivityLogLevel?.none)
                        Text("Debug").tag(TorrentCoreActivityLogLevel?.some(.debug))
                        Text("Information").tag(TorrentCoreActivityLogLevel?.some(.information))
                        Text("Warning").tag(TorrentCoreActivityLogLevel?.some(.warning))
                        Text("Error").tag(TorrentCoreActivityLogLevel?.some(.error))
                        Text("Critical").tag(TorrentCoreActivityLogLevel?.some(.critical))
                    }
                    .labelsHidden()
                    .frame(width: 150)
                }
                VStack(alignment: .leading, spacing: 3) {
                    TorrentCoreMacHelpLabel(content: TorrentCoreHelpCatalog.Logs.category)
                    Picker("Category", selection: $categoryFilter) {
                        Text("All Categories").tag("")
                        ForEach(logCategoryOptions, id: \.self) {
                            Text($0).tag($0)
                        }
                    }
                    .labelsHidden()
                    .frame(width: 170)
                }
                VStack(alignment: .leading, spacing: 3) {
                    TorrentCoreMacHelpLabel(content: TorrentCoreHelpCatalog.Logs.eventType)
                    Picker("Event Type", selection: $eventTypeFilter) {
                        Text("All Event Types").tag("")
                        ForEach(logEventTypeOptions, id: \.self) {
                            Text($0).tag($0)
                        }
                    }
                    .labelsHidden()
                    .frame(width: 190)
                }
                VStack(alignment: .leading, spacing: 3) {
                    TorrentCoreMacHelpLabel(
                        "Recent",
                        content: TorrentCoreHelpCatalog.Logs.recentLimit
                    )
                    Picker("Recent", selection: limitBinding) {
                        Text("100 rows").tag(100)
                        Text("500 rows").tag(500)
                        Text("1,000 rows").tag(1_000)
                        Text("5,000 rows").tag(5_000)
                    }
                    .labelsHidden()
                    .frame(width: 130)
                }

                Button("Search", action: applyFilters)
                    .buttonStyle(.borderedProminent)
                    .keyboardShortcut(.return, modifiers: [])
                    .accessibilityIdentifier("logs.search")

                Button("Clear Filters", action: clearFilters)
                    .accessibilityIdentifier("logs.clearFilters")

                Button("Reset Filters", action: resetFilters)
                    .accessibilityIdentifier("logs.resetFilters")

                Spacer(minLength: 0)
            }

            HStack(alignment: .bottom, spacing: 10) {
                VStack(alignment: .leading, spacing: 3) {
                    TorrentCoreMacHelpLabel(content: TorrentCoreHelpCatalog.Logs.torrentID)
                    TextField("Torrent ID", text: $torrentIDFilter)
                        .textFieldStyle(.roundedBorder)
                        .frame(width: 200)
                }
                VStack(alignment: .leading, spacing: 3) {
                    TorrentCoreMacHelpLabel(
                        content: TorrentCoreHelpCatalog.Logs.serviceInstanceID
                    )
                    TextField("Service instance ID", text: $serviceInstanceIDFilter)
                        .textFieldStyle(.roundedBorder)
                        .frame(width: 200)
                }

                VStack(alignment: .leading, spacing: 3) {
                    TorrentCoreMacHelpLabel(
                        "From",
                        content: TorrentCoreHelpCatalog.Logs.fromDateTime
                    )
                    HStack(spacing: 4) {
                        Toggle("", isOn: $includesFromDate)
                            .labelsHidden()
                            .toggleStyle(.checkbox)
                        DatePicker("", selection: $fromDate)
                            .labelsHidden()
                            .disabled(!includesFromDate)
                    }
                }

                VStack(alignment: .leading, spacing: 3) {
                    TorrentCoreMacHelpLabel(
                        "To",
                        content: TorrentCoreHelpCatalog.Logs.toDateTime
                    )
                    HStack(spacing: 4) {
                        Toggle("", isOn: $includesToDate)
                            .labelsHidden()
                            .toggleStyle(.checkbox)
                        DatePicker("", selection: $toDate)
                            .labelsHidden()
                            .disabled(!includesToDate)
                    }
                }

                Button(role: .destructive) {
                    isDeleteConfirmationPresented = true
                } label: {
                    Label("Delete Orphaned", systemImage: "trash")
                }
                .disabled(!session.connectionState.isConnected || session.activeMutation != nil)
                .help(TorrentCoreHelpCatalog.Logs.deleteOrphaned.summary)

                Spacer(minLength: 0)
            }
        }
        .padding(12)
        .tint(.orange)
    }

    private var logTable: some View {
        Table(
            currentPage,
            selection: $selectedLogID,
            sortOrder: tableSortOrder,
            columnCustomization: $columnCustomization
        ) {
            TableColumn(sortHeaderTitle(.occurredAt), value: \.occurredAt) {
                Text(TorrentCoreDisplayFormatter.timestamp($0.log.occurredAt))
                    .monospacedDigit()
            }
            .width(min: 155, ideal: 175)
            .defaultVisibility(.visible)
            .customizationID("when")

            TableColumn(sortHeaderTitle(.level), value: \.level, comparator: .localizedStandard) {
                Text($0.level)
                    .foregroundStyle(color(for: $0.log.level))
                    .accessibilityIdentifier("logs.row")
            }
            .width(min: 95, ideal: 110)
            .defaultVisibility(.visible)
            .customizationID("level")

            TableColumn(sortHeaderTitle(.category), value: \.category, comparator: .localizedStandard) {
                Text($0.category)
            }
            .width(min: 125, ideal: 155)
            .defaultVisibility(.visible)
            .customizationID("category")

            TableColumn(
                sortHeaderTitle(.eventType),
                value: \.eventType,
                comparator: .localizedStandard
            ) { item in
                Text(item.eventType)
                    .lineLimit(2)
                    .contextMenu {
                        Button("Inspect Log") {
                            selectedLogID = item.log.logEntryID
                        }
                        if let torrentID = item.log.torrentID {
                            Button("Show Torrent") {
                                showTorrent(torrentID)
                            }
                            Button("Show History") {
                                showHistory(torrentID)
                            }
                        }
                    }
            }
            .width(min: 180, ideal: 230)
            .defaultVisibility(.visible)
            .customizationID("event")

            TableColumn(sortHeaderTitle(.message), value: \.message, comparator: .localizedStandard) {
                Text($0.message)
                    .lineLimit(2)
            }
            .width(min: 300, ideal: 520)
            .defaultVisibility(.visible)
            .disabledCustomizationBehavior(.visibility)
            .customizationID("message")

            TableColumn(sortHeaderTitle(.logEntryID), value: \.logEntryID) {
                Text($0.logEntryID.formatted()).monospacedDigit()
            }
            .width(min: 80, ideal: 100)
            .defaultVisibility(.hidden)
            .customizationID("logEntryID")

            TableColumn(sortHeaderTitle(.torrentID), value: \.torrentID, comparator: .localizedStandard) {
                Text(TorrentCoreDisplayFormatter.operatorValue($0.log.torrentID?.uuidString))
            }
            .width(min: 220, ideal: 285)
            .defaultVisibility(.hidden)
            .customizationID("torrentID")

            TableColumn(
                sortHeaderTitle(.serviceInstanceID),
                value: \.serviceInstanceID,
                comparator: .localizedStandard
            ) {
                Text(TorrentCoreDisplayFormatter.operatorValue($0.log.serviceInstanceID?.uuidString))
            }
            .width(min: 220, ideal: 285)
            .defaultVisibility(.hidden)
            .customizationID("serviceInstanceID")

            TableColumn(sortHeaderTitle(.traceID), value: \.traceID, comparator: .localizedStandard) {
                Text(TorrentCoreDisplayFormatter.operatorValue($0.log.traceID))
            }
            .width(min: 160, ideal: 240)
            .defaultVisibility(.hidden)
            .customizationID("traceID")

            TableColumn(sortHeaderTitle(.detailsJSON), value: \.detailsJSON, comparator: .localizedStandard) {
                Text(TorrentCoreDisplayFormatter.operatorValue($0.log.detailsJSON))
                    .font(.body.monospaced())
                    .lineLimit(2)
            }
            .width(min: 220, ideal: 420)
            .defaultVisibility(.hidden)
            .customizationID("detailsJSON")
        }
        .accessibilityIdentifier("logs.table")
    }

    private var paginationBar: some View {
        TorrentCoreMacPaginationBar(
            resultCount: sortedLogItems.count,
            pageIndex: $pageIndex,
            pageSize: pageSizeBinding,
            accessibilityPrefix: "logs"
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
        .accessibilityIdentifier("logs.sort")
    }

    private var columnsMenu: some View {
        Menu("Columns", systemImage: "rectangle.3.group") {
            ForEach(TorrentCoreMacLogColumn.allCases) { column in
                if column.canHide {
                    Toggle(column.title, isOn: columnVisibility(column))
                }
            }
            Divider()
            Button("Show All Columns") {
                for column in TorrentCoreMacLogColumn.allCases {
                    columnCustomization[visibility: column.id] = .visible
                }
            }
            Button("Restore Default Columns") {
                for column in TorrentCoreMacLogColumn.allCases {
                    columnCustomization[visibility: column.id] = .automatic
                }
            }
            Divider()
            Button("Reset Table Layout") {
                columnCustomization = .init()
            }
        }
        .accessibilityIdentifier("logs.columns")
    }

    private var exportMenu: some View {
        Menu("Export", systemImage: "square.and.arrow.up") {
            Button(selectedLog == nil ? "Export Selected Row" : "Export Selected Row (1)") {
                export(.selected)
            }
            .disabled(selectedLog == nil)
            Button("Export All Results (\(sortedLogItems.count.formatted()))") {
                export(.all)
            }
            .disabled(sortedLogItems.isEmpty)
        }
        .accessibilityIdentifier("logs.export")
    }

    private func sortHeaderTitle(_ field: TorrentCoreMacLogSortField) -> String {
        guard let index = sortDescriptors.firstIndex(where: { $0.field == field }) else {
            return field.title
        }
        return "\(field.title) \(sortDescriptors[index].descending ? "↓" : "↑")\(index + 1)"
    }

    private func columnVisibility(_ column: TorrentCoreMacLogColumn) -> Binding<Bool> {
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
        let logs = scope.rows(
            selected: selectedLog,
            all: sortedLogItems.map(\.log)
        )
        guard !logs.isEmpty else { return }
        do {
            let fileURL = try TorrentCoreMacTableExport.write(
                headers: Self.exportHeaders,
                rows: logs.map(Self.exportRow),
                fileName: "logs-\(scope.rawValue)-\(TorrentCoreMacTableExport.timestamp()).csv"
            )
            notice = .init(
                kind: .success,
                message: "Exported \(logs.count.formatted()) row\(logs.count == 1 ? "" : "s") to Downloads/\(fileURL.lastPathComponent)."
            )
        } catch {
            notice = .init(
                kind: .error,
                message: "Export failed: \(TorrentCoreMacErrorPresenter.message(error))"
            )
        }
    }

    private var logInspector: some View {
        ScrollView {
            if let log = selectedLog {
                VStack(alignment: .leading, spacing: 12) {
                    HStack {
                        Text(log.eventType ?? "Log Entry")
                            .font(.title2.weight(.semibold))
                        TorrentCoreMacHelpButton(
                            content: TorrentCoreHelpCatalog.Logs.selectedEntry
                        )
                    }
                    .padding(.trailing, 36)
                    Text(log.message ?? "No message")
                        .textSelection(.enabled)
                    Divider()
                    TorrentCoreMacDetailRow(
                        label: "Occurred",
                        value: TorrentCoreDisplayFormatter.timestamp(log.occurredAt)
                    )
                    TorrentCoreMacDetailRow(label: "Level", value: log.level ?? "--")
                    TorrentCoreMacDetailRow(label: "Category", value: log.category ?? "--")
                    TorrentCoreMacCopyableDetailRow(
                        label: "Torrent ID",
                        value: log.torrentID?.uuidString,
                        accessibilityIdentifier: "logs.copyTorrentID"
                    )
                    TorrentCoreMacCopyableDetailRow(
                        label: "Service Instance ID",
                        value: log.serviceInstanceID?.uuidString,
                        accessibilityIdentifier: "logs.copyServiceInstanceID"
                    )
                    TorrentCoreMacDetailRow(label: "Trace ID", value: log.traceID ?? "--")
                    if let torrentID = log.torrentID {
                        Divider()
                        HStack {
                            Button {
                                showTorrent(torrentID)
                            } label: {
                                Label("Show Torrent", systemImage: "arrow.down.circle")
                            }
                            Button {
                                showHistory(torrentID)
                            } label: {
                                Label("Show History", systemImage: "clock")
                            }
                        }
                    }
                    if let details = log.detailsJSON, !details.isEmpty {
                        Divider()
                        Text("Details")
                            .font(.headline)
                        Text(details)
                            .font(.body.monospaced())
                            .textSelection(.enabled)
                    }
                }
                .padding(16)
            }
        }
        .accessibilityIdentifier("logs.inspector.content")
        .overlay(alignment: .topTrailing) {
            Button("Close", systemImage: "xmark") {
                isInspectorPresented = false
            }
            .labelStyle(.iconOnly)
            .padding(12)
            .accessibilityIdentifier("logs.inspector.close")
        }
    }

    private var limitBinding: Binding<Int> {
        Binding(
            get: { query.take },
            set: {
                query.take = $0
                applyFilters()
            }
        )
    }

    private var selectedLog: TorrentCoreActivityLogEntry? {
        session.logs.value?.first(where: { $0.logEntryID == selectedLogID })
    }

    private var filteredLogs: [TorrentCoreActivityLogEntry] {
        guard !searchText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            return session.logs.value ?? []
        }
        return (session.logs.value ?? []).filter { log in
            [
                log.level,
                log.category,
                log.eventType,
                log.message,
                log.detailsJSON,
                log.traceID,
                log.torrentID?.uuidString,
                log.serviceInstanceID?.uuidString,
            ]
            .compactMap { $0 }
            .contains { $0.localizedCaseInsensitiveContains(searchText) }
        }
    }

    private var sortedLogItems: [TorrentCoreMacLogTableItem] {
        filteredLogs
            .map { TorrentCoreMacLogTableItem(log: $0) }
            .sorted(using: comparatorOrder)
    }

    private var currentPage: [TorrentCoreMacLogTableItem] {
        TorrentCoreMacTableSupport.page(
            sortedLogItems,
            index: pageIndex,
            size: pageSize
        )
    }

    private var comparatorOrder: [KeyPathComparator<TorrentCoreMacLogTableItem>] {
        sortDescriptors.map { $0.field.comparator(descending: $0.descending) }
    }

    private var tableSortOrder: Binding<[KeyPathComparator<TorrentCoreMacLogTableItem>]> {
        Binding(
            get: { comparatorOrder },
            set: { proposed in
                guard let comparator = proposed.first,
                      let field = TorrentCoreMacLogSortField.field(for: comparator.keyPath)
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

    private func clampPageAndReconcileSelection() {
        pageIndex = TorrentCoreMacTableSupport.clampedPageIndex(
            pageIndex,
            count: sortedLogItems.count,
            size: pageSize
        )
        reconcileSelectionWithCurrentPage()
    }

    private func reconcileSelectionWithCurrentPage() {
        guard let selectedLogID else { return }
        guard currentPage.contains(where: { $0.logEntryID == selectedLogID }) else {
            self.selectedLogID = nil
            isInspectorPresented = false
            return
        }
    }

    private var logCategoryOptions: [String] {
        uniqueLogOptions(
            (session.activityLogFilterOptions.value?.categories ?? [])
                + (session.logs.value ?? []).compactMap(\.category),
            preserving: categoryFilter
        )
    }

    private var logEventTypeOptions: [String] {
        uniqueLogOptions(
            (session.activityLogFilterOptions.value?.eventTypes ?? [])
                + (session.logs.value ?? []).compactMap(\.eventType),
            preserving: eventTypeFilter
        )
    }

    private func uniqueLogOptions(
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

    private var isLimited: Bool {
        session.logs.value?.count == query.take
    }

    private var unavailableMessage: String {
        if case .loading = session.logs.phase {
            return "Requesting recent activity logs from TorrentCore."
        }
        return switch session.connectionState {
        case .noProfile:
            "Create or select a connection before loading logs."
        case let .offline(_, _, message):
            message
        case .connecting:
            "Checking TorrentCore.Service…"
        case .notConnected:
            "Refresh to connect to the selected TorrentCore installation."
        case .connected:
            "TorrentCore did not return log entries."
        }
    }

    private var unavailableTitle: String {
        if case .loading = session.logs.phase {
            return "Loading Logs"
        }
        return "Logs Unavailable"
    }

    private var unavailableSystemImage: String {
        if case .loading = session.logs.phase {
            return "arrow.trianglehead.2.clockwise"
        }
        return "doc.text.magnifyingglass"
    }

    private func applyFilters() {
        query = TorrentCoreLogQuery(
            take: query.take,
            level: levelFilter,
            category: normalized(categoryFilter),
            eventType: normalized(eventTypeFilter),
            torrentID: UUID(uuidString: torrentIDFilter.trimmingCharacters(
                in: .whitespacesAndNewlines
            )),
            serviceInstanceID: UUID(uuidString: serviceInstanceIDFilter.trimmingCharacters(
                in: .whitespacesAndNewlines
            )),
            fromUTC: includesFromDate ? fromDate : nil,
            toUTC: includesToDate ? toDate : nil
        )
        selectedLogID = nil
        isInspectorPresented = false
        pageIndex = 0
        contextChanged()
    }

    private func clearFilters() {
        searchText = ""
        levelFilter = nil
        categoryFilter = ""
        eventTypeFilter = ""
        torrentIDFilter = ""
        serviceInstanceIDFilter = ""
        includesFromDate = false
        includesToDate = false
        applyFilters()
    }

    private func resetFilters() {
        searchText = ""
        levelFilter = Self.defaultQuery.level
        categoryFilter = Self.defaultQuery.category ?? ""
        eventTypeFilter = Self.defaultQuery.eventType ?? ""
        torrentIDFilter = Self.defaultQuery.torrentID?.uuidString ?? ""
        serviceInstanceIDFilter = Self.defaultQuery.serviceInstanceID?.uuidString ?? ""
        includesFromDate = Self.defaultQuery.fromUTC != nil
        includesToDate = Self.defaultQuery.toUTC != nil
        fromDate = Self.defaultQuery.fromUTC ?? Date()
        toDate = Self.defaultQuery.toUTC ?? Date()
        query = Self.defaultQuery
        applyFilters()
    }

    private func normalized(_ value: String) -> String? {
        let trimmed = value.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty ? nil : trimmed
    }

    private func deleteOrphanedLogs() {
        Task {
            do {
                let result = try await session.deleteOrphanedLogs()
                actionMessage = "Deleted \(result.deletedLogEntryCount.formatted()) orphaned log entries."
            } catch {
                actionError = TorrentCoreMacErrorPresenter.message(error)
            }
        }
    }

    static let defaultSortDescriptors = [
        TorrentCoreMacSortDescriptor(
            field: TorrentCoreMacLogSortField.occurredAt,
            descending: true
        ),
    ]

    static let exportHeaders = [
        "Category",
        "Details JSON",
        "Event Type",
        "Level",
        "Log Entry ID",
        "Message",
        "Occurred At",
        "Service Instance ID",
        "Torrent ID",
        "Trace ID",
    ]

    static func exportRow(_ value: TorrentCoreActivityLogEntry) -> [String] {
        [
            value.category ?? "",
            value.detailsJSON ?? "",
            value.eventType ?? "",
            value.level ?? "",
            String(value.logEntryID),
            value.message ?? "",
            TorrentCoreMacTableExport.isoTimestamp(value.occurredAt),
            value.serviceInstanceID?.uuidString ?? "",
            value.torrentID?.uuidString ?? "",
            value.traceID ?? "",
        ]
    }

    private func color(for level: String?) -> Color {
        switch level?.lowercased() {
        case "warning": .orange
        case "error", "critical": .red
        case "debug", "trace": .secondary
        default: .primary
        }
    }
}
