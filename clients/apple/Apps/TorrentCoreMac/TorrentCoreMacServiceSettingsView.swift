import SwiftUI
import TorrentCoreAPI
import TorrentCoreFeatures

enum TorrentCoreMacCleanupDates {
    static func defaults(
        now: Date = Date(),
        calendar: Calendar = .current
    ) -> (logs: Date, history: Date) {
        let today = calendar.startOfDay(for: now)
        return (
            logs: calendar.date(byAdding: .day, value: -7, to: today) ?? today,
            history: calendar.date(byAdding: .day, value: -30, to: today) ?? today
        )
    }

    static func isFuture(
        _ value: Date,
        now: Date = Date(),
        calendar: Calendar = .current
    ) -> Bool {
        calendar.startOfDay(for: value) > calendar.startOfDay(for: now)
    }
}

struct TorrentCoreMacServiceSettingsView: View {
    private enum CleanupAction: String, Identifiable {
        case logEntries
        case historyRecords
        case orphanedTorrentLogs

        var id: String { rawValue }
    }

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
    private static let expressVPNRecoveryModes = [
        SettingChoice(value: "Disabled", label: "Disabled"),
        SettingChoice(value: "DirectIspOnly", label: "Direct ISP only"),
        SettingChoice(value: "AnyValidationFailure", label: "Any validation failure"),
    ]

    enum SettingsGroup: String, CaseIterable, Identifiable {
        case downloads
        case seedingCleanup
        case metadataRecovery
        case engine
        case vpnEgress
        case diagnostics
        case completionCallback
        case categories
        case cleanup

        var id: String { rawValue }

        var title: String {
            switch self {
            case .downloads: "Downloads"
            case .seedingCleanup: "Seeding & Cleanup"
            case .metadataRecovery: "Metadata Recovery"
            case .engine: "Engine"
            case .vpnEgress: "VPN Egress"
            case .diagnostics: "Diagnostics"
            case .completionCallback: "Completion Callback"
            case .categories: "Categories"
            case .cleanup: "Cleanup"
            }
        }

