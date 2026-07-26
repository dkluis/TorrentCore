import AppKit
import SwiftUI
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
        copyValue ?? "—"
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
