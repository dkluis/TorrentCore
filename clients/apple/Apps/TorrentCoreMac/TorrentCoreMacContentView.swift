import SwiftUI
import TorrentCoreFeatures

enum TorrentCoreMacDestination: String, CaseIterable, Identifiable {
    case connection
    case dashboard
    case torrents

    var id: String { rawValue }

    var title: String {
        switch self {
        case .connection:
            "Connection"
        case .dashboard:
            "Dashboard"
        case .torrents:
            "Torrents"
        }
    }

    var systemImage: String {
        switch self {
        case .connection:
            "network"
        case .dashboard:
            "gauge.with.dots.needle.50percent"
        case .torrents:
            "arrow.down.circle"
        }
    }
}

struct TorrentCoreMacContentView: View {
    @Environment(\.scenePhase) private var scenePhase
    @AppStorage("TorrentCore.Mac.SelectedDestination.v1")
    private var storedDestination = TorrentCoreMacDestination.dashboard.rawValue

    let session: TorrentCoreFeatureSession

    @State private var destination: TorrentCoreMacDestination
    @State private var selectedTorrentID: UUID?
    @State private var isTorrentInspectorPresented = false
    @State private var isLoaded = false
    @State private var loadError: String?

    init(session: TorrentCoreFeatureSession) {
        self.session = session
        let stored = UserDefaults.standard.string(
            forKey: "TorrentCore.Mac.SelectedDestination.v1"
        )
        _destination = State(
            initialValue: TorrentCoreMacDestination(rawValue: stored ?? "") ?? .dashboard
        )
    }

    var body: some View {
        NavigationSplitView {
            List(TorrentCoreMacDestination.allCases, selection: $destination) { item in
                Label(item.title, systemImage: item.systemImage)
                    .tag(item)
                    .accessibilityIdentifier("navigation.\(item.rawValue)")
            }
            .navigationTitle("TorrentCore")
            .navigationSplitViewColumnWidth(min: 180, ideal: 210, max: 260)
        } detail: {
            Group {
                if !isLoaded {
                    ContentUnavailableView {
                        Label("Loading TorrentCore", systemImage: "arrow.trianglehead.2.clockwise")
                    } description: {
                        Text("Reading the saved connection settings.")
                    } actions: {
                        ProgressView()
                            .controlSize(.small)
                    }
                } else {
                    selectedDestinationView
                }
            }
            .navigationTitle(destination.title)
            .toolbar {
                ToolbarItemGroup(placement: .primaryAction) {
                    refreshMenu
                    manualRefreshButton
                    connectionStatusButton
                }
            }
        }
        .frame(minWidth: 1_000, minHeight: 650)
        .task {
            await loadIfNeeded()
        }
        .onChange(of: destination) { _, newValue in
            storedDestination = newValue.rawValue
            updateFeatureContext()
        }
        .onChange(of: scenePhase) { _, newValue in
            session.setApplicationActive(newValue == .active)
        }
        .alert(
            "Couldn’t Load Settings",
            isPresented: Binding(
                get: { loadError != nil },
                set: { if !$0 { loadError = nil } }
            )
        ) {
            Button("Retry") {
                Task {
                    isLoaded = false
                    await loadIfNeeded()
                }
            }
            Button("Dismiss", role: .cancel) {}
        } message: {
            Text(loadError ?? "The saved settings could not be read.")
        }
    }

    @ViewBuilder
    private var selectedDestinationView: some View {
        switch destination {
        case .connection:
            TorrentCoreMacConnectionView(session: session)
        case .dashboard:
            TorrentCoreMacDashboardView(session: session)
        case .torrents:
            TorrentCoreMacTorrentsView(
                session: session,
                selectedTorrentID: $selectedTorrentID,
                isInspectorPresented: $isTorrentInspectorPresented,
                contextChanged: updateFeatureContext
            )
        }
    }

    private var manualRefreshButton: some View {
        Button {
            Task {
                await session.refresh()
            }
        } label: {
            Label("Refresh", systemImage: "arrow.clockwise")
        }
        .keyboardShortcut("r", modifiers: .command)
        .disabled(session.activeProfile == nil || !isLoaded)
        .accessibilityIdentifier("toolbar.refresh")
        .help("Refresh \(destination.title)")
    }

    private var refreshMenu: some View {
        Menu {
            Button {
                Task {
                    try? await session.setAutoRefreshEnabled(
                        !session.preferences.autoRefreshEnabled
                    )
                }
            } label: {
                Label(
                    session.preferences.autoRefreshEnabled
                        ? "Turn Off Auto Refresh"
                        : "Turn On Auto Refresh",
                    systemImage: session.preferences.autoRefreshEnabled
                        ? "pause.circle"
                        : "play.circle"
                )
            }

            Divider()

            ForEach(TorrentCoreRefreshInterval.allCases, id: \.self) { interval in
                Button {
                    Task {
                        try? await session.setRefreshInterval(interval)
                    }
                } label: {
                    if session.preferences.refreshInterval == interval {
                        Label("\(interval.rawValue) seconds", systemImage: "checkmark")
                    } else {
                        Text("\(interval.rawValue) seconds")
                    }
                }
            }
        } label: {
            Label(
                "\(session.preferences.refreshInterval.rawValue)s",
                systemImage: session.preferences.autoRefreshEnabled
                    ? "timer"
                    : "timer.square"
            )
        }
        .accessibilityIdentifier("toolbar.refreshMenu")
        .help("Auto refresh settings")
    }

    private var connectionStatusButton: some View {
        Button {
            destination = .connection
        } label: {
            Label(connectionLabel, systemImage: connectionSystemImage)
        }
        .accessibilityIdentifier("toolbar.connectionStatus")
        .help("Manage TorrentCore connections")
    }

    private var connectionLabel: String {
        guard let profile = session.activeProfile else {
            return "No Connection"
        }
        switch session.connectionState {
        case .connected:
            return "\(profile.name) · Connected"
        case .offline:
            return "\(profile.name) · Offline"
        case .connecting:
            return "\(profile.name) · Connecting"
        case .notConnected:
            return "\(profile.name) · Not Connected"
        case .noProfile:
            return "No Connection"
        }
    }

    private var connectionSystemImage: String {
        switch session.connectionState {
        case .connected:
            "checkmark.circle"
        case .offline:
            "network.slash"
        case .connecting:
            "arrow.trianglehead.2.clockwise"
        case .notConnected, .noProfile:
            "network"
        }
    }

    @MainActor
    private func loadIfNeeded() async {
        guard !isLoaded else {
            return
        }
        do {
            try await session.load()
            isLoaded = true
            loadError = nil
            if session.activeProfile == nil {
                destination = .connection
            }
            session.setApplicationActive(scenePhase == .active)
            updateFeatureContext()
        } catch {
            loadError = TorrentCoreMacErrorPresenter.message(error)
        }
    }

    private func updateFeatureContext() {
        guard isLoaded else {
            return
        }
        switch destination {
        case .connection:
            session.setContext(.connection)
        case .dashboard:
            session.setContext(.dashboard)
        case .torrents:
            if isTorrentInspectorPresented, let selectedTorrentID {
                session.setContext(.torrentListAndDetail(selectedTorrentID))
            } else {
                session.setContext(.torrents)
            }
        }
    }
}
