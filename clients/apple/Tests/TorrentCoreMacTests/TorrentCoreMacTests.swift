import Testing
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
