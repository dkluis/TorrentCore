import AppKit
import SwiftUI
import TorrentCoreAPI
import TorrentCoreFeatures

enum TorrentCoreMacErrorPresenter {
    static func message(_ error: any Error) -> String {
        if let localized = error as? LocalizedError,
           let description = localized.errorDescription
        {
            return description
        }
        return error.localizedDescription
    }
}

struct TorrentCoreMacPhaseBanner: View {
    let phase: TorrentCoreFeaturePhase
    let lastSuccessfulAt: Date?

    var body: some View {
        switch phase {
        case .idle, .current:
            EmptyView()
        case .loading:
            HStack(spacing: 8) {
                ProgressView()
                    .controlSize(.small)
                Text(lastSuccessfulAt == nil ? "Loading…" : "Refreshing…")
            }
            .foregroundStyle(.secondary)
            .accessibilityIdentifier("state.loading")
        case let .stale(message):
            Label {
                VStack(alignment: .leading, spacing: 2) {
                    Text("Showing last-known information")
                        .fontWeight(.semibold)
                    Text(message)
                        .font(.caption)
                }
            } icon: {
                Image(systemName: "exclamationmark.triangle.fill")
            }
            .foregroundStyle(.orange)
            .padding(10)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(.orange.opacity(0.1), in: RoundedRectangle(cornerRadius: 8))
            .accessibilityIdentifier("state.stale")
        }
    }
}

struct TorrentCoreMacMetric: View {
    let title: String
    let value: String
    var systemImage: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            if let systemImage {
                Label(title, systemImage: systemImage)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            } else {
                Text(title)
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
            Text(value)
                .font(.title3.weight(.semibold))
                .textSelection(.enabled)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(12)
        .background(.quaternary.opacity(0.45), in: RoundedRectangle(cornerRadius: 10))
    }
}

struct TorrentCoreMacProcessingPausedOverlay: View {
    let hostStatus: TorrentCoreHostStatus
    let refresh: () -> Void

    var body: some View {
        ZStack {
            Rectangle()
                .fill(.regularMaterial)
                .contentShape(Rectangle())

            VStack(spacing: 12) {
                Image(systemName: isRestarting ? "arrow.clockwise.circle.fill" : "pause.circle.fill")
                    .font(.system(size: 38))
                    .foregroundStyle(isRestarting ? .blue : .orange)
                Text(isRestarting ? "Restarting Torrent Processing" : "Torrent Processing Paused")
                    .font(.title2.weight(.semibold))
                Text(hostStatus.torrentProcessingMessage
                     ?? "VPN connection could not be confirmed. Torrent processing is paused.")
                    .multilineTextAlignment(.center)
                    .foregroundStyle(.secondary)
                    .frame(maxWidth: 520)
                if let reason = reasonText {
                    Text(reason)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                Button {
                    refresh()
                } label: {
                    Label("Refresh", systemImage: "arrow.clockwise")
                }
                .keyboardShortcut("r", modifiers: .command)
            }
            .padding(28)
        }
        .accessibilityIdentifier("vpn.processingPaused")
    }

    private var isRestarting: Bool {
        hostStatus.vpnConnectionPhase == "Activating"
    }

    private var reasonText: String? {
        switch hostStatus.vpnConnectionReason {
        case "DirectIsp":
            "The service is using the configured direct ISP connection."
        case "InvalidResponse":
            "The VPN check returned an invalid public address."
        case "TimedOut":
            "The VPN check timed out."
        case "EndpointFailure":
            "The VPN check service could not be reached."
        case "UnexpectedFailure":
            "The VPN check failed unexpectedly."
        case "EngineActivationFailed":
            "The VPN is available, but torrent processing could not restart."
        case "EngineSuspensionFailed":
            "Torrent processing could not be paused cleanly."
        default:
            nil
        }
    }
}

struct TorrentCoreMacDetailRow: View {
    let label: String
    let value: String

    var body: some View {
        LabeledContent(label) {
            Text(value)
                .multilineTextAlignment(.trailing)
                .textSelection(.enabled)
        }
    }
}

private struct TorrentCoreMacTrailingOverlayModifier<OverlayContent: View>: ViewModifier {
    let isPresented: Bool
    @Binding var width: Double
    let overlayContent: () -> OverlayContent
    @State private var dragStartWidth: Double?
    @State private var liveWidth: Double?
    @State private var isResizeHandleHovered = false

    private var displayedWidth: Double {
        TorrentCoreMacTableSupport.clampedOverlayWidth(liveWidth ?? width)
    }

