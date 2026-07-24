import SwiftUI
import TorrentCoreAPI
import TorrentCoreFeatures

struct TorrentCoreMacServiceSettingsView: View {
    private struct SettingChoice: Identifiable {
        let value: String
        let label: String

        var id: String { value }
    }

    private static let seedingStopModes = [
        SettingChoice(value: "Unlimited", label: "Unlimited"),
        SettingChoice(value: "StopImmediately", label: "Stop immediately"),
        SettingChoice(value: "StopAfterRatio", label: "Stop after ratio"),
        SettingChoice(value: "StopAfterTime", label: "Stop after time"),
        SettingChoice(value: "StopAfterRatioOrTime", label: "Stop after ratio or time"),
    ]
    private static let completedTorrentCleanupModes = [
        SettingChoice(value: "Never", label: "Never"),
        SettingChoice(value: "AfterCompletedMinutes", label: "After completion delay"),
    ]
    private static let engineEncryptionModes = [
        SettingChoice(value: "PlainTextPreferred", label: "Plain text preferred"),
        SettingChoice(value: "EncryptedPreferred", label: "Encrypted preferred"),
        SettingChoice(value: "EncryptedRequired", label: "Encrypted required"),
    ]

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
                    ContentUnavailableView {
                        Label(unavailableTitle, systemImage: unavailableSystemImage)
                    } description: {
                        Text(unavailableMessage)
                    } actions: {
                        if isLoadingSelectedGroup {
                            ProgressView()
                                .controlSize(.small)
                        }
                    }
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
            .disabled(
                !isDirty
                    || !isCurrentDraftValid
                    || isSaving
                    || !session.connectionState.isConnected
            )
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
                        value: draft.maxActiveDownloads,
                        in: 1...Int.max
                    ) {
                        TorrentCoreMacHelpLabel(
                            "Active downloads: \(draft.maxActiveDownloads.wrappedValue)",
                            content: TorrentCoreHelpCatalog.Settings.maxActiveDownloads
                        )
                    }
                    Stepper(
                        value: draft.maxActiveMetadataResolutions,
                        in: 1...Int.max
                    ) {
                        TorrentCoreMacHelpLabel(
                            "Active metadata resolutions: \(draft.maxActiveMetadataResolutions.wrappedValue)",
                            content: TorrentCoreHelpCatalog.Settings.maxActiveMetadataResolutions
                        )
                    }
                }
            }
        case .seedingCleanup:
            if let draft = runtimeBinding {
                Section("Seeding Stop Policy") {
                    choiceField(
                        selection: draft.seedingStopMode,
                        choices: Self.seedingStopModes,
                        content: TorrentCoreHelpCatalog.Settings.seedingStopMode,
                        identifier: "serviceSettings.seedingStopMode"
                    )
                    LabeledContent {
                        TextField("Ratio", value: draft.seedingStopRatio, format: .number)
                            .frame(width: 120)
                            .disabled(!seedingModeUsesRatio)
                    } label: {
                        TorrentCoreMacHelpLabel(
                            "Ratio",
                            content: TorrentCoreHelpCatalog.Settings.seedingStopRatio
                        )
                    }
                    LabeledContent {
                        TextField("Minutes", value: draft.seedingStopMinutes, format: .number)
                            .frame(width: 120)
                            .disabled(!seedingModeUsesTime)
                    } label: {
                        TorrentCoreMacHelpLabel(
                            "Minutes",
                            content: TorrentCoreHelpCatalog.Settings.seedingStopMinutes
                        )
                    }
                }
                Section("Completed Torrent Cleanup") {
                    choiceField(
                        selection: draft.completedTorrentCleanupMode,
                        choices: Self.completedTorrentCleanupModes,
                        content: TorrentCoreHelpCatalog.Settings.completedTorrentCleanupMode,
                        identifier: "serviceSettings.completedTorrentCleanupMode"
                    )
                    LabeledContent {
                        TextField(
                            "Minutes",
                            value: draft.completedTorrentCleanupMinutes,
                            format: .number
                        )
                        .frame(width: 120)
                        .disabled(!cleanupModeUsesMinutes)
                    } label: {
                        TorrentCoreMacHelpLabel(
                            "Minutes",
                            content: TorrentCoreHelpCatalog.Settings.completedTorrentCleanupMinutes
                        )
                    }
                    Toggle(isOn: draft.deleteLogsForCompletedTorrents) {
                        TorrentCoreMacHelpLabel(
                            "Delete logs for completed torrents",
                            content: TorrentCoreHelpCatalog.Settings.deleteLogsForCompletedTorrents
                        )
                    }
                }
                validationMessage
            }
        case .metadataRecovery:
            if let draft = runtimeBinding {
                Section("Metadata Refresh") {
                    integerField(
                        "Stale after seconds",
                        value: draft.metadataRefreshStaleSeconds,
                        content: TorrentCoreHelpCatalog.Settings.metadataRefreshStaleSeconds
                    )
                    integerField(
                        "Restart delay seconds",
                        value: draft.metadataRefreshRestartDelaySeconds,
                        content: TorrentCoreHelpCatalog.Settings.metadataRefreshRestartDelaySeconds
                    )
                }
                Section("Cold Download Recovery") {
                    integerField(
                        "Recovery threshold minutes",
                        value: draft.coldDownloadRecoveryThresholdMinutes,
                        content: TorrentCoreHelpCatalog.Settings.coldDownloadRecoveryThresholdMinutes
                    )
                    integerField(
                        "Recovery interval minutes",
                        value: draft.coldDownloadRecoveryIntervalMinutes,
                        content: TorrentCoreHelpCatalog.Settings.coldDownloadRecoveryIntervalMinutes
                    )
                    integerField(
                        "Abandon after hours",
                        value: draft.coldDownloadAbandonAfterHours,
                        content: TorrentCoreHelpCatalog.Settings.coldDownloadAbandonAfterHours
                    )
                }
                validationMessage
            }
        case .engine:
            if let draft = runtimeBinding {
                Section("MonoTorrent Engine") {
                    choiceField(
                        selection: draft.engineEncryptionMode,
                        choices: Self.engineEncryptionModes,
                        content: TorrentCoreHelpCatalog.Settings.engineEncryptionMode,
                        identifier: "serviceSettings.engineEncryptionMode"
                    )
                    integerField(
                        "Maximum connections",
                        value: draft.engineMaximumConnections,
                        content: TorrentCoreHelpCatalog.Settings.engineMaximumConnections
                    )
                    integerField(
                        "Maximum half-open connections",
                        value: draft.engineMaximumHalfOpenConnections,
                        content: TorrentCoreHelpCatalog.Settings.engineMaximumHalfOpenConnections
                    )
                    integerField(
                        "Maximum download bytes/second (0 = unlimited)",
                        value: draft.engineMaximumDownloadRateBytesPerSecond,
                        content: TorrentCoreHelpCatalog.Settings
                            .engineMaximumDownloadRateBytesPerSecond
                    )
                    integerField(
                        "Maximum upload bytes/second (0 = unlimited)",
                        value: draft.engineMaximumUploadRateBytesPerSecond,
                        content: TorrentCoreHelpCatalog.Settings
                            .engineMaximumUploadRateBytesPerSecond
                    )
                }
                Section("Connection Failure Logging") {
                    integerField(
                        "Burst limit",
                        value: draft.engineConnectionFailureLogBurstLimit,
                        content: TorrentCoreHelpCatalog.Settings
                            .engineConnectionFailureLogBurstLimit
                    )
                    integerField(
                        "Window seconds",
                        value: draft.engineConnectionFailureLogWindowSeconds,
                        content: TorrentCoreHelpCatalog.Settings
                            .engineConnectionFailureLogWindowSeconds
                    )
                }
                if session.runtimeSettings.value?.engineSettingsRequireRestart == true {
                    Label(
                        "The current engine settings require a service restart.",
                        systemImage: "arrow.clockwise.circle"
                    )
                    .foregroundStyle(.orange)
                }
                validationMessage
            }
        case .completionCallback:
            if let draft = runtimeBinding {
                Section("Callback") {
                    Toggle(isOn: draft.completionCallbackEnabled) {
                        TorrentCoreMacHelpLabel(
                            "Enabled",
                            content: TorrentCoreHelpCatalog.Settings.completionCallbackEnabled
                        )
                    }
                    stringField(
                        "Command path",
                        text: optionalString(draft.completionCallbackCommandPath),
                        content: TorrentCoreHelpCatalog.Settings.completionCallbackCommandPath
                    )
                    stringField(
                        "Arguments",
                        text: optionalString(draft.completionCallbackArguments),
                        content: TorrentCoreHelpCatalog.Settings.completionCallbackArguments
                    )
                    stringField(
                        "Working directory",
                        text: optionalString(draft.completionCallbackWorkingDirectory),
                        content: TorrentCoreHelpCatalog.Settings.completionCallbackWorkingDirectory
                    )
                    stringField(
                        "API base URL override",
                        text: optionalString(draft.completionCallbackAPIBaseURLOverride),
                        content: TorrentCoreHelpCatalog.Settings.completionCallbackAPIBaseURLOverride
                    )
                    LabeledContent {
                        SecureField(
                            "API key override",
                            text: optionalString(draft.completionCallbackAPIKeyOverride)
                        )
                        .privacySensitive()
                    } label: {
                        TorrentCoreMacHelpLabel(
                            "API key override",
                            content: TorrentCoreHelpCatalog.Settings.completionCallbackAPIKeyOverride
                        )
                    }
                    integerField(
                        "Timeout seconds",
                        value: draft.completionCallbackTimeoutSeconds,
                        content: TorrentCoreHelpCatalog.Settings.completionCallbackTimeoutSeconds
                    )
                    integerField(
                        "Finalization timeout seconds",
                        value: draft.completionCallbackFinalizationTimeoutSeconds,
                        content: TorrentCoreHelpCatalog.Settings
                            .completionCallbackFinalizationTimeoutSeconds
                    )
                }
                Text(
                    "The API key is kept only in this unsaved form and sent directly to TorrentCore when you save. The Mac app does not persist or log it."
                )
                .font(.caption)
                .foregroundStyle(.secondary)
                validationMessage
            }
        case .categories:
            categoryEditor
        }
    }

    @ViewBuilder
    private var categoryEditor: some View {
        Section("Existing Category") {
            Picker("Category", selection: categorySelection) {
                ForEach(session.categories.value ?? [], id: \.key) { category in
                    Text(category.displayName ?? category.key ?? "Category")
                        .tag(category.key)
                }
            }
            if let draft = categoryBinding {
                stringField(
                    "Display name",
                    text: draft.displayName,
                    content: TorrentCoreHelpCatalog.Settings.categoryDisplayName
                )
                stringField(
                    "Download root path",
                    text: draft.downloadRootPath,
                    content: TorrentCoreHelpCatalog.Settings.categoryDownloadRootPath
                )
                stringField(
                    "Callback label",
                    text: draft.callbackLabel,
                    content: TorrentCoreHelpCatalog.Settings.categoryCallbackLabel
                )
                Toggle(isOn: draft.enabled) {
                    TorrentCoreMacHelpLabel(
                        "Enabled",
                        content: TorrentCoreHelpCatalog.Settings.categoryEnabled
                    )
                }
                Toggle(isOn: draft.invokeCompletionCallback) {
                    TorrentCoreMacHelpLabel(
                        "Invoke completion callback",
                        content: TorrentCoreHelpCatalog.Settings
                            .categoryInvokeCompletionCallback
                    )
                }
                integerField(
                    "Sort order",
                    value: draft.sortOrder,
                    content: TorrentCoreHelpCatalog.Settings.categorySortOrder
                )
            }
        }
        validationMessage
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
        if isLoadingSelectedGroup {
            return "Requesting the current service settings from TorrentCore."
        }
        return switch session.connectionState {
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

    private var isLoadingSelectedGroup: Bool {
        let phase = selectedGroup == .categories
            ? session.categories.phase
            : session.runtimeSettings.phase
        if case .loading = phase {
            return true
        }
        return false
    }

    private var unavailableTitle: String {
        isLoadingSelectedGroup ? "Loading Service Settings" : "Service Settings Unavailable"
    }

    private var unavailableSystemImage: String {
        isLoadingSelectedGroup ? "arrow.trianglehead.2.clockwise" : "server.rack"
    }

    private var seedingModeUsesRatio: Bool {
        guard let mode = runtimeDraft?.seedingStopMode else { return false }
        return mode == "StopAfterRatio" || mode == "StopAfterRatioOrTime"
    }

    private var seedingModeUsesTime: Bool {
        guard let mode = runtimeDraft?.seedingStopMode else { return false }
        return mode == "StopAfterTime" || mode == "StopAfterRatioOrTime"
    }

    private var cleanupModeUsesMinutes: Bool {
        runtimeDraft?.completedTorrentCleanupMode == "AfterCompletedMinutes"
    }

    private var isCurrentDraftValid: Bool {
        validationError == nil
    }

    private var validationError: String? {
        if selectedGroup == .categories {
            guard let categoryDraft else { return nil }
            if categoryDraft.displayName.trimmingCharacters(
                in: .whitespacesAndNewlines
            ).isEmpty {
                return "Display name is required."
            }
            if categoryDraft.downloadRootPath.trimmingCharacters(
                in: .whitespacesAndNewlines
            ).isEmpty {
                return "Download root path is required."
            }
            if categoryDraft.callbackLabel.trimmingCharacters(
                in: .whitespacesAndNewlines
            ).isEmpty {
                return "Callback label is required."
            }
            if categoryDraft.sortOrder < 0 {
                return "Sort order must be 0 or greater."
            }
            return nil
        }

        guard let draft = runtimeDraft else { return nil }
        if !Self.seedingStopModes.contains(where: { $0.value == draft.seedingStopMode }) {
            return "Choose a supported seeding stop mode."
        }
        if !Self.completedTorrentCleanupModes.contains(
            where: { $0.value == draft.completedTorrentCleanupMode }
        ) {
            return "Choose a supported completed-torrent cleanup mode."
        }
        if !Self.engineEncryptionModes.contains(
            where: { $0.value == draft.engineEncryptionMode }
        ) {
            return "Choose a supported engine encryption mode."
        }
        if draft.seedingStopRatio <= 0 {
            return "Seeding stop ratio must be greater than 0."
        }
        if draft.seedingStopMinutes < 1 {
            return "Seeding stop minutes must be 1 or greater."
        }
        if draft.completedTorrentCleanupMinutes < 0 {
            return "Completed-torrent cleanup minutes must be 0 or greater."
        }
        if draft.maxActiveMetadataResolutions < 1 {
            return "Active metadata resolutions must be 1 or greater."
        }
        if draft.maxActiveDownloads < 1 {
            return "Active downloads must be 1 or greater."
        }
        if draft.metadataRefreshStaleSeconds < 1 {
            return "Metadata refresh stale seconds must be 1 or greater."
        }
        if draft.metadataRefreshRestartDelaySeconds < 1 {
            return "Metadata refresh restart delay must be 1 or greater."
        }
        if draft.coldDownloadRecoveryThresholdMinutes < 1 {
            return "Cold-download recovery threshold must be 1 or greater."
        }
        if draft.coldDownloadRecoveryIntervalMinutes < 1 {
            return "Cold-download recovery interval must be 1 or greater."
        }
        if draft.coldDownloadAbandonAfterHours < 0 {
            return "Cold-download abandonment hours must be 0 or greater."
        }
        if draft.engineConnectionFailureLogBurstLimit < 1 {
            return "Connection-failure burst limit must be 1 or greater."
        }
        if draft.engineConnectionFailureLogWindowSeconds < 1 {
            return "Connection-failure window must be 1 or greater."
        }
        if draft.engineMaximumConnections < 1 {
            return "Maximum connections must be 1 or greater."
        }
        if draft.engineMaximumHalfOpenConnections < 1 {
            return "Maximum half-open connections must be 1 or greater."
        }
        if draft.engineMaximumDownloadRateBytesPerSecond < 0 {
            return "Maximum download rate must be 0 or greater."
        }
        if draft.engineMaximumUploadRateBytesPerSecond < 0 {
            return "Maximum upload rate must be 0 or greater."
        }
        if draft.completionCallbackTimeoutSeconds < 1 {
            return "Callback timeout must be 1 or greater."
        }
        if draft.completionCallbackFinalizationTimeoutSeconds < 1 {
            return "Callback finalization timeout must be 1 or greater."
        }
        if draft.completionCallbackEnabled,
           draft.completionCallbackCommandPath?.trimmingCharacters(
               in: .whitespacesAndNewlines
           ).isEmpty != false
        {
            return "Command path is required when the completion callback is enabled."
        }
        return nil
    }

    @ViewBuilder
    private var validationMessage: some View {
        if let validationError {
            Label(validationError, systemImage: "exclamationmark.triangle")
                .font(.caption)
                .foregroundStyle(.red)
                .accessibilityIdentifier("serviceSettings.validationError")
        }
    }

    private func choiceField(
        selection: Binding<String>,
        choices: [SettingChoice],
        content: TorrentCoreHelpContent,
        identifier: String
    ) -> some View {
        LabeledContent {
            Picker(content.label, selection: selection) {
                ForEach(choices) { choice in
                    Text(choice.label).tag(choice.value)
                }
                if !choices.contains(where: { $0.value == selection.wrappedValue }) {
                    Text("Unsupported (\(selection.wrappedValue))")
                        .tag(selection.wrappedValue)
                }
            }
            .labelsHidden()
            .frame(minWidth: 210)
            .accessibilityIdentifier(identifier)
        } label: {
            TorrentCoreMacHelpLabel(content: content)
        }
    }

    private func stringField(
        _ label: String,
        text: Binding<String>,
        content: TorrentCoreHelpContent
    ) -> some View {
        LabeledContent {
            TextField(label, text: text)
        } label: {
            TorrentCoreMacHelpLabel(label, content: content)
        }
    }

    private func integerField(
        _ label: String,
        value: Binding<Int>,
        content: TorrentCoreHelpContent
    ) -> some View {
        LabeledContent {
            TextField(label, value: value, format: .number)
                .frame(width: 140)
        } label: {
            TorrentCoreMacHelpLabel(label, content: content)
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
        guard isDirty, isCurrentDraftValid, !isSaving else { return !isDirty }
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
