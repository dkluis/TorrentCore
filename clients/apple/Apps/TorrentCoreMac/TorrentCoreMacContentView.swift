import SwiftUI
import TorrentCoreFeatures

struct TorrentCoreMacContentView: View {
    var body: some View {
        ContentUnavailableView {
            Label(TorrentCoreFeatureFoundation.productName, systemImage: "arrow.down.circle")
        } description: {
            Text("Native macOS operator client foundation")
        }
        .frame(minWidth: 720, minHeight: 480)
    }
}

#Preview {
    TorrentCoreMacContentView()
}

