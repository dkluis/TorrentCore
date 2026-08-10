import SwiftUI
import TorrentCoreAPI
import TorrentCoreFeatures

struct TorrentCoreMacDashboardView: View {
    let session: TorrentCoreFeatureSession

    private let metricColumns = [
        GridItem(.adaptive(minimum: 170, maximum: 260), spacing: 12),
    ]

    var body: some View {
        Group {
            if let status = session.hostStatus.value {
                ScrollView {
                    VStack(alignment: .leading, spacing: 18) {
                        TorrentCoreMacPhaseBanner(
                            phase: session.hostStatus.phase,
                            lastSuccessfulAt: session.hostStatus.lastSuccessfulAt
                        )

                        serviceHeader(status: status)
                        vpnConnection(status: status)
                        transferMetrics(status: status)
                        torrentMetrics(status: status)
                        queueAndRecovery(status: status)
                        lifecycleSection
                    }
                    .padding(20)
                    .frame(maxWidth: .infinity, alignment: .leading)
                }
                .accessibilityIdentifier("dashboard.content")
            } else {
                emptyState
            }
        }
        .torrentCoreRefreshWhileVisible(
            session: session,
            context: .dashboard
        )
    }

    private func serviceHeader(status: TorrentCoreHostStatus) -> some View {
        GroupBox("Service") {
            Grid(alignment: .leading, horizontalSpacing: 24, verticalSpacing: 10) {
                GridRow {
                    TorrentCoreMacDetailRow(
                        label: "Service",
                        value: status.serviceName ?? "TorrentCore.Service"
                    )
                    TorrentCoreMacDetailRow(
                        label: "Version",
                        value: status.serviceVersion ?? "--"
                    )
                }
                GridRow {
                    TorrentCoreMacDetailRow(
                        label: "Engine",
                        value: status.engineRuntime ?? "--"
                    )
                    TorrentCoreMacDetailRow(
                        label: "Environment",
                        value: status.environmentName ?? "--"
                    )
                }
                GridRow {
                    TorrentCoreMacDetailRow(
                        label: "Build",
                        value: shortBuild(status.serviceBuild)
                    )
                    TorrentCoreMacDetailRow(
                        label: "Instance",
                        value: status.serviceInstanceID?.uuidString ?? "--"
                    )
                }
                GridRow {
                    TorrentCoreMacDetailRow(
                        label: "Status",
                        value: TorrentCoreDisplayFormatter.splitIdentifier(status.status.rawValue)
                    )
                    TorrentCoreMacDetailRow(
                        label: "Checked",
                        value: TorrentCoreDisplayFormatter.timestamp(status.checkedAt)
                    )
                }
            }
            .padding(.top, 4)
        }
    }

    private func shortBuild(_ build: String?) -> String {
        guard let build, !build.isEmpty else {
            return "--"
        }
        return String(build.prefix(12))
    }

    private func vpnConnection(status: TorrentCoreHostStatus) -> some View {
        GroupBox("VPN Connection") {
            VStack(alignment: .leading, spacing: 12) {
                Grid(alignment: .leading, horizontalSpacing: 24, verticalSpacing: 10) {
                    GridRow {
                        TorrentCoreMacDetailRow(
                            label: "Validation",
                            value: validationDescription(status.vpnValidationEnabled)
                        )
                        TorrentCoreMacDetailRow(
                            label: "Connection",
                            value: displayIdentifier(status.vpnConnectionPhase)
                        )
                    }
                    GridRow {
                        TorrentCoreMacDetailRow(
                            label: "Torrent Processing",
                            value: processingDescription(status.torrentProcessingAvailable)
                        )
                        TorrentCoreMacDetailRow(
                            label: "Reason",
                            value: displayIdentifier(status.vpnConnectionReason)
                        )
                    }
                    GridRow {
                        TorrentCoreMacDetailRow(
                            label: "Current Public IPv4",
                            value: status.vpnObservedPublicIPv4 ?? "--"
                        )
                        TorrentCoreMacDetailRow(
                            label: "Last Check",
                            value: TorrentCoreDisplayFormatter.timestamp(status.vpnLastCheckAt)
                        )
                    }
                    GridRow {
                        TorrentCoreMacDetailRow(
                            label: "Last Successful Check",
                            value: TorrentCoreDisplayFormatter.timestamp(status.vpnLastSuccessAt)
                        )
                        TorrentCoreMacDetailRow(
                            label: "Next Automatic Retry",
                            value: TorrentCoreDisplayFormatter.timestamp(
                                status.vpnNextAutomaticRetryAt
                            )
                        )
                    }
                    GridRow {
                        TorrentCoreMacDetailRow(
                            label: "Ready Check Interval",
                            value: intervalDescription(status.vpnReadyCheckIntervalSeconds)
                        )
                        TorrentCoreMacDetailRow(
                            label: "Paused Check Interval",
                            value: intervalDescription(status.vpnDegradedCheckIntervalSeconds)
                        )
                    }
                }
                Text(vpnOperatorMessage(status))
                    .foregroundStyle(
                        status.torrentProcessingAvailable == false
                            ? Color.orange
                            : Color.secondary
                    )
            }
            .padding(.top, 4)
        }
    }

