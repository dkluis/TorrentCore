import AppKit
import SwiftUI
import TorrentCoreAPI
import TorrentCoreFeatures

enum TorrentCoreMacDestination: String, CaseIterable, Identifiable {
    case dashboard
    case torrents
    case history
    case logs
    case serviceSettings
    case connection

    var id: String { rawValue }

    var title: String {
        switch self {
        case .dashboard:
            "Dashboard"
        case .torrents:
            "Torrents"
        case .history:
            "History"
        case .logs:
            "Logs"
        case .serviceSettings:
            "Service Settings"
        case .connection:
            "Connection"
        }
    }

    var systemImage: String {
        switch self {
        case .dashboard:
            "gauge.with.dots.needle.50percent"
        case .torrents:
            "arrow.down.circle"
        case .history:
            "clock.arrow.trianglehead.counterclockwise.rotate.90"
        case .logs:
            "doc.text.magnifyingglass"
        case .serviceSettings:
            "server.rack"
        case .connection:
            "network"
        }
    }
}

private struct TorrentCoreMacRefreshTaskID: Equatable {
    let context: TorrentCoreFeatureContext
    let profileID: UUID?
    let interval: TorrentCoreRefreshInterval
    let autoRefreshEnabled: Bool
    let isEnabled: Bool
    let isSceneActive: Bool
}

private struct TorrentCoreMacPollingAllowedKey: EnvironmentKey {
    static let defaultValue = true
}

private extension EnvironmentValues {
    var torrentCorePollingAllowed: Bool {
        get { self[TorrentCoreMacPollingAllowedKey.self] }
        set { self[TorrentCoreMacPollingAllowedKey.self] = newValue }
    }
}

private struct TorrentCoreMacVisibleRefreshModifier: ViewModifier {
    @Environment(\.scenePhase) private var scenePhase
    @Environment(\.torrentCorePollingAllowed) private var pollingAllowed

    let session: TorrentCoreFeatureSession
    let context: TorrentCoreFeatureContext
    let isEnabled: Bool

    func body(content: Content) -> some View {
        let taskID = TorrentCoreMacRefreshTaskID(
            context: context,
            profileID: session.activeProfile?.id,
            interval: session.preferences.refreshInterval,
            autoRefreshEnabled: session.preferences.autoRefreshEnabled,
            isEnabled: isEnabled && pollingAllowed,
            isSceneActive: scenePhase == .active
        )

        content.task(id: taskID) {
            guard taskID.isEnabled, taskID.isSceneActive, taskID.profileID != nil else {
                return
            }
            await session.refreshWhileVisible(context)
        }
    }
}

extension View {
    func torrentCoreRefreshWhileVisible(
        session: TorrentCoreFeatureSession,
        context: TorrentCoreFeatureContext,
        isEnabled: Bool = true
    ) -> some View {
        modifier(TorrentCoreMacVisibleRefreshModifier(
            session: session,
            context: context,
            isEnabled: isEnabled
        ))
    }
}

struct TorrentCoreMacContentView: View {
    @AppStorage("TorrentCore.Mac.SelectedDestination.v1")
    private var storedDestination = TorrentCoreMacDestination.dashboard.rawValue

    let session: TorrentCoreFeatureSession

