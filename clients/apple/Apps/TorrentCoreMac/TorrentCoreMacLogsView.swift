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
}

private enum TorrentCoreMacLogSortField: String {
    case occurredAt
    case level
    case category
    case eventType
    case message

    func comparator(descending: Bool) -> KeyPathComparator<TorrentCoreMacLogTableItem> {
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
        }
    }

    static func field(for keyPath: PartialKeyPath<TorrentCoreMacLogTableItem>) -> Self? {
        switch keyPath {
        case \TorrentCoreMacLogTableItem.occurredAt: .occurredAt
        case \TorrentCoreMacLogTableItem.level: .level
        case \TorrentCoreMacLogTableItem.category: .category
        case \TorrentCoreMacLogTableItem.eventType: .eventType
        case \TorrentCoreMacLogTableItem.message: .message
        default: nil
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

    @AppStorage("TorrentCore.Mac.Logs.PageSize.v1") private var pageSize = 50
    @AppStorage("TorrentCore.Mac.Logs.SortField.v1")
    private var storedSortField = TorrentCoreMacLogSortField.occurredAt.rawValue
    @AppStorage("TorrentCore.Mac.Logs.SortDescending.v1")
    private var storedSortDescending = true
    @AppStorage("TorrentCore.Mac.Logs.Columns.v1")
    private var columnCustomization =
        TableColumnCustomization<TorrentCoreMacLogTableItem>()

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
    @State private var sortOrder: [KeyPathComparator<TorrentCoreMacLogTableItem>]

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
        let storedField = defaults.string(forKey: "TorrentCore.Mac.Logs.SortField.v1")
        let field = TorrentCoreMacLogSortField(rawValue: storedField ?? "")
            ?? .occurredAt
        let descending = defaults.object(
            forKey: "TorrentCore.Mac.Logs.SortDescending.v1"
        ) as? Bool ?? true
        _sortOrder = State(initialValue: [field.comparator(descending: descending)])
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
        .torrentCoreTrailingOverlay(
            isPresented: isInspectorPresented,
            width: 420
        ) {
            logInspector
        }
        .onChange(of: selectedLogID) { _, value in
            if value != nil {
                isInspectorPresented = true
            }
        }
        .onChange(of: searchText) { _, _ in
            pageIndex = 0
        }
        .onChange(of: pageSize) { _, _ in
            pageIndex = 0
        }
        .onChange(of: sortOrder) { _, newValue in
            guard let comparator = newValue.first,
                  let field = TorrentCoreMacLogSortField.field(for: comparator.keyPath)
            else {
                return
            }
            storedSortField = field.rawValue
            storedSortDescending = comparator.order == .reverse
            pageIndex = 0
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
        VStack(spacing: 8) {
            HStack(spacing: 10) {
                VStack(alignment: .leading, spacing: 3) {
                    TorrentCoreMacHelpLabel(
                        "Search",
                        content: TorrentCoreHelpCatalog.Logs.searchMessage
                    )
                    TextField("Search loaded logs", text: $searchText)
                        .textFieldStyle(.roundedBorder)
                        .frame(minWidth: 190)
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
            }

            HStack(spacing: 10) {
                VStack(alignment: .leading, spacing: 3) {
                    TorrentCoreMacHelpLabel(content: TorrentCoreHelpCatalog.Logs.torrentID)
                    TextField("Torrent ID", text: $torrentIDFilter)
                        .textFieldStyle(.roundedBorder)
                }
                VStack(alignment: .leading, spacing: 3) {
                    TorrentCoreMacHelpLabel(
                        content: TorrentCoreHelpCatalog.Logs.serviceInstanceID
                    )
                    TextField("Service instance ID", text: $serviceInstanceIDFilter)
                        .textFieldStyle(.roundedBorder)
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

                Button("Apply", action: applyFilters)
                    .accessibilityIdentifier("logs.apply")

                Button(role: .destructive) {
                    isDeleteConfirmationPresented = true
                } label: {
                    Label("Delete Orphaned", systemImage: "trash")
                }
                .disabled(!session.connectionState.isConnected || session.activeMutation != nil)
                .help(TorrentCoreHelpCatalog.Logs.deleteOrphaned.summary)
            }
        }
        .padding(12)
    }

    private var logTable: some View {
        Table(
            currentPage,
            selection: $selectedLogID,
            sortOrder: $sortOrder,
            columnCustomization: $columnCustomization
        ) {
            TableColumn("When", value: \.occurredAt) {
                Text(TorrentCoreDisplayFormatter.timestamp($0.log.occurredAt))
                    .monospacedDigit()
            }
            .width(min: 155, ideal: 175)
            .customizationID("when")

            TableColumn("Level", value: \.level, comparator: .localizedStandard) {
                Text($0.level)
                    .foregroundStyle(color(for: $0.log.level))
                    .accessibilityIdentifier("logs.row")
            }
            .width(min: 95, ideal: 110)
            .customizationID("level")

            TableColumn("Category", value: \.category, comparator: .localizedStandard) {
                Text($0.category)
            }
            .width(min: 125, ideal: 155)
            .customizationID("category")

            TableColumn(
                "Event",
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
            .customizationID("event")

            TableColumn("Message", value: \.message, comparator: .localizedStandard) {
                Text($0.message)
                    .lineLimit(2)
            }
            .width(min: 300, ideal: 520)
            .customizationID("message")
        }
        .accessibilityIdentifier("logs.table")
    }

    private var paginationBar: some View {
        HStack {
            Text(resultRangeLabel)
                .foregroundStyle(.secondary)
            Spacer()
            Picker("Rows", selection: $pageSize) {
                ForEach([25, 50, 100, 250], id: \.self) {
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
            .accessibilityIdentifier("logs.previousPage")
            Button {
                pageIndex = min(maxPageIndex, pageIndex + 1)
            } label: {
                Label("Next", systemImage: "chevron.right")
            }
            .disabled(pageIndex >= maxPageIndex)
            .accessibilityIdentifier("logs.nextPage")
            Text("Page \(min(pageIndex, maxPageIndex) + 1) of \(maxPageIndex + 1)")
                .foregroundStyle(.secondary)
        }
        .padding(10)
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
            .sorted(using: sortOrder)
    }

    private var currentPage: [TorrentCoreMacLogTableItem] {
        let safeIndex = min(pageIndex, maxPageIndex)
        let start = safeIndex * pageSize
        guard start < sortedLogItems.count else {
            return []
        }
        return Array(
            sortedLogItems[start..<min(sortedLogItems.count, start + pageSize)]
        )
    }

    private var maxPageIndex: Int {
        max(0, (sortedLogItems.count - 1) / max(1, pageSize))
    }

    private var resultRangeLabel: String {
        guard !sortedLogItems.isEmpty else {
            return "0 logs"
        }
        let start = min(pageIndex, maxPageIndex) * pageSize + 1
        let end = start + currentPage.count - 1
        return "\(start.formatted())–\(end.formatted()) of \(sortedLogItems.count.formatted())"
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

    private func color(for level: String?) -> Color {
        switch level?.lowercased() {
        case "warning": .orange
        case "error", "critical": .red
        case "debug", "trace": .secondary
        default: .primary
        }
    }
}
