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
- seeding policy, including durable single-event application across reevaluation and restart
- metadata recovery, host-wide background-reset single flight, stuck quarantine, circuit breaking, and connection
  policy behavior
- strict metadata-to-download admission, including a deterministic 20-magnet transition simulation and a real
  MonoTorrent 20-magnet queue burst
- durable metadata-resolution time slicing, never-tried-first rotation, oldest-yielded retry order, and one yield log
  per rotation
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
Milestone 4 adds shared tests for targeted History, Logs, peers, trackers, and Service Settings reads; separate
abandonment history; independent feature snapshot state; and single-item operational mutations
with authoritative refresh. Its signed fixture UI suite covers the existing torrent inspector and removal confirmation
plus automatic History, Logs, and Service Settings loading without manual refresh. The post-parity refinement coverage
also verifies the shared settings-help catalog, native help popovers, constrained service-setting selectors, and
show/hide behavior for the Torrents, History, and Logs inspectors. Service Settings UI coverage also verifies populated
Downloads values and the inline category grid. Runtime-settings mapping coverage verifies that the metadata-resolution
time slice and automatic-reset stuck threshold survive draft creation, request encoding, save, and returned-value
reconciliation.
The live read-only probe also decodes History, Logs, runtime settings, peer/tracker diagnostics, and history detail
when corresponding records exist.

Milestone 5A adds direct coverage for denied and interrupted networks, read and mutation timeout meaning, late
old-context responses, restart recovery retries, changed service-instance identity, and the agreed fixture maxima of
100 torrents, 500 history rows, 5,000 log rows, 250 peers, and 50 trackers. A changed instance must clear cached remote
snapshots, preserve device profiles and preferences, and reload only the open feature context.

Milestone 5B coverage verifies the additive History callback Final Result summary contract, generated Swift mapping,
Summary fallback from display message to Final Result, the four callback feedback fields in the History inspector, and
copying the complete stored magnet URI to the macOS pasteboard.

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
The private `--torrentcore-ui-large-fixtures` mode provides the agreed maximum collections to UI automation under the
same no-network boundary. The July 25, 2026 Milestone 5A signed fixture run passed all six UI tests, including keyboard
navigation, initial Add Magnet focus, and large-list pagination.

Production Swagger availability is verified by `OpenApiContractTests` alongside normalized contract comparison.
The same contract regression requires every `ServiceProblemDetailsDto` response to advertise
`application/problem+json`, matching the service's existing runtime response. Swift transport coverage verifies that
this media type is decoded into a structured operator-facing service error.

Milestone 5B also compiles direct checks for the device-local System/Light/Dark appearance choices and intentionally
narrow Add Magnet validation. The validation catches non-magnet text and missing or empty `xt` values without
duplicating MonoTorrent's authoritative parsing.
Refresh regression coverage drives more than one Peer and Tracker request through the shared global interval, verifies
that cancellation stops a visible view's task, and separately proves that Add Magnet categories and Service Settings
remain independent one-time master-data loads.

Milestone 5C adds a fail-fast release preflight and a repeatable Developer ID/notarization workflow. Its repository
surfaces can be checked without release credentials:

```bash
zsh -n Scripts/release-macos-app.zsh
plutil -lint clients/apple/ExportOptions-DeveloperID.plist
./Scripts/release-macos-app.zsh --help
```

On the designated release Mac, `./Scripts/release-macos-app.zsh --check` additionally requires a valid
`Developer ID Application` identity for Team `5GRR76N48V` and the local `TorrentCore-notary` Keychain profile. The full
release command archives, exports, signs, notarizes, staples, and verifies the DMG. Separate-Mac installation and
Gatekeeper acceptance belong to Stage 5D rather than routine fixture testing.

The Service/WebUI deployment DMG has a separate acceptance boundary from the native app DMG. Its release smoke test
must verify Developer ID signatures plus JIT and shared-runtime library-validation entitlements on both framework-
dependent apphosts, the signature on every native Mach-O dependency, an accepted and stapled DMG, and a direct-email
install on the target Mac without any xattr clearing.

