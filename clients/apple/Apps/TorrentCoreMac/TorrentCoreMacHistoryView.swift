import Foundation
import SwiftUI
import TorrentCoreAPI
import TorrentCoreFeatures

struct TorrentCoreMacHistoryView: View {
    enum SortField: String, CaseIterable {
        case updated
        case submitted
        case name
        case category
        case state
        case outcome
        case progress

        var label: String {
            TorrentCoreDisplayFormatter.splitIdentifier(rawValue)
        }
    }

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

    @AppStorage("TorrentCore.Mac.History.PageSize.v1") private var pageSize = 50
    @AppStorage("TorrentCore.Mac.History.Sort.v1") private var sortRaw = SortField.updated.rawValue
    @AppStorage("TorrentCore.Mac.History.SortDescending.v1")
    private var sortDescending = true

    @State private var nameFilter: String
    @State private var categoryFilter: String
    @State private var stateFilter: String
    @State private var outcomeFilter: String
    @State private var fromDate: Date
    @State private var toDate: Date
    @State private var includesFromDate: Bool
    @State private var includesToDate: Bool
    @State private var selectedHistoryKey: String?
    @State private var pageIndex = 0

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
        _includesFromDate = State(initialValue: initial.fromDate != nil)
        _includesToDate = State(initialValue: initial.toDate != nil)
        _fromDate = State(
            initialValue: Self.dateOnlyFormatter.date(from: initial.fromDate ?? "") ?? Date()
        )
        _toDate = State(
            initialValue: Self.dateOnlyFormatter.date(from: initial.toDate ?? "") ?? Date()
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
            } else if sortedValues.isEmpty {
                ContentUnavailableView(
                    "No History Matches",
                    systemImage: "line.3.horizontal.decrease.circle",
                    description: Text("Adjust the history filters and apply them again.")
                )
            } else {
                historyList
                Divider()
                paginationBar
            }
        }
        .inspector(isPresented: $isInspectorPresented) {
            historyInspector
                .inspectorColumnWidth(min: 330, ideal: 400, max: 540)
        }
        .onChange(of: selectedHistoryKey) { _, key in
            selectedTorrentID = sortedValues.first(where: { $0.id == key })?.torrentID
            isInspectorPresented = selectedTorrentID != nil
            contextChanged()
        }
        .onChange(of: isInspectorPresented) { _, _ in
            contextChanged()
        }
        .onChange(of: pageSize) { _, _ in pageIndex = 0 }
        .onChange(of: sortRaw) { _, _ in pageIndex = 0 }
        .onChange(of: sortDescending) { _, _ in pageIndex = 0 }
    }

    private var filterBar: some View {
        VStack(spacing: 8) {
            HStack(spacing: 10) {
                HStack(spacing: 4) {
                    TextField("Torrent name", text: $nameFilter)
                        .textFieldStyle(.roundedBorder)
                        .frame(minWidth: 180)
                    TorrentCoreMacHelpButton(
                        content: TorrentCoreHelpCatalog.History.torrentName
                    )
                }
                HStack(spacing: 4) {
                    TextField("Category", text: $categoryFilter)
                        .textFieldStyle(.roundedBorder)
                        .frame(width: 130)
                    TorrentCoreMacHelpButton(
                        content: TorrentCoreHelpCatalog.History.category
                    )
                }
                HStack(spacing: 4) {
                    TextField("State", text: $stateFilter)
                        .textFieldStyle(.roundedBorder)
                        .frame(width: 140)
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
            }

            HStack(spacing: 10) {
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

                Spacer()

                Picker("Sort", selection: $sortRaw) {
                    ForEach(SortField.allCases, id: \.rawValue) {
                        Text($0.label).tag($0.rawValue)
                    }
                }
                .frame(width: 165)

                Button {
                    sortDescending.toggle()
                } label: {
                    Label(
                        sortDescending ? "Descending" : "Ascending",
                        systemImage: sortDescending
                            ? "arrow.down"
                            : "arrow.up"
                    )
                }

                Button("Apply", action: applyFilters)
                    .keyboardShortcut(.return, modifiers: [])
                    .accessibilityIdentifier("history.apply")
            }
        }
        .padding(12)
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

    private var historyList: some View {
        List(selection: $selectedHistoryKey) {
            ForEach(currentPage) { item in
                HStack(spacing: 12) {
                    VStack(alignment: .leading, spacing: 3) {
                        Text(item.name ?? "Unnamed Torrent")
                            .lineLimit(1)
                        Text(TorrentCoreDisplayFormatter.timestamp(item.lastUpdatedAt))
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                    .frame(minWidth: 220, maxWidth: .infinity, alignment: .leading)

                    Text(item.categoryKey ?? "—")
                        .frame(width: 90, alignment: .leading)
                    Text(item.latestTorrentState ?? "—")
                        .frame(width: 110, alignment: .leading)
                    Text(item.outcome.rawValue)
                        .frame(width: 95, alignment: .leading)
                    Text(TorrentCoreDisplayFormatter.percent(item.latestProgressPercent))
                        .monospacedDigit()
                        .frame(width: 75, alignment: .trailing)
                    Text(TorrentCoreDisplayFormatter.bytes(item.latestDownloadedBytes))
                        .monospacedDigit()
                        .frame(width: 90, alignment: .trailing)
                }
                .tag(item.id)
                .contextMenu {
                    Button("Inspect History") {
                        selectedHistoryKey = item.id
                    }
                    if item.outcome == .active, let torrentID = item.torrentID {
                        Button("Show in Torrents") {
                            showTorrent(torrentID)
                        }
                    }
                }
                .accessibilityIdentifier("history.row")
            }
        }
        .listStyle(.inset)
    }

    private var paginationBar: some View {
        HStack {
            Text(resultRangeLabel)
                .foregroundStyle(.secondary)
            TorrentCoreMacHelpButton(content: TorrentCoreHelpCatalog.History.results)
            Spacer()
            Picker("Rows", selection: $pageSize) {
                ForEach([25, 50, 100, 250], id: \.self) { Text("\($0)").tag($0) }
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
                        .textSelection(.enabled)
                    TorrentCoreMacPhaseBanner(
                        phase: session.historyDetail.phase,
                        lastSuccessfulAt: session.historyDetail.lastSuccessfulAt
                    )
                    TorrentCoreMacDetailRow(label: "Outcome", value: detail.outcome.rawValue)
                    TorrentCoreMacDetailRow(
                        label: "State",
                        value: detail.latestTorrentState ?? "—"
                    )
                    TorrentCoreMacDetailRow(
                        label: "Progress",
                        value: TorrentCoreDisplayFormatter.percent(
                            detail.latestProgressPercent
                        )
                    )
                    TorrentCoreMacDetailRow(label: "Category", value: detail.categoryKey ?? "—")
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
                        value: detail.removalKind?.rawValue ?? "—"
                    )
                    TorrentCoreMacDetailRow(
                        label: "Removal Reason",
                        value: detail.removalReason ?? "—"
                    )
                    TorrentCoreMacDetailRow(label: "Info Hash", value: detail.infoHash ?? "—")
                    TorrentCoreMacDetailRow(
                        label: "Torrent ID",
                        value: detail.torrentID?.uuidString ?? "—"
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
    }

    private var sortedValues: [TorrentCoreHistorySummary] {
        let field = SortField(rawValue: sortRaw) ?? .updated
        return (session.history.value ?? []).sorted { lhs, rhs in
            let comparison: ComparisonResult
            switch field {
            case .updated:
                comparison = lhs.lastUpdatedAt.compare(rhs.lastUpdatedAt)
            case .submitted:
                comparison = lhs.submittedAt.compare(rhs.submittedAt)
            case .name:
                comparison = (lhs.name ?? "").localizedStandardCompare(
                    rhs.name ?? ""
                )
            case .category:
                comparison = (lhs.categoryKey ?? "").localizedStandardCompare(
                    rhs.categoryKey ?? ""
                )
            case .state:
                comparison = (lhs.latestTorrentState ?? "").localizedStandardCompare(
                    rhs.latestTorrentState ?? ""
                )
            case .outcome:
                comparison = lhs.outcome.rawValue.localizedStandardCompare(
                    rhs.outcome.rawValue
                )
            case .progress:
                comparison = lhs.latestProgressPercent == rhs.latestProgressPercent
                    ? .orderedSame
                    : lhs.latestProgressPercent < rhs.latestProgressPercent
                        ? .orderedAscending
                        : .orderedDescending
            }
            return sortDescending
                ? comparison == .orderedDescending
                : comparison == .orderedAscending
        }
    }

    private var currentPage: [TorrentCoreHistorySummary] {
        let safeIndex = min(pageIndex, maxPageIndex)
        let start = safeIndex * pageSize
        guard start < sortedValues.count else { return [] }
        return Array(sortedValues[start..<min(sortedValues.count, start + pageSize)])
    }

    private var maxPageIndex: Int {
        max(0, (sortedValues.count - 1) / max(1, pageSize))
    }

    private var resultRangeLabel: String {
        guard !sortedValues.isEmpty else { return "0 records" }
        let start = min(pageIndex, maxPageIndex) * pageSize + 1
        let end = start + currentPage.count - 1
        return "\(start)–\(end) of \(sortedValues.count)"
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
        let outcome = outcomeFilter.isEmpty
            ? nil
            : TorrentCoreHistoryOutcome(rawValue: outcomeFilter)
        query = TorrentCoreHistoryQuery(
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
