import SwiftUI
import TorrentCoreFeatures

struct TorrentCoreMacHelpButton: View {
    let content: TorrentCoreHelpContent

    @State private var isPresented = false

    var body: some View {
        Button {
            isPresented.toggle()
        } label: {
            Image(systemName: "info.circle")
                .font(.system(size: 11, weight: .medium))
                .foregroundStyle(.secondary)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Help for \(content.label)")
        .accessibilityIdentifier("help.\(identifierComponent)")
        .help(content.summary)
        .popover(isPresented: $isPresented, arrowEdge: .trailing) {
            VStack(alignment: .leading, spacing: 10) {
                Text(content.label)
                    .font(.headline)
                Text(content.summary)
                    .font(.subheadline.weight(.medium))
                Text(content.detail)
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .padding(16)
            .frame(width: 340, alignment: .leading)
        }
    }

    private var identifierComponent: String {
        content.label
            .lowercased()
            .map { $0.isLetter || $0.isNumber ? $0 : "-" }
            .reduce(into: "") { result, character in
                if character != "-" || result.last != "-" {
                    result.append(character)
                }
            }
            .trimmingCharacters(in: CharacterSet(charactersIn: "-"))
    }
}

struct TorrentCoreMacHelpLabel: View {
    let title: String
    let content: TorrentCoreHelpContent

    init(_ title: String? = nil, content: TorrentCoreHelpContent) {
        self.title = title ?? content.label
        self.content = content
    }

    var body: some View {
        HStack(spacing: 4) {
            Text(title)
            TorrentCoreMacHelpButton(content: content)
        }
    }
}

struct TorrentCoreMacHelpSectionTitle: View {
    let title: String
    let content: TorrentCoreHelpContent

    var body: some View {
        HStack(spacing: 5) {
            Text(title)
            TorrentCoreMacHelpButton(content: content)
        }
    }
}
