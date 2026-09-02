import Foundation
import Testing
import TorrentCoreAPI
@testable import TorrentCore

@Test
func macNavigationDestinationsExposeStableAccessibleMetadata() {
    #expect(TorrentCoreMacDestination.allCases.map(\.rawValue) == [
        "dashboard",
        "torrents",
        "history",
        "logs",
        "serviceSettings",
        "connection",
    ])
    #expect(TorrentCoreMacDestination.connection.title == "Connection")
    #expect(TorrentCoreMacDestination.dashboard.systemImage.isEmpty == false)
    #expect(TorrentCoreMacDestination.torrents.systemImage.isEmpty == false)
    #expect(TorrentCoreMacDestination.history.title == "History")
    #expect(TorrentCoreMacDestination.logs.systemImage.isEmpty == false)
    #expect(TorrentCoreMacDestination.serviceSettings.title == "Service Settings")
}

@Test
func serviceSettingsExposeCleanupAfterCategories() {
    let groups = TorrentCoreMacServiceSettingsView.SettingsGroup.allCases
    #expect(groups.contains(where: { $0.title == "VPN Egress" }))
    #expect(groups.suffix(2).map(\.title) == ["Categories", "Cleanup"])
    #expect(groups.last?.systemImage.isEmpty == false)
}

@Test
func cleanupDatesUseSevenAndThirtyDayDefaultsAndRejectFutureDates() throws {
    var calendar = Calendar(identifier: .gregorian)
    calendar.timeZone = try #require(TimeZone(secondsFromGMT: 0))
    let now = try #require(
        DateComponents(
            calendar: calendar,
            timeZone: calendar.timeZone,
            year: 2026,
            month: 7,
            day: 28,
            hour: 17
        ).date
    )
    let defaults = TorrentCoreMacCleanupDates.defaults(now: now, calendar: calendar)
    let expectedLogs = calendar.date(byAdding: .day, value: -7, to: calendar.startOfDay(for: now))
    let expectedHistory = calendar.date(byAdding: .day, value: -30, to: calendar.startOfDay(for: now))
    let tomorrow = calendar.date(byAdding: .day, value: 1, to: now)

    #expect(defaults.logs == expectedLogs)
    #expect(defaults.history == expectedHistory)
    #expect(TorrentCoreMacCleanupDates.isFuture(now, now: now, calendar: calendar) == false)
    #expect(
        TorrentCoreMacCleanupDates.isFuture(
            try #require(tomorrow),
            now: now,
            calendar: calendar
        )
    )
}

@Test
func macAppearancePreferenceExposesTheAgreedDeviceLocalChoices() {
    #expect(TorrentCoreMacAppearance.allCases.map(\.rawValue) == [
        "system",
        "light",
        "dark",
    ])
    #expect(TorrentCoreMacAppearance.system.title == "System")
    #expect(TorrentCoreMacAppearance.light.title == "Light")
    #expect(TorrentCoreMacAppearance.dark.title == "Dark")
    #expect(TorrentCoreMacAppearance.system.colorScheme == nil)
    #expect(TorrentCoreMacAppearance.light.colorScheme == .light)
    #expect(TorrentCoreMacAppearance.dark.colorScheme == .dark)
}

@Test
func addMagnetValidationRejectsOnlyClearlyInvalidInput() {
    #expect(TorrentCoreMacMagnetValidation.isValid(
        "magnet:?xt=urn:btih:a20db864aa3a28fa79f6f0815ba13c64132aa55c&dn=Disposable"
    ))
    #expect(TorrentCoreMacMagnetValidation.isValid(
        "  MAGNET:?XT=urn:btmh:1220abcdef  "
    ))
    #expect(!TorrentCoreMacMagnetValidation.isValid(""))
    #expect(!TorrentCoreMacMagnetValidation.isValid("not-a-magnet"))
    #expect(!TorrentCoreMacMagnetValidation.isValid("magnet:?dn=Missing%20Exact%20Topic"))
    #expect(!TorrentCoreMacMagnetValidation.isValid("magnet:?xt="))
}

@Test
func tablePagingClampsBoundariesAndReportsTheDisplayedRange() {
    let values = Array(1...61)

    #expect(TorrentCoreMacTableSupport.page(values, index: -1, size: 25) == Array(1...25))
    #expect(TorrentCoreMacTableSupport.page(values, index: 2, size: 25) == Array(51...61))
    #expect(TorrentCoreMacTableSupport.page(values, index: 99, size: 25) == Array(51...61))
    #expect(TorrentCoreMacTableSupport.maximumPageIndex(count: 61, size: 25) == 2)
    #expect(TorrentCoreMacTableSupport.clampedPageIndex(99, count: 61, size: 25) == 2)
    #expect(TorrentCoreMacTableSupport.resultRangeLabel(count: 61, pageIndex: 2, pageSize: 25) == "51–61 of 61")
    #expect(TorrentCoreMacTableSupport.resultRangeLabel(count: 0, pageIndex: 4, pageSize: 25) == "0 results")
    #expect(TorrentCoreMacTableSupport.pageSizes == [25, 50, 100, 250])
}

