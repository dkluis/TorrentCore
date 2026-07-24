# Testing

## Standard Commands

```bash
dotnet build TorrentCore.sln
dotnet test TorrentCore.sln
```

## Test Scope

The current suite focuses on service, persistence, engine-adapter, and client-boundary behavior.

Representative coverage areas:

- API behavior
- SQLite schema migration and persistence
- torrent state persistence and restart recovery
- history-store and history-service behavior
- callback finalization and callback lifecycle processing
- category routing
- seeding policy
- metadata recovery and connection policy behavior
- data-path cleanup
- client options and path defaults
- normalized OpenAPI contract generation for native clients

## Native Apple Contract

The committed Apple contract is
`clients/apple/Packages/TorrentCoreKit/Sources/TorrentCoreAPI/openapi.json`. The normal .NET suite compares it with a
normalized document produced by an in-process Development test host.

After an intentional public service-contract change, regenerate it with:

```bash
TORRENTCORE_UPDATE_OPENAPI=1 dotnet test \
  tests/TorrentCore.Service.Tests/TorrentCore.Service.Tests.csproj \
  --filter FullyQualifiedName~OpenApiContractTests
```

Then run the normal .NET suite and the Swift package tests. Live Apple integration remains opt-in through
`TORRENTCORE_INTEGRATION_BASE_URL` and is read-only unless the operator explicitly approves a mutation.

The separately gated `liveDisposableMutationSequence` also requires
`TORRENTCORE_ALLOW_DISPOSABLE_MUTATION=1`, the disposable magnet URI and expected info hash, and the exact live enabled
category display name. It refuses an existing hash, scopes every action to the ID returned by add, removes with data
deletion enabled, and attempts cleanup after a partial failure. Never commit those live values.

Milestone 2 shared-state tests cover device-local profile persistence, URL normalization, active profile isolation,
client-wide refresh preferences, open-context request routing, foreground/background behavior, last-known stale state,
single-item mutation refresh, and rejection of late responses from a previous profile.

Milestone 3 adds shared tests for version 1-to-2 client-preference migration, Auto Refresh disablement, combined
torrent-list and selected-detail refresh, WebUI-equivalent torrent filtering, and 25/50/100/250-row local pagination.
Milestone 4 adds shared tests for open-context History, Logs, peers, trackers, and Service Settings reads; separate
abandonment history; synchronous first-load presentation on context changes; and single-item operational mutations
with authoritative refresh. Its signed fixture UI suite covers the existing torrent inspector and removal confirmation
plus automatic History, Logs, and Service Settings loading without manual refresh. The post-parity refinement coverage
also verifies the shared settings-help catalog, native help popovers, constrained service-setting selectors, and
show/hide behavior for the Torrents, History, and Logs inspectors.
The live read-only probe also decodes History, Logs, runtime settings, peer/tracker diagnostics, and history detail
when corresponding records exist.
The `TorrentCoreMac` scheme includes a unit target and a fixture-only UI target. Compile both without launching an app:

```bash
xcodebuild \
  -project clients/apple/TorrentCoreApple.xcodeproj \
  -scheme TorrentCoreMac \
  -configuration Debug \
  -destination 'platform=macOS,arch=arm64' \
  -skipPackagePluginValidation \
  SYMROOT=/private/tmp/torrentcore-apple-test-products \
  OBJROOT=/private/tmp/torrentcore-apple-test-intermediates \
  CODE_SIGNING_ALLOWED=NO \
  build-for-testing
```

Run `TorrentCoreMacUITests` only from a normal development-signed test build. The target supplies
`--torrentcore-ui-fixtures`, which uses an in-memory service and never reads a saved endpoint or contacts a live
TorrentCore installation. Do not execute an unsigned UI-test product because macOS rejects it before test bootstrap.
The July 24, 2026 Milestone 4 signed fixture run passed all four UI tests.

Production Swagger availability is verified by `OpenApiContractTests` alongside normalized contract comparison.

## Testing Rules

- use real SQLite-backed tests for persistence behavior
- keep deterministic coverage for engine-adapter and runtime-policy logic where possible
- preserve regression coverage around callback timing, restart recovery, and history updates
- validate implementation at the relevant layer instead of relying on one large end-to-end path

## Operational Verification

Normal documentation cleanup work should at least re-run:

```bash
dotnet build TorrentCore.sln
dotnet test TorrentCore.sln
```

If a docs-only change ever coincides with config or script changes, verify those command surfaces separately.
