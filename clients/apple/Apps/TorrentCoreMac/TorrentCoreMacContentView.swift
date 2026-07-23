import SwiftUI
import TorrentCoreFeatures

struct TorrentCoreMacContentView: View {
    var state: TorrentCorePreviewState = .empty

    var body: some View {
        ContentUnavailableView {
            Label(state.title, systemImage: state.systemImage)
        } description: {
            Text(state.message)
        } actions: {
            if state.isLoading {
                ProgressView()
                    .controlSize(.small)
            }
        }
        .frame(minWidth: 720, minHeight: 480)
    }
}

#Preview("Connected") {
    TorrentCoreMacContentView(state: .connected)
}

#Preview("Loading") {
    TorrentCoreMacContentView(state: .loading)
}

#Preview("Empty") {
    TorrentCoreMacContentView(state: .empty)
}

#Preview("Offline") {
    TorrentCoreMacContentView(state: .offline)
}

#Preview("Error") {
    TorrentCoreMacContentView(state: .error)
}