    private func displayIdentifier(_ value: String?) -> String {
        guard let value, !value.isEmpty else {
            return "--"
        }
        return TorrentCoreDisplayFormatter.splitIdentifier(value)
    }

    private func intervalDescription(_ seconds: Int?) -> String {
        guard let seconds else {
            return "--"
        }
        return "\(seconds.formatted()) seconds"
    }

    private func validationDescription(_ enabled: Bool?) -> String {
        guard let enabled else {
            return "--"
        }
        return enabled ? "Enabled" : "Disabled"
    }

    private func processingDescription(_ available: Bool?) -> String {
        guard let available else {
            return "--"
        }
        return available ? "Active" : "Paused"
    }

    private func vpnOperatorMessage(_ status: TorrentCoreHostStatus) -> String {
        if let message = status.torrentProcessingMessage, !message.isEmpty {
            return message
        }
        if status.vpnValidationEnabled == true {
            return "VPN connection is available. Torrent processing is active."
        }
        if status.vpnValidationEnabled == nil {
            return "VPN connection status is unavailable from this Service version."
        }
        return "VPN validation is disabled. Torrent processing is active."
    }

    private func transferMetrics(status: TorrentCoreHostStatus) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            Text("Current Transfer")
                .font(.headline)
            LazyVGrid(columns: metricColumns, alignment: .leading, spacing: 12) {
                TorrentCoreMacMetric(
                    title: "Download",
                    value: TorrentCoreDisplayFormatter.rate(
                        status.currentDownloadRateBytesPerSecond
                    ),
                    systemImage: "arrow.down"
                )
                TorrentCoreMacMetric(
                    title: "Upload",
                    value: TorrentCoreDisplayFormatter.rate(
                        status.currentUploadRateBytesPerSecond
                    ),
                    systemImage: "arrow.up"
                )
                TorrentCoreMacMetric(
                    title: "Connected Peers",
                    value: status.currentConnectedPeerCount.formatted(),
                    systemImage: "person.2"
                )
                TorrentCoreMacMetric(
                    title: "Total Torrents",
                    value: status.torrentCount.formatted(),
                    systemImage: "arrow.down.circle"
                )
            }
        }
    }

    private func torrentMetrics(status: TorrentCoreHostStatus) -> some View {
        GroupBox("Torrent States") {
            LazyVGrid(columns: metricColumns, alignment: .leading, spacing: 12) {
                TorrentCoreMacMetric(
                    title: "Resolving Metadata",
                    value: status.resolvingMetadataCount.formatted()
                )
                TorrentCoreMacMetric(
                    title: "Queued",
                    value: (status.metadataQueueCount + status.downloadQueueCount).formatted()
                )
                TorrentCoreMacMetric(
                    title: "Downloading",
                    value: status.downloadingCount.formatted()
                )
                TorrentCoreMacMetric(
                    title: "Seeding",
                    value: status.seedingCount.formatted()
                )
                TorrentCoreMacMetric(
                    title: "Paused",
                    value: status.pausedCount.formatted()
                )
                TorrentCoreMacMetric(
                    title: "Completed",
                    value: status.completedCount.formatted()
                )
                TorrentCoreMacMetric(
                    title: "Errors",
                    value: status.errorCount.formatted()
                )
            }
            .padding(.top, 4)
        }
    }

    private func queueAndRecovery(status: TorrentCoreHostStatus) -> some View {
        GroupBox("Capacity & Startup Recovery") {
            Grid(alignment: .leading, horizontalSpacing: 24, verticalSpacing: 10) {
                GridRow {
                    TorrentCoreMacDetailRow(
                        label: "Download Slots",
                        value: "\(status.availableDownloadSlots) of \(status.maxActiveDownloads) available"
                    )
                    TorrentCoreMacDetailRow(
                        label: "Metadata Slots",
                        value: "\(status.availableMetadataResolutionSlots) of \(status.maxActiveMetadataResolutions) available"
                    )
                }
                GridRow {
                    TorrentCoreMacDetailRow(
                        label: "Recovery",
                        value: status.startupRecoveryCompleted ? "Completed" : "In progress"
                    )
                    TorrentCoreMacDetailRow(
                        label: "Recovered",
                        value: status.startupRecoveredTorrentCount.formatted()
                    )
                }
                GridRow {
                    TorrentCoreMacDetailRow(
                        label: "Normalized",
                        value: status.startupNormalizedTorrentCount.formatted()
                    )
                    TorrentCoreMacDetailRow(
                        label: "Completed At",
                        value: TorrentCoreDisplayFormatter.timestamp(
                            status.startupRecoveryCompletedAt
                        )
                    )
                }
            }
            .padding(.top, 4)
        }
    }

    @ViewBuilder
    private var lifecycleSection: some View {
        if let lifecycle = session.dashboardLifecycle.value {
            GroupBox("Recent Lifecycle Events") {
                if lifecycle.recentEvents.isEmpty {
                    Text("No recent lifecycle events for this service instance.")
                        .foregroundStyle(.secondary)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(.vertical, 8)
                } else {
                    VStack(spacing: 0) {
                        ForEach(
                            Array(lifecycle.recentEvents.prefix(12).enumerated()),
                            id: \.offset
                        ) { index, event in
                            VStack(alignment: .leading, spacing: 4) {
                                HStack {
                                    Text(event.eventType.map(
                                        TorrentCoreDisplayFormatter.splitIdentifier
                                    ) ?? "Event")
                                        .fontWeight(.semibold)
                                    Spacer()
                                    Text(TorrentCoreDisplayFormatter.timestamp(event.occurredAt))
                                        .font(.caption)
                                        .foregroundStyle(.secondary)
                                }
                                if let message = event.message, !message.isEmpty {
                                    Text(message)
                                }
                                if let category = event.category, !category.isEmpty {
                                    Text(category)
                                        .font(.caption)
                                        .foregroundStyle(.secondary)
                                }
                            }
                            .padding(.vertical, 9)
                            if index < min(lifecycle.recentEvents.count, 12) - 1 {
                                Divider()
                            }
                        }
                    }
                }
            }
            TorrentCoreMacPhaseBanner(
                phase: session.dashboardLifecycle.phase,
                lastSuccessfulAt: session.dashboardLifecycle.lastSuccessfulAt
            )
        }
    }

    private var emptyState: some View {
        ContentUnavailableView {
            Label(emptyTitle, systemImage: emptySystemImage)
        } description: {
            Text(emptyMessage)
        } actions: {
            if case .loading = session.hostStatus.phase {
                ProgressView()
                    .controlSize(.small)
            }
            if session.activeProfile != nil {
                Button("Refresh") {
                    Task { await session.refresh(.dashboard) }
                }
            }
        }
        .accessibilityIdentifier("dashboard.empty")
    }

    private var emptyTitle: String {
        if case .loading = session.hostStatus.phase {
            return "Loading Dashboard"
        }
        return switch session.connectionState {
        case .noProfile:
            "No TorrentCore Connection"
        case .offline:
            "TorrentCore Offline"
        case .connecting:
            "Connecting"
        case .notConnected, .connected:
            "Dashboard Unavailable"
        }
    }

    private var emptySystemImage: String {
        if case .loading = session.hostStatus.phase {
            return "arrow.trianglehead.2.clockwise"
        }
        return switch session.connectionState {
        case .offline:
            "network.slash"
        case .connecting:
            "arrow.trianglehead.2.clockwise"
        case .noProfile, .notConnected, .connected:
            "gauge.with.dots.needle.50percent"
        }
    }

    private var emptyMessage: String {
        if case .loading = session.hostStatus.phase {
            return "Requesting current dashboard information from TorrentCore."
        }
        return switch session.connectionState {
        case .noProfile:
            "Create or select a connection before opening the dashboard."
        case let .offline(_, _, message):
            message
        case .connecting:
            "Checking TorrentCore.Service…"
        case .notConnected:
            "Refresh to connect to the selected TorrentCore installation."
        case .connected:
            "TorrentCore did not return dashboard information."
        }
    }
}
