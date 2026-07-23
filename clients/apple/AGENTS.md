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

- TorrentCore does not run on the development Mac.
- SwiftUI previews and routine tests must use fakes and fixtures.
- Live integration is opt-in through `TORRENTCORE_INTEGRATION_BASE_URL`.
- Do not commit a live endpoint, signing secret, provisioning profile, or credential.
- Use `ca-server.local` only when the operator explicitly requests live integration.
- Run read-only live checks before any mutation.
- Mutating or administrative live tests require explicit operator confirmation.

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
  CODE_SIGNING_ALLOWED=NO \
  build

xcodebuild \
  -project clients/apple/TorrentCoreApple.xcodeproj \
  -scheme TorrentCoreMobile \
  -configuration Debug \
  -destination 'generic/platform=iOS Simulator' \
  CODE_SIGNING_ALLOWED=NO \
  build
```

Run the relevant Swift tests and target builds before finishing Apple-client changes. If a service contract changes,
also update .NET callers and tests and run the relevant .NET verification.

On the current development Mac, the normal Swift/Xcode cache locations may point to an optional external volume. When
that volume is unavailable, redirect package caches, module caches, and DerivedData to a task-specific directory under
`/private/tmp`; do not replace the user's cache symlinks. A signed command-line build also needs a temporary
`CFFIXED_USER_HOME` containing copies of the current Xcode preference files so account metadata remains available.
