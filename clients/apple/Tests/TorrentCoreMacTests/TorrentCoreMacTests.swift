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
