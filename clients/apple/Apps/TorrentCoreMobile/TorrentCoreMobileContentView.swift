import SwiftUI
import TorrentCoreFeatures

struct TorrentCoreMobileContentView: View {
    var body: some View {
        ContentUnavailableView {
            Label(TorrentCoreFeatureFoundation.productName, systemImage: "arrow.down.circle")
        } description: {
            Text("Shared iOS and iPadOS client foundation")
        }
    }
}

#Preview {
    TorrentCoreMobileContentView()
}

