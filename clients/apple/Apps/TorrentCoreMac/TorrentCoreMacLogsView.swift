import Foundation
import SwiftUI
import TorrentCoreAPI
import TorrentCoreFeatures

struct TorrentCoreMacLogsView: View {
    static let defaultQuery = TorrentCoreLogQuery(take: 1_000)

    let session: TorrentCoreFeatureSession
    @Binding var query: TorrentCoreLogQuery
    @Binding var selectedLogID: Int64?
    @Binding var isInspectorPresented: Bool
    let contextChanged: () -> Void
    let showTorrent: (UUID) -> Void
    let showHistory: (UUID) -> Void

    @State private var searchText = ""
    @State private var levelFilter: Int32?
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
        _levelFilter = State(initialValue: initial.level?.rawValue)
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
                    systemImage: "text.badge.ellipsis"
                )
                .font(.caption)
                .foregroundStyle(.secondary)
                .padding(.horizontal, 12)
                .padding(.vertical, 8)
                .frame(maxWidth: .infinity, alignment: .leading)
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
            } else if filteredLogs.isEmpty {
                ContentUnavailableView(
                    "No Logs Match",
                    systemImage: "line.3.horizontal.decrease.circle",
                    description: Text("Adjust the server filters or local search.")
                )
            } else {
                logList
            }
        }
        .inspector(isPresented: $isInspectorPresented) {
            logInspector
                .inspectorColumnWidth(min: 330, ideal: 420, max: 580)
        }
        .onChange(of: selectedLogID) { _, value in
            if value != nil {
                isInspectorPresented = true
            }
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
                        Text("All Levels").tag(Int32?.none)
                        Text("Trace").tag(Int32?.some(0))
                        Text("Debug").tag(Int32?.some(1))
                        Text("Information").tag(Int32?.some(2))
                        Text("Warning").tag(Int32?.some(3))
                        Text("Error").tag(Int32?.some(4))
                        Text("Critical").tag(Int32?.some(5))
                    }
                    .labelsHidden()
                    .frame(width: 150)
                }
                VStack(alignment: .leading, spacing: 3) {
                    TorrentCoreMacHelpLabel(content: TorrentCoreHelpCatalog.Logs.category)
                    TextField("Category", text: $categoryFilter)
                        .textFieldStyle(.roundedBorder)
                        .frame(width: 130)
                }
                VStack(alignment: .leading, spacing: 3) {
                    TorrentCoreMacHelpLabel(content: TorrentCoreHelpCatalog.Logs.eventType)
                    TextField("Event type", text: $eventTypeFilter)
                        .textFieldStyle(.roundedBorder)
                        .frame(width: 155)
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

    private var logList: some View {
        List(selection: $selectedLogID) {
            ForEach(filteredLogs) { log in
                HStack(spacing: 12) {
                    Text(log.occurredAt.formatted(date: .numeric, time: .standard))
                        .monospacedDigit()
                        .frame(width: 165, alignment: .leading)
                    Text(log.level ?? "—")
                        .foregroundStyle(color(for: log.level))
                        .frame(width: 90, alignment: .leading)
                    Text(log.category ?? "—")
                        .frame(width: 105, alignment: .leading)
                    Text(log.eventType ?? "—")
                        .frame(width: 160, alignment: .leading)
                    Text(log.message ?? "—")
                        .lineLimit(2)
                        .frame(minWidth: 260, maxWidth: .infinity, alignment: .leading)
                }
                .tag(log.logEntryID)
                .contextMenu {
                    Button("Inspect Log") {
                        selectedLogID = log.logEntryID
                    }
                    if let torrentID = log.torrentID {
                        Button("Show Torrent") {
                            showTorrent(torrentID)
                        }
                        Button("Show History") {
                            showHistory(torrentID)
                        }
                    }
                }
                .accessibilityIdentifier("logs.row")
            }
        }
        .listStyle(.inset)
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
                        value: log.occurredAt.formatted(date: .abbreviated, time: .standard)
                    )
                    TorrentCoreMacDetailRow(label: "Level", value: log.level ?? "—")
                    TorrentCoreMacDetailRow(label: "Category", value: log.category ?? "—")
                    TorrentCoreMacDetailRow(
                        label: "Torrent ID",
                        value: log.torrentID?.uuidString ?? "—"
                    )
                    TorrentCoreMacDetailRow(
                        label: "Service Instance",
                        value: log.serviceInstanceID?.uuidString ?? "—"
                    )
                    TorrentCoreMacDetailRow(label: "Trace ID", value: log.traceID ?? "—")
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
            level: levelFilter.map(TorrentCoreActivityLogLevel.init(rawValue:)),
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
