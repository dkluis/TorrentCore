import SwiftUI
import TorrentCoreAPI
import TorrentCoreFeatures

struct TorrentCoreMacServiceSettingsView: View {
    enum SettingsGroup: String, CaseIterable, Identifiable {
        case downloads
        case seedingCleanup
        case metadataRecovery
        case engine
        case completionCallback
        case categories

        var id: String { rawValue }

        var title: String {
            switch self {
            case .downloads: "Downloads"
            case .seedingCleanup: "Seeding & Cleanup"
            case .metadataRecovery: "Metadata Recovery"
            case .engine: "Engine"
            case .completionCallback: "Completion Callback"
            case .categories: "Categories"
            }
        }

        var systemImage: String {
            switch self {
            case .downloads: "arrow.down.circle"
            case .seedingCleanup: "externaldrive.badge.checkmark"
            case .metadataRecovery: "arrow.trianglehead.2.clockwise.rotate.90"
            case .engine: "gearshape.2"
            case .completionCallback: "terminal"
            case .categories: "folder"
            }
        }
    }

    let session: TorrentCoreFeatureSession
    let dirtyChanged: (Bool) -> Void
    let registerLeaveActions: (
        @escaping @MainActor () async -> Bool,
        @escaping @MainActor () -> Void
    ) -> Void

    @State private var selectedGroup = SettingsGroup.downloads
    @State private var pendingGroup: SettingsGroup?
    @State private var runtimeDraft: TorrentCoreRuntimeSettingsUpdate?
    @State private var selectedCategoryKey: String?
    @State private var categoryDraft: TorrentCoreCategoryUpdate?
    @State private var isSaving = false
    @State private var isRestartConfirmationPresented = false
    @State private var actionMessage: String?
    @State private var actionError: String?

    init(
        session: TorrentCoreFeatureSession,
        dirtyChanged: @escaping (Bool) -> Void = { _ in },
        registerLeaveActions: @escaping (
            @escaping @MainActor () async -> Bool,
            @escaping @MainActor () -> Void
        ) -> Void = { _, _ in }
    ) {
        self.session = session
        self.dirtyChanged = dirtyChanged
        self.registerLeaveActions = registerLeaveActions
    }

    var body: some View {
        HSplitView {
            List(SettingsGroup.allCases, selection: groupSelection) { group in
                Label(group.title, systemImage: group.systemImage)
                    .tag(group)
            }
            .frame(minWidth: 190, idealWidth: 210, maxWidth: 240)

            VStack(spacing: 0) {
                header
                Divider()

                TorrentCoreMacPhaseBanner(
                    phase: selectedGroup == .categories
                        ? session.categories.phase
                        : session.runtimeSettings.phase,
                    lastSuccessfulAt: selectedGroup == .categories
                        ? session.categories.lastSuccessfulAt
                        : session.runtimeSettings.lastSuccessfulAt
                )
                .padding()

                if hasLoadedSelectedGroup {
                    Form {
                        selectedGroupContent
                    }
                    .formStyle(.grouped)
                } else {
                    ContentUnavailableView(
                        "Service Settings Unavailable",
                        systemImage: "server.rack",
                        description: Text(unavailableMessage)
                    )
                }
            }
        }
        .onAppear {
            synchronizeDrafts(force: true)
            registerLeaveActions(saveCurrentGroup, revertCurrentGroup)
            dirtyChanged(isDirty)
        }
        .onChange(of: session.runtimeSettings.value) { _, _ in
            synchronizeDrafts(force: false)
        }
        .onChange(of: session.categories.value) { _, _ in
            synchronizeDrafts(force: false)
        }
        .onChange(of: isDirty) { _, value in
            dirtyChanged(value)
        }
        .confirmationDialog(
            "Save Changes Before Switching Groups?",
            isPresented: Binding(
                get: { pendingGroup != nil },
                set: { if !$0 { pendingGroup = nil } }
            ),
            titleVisibility: .visible
        ) {
            Button("Save") {
                Task {
                    if await saveCurrentGroup() {
                        finishPendingGroupChange()
                    }
                }
            }
            Button("Discard Changes", role: .destructive) {
                revertCurrentGroup()
                finishPendingGroupChange()
            }
            Button("Cancel", role: .cancel) {
                pendingGroup = nil
            }
        } message: {
            Text("Only one service-settings group can have unsaved changes at a time.")
        }
        .confirmationDialog(
            "Restart TorrentCore Service?",
            isPresented: $isRestartConfirmationPresented,
            titleVisibility: .visible
        ) {
            Button("Restart Service", role: .destructive) {
                restartService()
            }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text(
                "TorrentCore will be briefly unavailable. The app will wait up to about 30 seconds for it to return."
            )
        }
        .alert(
            "Service Settings Action Failed",
            isPresented: Binding(
                get: { actionError != nil },
                set: { if !$0 { actionError = nil } }
            )
        ) {
            Button("OK", role: .cancel) {}
        } message: {
            Text(actionError ?? "TorrentCore could not complete the settings action.")
        }
    }

