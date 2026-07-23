# TorrentCore Apple Clients

## Status

Milestone 0 is complete. The targets, shared package, build configurations, network boundary, and automatic-signing
baseline are established. Milestone 1 (shared contract and transport) has not started.

`TorrentCore.WebUI` remains the supported operator UI.

## Targets

| Target | Platform | Bundle identifier |
|---|---|---|
| `TorrentCoreMac` | macOS 26+, Apple Silicon | `com.conadv.TorrentCore.mac` |
| `TorrentCoreMobile` | iOS/iPadOS 26+ | `com.conadv.TorrentCore.mobile` |

Both application targets use the local `TorrentCoreKit` Swift package. macOS is implemented first; the mobile target
exists at the baseline so shared code is continuously buildable for iOS.

## Development Model

TorrentCore does not run on the development Mac. Routine development, tests, and SwiftUI previews use fakes and
fixtures. A live service address is not compiled into the app or committed as executable configuration; the
operator-approved integration host is documented only to support explicit integration work.

Live integration is opt-in:

```bash
export TORRENTCORE_INTEGRATION_BASE_URL='http://ca-server.local:7033/'
```

The variable is reserved for integration tooling added in a later milestone. Its presence must never make a routine
unit or UI test perform an unannounced live mutation.

## Build And Test

From the repository root:

```bash
swift test --package-path clients/apple/Packages/TorrentCoreKit
```

Build the macOS target without requiring signing:

```bash
xcodebuild \
  -project clients/apple/TorrentCoreApple.xcodeproj \
  -scheme TorrentCoreMac \
  -configuration Debug \
  -destination 'platform=macOS,arch=arm64' \
  CODE_SIGNING_ALLOWED=NO \
  build
```

Build the mobile target without requiring signing:

```bash
xcodebuild \
  -project clients/apple/TorrentCoreApple.xcodeproj \
  -scheme TorrentCoreMobile \
  -configuration Debug \
  -destination 'generic/platform=iOS Simulator' \
  CODE_SIGNING_ALLOWED=NO \
  build
```

## Signing And Distribution

- Apple Developer Team ID: `5GRR76N48V`
- signing style: automatic
- macOS distribution: signed and notarized outside the Mac App Store
- mobile distribution: TestFlight

Signing identities and provisioning profiles remain machine/account state and are not committed.

## Network Boundary

Initial clients connect over HTTP only inside a trusted LAN or a VPN that routes onto the LAN. Direct public-internet
access is outside scope. The WebUI remains deployed for Windows clients and recovery access.

See [the development plan](../../docs/native-apple-client-development-plan.md).
