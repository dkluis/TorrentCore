# AGENTS.md

## Scope

These instructions apply to the native Apple clients under `clients/apple/`.

## Product Boundary

- `TorrentCore.Service` remains authoritative for engine, persistence, queueing, recovery, callback, seeding, cleanup,
  and filesystem policy.
- Apple clients communicate only through TorrentCore HTTP contracts.
- Apple clients never read TorrentCore SQLite files or inspect download paths to infer service state.
- `TorrentCore.WebUI` remains supported for Windows and fallback administration.

## Platforms And Targets

- macOS 26 or later
- iOS/iPadOS 26 or later
- Apple Silicon only
- macOS implementation first
- shared non-UI implementation in `Packages/TorrentCoreKit`
- platform-specific presentation in `Apps/TorrentCoreMac` and `Apps/TorrentCoreMobile`

## Runtime And Preview Rules

- SwiftUI previews and routine tests must use fakes and fixtures.
- A deployed TorrentCore runtime may coexist on the development Mac, but routine builds and tests must not depend on it.
- Live integration is opt-in through `TORRENTCORE_INTEGRATION_BASE_URL`.
- Do not commit a live endpoint, signing secret, provisioning profile, or credential.
- Use `ca-desktop.local`, `ca-server.local`, or another installation only when the operator explicitly requests live
  integration.
- Run read-only live checks before any mutation.
- Mutating or administrative live tests require explicit operator confirmation and a designated disposable target.

## Network Model

- Support trusted LAN and routed VPN access.
- Initial service transport is HTTP inside that private boundary.
- Do not support or document direct public-internet exposure.
- Local-network privacy and transport exceptions must be narrowly scoped.

## Build And Test

From the repository root:

```bash
swift test --package-path clients/apple/Packages/TorrentCoreKit

xcodebuild \
  -project clients/apple/TorrentCoreApple.xcodeproj \
  -scheme TorrentCoreMac \
  -configuration Debug \
  -destination 'platform=macOS,arch=arm64' \
  -skipPackagePluginValidation \
  SYMROOT=/private/tmp/torrentcore-apple-mac-products \
  OBJROOT=/private/tmp/torrentcore-apple-mac-intermediates \
  CODE_SIGNING_ALLOWED=NO \
  build

xcodebuild \
  -project clients/apple/TorrentCoreApple.xcodeproj \
  -scheme TorrentCoreMobile \
  -configuration Debug \
  -destination 'generic/platform=iOS Simulator' \
  -skipPackagePluginValidation \
  SYMROOT=/private/tmp/torrentcore-apple-mobile-products \
  OBJROOT=/private/tmp/torrentcore-apple-mobile-intermediates \
  CODE_SIGNING_ALLOWED=NO \
  build
```

Run the relevant Swift tests and target builds before finishing Apple-client changes. If a service contract changes,
also update .NET callers and tests and run the relevant .NET verification.

Keep unsigned command-line products under an explicit temporary `SYMROOT` and `OBJROOT`. Never place an unsigned app
in the operator's normal Xcode build location because macOS may reject that product when Xcode or UI automation
launches it.