The first complete release run passed on July 26, 2026. Apple accepted the 0.1.0/build 1 notarization submission, and
the final copied DMG independently passed code-signature, stapler-ticket, disk-image checksum, and Gatekeeper
assessment.
The 0.2.0/build 2 upgrade candidate completed the same release workflow on July 26, 2026. Apple accepted submission
`f6dd6d0f-fa7e-4b5c-9260-2387f7cdecfd`; the copied DMG passed signature, stapler-ticket, disk-image, and Gatekeeper
checks. Installation over 0.1.0 subsequently passed operator upgrade acceptance.
The 0.2.1/build 3 compatibility hotfix also completed the release workflow on July 26, 2026. Apple accepted submission
`72257d2e-d315-40dc-a315-71530bfdd9af`; the copied DMG passed signature, stapler-ticket, disk-image, and Gatekeeper
checks. Its focused signed UI test passed on macOS 26. Subsequent separate-Mac testing on macOS 27 proved the same
split-view constraint abort could still occur after saving a connection, so the compatibility fix was incomplete.
The 0.3.0/build 4 UI-refinement update completed the release workflow on July 27, 2026. Apple accepted submission
`cea84cc3-1f89-49fa-9766-8c12dd6cd597`; the copied DMG passed signature, stapler-ticket, disk-image, Gatekeeper, and
checksum verification. Its SHA-256 checksum is
`eec8762805329edbe626b425484e57ef20b0c8335836aa60ca7422dc611e3f27`. Separate-Mac upgrade acceptance passed on
Apple Silicon macOS 26. On macOS 27, saving a connection reproduced the split-view abort in 0.3.0 and then in
downgraded 0.2.1 with the same persisted state. The stable-layout follow-up adds a signed UI regression that launches
directly into a saved active connection at the 1000×650 minimum size, verifies that Connection maintenance actions
remain above the global status bar, and verifies sidebar hiding without resizing the content. Its focused signed run
passed on macOS 26; separate macOS 27 confirmation remains pending.
The 0.3.1/build 5 stable-layout update completed the release workflow on July 27, 2026. Apple accepted submission
`d0ee05c1-d3a0-4434-9314-94ba3f841cd5`; the copied DMG passed signature, stapler-ticket, disk-image, Gatekeeper, and
checksum verification. Its SHA-256 checksum is
`b4c746f3fa62c0cf47af52e9b3bc324de6239754ef4e4d58ae74a19ce14bb87d`. Separate-Mac upgrade and saved-connection
startup acceptance on macOS 27 remain pending.

The July 28 Service Settings Cleanup refinement passed all 182 Service tests and all 33 shared Apple package tests.
The signed macOS run passed five unit tests, including the seven-day/30-day defaults and future-date validation, plus
the focused fixture UI test that opens the final Cleanup group and verifies its fields, actions, and three separate
confirmation dialogs. The macOS target built for testing and the shared client also built successfully for the iOS
Simulator. Persistence tests verify exclusive cutoffs and preservation of rows associated with live torrent ids;
API tests cover Service-local cutoff conversion, audit events, and future-date rejection.
The operator then deployed the updated Service to CA-Desktop and successfully exercised the macOS client through
Xcode against that live installation. The signed, notarized, and stapled 0.4.0/build 7 DMG completed the release
workflow under submission `b55e898b-b73d-4ff0-b3ad-0b3a1563d373`; the copied deployment artifact passed signature,
stapler-ticket, disk-image, Gatekeeper, and checksum verification. Its SHA-256 checksum is
`74e8325562a90bafa9a1a982b881c6d75a92c34166dc88acfb2e0c43e74459c8`.
The 0.4.1/build 8 app-icon update completed the same release workflow on July 29, 2026 under submission
`a53db386-2133-4910-be77-4354fea77089`; the copied deployment artifact passed signature, stapler-ticket, disk-image,
Gatekeeper, and checksum verification. Its SHA-256 checksum is
`acb507af743172642d0440a59e61a46dd5c95bccb1606279ace35e7e57c7f835`. Separate-Mac installation-over-0.4.0
acceptance remains pending.

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