@Test
@MainActor
func orderedTableSortStorageRoundTripsWithoutLosingPriority() throws {
    enum Field: String, Codable, Hashable {
        case name
        case state
        case added
    }
    let expected = [
        TorrentCoreMacSortDescriptor(field: Field.state, descending: true),
        TorrentCoreMacSortDescriptor(field: Field.added, descending: false),
        TorrentCoreMacSortDescriptor(field: Field.name, descending: false),
    ]

    let stored = TorrentCoreMacSortStorage.encode(expected)
    let decoded = try #require(TorrentCoreMacSortStorage.decode(stored, as: Field.self))

    #expect(decoded == expected)
    #expect(TorrentCoreMacSortStorage.decode("not-base64", as: Field.self) == nil)
    #expect(TorrentCoreMacTorrentsView.defaultSortDescriptors == [
        TorrentCoreMacSortDescriptor(field: TorrentCoreMacTorrentSortField.state, descending: false),
        TorrentCoreMacSortDescriptor(field: TorrentCoreMacTorrentSortField.progress, descending: true),
    ])
}

@Test
func tableExportEscapesQuotedDelimiterRowsAndUsesUtcTimestamps() {
    let content = TorrentCoreMacTableExport.delimitedContent(
        headers: ["Name", "Detail"],
        rows: [["Example ## Torrent", "line one\n\"line two\""]]
    )
    let date = Date(timeIntervalSince1970: 0)

    #expect(content == "Name##Detail\n\"Example ## Torrent\"##\"line one \"\"line two\"\"\"\n")
    #expect(TorrentCoreMacTableExport.isoTimestamp(date) == "1970-01-01T00:00:00.000Z")
    #expect(TorrentCoreMacTableExport.isoTimestamp(nil).isEmpty)
    #expect(TorrentCoreMacTableExport.sanitizedFileName(" Torrents / selected: now.csv ") == "Torrents-selected-now.csv")
}

@Test
@MainActor
func torrentExportKeepsFullSummaryFieldOrder() {
    var summary = TorrentCorePreviewFixtures.downloadingTorrent
    summary.priorityQueuePosition = 4
    summary.heldQueuePosition = 7
    summary.isQueueHeld = true
    summary.completionCallbackState = "Pending"
    let row = TorrentCoreMacTorrentsView.exportRow(summary)

    #expect(row.count == TorrentCoreMacTorrentsView.exportHeaders.count)
    #expect(TorrentCoreMacTorrentsView.exportHeaders.first == "Torrent ID")
    #expect(row.first == summary.torrentID?.uuidString)
    #expect(TorrentCoreMacTorrentsView.exportHeaders[17] == "Priority Queue Position")
    #expect(row[17] == "4")
    #expect(TorrentCoreMacTorrentsView.exportHeaders[19] == "Is Queue Held")
    #expect(row[19] == "Yes")
    #expect(TorrentCoreMacTorrentsView.exportHeaders[23] == "Completion Callback State")
    #expect(row[23] == "Pending")
    #expect(TorrentCoreMacTorrentsView.exportHeaders.last == "Can Resume On Hold")
}

@Test
func tableExportScopeUsesTheSelectedRowOrEveryFilteredResult() {
    let all = ["first", "second"]

    #expect(TorrentCoreMacExportScope.selected.rows(selected: "second", all: all) == ["second"])
    #expect(TorrentCoreMacExportScope.selected.rows(selected: nil as String?, all: all).isEmpty)
    #expect(TorrentCoreMacExportScope.all.rows(selected: "second", all: all) == all)
}

@Test
func trailingOverlayWidthUsesTheAcceptedBounds() {
    #expect(TorrentCoreMacTableSupport.clampedOverlayWidth(120) == 340)
    #expect(TorrentCoreMacTableSupport.clampedOverlayWidth(480) == 480)
    #expect(TorrentCoreMacTableSupport.clampedOverlayWidth(900) == 720)
}

@Test
@MainActor
func historyAndLogsRestoreTheirExistingDefaultSorts() {
    #expect(TorrentCoreMacHistoryView.defaultSortDescriptors == [
        TorrentCoreMacSortDescriptor(
            field: TorrentCoreMacHistorySortField.lastUpdated,
            descending: true
        ),
    ])
    #expect(TorrentCoreMacLogsView.defaultSortDescriptors == [
        TorrentCoreMacSortDescriptor(
            field: TorrentCoreMacLogSortField.occurredAt,
            descending: true
        ),
    ])
}

