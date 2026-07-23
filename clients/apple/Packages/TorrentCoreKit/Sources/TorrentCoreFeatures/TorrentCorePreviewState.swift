public struct TorrentCorePreviewState: Hashable, Sendable {
    public var title: String
    public var message: String
    public var systemImage: String
    public var isLoading: Bool

    public init(title: String, message: String, systemImage: String, isLoading: Bool = false) {
        self.title = title
        self.message = message
        self.systemImage = systemImage
        self.isLoading = isLoading
    }

    public static let connected = Self(
        title: "TorrentCore Connected",
        message: "5 torrents · 1 downloading",
        systemImage: "checkmark.circle"
    )

    public static let loading = Self(
        title: "Connecting",
        message: "Checking the TorrentCore service…",
        systemImage: "arrow.trianglehead.2.clockwise",
        isLoading: true
    )

    public static let empty = Self(
        title: "No Torrents",
        message: "The service is connected and has no torrents.",
        systemImage: "tray"
    )

    public static let offline = Self(
        title: "TorrentCore Offline",
        message: "Check the server address and LAN or VPN connection.",
        systemImage: "network.slash"
    )

    public static let error = Self(
        title: "Couldn’t Load TorrentCore",
        message: "The service returned an error. Try refreshing.",
        systemImage: "exclamationmark.triangle"
    )
}
