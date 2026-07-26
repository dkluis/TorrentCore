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
    var submitted: Date { summary.submittedAt }
    var name: String { summary.name ?? "Unnamed Torrent" }
    var category: String { TorrentCoreDisplayFormatter.category(summary.categoryKey) }
    var state: String { summary.latestTorrentState ?? "—" }
    var outcome: String { summary.outcome.rawValue }
    var progress: Double { summary.latestProgressPercent }
    var downloaded: Int64 { summary.latestDownloadedBytes }
    var total: Int64 { summary.latestTotalBytes ?? -1 }
    var callback: String { summary.latestCallbackStatus ?? "—" }
    var removed: Date { summary.removedAt ?? .distantPast }
    var removalReason: String { summary.removalReason ?? "—" }
}

private enum TorrentCoreMacHistorySortField: String {
    case submitted
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

    func comparator(descending: Bool) -> KeyPathComparator<TorrentCoreMacHistoryTableItem> {
        let order: SortOrder = descending ? .reverse : .forward
        return switch self {
        case .submitted:
            KeyPathComparator(\.submitted, order: order)
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
        }
    }

    static func field(
        for keyPath: PartialKeyPath<TorrentCoreMacHistoryTableItem>
    ) -> Self? {
        switch keyPath {
        case \TorrentCoreMacHistoryTableItem.submitted: .submitted
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
        default: nil
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

    @AppStorage("TorrentCore.Mac.History.PageSize.v1") private var pageSize = 50
    @AppStorage("TorrentCore.Mac.History.Sort.v2")
    private var storedSortField = TorrentCoreMacHistorySortField.submitted.rawValue
    @AppStorage("TorrentCore.Mac.History.SortDescending.v1")
    private var storedSortDescending = true
    @AppStorage("TorrentCore.Mac.History.Columns.v1")
    private var columnCustomization =
        TableColumnCustomization<TorrentCoreMacHistoryTableItem>()

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
    @State private var sortOrder: [KeyPathComparator<TorrentCoreMacHistoryTableItem>]
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
        _includesFromDate = State(initialValue: initial.fromDate != nil)
        _includesToDate = State(initialValue: initial.toDate != nil)
        _fromDate = State(
            initialValue: Self.dateOnlyFormatter.date(from: initial.fromDate ?? "") ?? Date()
        )
        _toDate = State(
            initialValue: Self.dateOnlyFormatter.date(from: initial.toDate ?? "") ?? Date()
        )
        let defaults = UserDefaults.standard
        let storedField = defaults.string(
            forKey: "TorrentCore.Mac.History.Sort.v2"
        )
        let field = TorrentCoreMacHistorySortField(rawValue: storedField ?? "")
            ?? .submitted
        let descending = defaults.object(
            forKey: "TorrentCore.Mac.History.SortDescending.v1"
        ) as? Bool ?? true
        _sortOrder = State(initialValue: [field.comparator(descending: descending)])
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
                    description: Text("Adjust the history filters and apply them again.")
                )
            } else {
                historyTable
                Divider()
                paginationBar
            }
        }
        .inspector(isPresented: $isInspectorPresented) {
            historyInspector
                .inspectorColumnWidth(min: 330, ideal: 400, max: 540)
        }
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
        .onChange(of: pageSize) { _, _ in pageIndex = 0 }
        .onChange(of: sortOrder) { _, newValue in
            guard let comparator = newValue.first,
                  let field = TorrentCoreMacHistorySortField.field(
                      for: comparator.keyPath
                  )
            else {
                return
            }
            storedSortField = field.rawValue
            storedSortDescending = comparator.order == .reverse
            pageIndex = 0
        }
        .onDisappear {
            magnetCopyResetTask?.cancel()
        }
        .torrentCoreRefreshWhileVisible(
            session: session,
            context: refreshContext
        )
    }

    private var refreshContext: TorrentCoreFeatureContext {
        .history(
            query: query,
            selectedTorrentID: isInspectorPresented ? selectedTorrentID : nil
        )
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

    private var historyTable: some View {
        Table(
            currentPage,
            selection: $selectedHistoryKey,
            sortOrder: $sortOrder,
            columnCustomization: $columnCustomization
        ) {
            TableColumn("Submitted", value: \.submitted) {
                Text(TorrentCoreDisplayFormatter.timestamp($0.summary.submittedAt))
                    .monospacedDigit()
            }
            .width(min: 85, ideal: 165)
            .customizationID("submitted")

            TableColumn("Name", value: \.name, comparator: .localizedStandard) { item in
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
            .customizationID("name")

            TableColumn("Category", value: \.category, comparator: .localizedStandard) {
                Text($0.category)
            }
            .width(min: 55, ideal: 135)
            .customizationID("category")

            TableColumn("State", value: \.state, comparator: .localizedStandard) {
                Text(TorrentCoreDisplayFormatter.splitIdentifier($0.state))
            }
            .width(min: 65, ideal: 160)
            .customizationID("state")

            TableColumn("Outcome", value: \.outcome, comparator: .localizedStandard) {
                Text(TorrentCoreDisplayFormatter.splitIdentifier($0.outcome))
            }
            .width(min: 55, ideal: 125)
            .customizationID("outcome")

            TableColumn("Progress", value: \.progress) {
                Text(TorrentCoreDisplayFormatter.percent($0.progress))
                    .monospacedDigit()
            }
            .width(min: 50, ideal: 95)
            .customizationID("progress")

            TableColumn("Downloaded", value: \.downloaded) {
                Text(TorrentCoreDisplayFormatter.bytes($0.downloaded))
                    .monospacedDigit()
            }
            .width(min: 65, ideal: 120)
            .customizationID("downloaded")

            TableColumn("Total", value: \.total) {
                Text(TorrentCoreDisplayFormatter.bytes($0.summary.latestTotalBytes))
                    .monospacedDigit()
            }
            .width(min: 65, ideal: 120)
            .customizationID("total")

            TableColumn("Callback", value: \.callback, comparator: .localizedStandard) {
                Text(TorrentCoreDisplayFormatter.splitIdentifier($0.callback))
            }
            .width(min: 75, ideal: 190)
            .customizationID("callback")

            Group {
                TableColumn(
                    "Removed",
                    value: \TorrentCoreMacHistoryTableItem.removed
                ) {
                    Text(TorrentCoreDisplayFormatter.timestamp($0.summary.removedAt))
                        .monospacedDigit()
                }
                .width(min: 85, ideal: 165)
                .customizationID("removed")

                TableColumn(
                    "Removal Reason",
                    value: \TorrentCoreMacHistoryTableItem.removalReason,
                    comparator: .localizedStandard
                ) {
                    Text($0.removalReason)
                        .lineLimit(2)
                }
                .width(min: 70, ideal: 280)
                .customizationID("removalReason")
            }
        }
        .accessibilityIdentifier("history.table")
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
                    Divider()
                    Text("Callback Feedback")
                        .font(.headline)
                    TorrentCoreMacDetailRow(
                        label: "Summary",
                        value: TorrentCoreCompletionCallbackPresentation.feedbackSummary(
                            detail.completionCallbackFeedback
                        ) ?? "—"
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
                        value: detail.completionCallbackFeedback?.finalState ?? "—"
                    )
                    .accessibilityIdentifier("history.feedback.finalResult")
                    TorrentCoreMacDetailRow(
                        label: "Reason",
                        value: detail.completionCallbackFeedback?.reasonCode ?? "—"
                    )
                    .accessibilityIdentifier("history.feedback.reason")
                    historyMagnetRow(detail.magnetURI)
                    TorrentCoreMacDetailRow(label: "Info Hash", value: detail.infoHash ?? "—")
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
    }

    private func historyMagnetRow(_ magnetURI: String?) -> some View {
        LabeledContent("Magnet") {
            VStack(alignment: .trailing, spacing: 6) {
                Text(copyableMagnetURI(magnetURI) ?? "—")
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
            .sorted(using: sortOrder)
    }

    private var currentPage: [TorrentCoreMacHistoryTableItem] {
        let safeIndex = min(pageIndex, maxPageIndex)
        let start = safeIndex * pageSize
        guard start < sortedItems.count else { return [] }
        return Array(sortedItems[start..<min(sortedItems.count, start + pageSize)])
    }

    private var maxPageIndex: Int {
        max(0, (sortedItems.count - 1) / max(1, pageSize))
    }

    private var resultRangeLabel: String {
        guard !sortedItems.isEmpty else { return "0 records" }
        let start = min(pageIndex, maxPageIndex) * pageSize + 1
        let end = start + currentPage.count - 1
        return "\(start)–\(end) of \(sortedItems.count)"
    }

    private var historyCategoryOptions: [String] {
        uniqueOptions(
            (session.history.value ?? []).compactMap(\.categoryKey),
            preserving: categoryFilter
        )
    }

    private var historyStateOptions: [String] {
        uniqueOptions(
            TorrentCoreKnownTorrentState.allCases.map(\.rawValue)
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