    private var header: some View {
        HStack {
            VStack(alignment: .leading, spacing: 3) {
                Text(selectedGroup.title)
                    .font(.title2.weight(.semibold))
                if let actionMessage {
                    Text(actionMessage)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                } else {
                    Text("Changes are saved to the connected TorrentCore installation.")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }
            Spacer()
            if isSaving || session.activeMutation != nil {
                ProgressView()
                    .controlSize(.small)
            }
            Button("Revert", action: revertCurrentGroup)
                .disabled(!isDirty || isSaving)
            Button("Save") {
                Task { _ = await saveCurrentGroup() }
            }
            .keyboardShortcut("s", modifiers: .command)
            .disabled(!isDirty || isSaving || !session.connectionState.isConnected)
            Button(role: .destructive) {
                isRestartConfirmationPresented = true
            } label: {
                Label("Restart Service", systemImage: "arrow.clockwise.circle")
            }
            .disabled(
                isDirty
                    || isSaving
                    || !session.connectionState.isConnected
                    || session.activeMutation != nil
            )
        }
        .padding()
    }

    @ViewBuilder
    private var selectedGroupContent: some View {
        switch selectedGroup {
        case .downloads:
            if let draft = runtimeBinding {
                Section("Concurrency") {
                    Stepper(
                        "Active downloads: \(draft.maxActiveDownloads.wrappedValue)",
                        value: draft.maxActiveDownloads,
                        in: 1...100
                    )
                    Stepper(
                        "Active metadata resolutions: \(draft.maxActiveMetadataResolutions.wrappedValue)",
                        value: draft.maxActiveMetadataResolutions,
                        in: 1...100
                    )
                }
            }
        case .seedingCleanup:
            if let draft = runtimeBinding {
                Section("Seeding Stop Policy") {
                    TextField("Mode", text: draft.seedingStopMode)
                    LabeledContent("Ratio") {
                        TextField("Ratio", value: draft.seedingStopRatio, format: .number)
                            .frame(width: 120)
                    }
                    LabeledContent("Minutes") {
                        TextField("Minutes", value: draft.seedingStopMinutes, format: .number)
                            .frame(width: 120)
                    }
                }
                Section("Completed Torrent Cleanup") {
                    TextField("Mode", text: draft.completedTorrentCleanupMode)
                    LabeledContent("Minutes") {
                        TextField(
                            "Minutes",
                            value: draft.completedTorrentCleanupMinutes,
                            format: .number
                        )
                        .frame(width: 120)
                    }
                    Toggle(
                        "Delete logs for completed torrents",
                        isOn: draft.deleteLogsForCompletedTorrents
                    )
                }
            }
        case .metadataRecovery:
            if let draft = runtimeBinding {
                Section("Metadata Refresh") {
                    integerField(
                        "Stale after seconds",
                        value: draft.metadataRefreshStaleSeconds
                    )
                    integerField(
                        "Restart delay seconds",
                        value: draft.metadataRefreshRestartDelaySeconds
                    )
                }
                Section("Cold Download Recovery") {
                    integerField(
                        "Recovery threshold minutes",
                        value: draft.coldDownloadRecoveryThresholdMinutes
                    )
                    integerField(
                        "Recovery interval minutes",
                        value: draft.coldDownloadRecoveryIntervalMinutes
                    )
                    integerField(
                        "Abandon after hours",
                        value: draft.coldDownloadAbandonAfterHours
                    )
                }
            }
        case .engine:
            if let draft = runtimeBinding {
                Section("MonoTorrent Engine") {
                    TextField("Encryption mode", text: draft.engineEncryptionMode)
                    integerField(
                        "Maximum connections",
                        value: draft.engineMaximumConnections
                    )
                    integerField(
                        "Maximum half-open connections",
                        value: draft.engineMaximumHalfOpenConnections
                    )
                    integerField(
                        "Maximum download bytes/second (0 = unlimited)",
                        value: draft.engineMaximumDownloadRateBytesPerSecond
                    )
                    integerField(
                        "Maximum upload bytes/second (0 = unlimited)",
                        value: draft.engineMaximumUploadRateBytesPerSecond
                    )
                }
                Section("Connection Failure Logging") {
                    integerField(
                        "Burst limit",
                        value: draft.engineConnectionFailureLogBurstLimit
                    )
                    integerField(
                        "Window seconds",
                        value: draft.engineConnectionFailureLogWindowSeconds
                    )
                }
                if session.runtimeSettings.value?.engineSettingsRequireRestart == true {
                    Label(
                        "The current engine settings require a service restart.",
                        systemImage: "arrow.clockwise.circle"
                    )
                    .foregroundStyle(.orange)
                }
            }
        case .completionCallback:
            if let draft = runtimeBinding {
                Section("Callback") {
                    Toggle("Enabled", isOn: draft.completionCallbackEnabled)
                    TextField(
                        "Command path",
                        text: optionalString(draft.completionCallbackCommandPath)
                    )
                    TextField(
                        "Arguments",
                        text: optionalString(draft.completionCallbackArguments)
                    )
                    TextField(
                        "Working directory",
                        text: optionalString(draft.completionCallbackWorkingDirectory)
                    )
                    TextField(
                        "API base URL override",
                        text: optionalString(draft.completionCallbackAPIBaseURLOverride)
                    )
                    SecureField(
                        "API key override",
                        text: optionalString(draft.completionCallbackAPIKeyOverride)
                    )
                    .privacySensitive()
                    integerField(
                        "Timeout seconds",
                        value: draft.completionCallbackTimeoutSeconds
                    )
                    integerField(
                        "Finalization timeout seconds",
                        value: draft.completionCallbackFinalizationTimeoutSeconds
                    )
                }
                Text(
                    "The API key is kept only in this unsaved form and sent directly to TorrentCore when you save. The Mac app does not persist or log it."
                )
                .font(.caption)
                .foregroundStyle(.secondary)
            }
        case .categories:
            categoryEditor
        }
    }

    private var categoryEditor: some View {
        Section("Existing Category") {
            Picker("Category", selection: categorySelection) {
                ForEach(session.categories.value ?? [], id: \.key) { category in
                    Text(category.displayName ?? category.key ?? "Category")
                        .tag(category.key)
                }
            }
            if let draft = categoryBinding {
                TextField("Display name", text: draft.displayName)
                TextField("Download root path", text: draft.downloadRootPath)
                TextField("Callback label", text: draft.callbackLabel)
                Toggle("Enabled", isOn: draft.enabled)
                Toggle("Invoke completion callback", isOn: draft.invokeCompletionCallback)
                integerField("Sort order", value: draft.sortOrder)
            }
        }
    }

    private var groupSelection: Binding<SettingsGroup?> {
        Binding(
            get: { selectedGroup },
            set: { value in
                guard let value, value != selectedGroup else { return }
                if isDirty {
                    pendingGroup = value
                } else {
                    selectedGroup = value
                    synchronizeDrafts(force: true)
                }
            }
        )
    }

    private var categorySelection: Binding<String?> {
        Binding(
            get: { selectedCategoryKey },
            set: { key in
                guard key != selectedCategoryKey else { return }
                if isDirty {
                    return
                }
                selectedCategoryKey = key
                loadCategoryDraft()
            }
        )
    }

    private var runtimeBinding: Binding<TorrentCoreRuntimeSettingsUpdate>? {
        guard runtimeDraft != nil else { return nil }
        return Binding(
            get: { runtimeDraft! },
            set: { runtimeDraft = $0 }
        )
    }

    private var categoryBinding: Binding<TorrentCoreCategoryUpdate>? {
        guard categoryDraft != nil else { return nil }
        return Binding(
            get: { categoryDraft! },
            set: { categoryDraft = $0 }
        )
    }

    private var isDirty: Bool {
        if selectedGroup == .categories {
            guard let category = selectedCategory,
                  let categoryDraft
            else {
                return false
            }
            return categoryDraft != TorrentCoreCategoryUpdate(category: category)
        }
        guard let settings = session.runtimeSettings.value, let runtimeDraft else {
            return false
        }
        return runtimeDraft != TorrentCoreRuntimeSettingsUpdate(settings: settings)
    }

    private var selectedCategory: TorrentCoreCategory? {
        session.categories.value?.first(where: { $0.key == selectedCategoryKey })
    }

    private var hasLoadedSelectedGroup: Bool {
        selectedGroup == .categories
            ? session.categories.value != nil
            : session.runtimeSettings.value != nil
    }

    private var unavailableMessage: String {
        switch session.connectionState {
        case .noProfile:
            "Create or select a connection before loading service settings."
        case let .offline(_, _, message):
            message
        case .connecting:
            "Checking TorrentCore.Service…"
        case .notConnected:
            "Refresh to connect to the selected TorrentCore installation."
        case .connected:
            "TorrentCore did not return service settings."
        }
    }

    @ViewBuilder
    private func integerField(_ label: String, value: Binding<Int>) -> some View {
        LabeledContent(label) {
            TextField(label, value: value, format: .number)
                .frame(width: 140)
        }
    }

    private func optionalString(_ binding: Binding<String?>) -> Binding<String> {
        Binding(
            get: { binding.wrappedValue ?? "" },
            set: { binding.wrappedValue = $0.isEmpty ? nil : $0 }
        )
    }

    private func synchronizeDrafts(force: Bool) {
        if (force || !isDirty), let settings = session.runtimeSettings.value {
            runtimeDraft = TorrentCoreRuntimeSettingsUpdate(settings: settings)
        }
        if selectedCategoryKey == nil {
            selectedCategoryKey = session.categories.value?.first?.key
        }
        if force || !isDirty {
            loadCategoryDraft()
        }
    }

    private func loadCategoryDraft() {
        categoryDraft = selectedCategory.map(TorrentCoreCategoryUpdate.init(category:))
    }

    @MainActor
    private func saveCurrentGroup() async -> Bool {
        guard isDirty, !isSaving else { return !isDirty }
        isSaving = true
        actionError = nil
        actionMessage = nil
        defer { isSaving = false }

        do {
            if selectedGroup == .categories {
                guard let key = selectedCategoryKey, let categoryDraft else {
                    return false
                }
                _ = try await session.updateCategory(key: key, update: categoryDraft)
                loadCategoryDraft()
            } else {
                guard let runtimeDraft else { return false }
                let updated = try await session.updateRuntimeSettings(runtimeDraft)
                self.runtimeDraft = TorrentCoreRuntimeSettingsUpdate(settings: updated)
            }
            actionMessage = "Saved \(selectedGroup.title)."
            dirtyChanged(false)
            return true
        } catch {
            actionError = TorrentCoreMacErrorPresenter.message(error)
            return false
        }
    }

    @MainActor
    private func revertCurrentGroup() {
        if selectedGroup == .categories {
            loadCategoryDraft()
        } else if let settings = session.runtimeSettings.value {
            runtimeDraft = TorrentCoreRuntimeSettingsUpdate(settings: settings)
        }
        actionMessage = "Reverted unsaved \(selectedGroup.title) changes."
        dirtyChanged(false)
    }

    private func finishPendingGroupChange() {
        if let pendingGroup {
            selectedGroup = pendingGroup
            self.pendingGroup = nil
            synchronizeDrafts(force: true)
        }
    }

    private func restartService() {
        Task {
            do {
                _ = try await session.restartService()
                actionMessage = "TorrentCore restarted and reconnected."
            } catch {
                actionError = TorrentCoreMacErrorPresenter.message(error)
            }
        }
    }
}