    @State private var destination: TorrentCoreMacDestination
    @State private var selectedTorrentID: UUID?
    @State private var isTorrentInspectorPresented = false
    @State private var isAddMagnetPresented = false
    @State private var historyQuery = TorrentCoreMacHistoryView.defaultQuery
    @State private var selectedHistoryTorrentID: UUID?
    @State private var isHistoryInspectorPresented = false
    @State private var logQuery = TorrentCoreMacLogsView.defaultQuery
    @State private var selectedLogID: Int64?
    @State private var isLogInspectorPresented = false
    @State private var serviceSettingsDirty = false
    @State private var pendingDestination: TorrentCoreMacDestination?
    @State private var saveServiceSettings: (() async -> Bool)?
    @State private var discardServiceSettings: (() -> Void)?
    @State private var isNavigationSidebarPresented = true
    @State private var isLoaded = false
    @State private var loadError: String?
    @State private var toolbarActionError: String?

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
        mainWindow
            .environment(\.torrentCorePollingAllowed, !isAddMagnetPresented)
            .task {
                await loadIfNeeded()
            }
            .onChange(of: destination) { _, newValue in
                storedDestination = newValue.rawValue
                updateFeatureContext()
            }
            .alert(
                "Couldn’t Load Settings",
                isPresented: loadErrorPresented
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
            .alert(
                "Torrent Action Failed",
                isPresented: toolbarActionErrorPresented
            ) {
                Button("OK", role: .cancel) {}
            } message: {
                Text(toolbarActionError ?? "TorrentCore could not complete the torrent action.")
            }
            .confirmationDialog(
                "Save Service Settings Before Leaving?",
                isPresented: pendingDestinationConfirmation,
                titleVisibility: .visible
            ) {
                Button("Save") {
                    Task {
                        if await saveServiceSettings?() == true {
                            finishPendingNavigation()
                        }
                    }
                }
                Button("Discard Changes", role: .destructive) {
                    discardServiceSettings?()
                    serviceSettingsDirty = false
                    finishPendingNavigation()
                }
                Button("Cancel", role: .cancel) {
                    pendingDestination = nil
                }
            } message: {
                Text("The current service-settings group has unsaved changes.")
            }
            .sheet(isPresented: $isAddMagnetPresented) {
                TorrentCoreMacAddMagnetView(session: session) { _ in
                    selectedTorrentID = nil
                    isTorrentInspectorPresented = false
                    destination = .torrents
                }
            }
    }

    // Stable stack layout avoids restored split constraints changing while a
    // saved profile replaces the launch placeholder on macOS 27.
    private var mainWindow: some View {
        HStack(spacing: 0) {
            if isNavigationSidebarPresented {
                navigationSidebar
                Divider()
            }

            VStack(spacing: 0) {
                mainContent

                Divider()
                connectionStatusBar
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        .toolbar(id: "TorrentCore.MainToolbar.v7") {
            ToolbarItem(id: "navigationSidebar", placement: .navigation) {
                navigationSidebarButton
            }

            ToolbarItem(id: "addMagnet", placement: .navigation) {
                addMagnetButton
            }
            .customizationBehavior(.reorderable)

            ToolbarItem(id: "refresh", placement: .navigation) {
                manualRefreshButton
            }
            .customizationBehavior(.reorderable)

            ToolbarItem(id: "autoRefresh", placement: .secondaryAction) {
                refreshMenu
            }

            if destination == .torrents {
                ToolbarItem(id: "pauseTorrent", placement: .secondaryAction) {
                    pauseTorrentButton
                }

                ToolbarItem(id: "resumeTorrent", placement: .secondaryAction) {
                    resumeTorrentButton
                }

            }

            ToolbarItem(id: "torrentInspector", placement: .primaryAction) {
                inspectorButton
            }
            .hidden(!isInspectorControlAvailable)

        }
        .frame(minWidth: 1_000, minHeight: 650)
        .background {
            TorrentCoreMacEnvironmentBarAccessoryView(profile: session.activeProfile)
        }
        .focusedSceneValue(\.torrentCoreDestination, destinationSelection)
        .focusedSceneValue(\.torrentCoreInspectorCommand, inspectorCommand)
        .focusedSceneValue(\.torrentCoreSidebarCommand, sidebarCommand)
    }

    private var navigationSidebar: some View {
        List(TorrentCoreMacDestination.allCases, selection: destinationSelection) { item in
            Label(item.title, systemImage: item.systemImage)
                .tag(item)
                .accessibilityIdentifier("navigation.\(item.rawValue)")
        }
        .listStyle(.sidebar)
        .frame(width: 220)
        .accessibilityIdentifier("navigation.sidebar")
    }

    private var mainContent: some View {
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
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
        .navigationTitle(destination.title)
    }

    private var navigationSidebarButton: some View {
        Button {
            isNavigationSidebarPresented.toggle()
        } label: {
            Label(
                isNavigationSidebarPresented ? "Hide Sidebar" : "Show Sidebar",
                systemImage: "sidebar.left"
            )
        }
        .keyboardShortcut("s", modifiers: [.command, .control])
        .accessibilityIdentifier("toolbar.sidebar")
        .help(isNavigationSidebarPresented ? "Hide Sidebar" : "Show Sidebar")
    }

    private var sidebarCommand: TorrentCoreMacSidebarCommand {
        TorrentCoreMacSidebarCommand(
            isPresented: isNavigationSidebarPresented,
            toggle: { isNavigationSidebarPresented.toggle() }
        )
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
                contextChanged: updateFeatureContext,
                addMagnet: { isAddMagnetPresented = true },
                showHistory: showHistory,
                showLogs: showLogs
            )
        case .history:
            TorrentCoreMacHistoryView(
                session: session,
                query: $historyQuery,
                selectedTorrentID: $selectedHistoryTorrentID,
                isInspectorPresented: $isHistoryInspectorPresented,
                contextChanged: updateFeatureContext,
                showTorrent: showTorrent
            )
        case .logs:
            TorrentCoreMacLogsView(
                session: session,
                query: $logQuery,
                selectedLogID: $selectedLogID,
                isInspectorPresented: $isLogInspectorPresented,
                contextChanged: updateFeatureContext,
                showTorrent: showTorrent,
                showHistory: showHistory
            )
        case .serviceSettings:
            TorrentCoreMacServiceSettingsView(
                session: session,
                dirtyChanged: { serviceSettingsDirty = $0 },
                registerLeaveActions: { save, discard in
                    saveServiceSettings = save
                    discardServiceSettings = discard
                }
            )
        }
    }

