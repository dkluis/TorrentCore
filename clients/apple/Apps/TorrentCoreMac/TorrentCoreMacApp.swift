import SwiftUI
import TorrentCoreFeatures

@main
@MainActor
struct TorrentCoreMacApp: App {
    @State private var session: TorrentCoreFeatureSession

    init() {
        let usesFixtures = ProcessInfo.processInfo.arguments.contains("--torrentcore-ui-fixtures")
        if usesFixtures {
            UserDefaults.standard.set(
                TorrentCoreMacDestination.dashboard.rawValue,
                forKey: "TorrentCore.Mac.SelectedDestination.v1"
            )
        }
        let session: TorrentCoreFeatureSession
        if usesFixtures, let fixtureSession = try? TorrentCoreFixtureEnvironment.makeSession() {
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
        }

        Settings {
            TorrentCoreMacSettingsView(session: session)
        }
    }
}