        var systemImage: String {
            switch self {
            case .downloads: "arrow.down.circle"
            case .seedingCleanup: "externaldrive.badge.checkmark"
            case .metadataRecovery: "arrow.trianglehead.2.clockwise.rotate.90"
            case .engine: "gearshape.2"
            case .vpnEgress: "network.badge.shield.half.filled"
            case .diagnostics: "waveform.path.ecg"
            case .completionCallback: "terminal"
            case .categories: "folder"
            case .cleanup: "trash"
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
    @State private var categoryDrafts: [String: TorrentCoreCategoryUpdate] = [:]
    @State private var isSaving = false
    @State private var isPerformingCleanup = false
    @State private var isRestartConfirmationPresented = false
    @State private var pendingCleanup: CleanupAction?
    @State private var logCleanupDate = Date()
    @State private var historyCleanupDate = Date()
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
        HStack(spacing: 0) {
            List(SettingsGroup.allCases, selection: groupSelection) { group in
                Label(group.title, systemImage: group.systemImage)
                    .tag(group)
            }
            .frame(width: 210)

            Divider()

            VStack(spacing: 0) {
                header
                Divider()

                if selectedGroup != .cleanup {
                    TorrentCoreMacPhaseBanner(
                        phase: selectedGroup == .categories
                            ? session.categories.phase
                            : session.runtimeSettings.phase,
                        lastSuccessfulAt: selectedGroup == .categories
                            ? session.categories.lastSuccessfulAt
                            : session.runtimeSettings.lastSuccessfulAt
                    )
                    .padding()
                }

                if hasLoadedSelectedGroup {
                    if selectedGroup == .cleanup {
                        ScrollView {
                            cleanupEditor
                        }
                    } else if selectedGroup == .categories {
                        ScrollView {
                            categoryEditor
                        }
                    } else {
                        Form {
                            selectedGroupContent
                        }
                        .formStyle(.grouped)
                    }
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
            if selectedGroup == .cleanup {
                resetCleanupDates()
            }
            registerLeaveActions(saveCurrentGroup, revertCurrentGroup)
            dirtyChanged(isDirty)
        }
        .task(id: session.activeProfile?.id) {
            guard session.activeProfile != nil else {
                return
            }
            await session.refresh(.serviceSettings)
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
        .onChange(of: selectedGroup) { _, value in
            if value == .cleanup {
                resetCleanupDates()
            }
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
        .confirmationDialog(
            cleanupConfirmationTitle,
            isPresented: Binding(
                get: { pendingCleanup != nil },
                set: { if !$0 { pendingCleanup = nil } }
            ),
            titleVisibility: .visible
        ) {
            Button(cleanupConfirmationButtonTitle, role: .destructive) {
                if let pendingCleanup {
                    performCleanup(pendingCleanup)
                }
            }
            Button("Cancel", role: .cancel) {
                pendingCleanup = nil
            }
        } message: {
            Text(cleanupConfirmationMessage)
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
                    Text(
                        selectedGroup == .cleanup
                            ? "Cleanup actions permanently delete eligible records from the connected TorrentCore installation."
                            : "Changes are saved to the connected TorrentCore installation."
                    )
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
            }
            Spacer()
            if isSaving || isPerformingCleanup || session.activeMutation != nil {
                ProgressView()
                    .controlSize(.small)
            }
            if selectedGroup != .cleanup {
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
            }
            Button(role: .destructive) {
                isRestartConfirmationPresented = true
            } label: {
                Label("Restart Service", systemImage: "arrow.clockwise.circle")
            }
            .disabled(
                isDirty
                    || isSaving
                    || isPerformingCleanup
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
                    integerField(
                        "Active downloads",
                        value: draft.maxActiveDownloads,
                        content: TorrentCoreHelpCatalog.Settings.maxActiveDownloads,
                        identifier: "serviceSettings.maxActiveDownloads"
                    )
                    integerField(
                        "Active metadata resolutions",
                        value: draft.maxActiveMetadataResolutions,
                        content: TorrentCoreHelpCatalog.Settings.maxActiveMetadataResolutions,
                        identifier: "serviceSettings.maxActiveMetadataResolutions"
                    )
                }
                validationMessage
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
                            .labelsHidden()
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
                            .labelsHidden()
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
                        .labelsHidden()
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
                Section("Metadata Resolution") {
                    integerField(
                        "Time slice minutes",
                        value: draft.metadataResolutionTimeSliceMinutes,
                        content: TorrentCoreHelpCatalog.Settings.metadataResolutionTimeSliceMinutes,
                        identifier: "serviceSettings.metadataResolutionTimeSliceMinutes"
                    )
                    integerField(
                        "Priority metadata attempts",
                        value: draft.priorityMetadataAttempts,
                        content: TorrentCoreHelpCatalog.Settings.priorityMetadataAttempts,
                        identifier: "serviceSettings.priorityMetadataAttempts"
                    )
                    integerField(
                        "Reset stuck threshold seconds",
                        value: draft.automaticMetadataResetStuckThresholdSeconds,
                        content: TorrentCoreHelpCatalog.Settings.automaticMetadataResetStuckThresholdSeconds,
                        identifier: "serviceSettings.automaticMetadataResetStuckThresholdSeconds"
                    )
                }
                Section("Download Rotation") {
                    integerField(
                        "No-progress time slice minutes",
                        value: draft.downloadNoProgressTimeSliceMinutes,
                        content: TorrentCoreHelpCatalog.Settings.downloadNoProgressTimeSliceMinutes,
                        identifier: "serviceSettings.downloadNoProgressTimeSliceMinutes"
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
                    Toggle(isOn: draft.engineAllowPeerExchange) {
                        TorrentCoreMacHelpLabel(
                            "Allow Peer Exchange (PEX)",
                            content: TorrentCoreHelpCatalog.Settings.engineAllowPeerExchange
                        )
                    }
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
                LabeledContent(
                    "Applied peer exchange",
                    value: session.runtimeSettings.value?.appliedEngineAllowPeerExchange == true
                        ? "Enabled"
                        : "Disabled"
                )
                validationMessage
            }
        case .vpnEgress:
            if let draft = runtimeBinding {
                Section("Validation") {
                    Toggle(isOn: draft.vpnEgressValidationEnabled) {
                        TorrentCoreMacHelpLabel(
                            "Enabled",
                            content: TorrentCoreHelpCatalog.Settings.vpnEgressValidationEnabled
                        )
                    }
                    stringField(
                        "Public IP endpoint",
                        text: draft.vpnEgressValidationEndpoint,
                        content: TorrentCoreHelpCatalog.Settings.vpnEgressValidationEndpoint
                    )
                    stringField(
                        "Direct ISP IPv4 CIDRs",
                        text: commaSeparatedStrings(draft.vpnEgressDirectIspCidrs),
                        content: TorrentCoreHelpCatalog.Settings.vpnEgressDirectIspCidrs
                    )
                }
                Section("Check Intervals") {
                    integerField(
                        "Degraded seconds",
                        value: draft.vpnEgressDegradedCheckIntervalSeconds,
                        content: TorrentCoreHelpCatalog.Settings.vpnEgressDegradedCheckIntervalSeconds
                    )
                    integerField(
                        "Ready seconds",
                        value: draft.vpnEgressReadyCheckIntervalSeconds,
                        content: TorrentCoreHelpCatalog.Settings.vpnEgressReadyCheckIntervalSeconds
                    )
                    integerField(
                        "Request timeout seconds",
                        value: draft.vpnEgressRequestTimeoutSeconds,
                        content: TorrentCoreHelpCatalog.Settings.vpnEgressRequestTimeoutSeconds
                    )
                    integerField(
                        "Engine suspension timeout seconds",
                        value: draft.vpnEgressEngineSuspensionTimeoutSeconds,
                        content: TorrentCoreHelpCatalog.Settings.vpnEgressEngineSuspensionTimeoutSeconds
                    )
                }
                Section("ExpressVPN Recovery") {
                    choiceField(
                        selection: draft.expressVPNAutomaticRecoveryMode,
                        choices: Self.expressVPNRecoveryModes,
                        content: TorrentCoreHelpCatalog.Settings.expressVPNAutomaticRecoveryMode,
                        identifier: "serviceSettings.expressVPNAutomaticRecoveryMode"
                    )
                    integerField(
                        "Recovery delay seconds",
                        value: draft.expressVPNRecoveryDelaySeconds,
                        content: TorrentCoreHelpCatalog.Settings.expressVPNRecoveryDelaySeconds,
                        identifier: "serviceSettings.expressVPNRecoveryDelaySeconds"
                    )
                    integerField(
                        "Unavailable launch delay seconds",
                        value: draft.expressVPNUnavailableLaunchDelaySeconds,
                        content: TorrentCoreHelpCatalog.Settings.expressVPNUnavailableLaunchDelaySeconds,
                        identifier: "serviceSettings.expressVPNUnavailableLaunchDelaySeconds"
                    )
                }
                Text(
                    "These values apply live. Interval edits take effect at the next scheduled check without resetting the current countdown."
                )
                .font(.caption)
                .foregroundStyle(.secondary)
                validationMessage
            }
        case .diagnostics:
            if let draft = runtimeBinding {
                Section("Performance Logging") {
                    Toggle(isOn: draft.runtimeTickDurationSummaryEnabled) {
                        TorrentCoreMacHelpLabel(
                            "Performance Timing Summaries",
                            content: TorrentCoreHelpCatalog.Settings.runtimeTickDurationSummaryEnabled
                        )
                    }
                }
                Text(
                    "This setting controls only the one-minute synchronization timing summary written to the Service log. Torrent processing and all other diagnostics remain unchanged."
                )
                .font(.caption)
                .foregroundStyle(.secondary)
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
                        .labelsHidden()
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
            EmptyView()
        case .cleanup:
            EmptyView()
        }
    }

    @ViewBuilder
    private var cleanupEditor: some View {
        VStack(alignment: .leading, spacing: 16) {
            GroupBox {
                VStack(alignment: .leading, spacing: 12) {
                    Text(
                        "Delete log entries strictly before Service-local 00:00:00 on the selected date. Logs associated with torrents still in the live torrent table are protected."
                    )
                    .font(.callout)
                    .foregroundStyle(.secondary)

                    DatePicker(
                        "Up To Date",
                        selection: $logCleanupDate,
                        displayedComponents: .date
                    )
                    .accessibilityIdentifier("serviceSettings.cleanup.logs.date")

                    Button("Delete Log Entries", role: .destructive) {
                        requestCleanup(.logEntries)
                    }
                    .accessibilityIdentifier("serviceSettings.cleanup.logs.delete")
                    .disabled(cleanupActionsDisabled)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.top, 4)
            } label: {
                TorrentCoreMacHelpLabel(
                    "Log Entries",
                    content: TorrentCoreHelpCatalog.Settings.cleanupLogEntries
                )
            }

            GroupBox {
                VStack(alignment: .leading, spacing: 12) {
                    Text(
                        "Delete history records whose Last Updated value is strictly before Service-local 00:00:00 on the selected date. Records associated with torrents still in the live torrent table are protected."
                    )
                    .font(.callout)
                    .foregroundStyle(.secondary)

                    DatePicker(
                        "Up To Date",
                        selection: $historyCleanupDate,
                        displayedComponents: .date
                    )
                    .accessibilityIdentifier("serviceSettings.cleanup.history.date")

                    Button("Delete History Records", role: .destructive) {
                        requestCleanup(.historyRecords)
                    }
                    .accessibilityIdentifier("serviceSettings.cleanup.history.delete")
                    .disabled(cleanupActionsDisabled)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.top, 4)
            } label: {
                TorrentCoreMacHelpLabel(
                    "History Records",
                    content: TorrentCoreHelpCatalog.Settings.cleanupHistoryRecords
                )
            }

            GroupBox {
                VStack(alignment: .leading, spacing: 12) {
                    Text(
                        "Delete torrent-scoped logs whose Torrent ID is no longer present in the live torrent table."
                    )
                    .font(.callout)
                    .foregroundStyle(.secondary)

                    Button("Delete Orphan Logs", role: .destructive) {
                        requestCleanup(.orphanedTorrentLogs)
                    }
                    .accessibilityIdentifier("serviceSettings.cleanup.orphanedLogs.delete")
                    .disabled(cleanupActionsDisabled)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.top, 4)
            } label: {
                TorrentCoreMacHelpLabel(
                    "Orphaned Torrent Logs",
                    content: TorrentCoreHelpCatalog.Settings.cleanupOrphanedTorrentLogs
                )
            }
        }
        .padding()
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    @ViewBuilder
    private var categoryEditor: some View {
        VStack(alignment: .leading, spacing: 12) {
            GroupBox("Existing Categories") {
                VStack(alignment: .leading, spacing: 10) {
                    ScrollView(.horizontal) {
                        categoryGrid
                    }
                    .accessibilityIdentifier("serviceSettings.categories.grid")

                    Text("Edit any category in place, then save all changed rows together.")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.top, 4)
            }
            .frame(maxWidth: .infinity, alignment: .leading)

            validationMessage
        }
        .padding([.horizontal, .bottom])
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private var categoryGrid: some View {
        Grid(alignment: .leading, horizontalSpacing: 12, verticalSpacing: 8) {
            GridRow {
                categoryColumnHeader("Key", width: 90)
                categoryColumnHeader(
                    content: TorrentCoreHelpCatalog.Settings.categoryDisplayName,
                    width: 150
                )
                categoryColumnHeader(
                    content: TorrentCoreHelpCatalog.Settings.categoryDownloadRootPath,
                    width: 300
                )
                categoryColumnHeader(
                    content: TorrentCoreHelpCatalog.Settings.categoryCallbackLabel,
                    width: 150
                )
                categoryColumnHeader(
                    content: TorrentCoreHelpCatalog.Settings.categoryEnabled,
                    width: 70
                )
                categoryColumnHeader(
                    "Completion callback",
                    content: TorrentCoreHelpCatalog.Settings
                        .categoryInvokeCompletionCallback,
                    width: 150
                )
                categoryColumnHeader(
                    content: TorrentCoreHelpCatalog.Settings.categorySortOrder,
                    width: 80
                )
            }

            Divider()
                .gridCellColumns(7)

            ForEach(session.categories.value ?? [], id: \.key) { category in
                if let key = category.key,
                   let draft = categoryBinding(for: key)
                {
                    GridRow {
                        Text(key)
                            .lineLimit(1)
                            .frame(width: 90, alignment: .leading)
                        TextField("Display name", text: draft.displayName)
                            .labelsHidden()
                            .frame(width: 150)
                            .accessibilityIdentifier(
                                "serviceSettings.category.\(key).displayName"
                            )
                        TextField("Download root path", text: draft.downloadRootPath)
                            .labelsHidden()
                            .frame(width: 300)
                            .accessibilityIdentifier(
                                "serviceSettings.category.\(key).downloadRootPath"
                            )
                        TextField("Callback label", text: draft.callbackLabel)
                            .labelsHidden()
                            .frame(width: 150)
                            .accessibilityIdentifier(
                                "serviceSettings.category.\(key).callbackLabel"
                            )
                        Toggle("Enabled", isOn: draft.enabled)
                            .labelsHidden()
                            .frame(width: 70)
                            .accessibilityIdentifier(
                                "serviceSettings.category.\(key).enabled"
                            )
                        Toggle(
                            "Invoke completion callback",
                            isOn: draft.invokeCompletionCallback
                        )
                        .labelsHidden()
                        .frame(width: 150)
                        .accessibilityIdentifier(
                            "serviceSettings.category.\(key).completionCallback"
                        )
                        TextField("Sort order", value: draft.sortOrder, format: .number)
                            .labelsHidden()
                            .frame(width: 80)
                            .accessibilityIdentifier(
                                "serviceSettings.category.\(key).sortOrder"
                            )
                    }

                    Divider()
                        .gridCellColumns(7)
                }
            }
        }
        .padding(.vertical, 4)
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
                    if value != .cleanup {
                        synchronizeDrafts(force: true)
                    }
                }
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

    private func categoryBinding(
        for key: String
    ) -> Binding<TorrentCoreCategoryUpdate>? {
        guard categoryDrafts[key] != nil else { return nil }
        return Binding(
            get: { categoryDrafts[key]! },
            set: { categoryDrafts[key] = $0 }
        )
    }

    private var isDirty: Bool {
        if selectedGroup == .cleanup {
            return false
        }
        if selectedGroup == .categories {
            return !changedCategoryDrafts.isEmpty
        }
        guard let settings = session.runtimeSettings.value, let runtimeDraft else {
            return false
        }
        return runtimeDraft != TorrentCoreRuntimeSettingsUpdate(settings: settings)
    }

    private var changedCategoryDrafts: [
        (key: String, draft: TorrentCoreCategoryUpdate)
    ] {
        (session.categories.value ?? []).compactMap { category in
            guard let key = category.key,
                  let draft = categoryDrafts[key],
                  draft != TorrentCoreCategoryUpdate(category: category)
            else {
                return nil
            }
            return (key, draft)
        }
    }

    private var hasLoadedSelectedGroup: Bool {
        switch selectedGroup {
        case .cleanup:
            session.connectionState.isConnected
        case .categories:
            session.categories.value != nil
        default:
            session.runtimeSettings.value != nil
        }
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
        if selectedGroup == .cleanup {
            return false
        }
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
        if selectedGroup == .cleanup {
            return nil
        }
        if selectedGroup == .categories {
            for category in session.categories.value ?? [] {
                guard let key = category.key else {
                    return "TorrentCore returned a category without a key."
                }
                guard let draft = categoryDrafts[key] else {
                    return "Category '\(key)' could not be edited."
                }
                let categoryName = draft.displayName.trimmingCharacters(
                    in: .whitespacesAndNewlines
                ).isEmpty ? key : draft.displayName
                if draft.displayName.trimmingCharacters(
                    in: .whitespacesAndNewlines
                ).isEmpty {
                    return "\(categoryName): display name is required."
                }
                if draft.downloadRootPath.trimmingCharacters(
                    in: .whitespacesAndNewlines
                ).isEmpty {
                    return "\(categoryName): download root path is required."
                }
                if draft.callbackLabel.trimmingCharacters(
                    in: .whitespacesAndNewlines
                ).isEmpty {
                    return "\(categoryName): callback label is required."
                }
                if draft.sortOrder < 0 {
                    return "\(categoryName): sort order must be 0 or greater."
                }
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
        if !(1...1_440).contains(draft.metadataResolutionTimeSliceMinutes) {
            return "Metadata resolution time slice must be between 1 and 1,440 minutes."
        }
        if !(1...10).contains(draft.priorityMetadataAttempts) {
            return "Priority metadata attempts must be between 1 and 10."
        }
        if !(1...60).contains(draft.downloadNoProgressTimeSliceMinutes) {
            return "Download no-progress time slice must be between 1 and 60 minutes."
        }
        if !(15...300).contains(draft.automaticMetadataResetStuckThresholdSeconds) {
            return "Automatic metadata reset stuck threshold must be between 15 and 300 seconds."
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
        guard let endpoint = URLComponents(
            string: draft.vpnEgressValidationEndpoint.trimmingCharacters(in: .whitespacesAndNewlines)
        ), endpoint.scheme?.lowercased() == "https", endpoint.host?.isEmpty == false,
           endpoint.user == nil, endpoint.password == nil, endpoint.fragment == nil
        else {
            return "VPN validation endpoint must be an absolute HTTPS URL without credentials or a fragment."
        }
        if draft.vpnEgressValidationEnabled && draft.vpnEgressDirectIspCidrs.isEmpty {
            return "At least one direct ISP IPv4 CIDR is required when VPN validation is enabled."
        }
        if draft.vpnEgressDegradedCheckIntervalSeconds < 1 {
            return "VPN degraded check interval must be 1 or greater."
        }
        if draft.vpnEgressReadyCheckIntervalSeconds < 1 {
            return "VPN ready check interval must be 1 or greater."
        }
        if draft.vpnEgressRequestTimeoutSeconds < 1 {
            return "VPN request timeout must be 1 or greater."
        }
        if draft.vpnEgressEngineSuspensionTimeoutSeconds < 1 {
            return "VPN engine suspension timeout must be 1 or greater."
        }
        if !Self.expressVPNRecoveryModes.contains(
            where: { $0.value == draft.expressVPNAutomaticRecoveryMode }
        ) {
            return "ExpressVPN automatic recovery mode is invalid."
        }
        if draft.expressVPNRecoveryDelaySeconds < 1 {
            return "ExpressVPN recovery delay must be 1 or greater."
        }
        if draft.expressVPNUnavailableLaunchDelaySeconds < 1 {
            return "ExpressVPN unavailable launch delay must be 1 or greater."
        }
        if draft.vpnEgressRequestTimeoutSeconds >= draft.vpnEgressDegradedCheckIntervalSeconds
            || draft.vpnEgressRequestTimeoutSeconds >= draft.vpnEgressReadyCheckIntervalSeconds
        {
            return "VPN request timeout must be shorter than both check intervals."
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
                .labelsHidden()
        } label: {
            TorrentCoreMacHelpLabel(label, content: content)
        }
    }

    private func integerField(
        _ label: String,
        value: Binding<Int>,
        content: TorrentCoreHelpContent,
        identifier: String? = nil
    ) -> some View {
        LabeledContent {
            TextField(label, value: value, format: .number)
                .labelsHidden()
                .frame(width: 140)
                .accessibilityIdentifier(identifier ?? label)
        } label: {
            TorrentCoreMacHelpLabel(label, content: content)
        }
    }

    private func categoryColumnHeader(
        content: TorrentCoreHelpContent,
        width: CGFloat
    ) -> some View {
        categoryColumnHeader(content.label, content: content, width: width)
    }

    @ViewBuilder
    private func categoryColumnHeader(
        _ title: String,
        content: TorrentCoreHelpContent? = nil,
        width: CGFloat
    ) -> some View {
        Group {
            if let content {
                TorrentCoreMacHelpLabel(title, content: content)
            } else {
                Text(title)
            }
        }
        .font(.caption.weight(.semibold))
        .frame(width: width, alignment: .leading)
    }

    private func optionalString(_ binding: Binding<String?>) -> Binding<String> {
        Binding(
            get: { binding.wrappedValue ?? "" },
            set: { binding.wrappedValue = $0.isEmpty ? nil : $0 }
        )
    }

    private func commaSeparatedStrings(_ binding: Binding<[String]>) -> Binding<String> {
        Binding(
            get: { binding.wrappedValue.joined(separator: ", ") },
            set: { value in
                binding.wrappedValue = value
                    .split(separator: ",", omittingEmptySubsequences: true)
                    .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
                    .filter { !$0.isEmpty }
            }
        )
    }

    private func synchronizeDrafts(force: Bool) {
        if (force || !isDirty), let settings = session.runtimeSettings.value {
            runtimeDraft = TorrentCoreRuntimeSettingsUpdate(settings: settings)
        }
        if force || !isDirty {
            loadCategoryDrafts()
        }
    }

    private func loadCategoryDrafts() {
        categoryDrafts = Dictionary(
            uniqueKeysWithValues: (session.categories.value ?? []).compactMap { category in
                guard let key = category.key else { return nil }
                return (key, TorrentCoreCategoryUpdate(category: category))
            }
        )
    }

    @MainActor
    private func saveCurrentGroup() async -> Bool {
        if selectedGroup == .cleanup {
            return true
        }
        guard isDirty, isCurrentDraftValid, !isSaving else { return !isDirty }
        isSaving = true
        actionError = nil
        actionMessage = nil
        defer { isSaving = false }

        do {
            if selectedGroup == .categories {
                let changes = changedCategoryDrafts
                guard !changes.isEmpty else {
                    return true
                }
                for change in changes {
                    _ = try await session.updateCategory(
                        key: change.key,
                        update: change.draft
                    )
                }
                loadCategoryDrafts()
                actionMessage = changes.count == 1
                    ? "Saved 1 category."
                    : "Saved \(changes.count) categories."
            } else {
                guard let runtimeDraft else { return false }
                let updated = try await session.updateRuntimeSettings(runtimeDraft)
                self.runtimeDraft = TorrentCoreRuntimeSettingsUpdate(settings: updated)
                actionMessage = "Saved \(selectedGroup.title)."
            }
            dirtyChanged(false)
            return true
        } catch {
            actionError = TorrentCoreMacErrorPresenter.message(error)
            return false
        }
    }

    @MainActor
    private func revertCurrentGroup() {
        if selectedGroup == .cleanup {
            return
        } else if selectedGroup == .categories {
            loadCategoryDrafts()
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
            if pendingGroup != .cleanup {
                synchronizeDrafts(force: true)
            }
        }
    }

    private var cleanupActionsDisabled: Bool {
        !session.connectionState.isConnected
            || isPerformingCleanup
            || session.activeMutation != nil
    }

    private var cleanupConfirmationTitle: String {
        switch pendingCleanup {
        case .logEntries:
            "Delete Log Entries?"
        case .historyRecords:
            "Delete History Records?"
        case .orphanedTorrentLogs:
            "Delete Orphan Logs?"
        case nil:
            "Confirm Cleanup"
        }
    }

    private var cleanupConfirmationButtonTitle: String {
        switch pendingCleanup {
        case .logEntries:
            "Delete Log Entries"
        case .historyRecords:
            "Delete History Records"
        case .orphanedTorrentLogs:
            "Delete Orphan Logs"
        case nil:
            "Delete"
        }
    }

    private var cleanupConfirmationMessage: String {
        switch pendingCleanup {
        case .logEntries:
            "Permanently delete eligible log entries before \(Self.cleanupDateFormatter.string(from: logCleanupDate)) at Service-local 00:00:00? Logs for torrents still in the live torrent table are protected."
        case .historyRecords:
            "Permanently delete eligible history records last updated before \(Self.cleanupDateFormatter.string(from: historyCleanupDate)) at Service-local 00:00:00? History for torrents still in the live torrent table is protected."
        case .orphanedTorrentLogs:
            "Permanently delete torrent-scoped logs whose Torrent ID is no longer present in the live torrent table?"
        case nil:
            "The selected cleanup permanently deletes eligible records."
        }
    }

    private func resetCleanupDates() {
        let defaults = TorrentCoreMacCleanupDates.defaults()
        logCleanupDate = defaults.logs
        historyCleanupDate = defaults.history
        pendingCleanup = nil
        actionMessage = nil
    }

    private func requestCleanup(_ action: CleanupAction) {
        if action == .logEntries, isFutureCleanupDate(logCleanupDate) {
            actionError = "Log Entries Up To Date cannot be in the future."
            return
        }
        if action == .historyRecords, isFutureCleanupDate(historyCleanupDate) {
            actionError = "History Records Up To Date cannot be in the future."
            return
        }
        pendingCleanup = action
    }

    private func isFutureCleanupDate(_ value: Date) -> Bool {
        TorrentCoreMacCleanupDates.isFuture(value)
    }

    private func performCleanup(_ action: CleanupAction) {
        pendingCleanup = nil
        Task {
            isPerformingCleanup = true
            actionError = nil
            actionMessage = nil
            defer { isPerformingCleanup = false }

            do {
                switch action {
                case .logEntries:
                    let result = try await session.cleanupLogs(
                        upToDate: Self.cleanupDateFormatter.string(from: logCleanupDate)
                    )
                    actionMessage = result.deletedRecordCount == 1
                        ? "Deleted 1 log entry."
                        : "Deleted \(result.deletedRecordCount) log entries."
                case .historyRecords:
                    let result = try await session.cleanupHistory(
                        upToDate: Self.cleanupDateFormatter.string(from: historyCleanupDate)
                    )
                    actionMessage = result.deletedRecordCount == 1
                        ? "Deleted 1 history record."
                        : "Deleted \(result.deletedRecordCount) history records."
                case .orphanedTorrentLogs:
                    let result = try await session.deleteOrphanedLogs()
                    actionMessage = result.deletedLogEntryCount == 1
                        ? "Deleted 1 orphaned torrent log entry."
                        : "Deleted \(result.deletedLogEntryCount) orphaned torrent log entries."
                }
            } catch {
                actionError = TorrentCoreMacErrorPresenter.message(error)
            }
        }
    }

    private static let cleanupDateFormatter: DateFormatter = {
        let formatter = DateFormatter()
        formatter.calendar = Calendar(identifier: .gregorian)
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = .current
        formatter.dateFormat = "yyyy-MM-dd"
        return formatter
    }()

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
