import SwiftUI
import TorrentCoreFeatures

struct TorrentCoreMacConnectionView: View {
    let session: TorrentCoreFeatureSession

    @State private var selectedProfileID: UUID?
    @State private var name = ""
    @State private var address = ""
    @State private var isCreating = false
    @State private var isWorking = false
    @State private var feedback: Feedback?
    @State private var profilePendingDeletion: TorrentCoreConnectionProfile?

    var body: some View {
        HSplitView {
            VStack(spacing: 0) {
                List(session.preferences.profiles, selection: $selectedProfileID) { profile in
                    HStack {
                        VStack(alignment: .leading, spacing: 2) {
                            Text(profile.name)
                            Text(profile.baseURL.absoluteString)
                                .font(.caption)
                                .foregroundStyle(.secondary)
                                .lineLimit(1)
                        }
                        Spacer()
                        if profile.id == session.preferences.activeProfileID {
                            Image(systemName: "checkmark.circle.fill")
                                .foregroundStyle(.green)
                                .accessibilityLabel("Active connection")
                        }
                    }
                    .tag(profile.id)
                    .accessibilityIdentifier("connection.row.\(profile.id.uuidString)")
                }
                .accessibilityIdentifier("connection.list")

                Divider()

                HStack {
                    Button {
                        beginNewConnection()
                    } label: {
                        Label("New Connection", systemImage: "plus")
                    }
                    .accessibilityIdentifier("connection.new")

                    Spacer()

                    Button(role: .destructive) {
                        profilePendingDeletion = selectedProfile
                    } label: {
                        Label("Delete", systemImage: "trash")
                    }
                    .disabled(selectedProfile == nil || isWorking)
                    .accessibilityIdentifier("connection.delete")
                }
                .padding(10)
            }
            .frame(minWidth: 280, idealWidth: 330, maxWidth: 420)

            Form {
                Section {
                    TextField("Name", text: $name)
                        .accessibilityIdentifier("connection.name")
                    LabeledContent {
                        TextField("Service Address", text: $address)
                            .textContentType(.URL)
                            .accessibilityIdentifier("connection.address")
                    } label: {
                        TorrentCoreMacHelpLabel(
                            content: TorrentCoreHelpCatalog.Connection.serviceBaseURL
                        )
                    }
                    Text("Enter an HTTP or HTTPS hostname or IP address with an optional port. The connection can be saved while its LAN or VPN is unavailable.")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                } header: {
                    TorrentCoreMacHelpSectionTitle(
                        title: "TorrentCore Connection",
                        content: TorrentCoreHelpCatalog.Connection.currentEndpoint
                    )
                }

                Section {
                    connectionState
                }

                if let feedback {
                    Section {
                        Label(feedback.message, systemImage: feedback.isError
                            ? "exclamationmark.triangle"
                            : "checkmark.circle")
                            .foregroundStyle(feedback.isError ? .red : .green)
                            .accessibilityIdentifier(
                                feedback.isError ? "connection.error" : "connection.success"
                            )
                    }
                }

                Section {
                    HStack {
                        Button("Test Connection") {
                            testConnection()
                        }
                        .disabled(isWorking || address.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                        .accessibilityIdentifier("connection.test")
                        .help(TorrentCoreHelpCatalog.Connection.test.summary)

                        Button("Use Connection") {
                            activateSelectedConnection()
                        }
                        .disabled(isWorking || selectedProfile == nil)
                        .accessibilityIdentifier("connection.activate")

                        Spacer()

                        Button("Save & Connect") {
                            saveAndConnect()
                        }
                        .buttonStyle(.borderedProminent)
                        .disabled(
                            isWorking
                                || name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
                                || address.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
                        )
                        .accessibilityIdentifier("connection.save")
                        .help(TorrentCoreHelpCatalog.Connection.save.summary)
                    }
                }
            }
            .formStyle(.grouped)
            .frame(minWidth: 520)
        }
        .onAppear {
            initializeSelectionIfNeeded()
        }
        .task {
            guard session.activeProfile != nil else {
                return
            }
            await session.refresh(.connection)
        }
        .onChange(of: selectedProfileID) { _, _ in
            loadSelectedProfile()
        }
        .onChange(of: session.preferences.profiles) { _, profiles in
            if let selectedProfileID,
               !profiles.contains(where: { $0.id == selectedProfileID })
            {
                self.selectedProfileID = session.preferences.activeProfileID
            }
        }
        .confirmationDialog(
            "Delete Connection?",
            isPresented: Binding(
                get: { profilePendingDeletion != nil },
                set: { if !$0 { profilePendingDeletion = nil } }
            ),
            titleVisibility: .visible
        ) {
            Button("Delete Connection", role: .destructive) {
                deletePendingConnection()
            }
            Button("Cancel", role: .cancel) {
                profilePendingDeletion = nil
            }
        } message: {
            if let profilePendingDeletion {
                Text("Delete “\(profilePendingDeletion.name)” from this Mac? TorrentCore.Service is not changed.")
            }
        }
    }

    @ViewBuilder
    private var connectionState: some View {
        switch session.connectionState {
        case .noProfile:
            Label("No active connection", systemImage: "network")
                .foregroundStyle(.secondary)
        case .notConnected:
            Label("Not connected", systemImage: "network")
                .foregroundStyle(.secondary)
        case .connecting:
            HStack {
                ProgressView()
                    .controlSize(.small)
                Text("Connecting…")
            }
        case .connected:
            Label("Connected", systemImage: "checkmark.circle.fill")
                .foregroundStyle(.green)
        case let .offline(_, attemptedAt, message):
            VStack(alignment: .leading, spacing: 4) {
                Label("Offline", systemImage: "network.slash")
                    .foregroundStyle(.orange)
                Text(message)
                    .font(.caption)
                Text("Last attempted \(TorrentCoreDisplayFormatter.timestamp(attemptedAt))")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
    }

    private var selectedProfile: TorrentCoreConnectionProfile? {
        guard let selectedProfileID else {
            return nil
        }
        return session.preferences.profiles.first { $0.id == selectedProfileID }
    }

    private func initializeSelectionIfNeeded() {
        guard selectedProfileID == nil, !isCreating else {
            return
        }
        selectedProfileID = session.preferences.activeProfileID
            ?? session.preferences.profiles.first?.id
        if selectedProfileID == nil {
            beginNewConnection()
        } else {
            loadSelectedProfile()
        }
    }

    private func loadSelectedProfile() {
        guard let profile = selectedProfile else {
            if !isCreating {
                name = ""
                address = ""
            }
            return
        }
        isCreating = false
        name = profile.name
        address = profile.baseURL.absoluteString
        feedback = nil
    }

    private func beginNewConnection() {
        selectedProfileID = nil
        isCreating = true
        name = ""
        address = ""
        feedback = nil
    }

    private func testConnection() {
        isWorking = true
        feedback = nil
        Task {
            defer { isWorking = false }
            do {
                let health = try await session.testConnection(address: address)
                let serviceName = health.serviceName ?? "TorrentCore.Service"
                feedback = Feedback(
                    message: "Connected to \(serviceName) at the entered address.",
                    isError: false
                )
            } catch {
                feedback = Feedback(
                    message: TorrentCoreMacErrorPresenter.message(error),
                    isError: true
                )
            }
        }
    }

    private func saveAndConnect() {
        isWorking = true
        feedback = nil
        Task {
            defer { isWorking = false }
            do {
                let profile: TorrentCoreConnectionProfile
                if let selectedProfileID, !isCreating {
                    profile = try await session.updateProfile(
                        id: selectedProfileID,
                        name: name,
                        address: address
                    )
                    try await session.selectProfile(id: profile.id)
                } else {
                    profile = try await session.addProfile(
                        name: name,
                        address: address,
                        makeActive: true
                    )
                }
                self.selectedProfileID = profile.id
                isCreating = false
                await session.refresh(.connection)

                if session.connectionState.isConnected {
                    feedback = Feedback(
                        message: "Saved and connected to \(profile.name).",
                        isError: false
                    )
                } else {
                    feedback = Feedback(
                        message: "The connection was saved, but the service is currently unavailable.",
                        isError: true
                    )
                }
            } catch {
                feedback = Feedback(
                    message: TorrentCoreMacErrorPresenter.message(error),
                    isError: true
                )
            }
        }
    }

    private func activateSelectedConnection() {
        guard let selectedProfile else {
            return
        }
        isWorking = true
        feedback = nil
        Task {
            defer { isWorking = false }
            do {
                try await session.selectProfile(id: selectedProfile.id)
                await session.refresh(.connection)
                feedback = Feedback(
                    message: session.connectionState.isConnected
                        ? "Connected to \(selectedProfile.name)."
                        : "\(selectedProfile.name) is selected but currently unavailable.",
                    isError: !session.connectionState.isConnected
                )
            } catch {
                feedback = Feedback(
                    message: TorrentCoreMacErrorPresenter.message(error),
                    isError: true
                )
            }
        }
    }

    private func deletePendingConnection() {
        guard let profile = profilePendingDeletion else {
            return
        }
        profilePendingDeletion = nil
        isWorking = true
        Task {
            defer { isWorking = false }
            do {
                try await session.removeProfile(id: profile.id)
                selectedProfileID = session.preferences.activeProfileID
                if selectedProfileID == nil {
                    beginNewConnection()
                }
                feedback = Feedback(
                    message: "Deleted \(profile.name) from this Mac.",
                    isError: false
                )
            } catch {
                feedback = Feedback(
                    message: TorrentCoreMacErrorPresenter.message(error),
                    isError: true
                )
            }
        }
    }
}

private extension TorrentCoreMacConnectionView {
    struct Feedback {
        let message: String
        let isError: Bool
    }
}
