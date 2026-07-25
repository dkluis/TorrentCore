import SwiftUI
import TorrentCoreFeatures

@main
@MainActor
struct TorrentCoreMacApp: App {
    @State private var session: TorrentCoreFeatureSession

    init() {
        let arguments = ProcessInfo.processInfo.arguments
        let usesLargeFixtures = arguments.contains(
            "--torrentcore-ui-large-fixtures"
        )
        let usesFixtures = usesLargeFixtures
            || arguments.contains("--torrentcore-ui-fixtures")
        if usesFixtures {
            UserDefaults.standard.set(
                TorrentCoreMacDestination.dashboard.rawValue,
                forKey: "TorrentCore.Mac.SelectedDestination.v1"
            )
        }
        if usesLargeFixtures {
            UserDefaults.standard.set(
                TorrentCoreTorrentPageSize.twentyFive.rawValue,
                forKey: "TorrentCore.Mac.Torrents.PageSize.v1"
            )
            UserDefaults.standard.set(
                50,
                forKey: "TorrentCore.Mac.History.PageSize.v1"
            )
        }
        let session: TorrentCoreFeatureSession
        if usesFixtures,
           let fixtureSession = try? TorrentCoreFixtureEnvironment.makeSession(
               largeCollections: usesLargeFixtures
           )
        {
            session = fixtureSession
        } else {
            session = TorrentCoreFeatureSession()
        }
        _session = State(initialValue: session)
    }

    var body: some Scene {
        WindowGroup {
            TorrentCoreMacContentView(session: session)
        }
        .defaultSize(width: 1_180, height: 760)
        .commands {
            TorrentCoreMacNavigationCommands()
            TorrentCoreMacInspectorCommands()
            ToolbarCommands()
        }

        Settings {
            TorrentCoreMacSettingsView(session: session)
        }
    }
}