    func body(content: Content) -> some View {
        content.overlay(alignment: .trailing) {
            if isPresented {
                overlayContent()
                    .frame(width: displayedWidth)
                    .frame(maxHeight: .infinity)
                    .background(.regularMaterial)
                    .overlay(alignment: .leading) {
                        Rectangle()
                            .fill(
                                isResizeHandleHovered
                                    ? Color.accentColor.opacity(0.14)
                                    : .clear
                            )
                            .frame(width: 12)
                            .contentShape(Rectangle())
                            .overlay(alignment: .leading) { Divider() }
                            .onContinuousHover { phase in
                                switch phase {
                                case .active:
                                    isResizeHandleHovered = true
                                    NSCursor.resizeLeftRight.set()
                                case .ended:
                                    isResizeHandleHovered = false
                                    NSCursor.arrow.set()
                                }
                            }
                            .gesture(
                                DragGesture()
                                    .onChanged { value in
                                        if dragStartWidth == nil {
                                            dragStartWidth = width
                                        }
                                        liveWidth = TorrentCoreMacTableSupport
                                            .clampedOverlayWidth(
                                                (dragStartWidth ?? width)
                                                    - value.translation.width
                                            )
                                    }
                                    .onEnded { _ in
                                        if let liveWidth {
                                            width = TorrentCoreMacTableSupport
                                                .clampedOverlayWidth(liveWidth)
                                        }
                                        liveWidth = nil
                                        dragStartWidth = nil
                                    }
                            )
                            .help("Drag to resize details")
                    }
                    .shadow(color: .black.opacity(0.18), radius: 12, x: -4)
                    .contentShape(Rectangle())
                    .zIndex(1)
                    .onDisappear { NSCursor.arrow.set() }
            }
        }
    }
}

private struct TorrentCoreMacFixedTrailingOverlayModifier<OverlayContent: View>: ViewModifier {
    let isPresented: Bool
    let width: CGFloat
    let overlayContent: () -> OverlayContent

    func body(content: Content) -> some View {
        content.overlay(alignment: .trailing) {
            if isPresented {
                overlayContent()
                    .frame(width: width)
                    .frame(maxHeight: .infinity)
                    .background(.regularMaterial)
                    .overlay(alignment: .leading) { Divider() }
                    .shadow(color: .black.opacity(0.18), radius: 12, x: -4)
                    .contentShape(Rectangle())
                    .zIndex(1)
            }
        }
    }
}

extension View {
    func torrentCoreTrailingOverlay<OverlayContent: View>(
        isPresented: Bool,
        width: Binding<Double>,
        @ViewBuilder content: @escaping () -> OverlayContent
    ) -> some View {
        modifier(
            TorrentCoreMacTrailingOverlayModifier(
                isPresented: isPresented,
                width: width,
                overlayContent: content
            )
        )
    }

    func torrentCoreTrailingOverlay<OverlayContent: View>(
        isPresented: Bool,
        width: CGFloat,
        @ViewBuilder content: @escaping () -> OverlayContent
    ) -> some View {
        modifier(
            TorrentCoreMacFixedTrailingOverlayModifier(
                isPresented: isPresented,
                width: width,
                overlayContent: content
            )
        )
    }
}

struct TorrentCoreMacCopyableDetailRow: View {
    let label: String
    let value: String?
    let accessibilityIdentifier: String

    @State private var copied = false
    @State private var resetTask: Task<Void, Never>?

    var body: some View {
        LabeledContent(label) {
            VStack(alignment: .trailing, spacing: 6) {
                Text(displayValue)
                    .multilineTextAlignment(.trailing)
                    .textSelection(.enabled)

                Button {
                    copyToPasteboard()
                } label: {
                    Label(copied ? "Copied" : "Copy", systemImage: copied ? "checkmark" : "doc.on.doc")
                }
                .disabled(copyValue == nil)
                .accessibilityLabel(copied ? "Copied \(label)" : "Copy \(label)")
                .accessibilityIdentifier(accessibilityIdentifier)
                .help(copied ? "Copied \(label)" : "Copy \(label)")
            }
        }
        .onDisappear {
            resetTask?.cancel()
        }
    }

    private var copyValue: String? {
        let trimmed = (value ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty ? nil : trimmed
    }

    private var displayValue: String {
        copyValue ?? "--"
    }

    private func copyToPasteboard() {
        guard let copyValue else {
            return
        }
        resetTask?.cancel()
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        copied = pasteboard.setString(copyValue, forType: .string)
        guard copied else {
            return
        }
        resetTask = Task {
            try? await Task.sleep(for: .seconds(2))
            guard !Task.isCancelled else {
                return
            }
            copied = false
        }
    }
}