    private var destinationSelection: Binding<TorrentCoreMacDestination> {
        Binding(
            get: { destination },
            set: { requestedDestination in
                requestNavigation(to: requestedDestination)
            }
        )
    }

    private var pendingDestinationConfirmation: Binding<Bool> {
        Binding(
            get: { pendingDestination != nil },
            set: { isPresented in
                if !isPresented {
                    pendingDestination = nil
                }
            }
        )
    }

    private var loadErrorPresented: Binding<Bool> {
        Binding(
            get: { loadError != nil },
            set: { isPresented in
                if !isPresented {
                    loadError = nil
                }
            }
        )
    }

    private var toolbarActionErrorPresented: Binding<Bool> {
        Binding(
            get: { toolbarActionError != nil },
            set: { isPresented in
                if !isPresented {
                    toolbarActionError = nil
                }
            }
        )
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

    private var addMagnetButton: some View {
        Button {
            isAddMagnetPresented = true
        } label: {
            Label("Add Magnet", systemImage: "plus")
        }
        .disabled(!session.connectionState.isConnected)
        .accessibilityIdentifier("toolbar.addMagnet")
        .help("Add a magnet to TorrentCore")
    }

    private var pauseTorrentButton: some View {
        Button {
            performToolbarTorrentAction { summary in
                _ = try await session.pause(summary)
            }
        } label: {
            Label("Pause", systemImage: "pause")
        }
        .disabled(!canPauseSelectedTorrent)
        .accessibilityIdentifier("torrents.pause")
        .help("Pause the selected torrent")
    }

    private var resumeTorrentButton: some View {
        Button {
            performToolbarTorrentAction { summary in
                _ = try await session.resume(summary)
            }
        } label: {
            Label("Resume", systemImage: "play")
        }
        .disabled(!canResumeSelectedTorrent)
        .accessibilityIdentifier("torrents.resume")
        .help("Resume the selected torrent")
    }

    private var inspectorButton: some View {
        Button {
            toggleActiveInspector()
        } label: {
            Label(
                isActiveInspectorPresented ? "Hide Inspector" : "Show Inspector",
                systemImage: "sidebar.trailing"
            )
        }
        .disabled(!isInspectorControlAvailable)
        .accessibilityIdentifier("toolbar.inspector")
        .help(isActiveInspectorPresented ? "Hide Inspector" : "Show Inspector")
    }

    private var isInspectorControlAvailable: Bool {
        switch destination {
        case .torrents:
            selectedTorrentID != nil || isTorrentInspectorPresented
        case .history:
            selectedHistoryTorrentID != nil || isHistoryInspectorPresented
        case .logs:
            selectedLogID != nil || isLogInspectorPresented
        case .dashboard, .serviceSettings, .connection:
            false
        }
    }

    private var isActiveInspectorPresented: Bool {
        switch destination {
        case .torrents:
            isTorrentInspectorPresented
        case .history:
            isHistoryInspectorPresented
        case .logs:
            isLogInspectorPresented
        case .dashboard, .serviceSettings, .connection:
            false
        }
    }

    private var inspectorCommand: TorrentCoreMacInspectorCommand {
        TorrentCoreMacInspectorCommand(
            isAvailable: isInspectorControlAvailable,
            isPresented: isActiveInspectorPresented,
            toggle: toggleActiveInspector
        )
    }

    private func toggleActiveInspector() {
        guard isInspectorControlAvailable else { return }
        switch destination {
        case .torrents:
            isTorrentInspectorPresented.toggle()
        case .history:
            isHistoryInspectorPresented.toggle()
        case .logs:
            isLogInspectorPresented.toggle()
        case .dashboard, .serviceSettings, .connection:
            return
        }
        updateFeatureContext()
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

    private var connectionStatusBar: some View {
        HStack(spacing: 8) {
            Text(connectionName)
                .fontWeight(.semibold)
            Text("→")
                .foregroundStyle(.tertiary)
            Text(connectionAddress)
                .foregroundStyle(.secondary)
            Text("→")
                .foregroundStyle(.tertiary)
            Text(connectionStateLabel)
                .foregroundStyle(connectionStateColor)
            Spacer(minLength: 0)
        }
        .font(.caption)
        .lineLimit(1)
        .textSelection(.enabled)
        .padding(.horizontal, 12)
        .padding(.vertical, 7)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(.bar)
        .accessibilityElement(children: .combine)
        .accessibilityLabel("\(connectionName), \(connectionAddress), \(connectionStateLabel)")
        .accessibilityIdentifier("status.connection")
    }

    private var selectedTorrentSummary: TorrentCoreTorrentSummary? {
        guard let selectedTorrentID else {
            return nil
        }
        return session.torrents.value?.first(where: {
            $0.torrentID == selectedTorrentID
        })
    }

    private var canPauseSelectedTorrent: Bool {
        guard let selectedTorrentSummary else {
            return false
        }
        return session.activeMutation == nil && session.canPause(selectedTorrentSummary)
    }

    private var canResumeSelectedTorrent: Bool {
        guard let selectedTorrentSummary else {
            return false
        }
        return session.activeMutation == nil && session.canResume(selectedTorrentSummary)
    }

    private func performToolbarTorrentAction(
        _ action: @escaping (TorrentCoreTorrentSummary) async throws -> Void
    ) {
        guard let selectedTorrentSummary else {
            return
        }
        Task {
            do {
                try await action(selectedTorrentSummary)
            } catch {
                toolbarActionError = TorrentCoreMacErrorPresenter.message(error)
            }
        }
    }

    private var connectionName: String {
        session.activeProfile?.name ?? "No TorrentCore Connection"
    }

    private var connectionAddress: String {
        session.activeProfile?.baseURL.absoluteString ?? "Select an installation"
    }

    private var connectionStateLabel: String {
        switch session.connectionState {
        case .connected:
            return "Connected"
        case .offline:
            return "Offline"
        case .connecting:
            return "Connecting"
        case .notConnected:
            return "Not Connected"
        case .noProfile:
            return "Not Connected"
        }
    }

    private var connectionStateColor: Color {
        switch session.connectionState {
        case .connected:
            .green
        case .offline:
            .orange
        case .connecting:
            .blue
        case .notConnected, .noProfile:
            .secondary
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
        case .history:
            session.setContext(.history(
                query: historyQuery,
                selectedTorrentID: isHistoryInspectorPresented
                    ? selectedHistoryTorrentID
                    : nil
            ))
        case .logs:
            session.setContext(.logs(logQuery))
        case .serviceSettings:
            session.setContext(.serviceSettings)
        }
    }

    private func finishPendingNavigation() {
        guard let pendingDestination else { return }
        serviceSettingsDirty = false
        self.pendingDestination = nil
        destination = pendingDestination
    }

    private func requestNavigation(to requestedDestination: TorrentCoreMacDestination) {
        guard requestedDestination != destination else { return }
        if destination == .serviceSettings, serviceSettingsDirty {
            pendingDestination = requestedDestination
        } else {
            destination = requestedDestination
        }
    }

    private func showTorrent(_ torrentID: UUID) {
        selectedTorrentID = torrentID
        isTorrentInspectorPresented = true
        destination = .torrents
    }

    private func showHistory(_ torrentID: UUID) {
        historyQuery = TorrentCoreHistoryQuery(torrentID: torrentID, take: 500)
        selectedHistoryTorrentID = torrentID
        isHistoryInspectorPresented = true
        destination = .history
    }

    private func showLogs(_ torrentID: UUID) {
        logQuery = TorrentCoreLogQuery(take: 1_000, torrentID: torrentID)
        selectedLogID = nil
        isLogInspectorPresented = false
        destination = .logs
    }
}

private struct TorrentCoreMacEnvironmentBar: View {
    let profile: TorrentCoreConnectionProfile?

    var body: some View {
        HStack(spacing: 8) {
            Image(systemName: environmentSymbol)
            Text(environmentTitle.uppercased())
                .fontWeight(.bold)
            Text("•")
            Text(profile?.name ?? "No Connection")
                .fontWeight(.semibold)
                .lineLimit(1)
            Spacer()
        }
        .font(.callout)
        .foregroundStyle(.white)
        .padding(.horizontal, 12)
        .frame(height: 34)
        .background(environmentColor, ignoresSafeAreaEdges: [])
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(
            "\(environmentTitle) environment, \(profile?.name ?? "No Connection")"
        )
        .accessibilityIdentifier("main.environmentBar")
    }

    private var environment: TorrentCoreConnectionEnvironment {
        profile?.environment ?? .unclassified
    }

    private var environmentTitle: String {
        environment.displayName
    }

    private var environmentSymbol: String {
        switch environment {
        case .production: "checkmark.shield.fill"
        case .test: "exclamationmark.triangle.fill"
        case .unclassified: "questionmark.circle.fill"
        }
    }

    private var environmentColor: Color {
        switch environment {
        case .production: .blue
        case .test: .red
        case .unclassified: .gray
        }
    }
}

private struct TorrentCoreMacEnvironmentBarAccessoryView: NSViewRepresentable {
    let profile: TorrentCoreConnectionProfile?

    func makeCoordinator() -> Coordinator {
        Coordinator(profile: profile)
    }

    func makeNSView(context: Context) -> NSView {
        let view = NSView()
        DispatchQueue.main.async {
            context.coordinator.attach(to: view.window)
        }
        return view
    }

    func updateNSView(_ nsView: NSView, context: Context) {
        context.coordinator.update(profile: profile)
        DispatchQueue.main.async {
            context.coordinator.attach(to: nsView.window)
        }
    }

    static func dismantleNSView(_ nsView: NSView, coordinator: Coordinator) {
        coordinator.detach()
    }

    @MainActor
    final class Coordinator {
        private weak var window: NSWindow?
        private let accessoryController: NSTitlebarAccessoryViewController
        private let containerView: EnvironmentBarContainerView

        init(profile: TorrentCoreConnectionProfile?) {
            containerView = EnvironmentBarContainerView(profile: profile)
            accessoryController = NSTitlebarAccessoryViewController()
            accessoryController.layoutAttribute = .bottom
            accessoryController.view = containerView
        }

        func update(profile: TorrentCoreConnectionProfile?) {
            containerView.update(profile: profile)
        }

        func attach(to newWindow: NSWindow?) {
            guard let newWindow, window !== newWindow else { return }
            detach()
            window = newWindow
            containerView.frame = NSRect(
                x: 0,
                y: 0,
                width: newWindow.frame.width,
                height: EnvironmentBarContainerView.height
            )
            newWindow.addTitlebarAccessoryViewController(accessoryController)
        }

        func detach() {
            guard let window,
                  let index = window.titlebarAccessoryViewControllers.firstIndex(where: {
                      $0 === accessoryController
                  }) else {
                self.window = nil
                return
            }
            window.removeTitlebarAccessoryViewController(at: index)
            self.window = nil
        }
    }

    @MainActor
    final class EnvironmentBarContainerView: NSView {
        static let height: CGFloat = 34

        private let hostingView: NSHostingView<TorrentCoreMacEnvironmentBar>

        init(profile: TorrentCoreConnectionProfile?) {
            hostingView = NSHostingView(
                rootView: TorrentCoreMacEnvironmentBar(profile: profile)
            )
            super.init(frame: NSRect(x: 0, y: 0, width: 1, height: Self.height))

            hostingView.translatesAutoresizingMaskIntoConstraints = false
            addSubview(hostingView)
            NSLayoutConstraint.activate([
                hostingView.leadingAnchor.constraint(equalTo: leadingAnchor),
                hostingView.trailingAnchor.constraint(equalTo: trailingAnchor),
                hostingView.topAnchor.constraint(equalTo: topAnchor),
                hostingView.bottomAnchor.constraint(equalTo: bottomAnchor),
            ])
        }

        @available(*, unavailable)
        required init?(coder: NSCoder) {
            fatalError("init(coder:) has not been implemented")
        }

        override var intrinsicContentSize: NSSize {
            NSSize(width: NSView.noIntrinsicMetric, height: Self.height)
        }

        func update(profile: TorrentCoreConnectionProfile?) {
            hostingView.rootView = TorrentCoreMacEnvironmentBar(profile: profile)
        }
    }
}

private struct TorrentCoreMacDestinationFocusedValueKey: FocusedValueKey {
    typealias Value = Binding<TorrentCoreMacDestination>
}

extension FocusedValues {
    var torrentCoreDestination: Binding<TorrentCoreMacDestination>? {
        get { self[TorrentCoreMacDestinationFocusedValueKey.self] }
        set { self[TorrentCoreMacDestinationFocusedValueKey.self] = newValue }
    }
}

struct TorrentCoreMacInspectorCommand {
    let isAvailable: Bool
    let isPresented: Bool
    let toggle: () -> Void
}

private struct TorrentCoreMacInspectorFocusedValueKey: FocusedValueKey {
    typealias Value = TorrentCoreMacInspectorCommand
}

extension FocusedValues {
    var torrentCoreInspectorCommand: TorrentCoreMacInspectorCommand? {
        get { self[TorrentCoreMacInspectorFocusedValueKey.self] }
        set { self[TorrentCoreMacInspectorFocusedValueKey.self] = newValue }
    }
}

struct TorrentCoreMacSidebarCommand {
    let isPresented: Bool
    let toggle: () -> Void
}

private struct TorrentCoreMacSidebarFocusedValueKey: FocusedValueKey {
    typealias Value = TorrentCoreMacSidebarCommand
}

extension FocusedValues {
    var torrentCoreSidebarCommand: TorrentCoreMacSidebarCommand? {
        get { self[TorrentCoreMacSidebarFocusedValueKey.self] }
        set { self[TorrentCoreMacSidebarFocusedValueKey.self] = newValue }
    }
}

struct TorrentCoreMacSidebarCommands: Commands {
    @FocusedValue(\.torrentCoreSidebarCommand)
    private var sidebar

    var body: some Commands {
        CommandGroup(replacing: .sidebar) {
            Button(sidebar?.isPresented == true ? "Hide Sidebar" : "Show Sidebar") {
                sidebar?.toggle()
            }
            .keyboardShortcut("s", modifiers: [.command, .control])
            .disabled(sidebar == nil)
        }
    }
}

struct TorrentCoreMacNavigationCommands: Commands {
    @FocusedValue(\.torrentCoreDestination)
    private var destination

    var body: some Commands {
        CommandMenu("Navigate") {
            navigationButton("Dashboard", destination: .dashboard, key: "1")
            navigationButton("Torrents", destination: .torrents, key: "2")
            navigationButton("History", destination: .history, key: "3")
            navigationButton("Logs", destination: .logs, key: "4")
            navigationButton("Service Settings", destination: .serviceSettings, key: "5")
            navigationButton("Connection", destination: .connection, key: "6")
        }
    }

    private func navigationButton(
        _ title: String,
        destination requestedDestination: TorrentCoreMacDestination,
        key: KeyEquivalent
    ) -> some View {
        Button(title) {
            destination?.wrappedValue = requestedDestination
        }
        .keyboardShortcut(key, modifiers: .command)
        .disabled(destination == nil)
    }
}

struct TorrentCoreMacInspectorCommands: Commands {
    @FocusedValue(\.torrentCoreInspectorCommand)
    private var inspector

    var body: some Commands {
        CommandGroup(after: .toolbar) {
            Button(inspector?.isPresented == true ? "Hide Inspector" : "Show Inspector") {
                inspector?.toggle()
            }
            .keyboardShortcut("i", modifiers: [.command, .option])
            .disabled(inspector?.isAvailable != true)
        }
    }
}
