import SwiftUI
import TorrentCoreFeatures

enum TorrentCoreMacAppearance: String, CaseIterable, Identifiable {
    static let storageKey = "TorrentCore.Mac.Appearance.v1"

    case system
    case light
    case dark

    var id: String { rawValue }

    var title: String {
        switch self {
        case .system:
            "System"
        case .light:
            "Light"
        case .dark:
            "Dark"
        }
    }

    var colorScheme: ColorScheme? {
        switch self {
        case .system:
            nil
        case .light:
            .light
        case .dark:
            .dark
        }
    }
}

struct TorrentCoreMacSettingsView: View {
    let session: TorrentCoreFeatureSession

    @AppStorage(TorrentCoreMacAppearance.storageKey)
    private var appearanceRawValue = TorrentCoreMacAppearance.system.rawValue
    @State private var errorMessage: String?

    var body: some View {
        Form {
            Section("Appearance") {
                Picker("Appearance", selection: appearanceSelection) {
                    ForEach(TorrentCoreMacAppearance.allCases) { appearance in
                        Text(appearance.title).tag(appearance)
                    }
                }
                .pickerStyle(.segmented)
                .accessibilityIdentifier("settings.appearance")

                Text("System follows the appearance selected in macOS. Light and Dark apply only to TorrentCore on this Mac.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            Section("Refresh") {
                Toggle(
                    "Auto Refresh",
                    isOn: Binding(
                        get: { session.preferences.autoRefreshEnabled },
                        set: { value in
                            Task {
                                await update {
                                    try await session.setAutoRefreshEnabled(value)
                                }
                            }
                        }
                    )
                )
                .accessibilityIdentifier("settings.autoRefresh")

                Picker(
                    "Interval",
                    selection: Binding(
                        get: { session.preferences.refreshInterval },
                        set: { value in
                            Task {
                                await update {
                                    try await session.setRefreshInterval(value)
                                }
                            }
                        }
                    )
                ) {
                    ForEach(TorrentCoreRefreshInterval.allCases, id: \.self) { interval in
                        Text("\(interval.rawValue) seconds").tag(interval)
                    }
                }
                .disabled(!session.preferences.autoRefreshEnabled)
                .accessibilityIdentifier("settings.refreshInterval")

                Text("Refresh applies only to the open screen while TorrentCore is in the foreground. Manual Refresh remains available when Auto Refresh is off.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            Section("Connections") {
                LabeledContent("Active Connection") {
                    Text(session.activeProfile?.name ?? "None")
                }
                LabeledContent("Service Address") {
                    Text(session.activeProfile?.baseURL.absoluteString ?? "—")
                        .textSelection(.enabled)
                }
                Text("Create, edit, test, select, or delete connections from the Connection screen in the main window.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }

            if let errorMessage {
                Section {
                    Label(errorMessage, systemImage: "exclamationmark.triangle")
                        .foregroundStyle(.red)
                }
            }
        }
        .formStyle(.grouped)
        .padding(12)
        .frame(width: 520, height: 440)
    }

    private var appearanceSelection: Binding<TorrentCoreMacAppearance> {
        Binding(
            get: {
                TorrentCoreMacAppearance(rawValue: appearanceRawValue) ?? .system
            },
            set: { appearanceRawValue = $0.rawValue }
        )
    }

    @MainActor
    private func update(
        _ operation: @escaping @MainActor () async throws -> Void
    ) async {
        do {
            try await operation()
            errorMessage = nil
        } catch {
            errorMessage = TorrentCoreMacErrorPresenter.message(error)
        }
    }
}
