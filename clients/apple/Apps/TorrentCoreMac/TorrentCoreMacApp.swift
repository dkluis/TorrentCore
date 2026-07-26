import SwiftUI
import TorrentCoreFeatures

@main
@MainActor
struct TorrentCoreMacApp: App {
    @State private var session: TorrentCoreFeatureSession
    @AppStorage(TorrentCoreMacAppearance.storageKey)
    private var appearanceRawValue = TorrentCoreMacAppearance.system.rawValue
    private let usesFixtures: Bool

    init() {
        let arguments = ProcessInfo.processInfo.arguments
        let usesLargeFixtures = arguments.contains(
            "--torrentcore-ui-large-fixtures"
        )
        let usesFixtures = usesLargeFixtures
            || arguments.contains("--torrentcore-ui-fixtures")
        self.usesFixtures = usesFixtures
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
        Window("TorrentCore", id: "main") {
            TorrentCoreMacContentView(session: session)
                .preferredColorScheme(appearance.colorScheme)
        }
        .defaultSize(width: 1_180, height: 760)
        .defaultPosition(.center)
        .restorationBehavior(usesFixtures ? .disabled : .automatic)
        .commands {
            TorrentCoreMacWindowCommands()
            TorrentCoreMacNavigationCommands()
            TorrentCoreMacInspectorCommands()
            ToolbarCommands()
        }

        Settings {
            TorrentCoreMacSettingsView(session: session)
                .preferredColorScheme(appearance.colorScheme)
        }
    }

    private var appearance: TorrentCoreMacAppearance {
        TorrentCoreMacAppearance(rawValue: appearanceRawValue) ?? .system
    }
}

private struct TorrentCoreMacWindowCommands: Commands {
    @Environment(\.openWindow) private var openWindow

    var body: some Commands {
        CommandGroup(replacing: .newItem) {
            Button("Show Main Window") {
                openWindow(id: "main")
            }
            .keyboardShortcut("n", modifiers: .command)
        }
    }
}
