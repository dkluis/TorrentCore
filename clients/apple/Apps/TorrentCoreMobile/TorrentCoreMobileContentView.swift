import SwiftUI
import TorrentCoreFeatures

struct TorrentCoreMobileContentView: View {
    var state: TorrentCorePreviewState = .empty

    var body: some View {
        ContentUnavailableView {
            Label(state.title, systemImage: state.systemImage)
        } description: {
            Text(state.message)
        } actions: {
            if state.isLoading {
                ProgressView()
            }
        }
    }
}

#Preview("Connected") {
    TorrentCoreMobileContentView(state: .connected)
}

#Preview("Loading") {
    TorrentCoreMobileContentView(state: .loading)
}

#Preview("Empty") {
    TorrentCoreMobileContentView(state: .empty)
}

#Preview("Offline") {
    TorrentCoreMobileContentView(state: .offline)
}

#Preview("Error") {
    TorrentCoreMobileContentView(state: .error)
}
