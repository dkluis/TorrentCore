// swift-tools-version: 6.2

import PackageDescription

let package = Package(
    name: "TorrentCoreKit",
    platforms: [
        .macOS(.v26),
        .iOS(.v26),
    ],
    products: [
        .library(name: "TorrentCoreAPI", targets: ["TorrentCoreAPI"]),
        .library(name: "TorrentCoreFeatures", targets: ["TorrentCoreFeatures"]),
        .library(name: "TorrentCoreSupport", targets: ["TorrentCoreSupport"]),
    ],
    dependencies: [
        .package(
            url: "https://github.com/apple/swift-openapi-generator",
            exact: "1.13.0"
        ),
        .package(
            url: "https://github.com/apple/swift-openapi-runtime",
            exact: "1.12.0"
        ),
        .package(
            url: "https://github.com/apple/swift-openapi-urlsession",
            exact: "1.3.0"
        ),
    ],
    targets: [
        .target(
            name: "TorrentCoreSupport"
        ),
        .target(
            name: "TorrentCoreAPI",
            dependencies: [
                "TorrentCoreSupport",
                .product(name: "OpenAPIRuntime", package: "swift-openapi-runtime"),
                .product(name: "OpenAPIURLSession", package: "swift-openapi-urlsession"),
            ],
            plugins: [
                .plugin(name: "OpenAPIGenerator", package: "swift-openapi-generator"),
            ]
        ),
        .target(
            name: "TorrentCoreFeatures",
            dependencies: [
                "TorrentCoreAPI",
                "TorrentCoreSupport",
            ]
        ),
        .testTarget(
            name: "TorrentCoreKitTests",
            dependencies: [
                "TorrentCoreAPI",
                "TorrentCoreFeatures",
                "TorrentCoreSupport",
                .product(name: "OpenAPIRuntime", package: "swift-openapi-runtime"),
            ]
        ),
    ]
)
