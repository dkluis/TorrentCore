import SwiftUI
import TorrentCoreAPI
import TorrentCoreFeatures

struct TorrentCoreMacPeersSheet: View {
    @Environment(\.dismiss) private var dismiss

    let session: TorrentCoreFeatureSession
    let torrentID: UUID
    let torrentName: String
    let restoreContext: () -> Void

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                VStack(alignment: .leading) {
                    Text("Peers")
                        .font(.title2.weight(.semibold))
                    Text(torrentName)
                        .foregroundStyle(.secondary)
                }
                Spacer()
                Button {
                    Task { await session.refresh() }
                } label: {
                    Label("Refresh", systemImage: "arrow.clockwise")
                }
                Button("Done") { dismiss() }
                    .keyboardShortcut(.defaultAction)
            }
            .padding()

            Divider()

            TorrentCoreMacPhaseBanner(
                phase: session.peers.phase,
                lastSuccessfulAt: session.peers.lastSuccessfulAt
            )
            .padding()

            if let peers = session.peers.value {
                if peers.isEmpty {
                    ContentUnavailableView(
                        "No Connected Peers",
                        systemImage: "person.2.slash",
                        description: Text("TorrentCore currently reports no connected peers.")
                    )
                } else {
                    Table(peers) {
                        TableColumn("Endpoint") { Text($0.endpoint ?? "—") }
                            .width(min: 130, ideal: 170)
                        TableColumn("Client") { Text($0.client ?? "—") }
                            .width(min: 110, ideal: 150)
                        TableColumn("Direction") { Text($0.direction ?? "—") }
                            .width(min: 80, ideal: 100)
                        TableColumn("Download") {
                            Text(TorrentCoreDisplayFormatter.rate(
                                $0.downloadRateBytesPerSecond
                            ))
                            .monospacedDigit()
                        }
                        .width(min: 90, ideal: 110)
                        TableColumn("Upload") {
                            Text(TorrentCoreDisplayFormatter.rate(
                                $0.uploadRateBytesPerSecond
                            ))
                            .monospacedDigit()
                        }
                        .width(min: 90, ideal: 110)
                        TableColumn("Encryption") { Text($0.encryption ?? "—") }
                            .width(min: 80, ideal: 100)
                    }
                }
            } else {
                ContentUnavailableView(
                    "Peers Unavailable",
                    systemImage: "person.2",
                    description: Text("Waiting for TorrentCore peer diagnostics.")
                )
            }
        }
        .frame(minWidth: 840, minHeight: 500)
        .task {
            session.setContext(.peers(torrentID))
            while !Task.isCancelled {
                do {
                    try await Task.sleep(for: .seconds(5))
                } catch {
                    return
                }
                await session.refresh()
            }
        }
        .onDisappear(perform: restoreContext)
    }
}

struct TorrentCoreMacTrackersSheet: View {
    @Environment(\.dismiss) private var dismiss

    let session: TorrentCoreFeatureSession
    let torrentID: UUID
    let torrentName: String
    let restoreContext: () -> Void

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                VStack(alignment: .leading) {
                    Text("Trackers")
                        .font(.title2.weight(.semibold))
                    Text(torrentName)
                        .foregroundStyle(.secondary)
                }
                Spacer()
                Button {
                    Task { await session.refresh() }
                } label: {
                    Label("Refresh", systemImage: "arrow.clockwise")
                }
                Button("Done") { dismiss() }
                    .keyboardShortcut(.defaultAction)
            }
            .padding()

            Divider()

            TorrentCoreMacPhaseBanner(
                phase: session.trackers.phase,
                lastSuccessfulAt: session.trackers.lastSuccessfulAt
            )
            .padding()

            if let trackers = session.trackers.value {
                if trackers.isEmpty {
                    ContentUnavailableView(
                        "No Trackers",
                        systemImage: "antenna.radiowaves.left.and.right.slash",
                        description: Text("TorrentCore reports no trackers for this torrent.")
                    )
                } else {
                    Table(trackers) {
                        TableColumn("Tier") { Text($0.tierNumber.formatted()) }
                            .width(45)
                        TableColumn("Tracker") { Text($0.trackerNumber.formatted()) }
                            .width(55)
                        TableColumn("Status") { Text($0.status ?? "—") }
                            .width(min: 110, ideal: 150)
                        TableColumn("Active") {
                            Image(systemName: $0.isActive ? "checkmark.circle.fill" : "circle")
                        }
                        .width(55)
                        TableColumn("Last Announce") {
                            Text(duration($0.timeSinceLastAnnounceSeconds))
                        }
                        .width(min: 100, ideal: 125)
                        TableColumn("Last Scrape") {
                            Text(duration($0.timeSinceLastScrapeSeconds))
                        }
                        .width(min: 100, ideal: 125)
                        TableColumn("Message") {
                            Text($0.failureMessage ?? $0.warningMessage ?? "—")
                                .lineLimit(2)
                        }
                        .width(min: 180, ideal: 260)
                    }
                }
            } else {
                ContentUnavailableView(
                    "Trackers Unavailable",
                    systemImage: "antenna.radiowaves.left.and.right",
                    description: Text("Waiting for TorrentCore tracker diagnostics.")
                )
            }
        }
        .frame(minWidth: 880, minHeight: 500)
        .task {
            session.setContext(.trackers(torrentID))
        }
        .onDisappear(perform: restoreContext)
    }

    private func duration(_ seconds: Int64?) -> String {
        guard let seconds else { return "Never" }
        return Duration.seconds(seconds).formatted(.units(
            allowed: [.hours, .minutes, .seconds],
            width: .abbreviated,
            maximumUnitCount: 2
        ))
    }
}
