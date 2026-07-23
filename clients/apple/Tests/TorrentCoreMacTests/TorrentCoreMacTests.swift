import Testing
@testable import TorrentCore

@Test
func macNavigationDestinationsExposeStableAccessibleMetadata() {
    #expect(TorrentCoreMacDestination.allCases.map(\.rawValue) == [
        "connection",
        "dashboard",
        "torrents",
    ])
    #expect(TorrentCoreMacDestination.connection.title == "Connection")
    #expect(TorrentCoreMacDestination.dashboard.systemImage.isEmpty == false)
    #expect(TorrentCoreMacDestination.torrents.systemImage.isEmpty == false)
}