@Test
@MainActor
func historyExportKeepsEverySummaryFieldInStableOrder() throws {
    let summary = try #require(TorrentCorePreviewFixtures.history.dropFirst().first)
    let row = TorrentCoreMacHistoryView.exportRow(summary)

    #expect(row.count == TorrentCoreMacHistoryView.exportHeaders.count)
    #expect(TorrentCoreMacHistoryView.exportHeaders.first == "Category Key")
    #expect(row.first == summary.categoryKey)
    #expect(TorrentCoreMacHistoryView.exportHeaders[1] == "Completion Callback Final Result")
    #expect(row[1] == summary.completionCallbackFinalResult)
    #expect(TorrentCoreMacHistoryView.exportHeaders[8] == "Last Updated At")
    #expect(row[8] == TorrentCoreMacTableExport.isoTimestamp(summary.lastUpdatedAt))
    #expect(TorrentCoreMacHistoryView.exportHeaders[22] == "Name")
    #expect(row[22] == summary.name)
    #expect(TorrentCoreMacHistoryView.exportHeaders.last == "Torrent ID")
    #expect(row.last == summary.torrentID?.uuidString)
}

@Test
@MainActor
func logExportKeepsEveryEntryFieldInStableOrder() throws {
    let log = try #require(TorrentCorePreviewFixtures.activityLogs.first)
    let row = TorrentCoreMacLogsView.exportRow(log)

    #expect(row.count == TorrentCoreMacLogsView.exportHeaders.count)
    #expect(TorrentCoreMacLogsView.exportHeaders.first == "Category")
    #expect(row.first == log.category)
    #expect(TorrentCoreMacLogsView.exportHeaders[1] == "Details JSON")
    #expect(row[1] == log.detailsJSON)
    #expect(TorrentCoreMacLogsView.exportHeaders[4] == "Log Entry ID")
    #expect(row[4] == String(log.logEntryID))
    #expect(TorrentCoreMacLogsView.exportHeaders[6] == "Occurred At")
    #expect(row[6] == TorrentCoreMacTableExport.isoTimestamp(log.occurredAt))
    #expect(TorrentCoreMacLogsView.exportHeaders.last == "Trace ID")
    #expect(row.last == log.traceID)
}

@Test
@MainActor
func peerAndTrackerTablesRestoreTheirAgreedDefaultSorts() {
    #expect(TorrentCoreMacPeersSheet.defaultSortDescriptors == [
        TorrentCoreMacSortDescriptor(
            field: TorrentCoreMacPeerSortField.endpoint,
            descending: false
        ),
    ])
    #expect(TorrentCoreMacTrackersSheet.defaultSortDescriptors == [
        TorrentCoreMacSortDescriptor(
            field: TorrentCoreMacTrackerSortField.tier,
            descending: false
        ),
        TorrentCoreMacSortDescriptor(
            field: TorrentCoreMacTrackerSortField.number,
            descending: false
        ),
    ])
}

@Test
@MainActor
func peerExportKeepsEveryReceivedFieldInStableOrder() throws {
    let peer = try #require(TorrentCorePreviewFixtures.peers.first)
    let row = TorrentCoreMacPeersSheet.exportRow(peer)

    #expect(row.count == TorrentCoreMacPeersSheet.exportHeaders.count)
    #expect(TorrentCoreMacPeersSheet.exportHeaders.first == "Client")
    #expect(row.first == peer.client)
    #expect(TorrentCoreMacPeersSheet.exportHeaders[5] == "Endpoint")
    #expect(row[5] == peer.endpoint)
    #expect(TorrentCoreMacPeersSheet.exportHeaders.last == "Uploaded Bytes")
    #expect(row.last == String(peer.uploadedBytes))
}

@Test
@MainActor
func trackerExportKeepsEveryReceivedFieldInStableOrder() throws {
    let tracker = try #require(TorrentCorePreviewFixtures.trackers.last)
    let row = TorrentCoreMacTrackersSheet.exportRow(tracker)

    #expect(row.count == TorrentCoreMacTrackersSheet.exportHeaders.count)
    #expect(TorrentCoreMacTrackersSheet.exportHeaders.first == "Can Announce")
    #expect(row.first == "Yes")
    #expect(TorrentCoreMacTrackersSheet.exportHeaders[7] == "Tier Number")
    #expect(row[7] == String(tracker.tierNumber))
    #expect(TorrentCoreMacTrackersSheet.exportHeaders.last == "Warning Message")
    #expect(row.last == tracker.warningMessage)
}
