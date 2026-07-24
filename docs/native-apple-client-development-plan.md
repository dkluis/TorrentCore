# Native Apple Client Development Plan

## Status

This document is an implementation plan, not a statement of current product support.

`TorrentCore.WebUI` remains the supported operator UI until the native macOS client reaches its release milestone and
the active architecture documentation is deliberately updated.

Milestones 0 through 3 were completed on July 23, 2026.

## Outcome

Build native macOS and iOS/iPadOS operator clients for TorrentCore without moving engine, persistence, queueing,
callback, recovery, or filesystem policy out of `TorrentCore.Service`.

The macOS client is the first product deliverable. Shared API, connection, state, formatting, and test layers are
designed for reuse by the later iPad and iPhone targets. Platform-specific views express the same capabilities through
native interaction models rather than sharing complete screens.

## Fixed Product Decisions

- Target macOS 26 or later and iOS/iPadOS 26 or later.
- Support Apple Silicon only.
- Use `TorrentCore` as the product name on macOS and mobile.
- Use bundle identifiers `com.conadv.TorrentCore.mac` and `com.conadv.TorrentCore.mobile`.
- Use Apple Developer Team `5GRR76N48V` with automatic signing.
- Use a normal Xcode-managed project without Tuist, XcodeGen, or another project generator.
- Distribute to a limited group rather than through a broad public release.
- Distribute the Mac app through signed and notarized direct download.
- Distribute the mobile app through TestFlight.
- Treat each TorrentCore installation as a private, effectively single-operator product.
- Allow a client to save profiles for unrelated installations, with one active installation at a time.
- Assume one TorrentCore host per LAN.
- Connect over a trusted LAN or a VPN that routes clients onto that LAN.
- Do not support direct public-internet exposure in the initial product.
- Continue using HTTP inside the trusted LAN/VPN boundary initially.
- Keep `TorrentCore.WebUI` available for Windows systems and as a fallback operator surface.
- Use manual connection profiles first; defer Bonjour discovery unless it proves necessary.
- Persist profiles and the selected profile device-locally rather than through iCloud.
- Use one client-wide selectable refresh interval of 5, 10, or 15 seconds, defaulting to 15 seconds.
- Allow client-wide Auto Refresh to be turned off while preserving manual Refresh.
- Refresh only the open feature context while the application is active; do not poll in the background.
- Keep torrent actions single-item. Defer native multi-selection unless later operator experience establishes a need.
- Paginate the native torrent table locally with 25, 50, 100, or 250 rows after local filtering and sorting.
- Present selected torrent details in the standard resizable trailing macOS inspector.

## Development And Runtime Model

Source development and an independently deployed integration runtime may coexist on the development Mac. Routine
builds, tests, and previews must not depend on that runtime.

Current explicitly approved integration hosts include:

- `ca-desktop.local` for local ARM integration, including designated mutation testing
- `ca-server.local` (`192.168.68.80`) for the Intel installation

Normal Apple-client development must use fakes, fixtures, and an injected transport. SwiftUI previews and routine tests
must not require a locally running .NET service.

Live integration is opt-in. Read-only checks should precede any mutating test. Tests that add, pause, resume, remove,
change settings, or restart the service must use an explicit test procedure, operator confirmation, and a designated
disposable target.

The native clients never read or copy the TorrentCore SQLite database. Database snapshots remain a separate diagnostic
workflow and must be copied consistently before offline analysis.

## Architecture Boundaries

The native clients:

- communicate only through stable TorrentCore HTTP contracts
- do not call MonoTorrent
- do not read or mutate TorrentCore persistence
- do not reproduce queue, recovery, callback, seeding, cleanup, or path policy
- do not embed, install, start, or supervise `TorrentCore.Service`
- do not treat filesystem inspection as evidence of torrent or callback completion
- do not replace the WebUI during the initial rollout

The service remains the authoritative source for capabilities and allowed actions. Clients honor fields such as
`CanPause`, `CanResume`, `CanRemove`, `CanRefreshMetadata`, and `CanRetryCompletionCallback` rather than independently
reconstructing service policy.

## Repository Shape

```text
clients/apple/
├── TorrentCoreApple.xcodeproj
├── Apps/
│   ├── TorrentCoreMac/
│   └── TorrentCoreMobile/
├── Packages/
│   └── TorrentCoreKit/
│       ├── Sources/
│       │   ├── TorrentCoreAPI/
│       │   ├── TorrentCoreFeatures/
│       │   └── TorrentCoreSupport/
│       └── Tests/
├── Tests/
│   ├── TorrentCoreMacUITests/
│   └── TorrentCoreMobileUITests/
└── README.md
```

Use one Xcode project with separate macOS and iOS/iPadOS application targets. Keep reusable implementation in the
local `TorrentCoreKit` Swift package so platform targets cannot accidentally become the source of shared behavior.

The nested `AGENTS.md` records supported platforms, schemes, build commands, signing boundaries, preview rules, and
Apple-specific conventions.

## Reuse Boundary

### Shared Across macOS, iPadOS, And iOS

- API DTOs and request DTOs
- JSON date, enum, nullable-value, and error decoding
- `URLSession` transport, timeouts, cancellation, and retry classification
- service-error mapping and trace identifiers
- connection profiles and active-profile selection
- health probing and service identity validation
- foreground refresh coordination
- observable feature models
- capability and action-result handling
- formatting for sizes, rates, durations, timestamps, states, and wait reasons
- filter and sort definitions where their semantics are platform independent
- multi-item action coordination and partial-failure reporting
- test fixtures, fake transports, clocks, and deterministic schedulers

Shared packages should avoid AppKit and UIKit. Navigation, presentation, and platform controls remain outside the shared
feature layer.

### Platform Specific

- navigation structure
- macOS tables, inspectors, toolbars, commands, and Settings window
- iPad sidebar/list/detail composition
- iPhone tabs, compact rows, drill-down navigation, and swipe actions
- selection behavior and keyboard shortcuts
- sheets, popovers, context menus, and confirmation presentation
- platform-specific accessibility and UI automation
- share-extension presentation and handoff

## API Client Strategy

Milestone 1 includes a decision gate for Swift client generation.

1. Produce the service's `/swagger/v1/swagger.json` document through an in-process Development test host, so generation
   requires neither the live installation nor a long-running local service.
2. Normalize and compare the generated document with a committed contract artifact in automated verification.
3. Evaluate the document with Swift OpenAPI Generator.
4. Use generation only if the generated client needs no hand edits and preserves TorrentCore error semantics.
5. Otherwise implement a small typed `URLSession` client against the same committed contract and fixtures.

Generated code, if selected, must be reproducible and must not be manually patched. Either approach requires decoding
tests for representative success and failure payloads.

The first client slice must cover:

- health and host status
- dashboard lifecycle
- torrent list and detail
- categories
- add magnet
- pause, resume, and remove

Later parity adds history, logs, runtime settings, category updates, peers, trackers, metadata recovery actions, callback
retry, orphaned-log cleanup, and service restart.

## Milestones

### Milestone 0: Delivery Baseline

Status: complete (July 23, 2026).

Establish the product, repository, signing, and network baseline before feature implementation.

Completion evidence:

- the macOS and mobile targets build for arm64 from the command line
- the shared Swift package test passes from the command line
- Xcode automatic signing produces a valid macOS development signature for Team `5GRR76N48V`
- Debug, Integration, and Release configurations and shared schemes are present
- endpoint selection remains runtime configuration and no live endpoint default or signing material is committed

Work:

- confirm application names and bundle identifiers
- confirm the Apple Developer team and limited-distribution method
- create macOS and iOS/iPadOS targets with macOS 26 and iOS/iPadOS 26 deployment targets
- configure Apple Silicon build destinations
- establish Debug, integration, and Release configurations
- document that local previews use fakes and that live integration is opt-in
- record the LAN/VPN-only network model and prohibit direct public port forwarding
- select an initial integration endpoint without committing host-local configuration
- decide how the OpenAPI document is produced and validated

Exit criteria:

- both targets build from a clean checkout
- Swift package tests run from the command line
- no signing secret or live endpoint is committed
- no local TorrentCore runtime is required

### Milestone 1: Shared Contract And Transport Foundation

Status: complete (July 23, 2026).

Build the cross-platform API boundary before building operator screens.

Completion evidence:

- an in-process .NET test produces and compares the committed OpenAPI v1 artifact
- the pinned Swift OpenAPI Generator produces internal types and a client with no hand edits
- the public Swift facade covers every initial-slice endpoint and preserves TorrentCore problem details
- deterministic tests cover request paths, methods, bodies, decoding, future enum values, cancellation, offline
  failures, and timeouts
- macOS and iOS Simulator targets build with the same shared package
- preview fixtures cover connected, loading, empty, offline, and error states without a service
- read-only probes pass through both `ca-server.local` and `192.168.68.80`

Work:

- implement or generate DTOs and request types
- implement injected HTTP transport
- map TorrentCore problem details and service errors
- support cancellation, bounded timeouts, and offline failures
- handle GUIDs, ISO timestamps, string enums, nullable values, and unknown future enum values safely
- implement health probing and service identity capture
- add fixture-based decoding and request-construction tests
- add fake responses for previews and UI automation
- configure local-network privacy messaging
- configure the narrow local-network HTTP transport allowance required by Apple platforms

Exit criteria:

- the same package builds for macOS and iOS
- all initial-slice endpoints have deterministic tests
- previews can show connected, loading, empty, offline, and error states without a service
- an opt-in read-only probe can reach `ca-server.local`

### Milestone 2: Shared Connection And Feature State

Status: complete (July 23, 2026).

Completion evidence:

- profiles and the selected profile persist in one versioned, device-local `UserDefaults` document
- addresses normalize consistently and reject unsafe URL components or duplicate service addresses
- 5, 10, and 15-second client-wide refresh choices are available, with 15 seconds as the default
- foreground refresh loads only the open feature context and stops when the application is inactive
- offline state retains last-known values in memory, marks them stale, and disables service actions
- profile changes cancel in-flight work, clear all remote state, and reject late responses from the old installation
- successful single-item mutations refresh the open authoritative context
- deterministic tests cover persistence, context routing, lifecycle, reconnect, mutation refresh, and response ordering

Build reusable application behavior without committing to macOS or mobile presentation.

Work:

- save named connection profiles and select one active profile
- validate and normalize manually entered base URLs
- persist nonsecret profile settings
- reserve Keychain-backed credential support without requiring credentials in the initial HTTP/VPN model
- implement reconnect and explicit refresh behavior
- pause periodic refresh when the application is not active
- use one client-wide 5/10/15-second refresh preference with a 15-second default
- refresh only health, dashboard, torrent, detail, or category data required by the open feature context
- implement shared dashboard and torrent feature models
- implement capability-driven action availability
- refresh authoritative state after successful mutations
- allow only one client mutation at a time and defer multi-item actions
- prevent overlapping refreshes and stale response replacement

Exit criteria:

- shared state tests run for macOS and iOS destinations
- switching profiles cannot leak state between unrelated installations
- foreground/background transitions do not create duplicate polling loops
- offline and reconnect flows preserve a clear last-known-state boundary

### Milestone 3: macOS Core Operator MVP

Status: complete (July 23, 2026). Shared/unit and compile-only macOS/iOS verification pass. The development-signed
fixture UI tests pass, and an operator-approved disposable target completed add, observation, pause, resume, and
remove-with-data verification against the local CA-Desktop installation.

Deliver the first usable native product slice.

Work:

- build a `NavigationSplitView` shell
- add Connection, Dashboard, and Torrents destinations
- build a native torrent `Table`
- add sorting, filtering, single selection, and an inspector-style detail view
- add magnet submission with category selection
- add pause, resume, remove, and remove-with-data actions
- add destructive-action confirmations
- add foreground auto-refresh and manual refresh
- add native client settings for connection and refresh preferences
- add clear offline, loading, empty, stale, and error states

Exit criteria:

- an operator can connect, inspect service health, add a magnet, monitor it, pause or resume it, and remove it
- destructive operations cannot run without confirmation
- the MVP passes unit, integration, accessibility-smoke, and basic UI tests
- the WebUI remains unchanged and operational

### Milestone 4: macOS Functional Parity

Status: complete (July 24, 2026). Shared/unit, unsigned macOS test-build, iOS Simulator build, signed fixture UI, and
opt-in live read-only verification pass. No TorrentCore service or WebUI implementation changed.

Express the supported WebUI capabilities through native macOS workflows.

Work:

- add History with filters and detail
- add Logs with filters, details, and orphaned-log cleanup
- add service runtime Settings and category administration
- distinguish native client settings from service settings
- add peer and tracker diagnostics
- add metadata refresh and metadata-session reset
- add callback retry and callback-state detail
- add service restart with outage and recovery feedback
- reassess multi-selection only if single-item operator experience establishes a concrete need
- add context menus, toolbar items, application commands, and keyboard shortcuts
- add cross-navigation between torrents, history, and filtered logs

Exit criteria:

- every supported WebUI operator capability is either present or has a documented native-platform exception
- long history and log collections remain responsive
- settings validation and restart-required behavior match the service contract

Potential service changes discovered here, such as server paging or bulk operations, must be evaluated as separate API
slices. The Mac client must not simulate new service semantics silently.

Implementation notes:

- History uses the existing server filters, defaults to Today, preserves a separate abandonment summary, and performs
  local sorting and 25/50/100/250-row paging over the bounded server result.
- Logs use existing server filters with selectable 100/500/1,000/5,000 recent-row limits and local search. The app
  clearly marks a result that reaches the selected limit; no paging API was added.
- History and Logs follow the global foreground auto-refresh policy. Peer diagnostics refresh every five seconds only
  while their sheet is visible. Tracker diagnostics and Service Settings are one-shot with manual refresh.
- Service settings remain distinct from device-local macOS Settings. One service-settings group can be dirty at a
  time, with Save/Revert and Save/Discard/Cancel navigation protection. Callback API-key text is transient form state
  and is neither persisted nor logged by the Mac client.
- Existing categories can be edited, but category creation and deletion are not invented by the native client.
- Service restart requires confirmation and polls for recovery for about 30 seconds.
- All actions remain single-torrent operations. Multi-selection and bulk APIs remain deferred because no concrete
  operator need was established.

### Milestone 5: macOS Hardening And Limited Release

Turn feature parity into a supportable application.

Work:

- complete VoiceOver, keyboard navigation, focus, contrast, and Dynamic Type review
- test slow, interrupted, denied, and changing network conditions
- test service restart, service-instance changes, and stale responses
- test large torrent, history, peer, tracker, and log collections
- verify no sensitive callback or connection data is written to diagnostic logs
- add release build, signing, notarization, packaging, and installation instructions
- add a repeatable opt-in live integration checklist
- document recovery through the WebUI when the native client is unavailable

Exit criteria:

- a signed and notarized release candidate installs cleanly on a separate macOS 26 system
- automated tests pass from a clean checkout
- live integration verification passes against a designated installation
- the release does not require a local .NET runtime

Only after this milestone should the active architecture documentation consider describing the Mac client as a
supported operator UI.

### Milestone 6: iPad Adaptation

Reuse the stable package while building an iPad-native presentation.

Work:

- add sidebar/list/detail navigation
- adapt torrent administration to touch and pointer input
- provide drill-down history, logs, peers, trackers, callback state, and settings
- reuse connection profiles, feature state, formatting, actions, and test fixtures
- validate foreground refresh and reconnection across iPad lifecycle transitions

Exit criteria:

- core operator workflows work without macOS-specific UI assumptions
- shared-package changes are general improvements rather than copied screen behavior
- iPad tests cover compact and regular horizontal size classes

### Milestone 7: iPhone Adaptation

Build a compact operator experience on the same shared foundation.

Work:

- add `TabView` and `NavigationStack` composition
- use compact torrent rows and grouped detail sections
- add appropriate swipe actions, context menus, and sheets
- move dense diagnostics into focused drill-down screens
- retain explicit confirmations for destructive actions
- verify interruption, foreground refresh, and reconnect behavior on real devices

Exit criteria:

- the main monitor, add, pause, resume, remove, history, log, and settings workflows are usable on iPhone
- no background-continuous-monitoring behavior is implied
- the app passes accessibility and real-device local-network permission testing

### Milestone 8: Mobile Distribution And Optional Integrations

Complete limited mobile distribution and add convenience integrations after the core product is stable.

Work:

- configure TestFlight distribution
- add release notes and installation guidance for each private installation
- evaluate a share extension for magnet submission
- evaluate magnet URL handling
- use App Group storage only if an extension requires it
- evaluate Bonjour discovery only if manual profiles remain an operator problem
- evaluate notifications only with an explicit service-side delivery design

Exit criteria:

- a TestFlight build can connect over LAN and routed VPN
- the application fails closed when neither LAN nor VPN can reach the host
- optional integrations do not bypass connection validation or action confirmation

## Verification Strategy

### Routine Development

- Swift unit tests for DTOs, transport, feature state, formatting, and single-action coordination
- fixture-based contract tests for representative service payloads
- mock-transport integration tests
- SwiftUI previews backed exclusively by fixtures and fakes
- UI tests for core platform workflows
- command-line builds for every supported target and scheme

### Live Integration

Live tests are opt-in and target `ca-desktop.local`, `ca-server.local`, or another explicitly named installation.

Order:

1. health
2. host identity and status
3. dashboard and torrent reads
4. history and log reads
5. peer and tracker reads
6. mutation against a designated test torrent
7. administrative settings or restart only with explicit operator approval

The live integration suite must not assume that a copied database is current, and it must not access SQLite directly.

### Service Or Contract Changes

If native-client work changes a public service contract:

- update `TorrentCore.Contracts`
- update `TorrentCore.Client`
- update WebUI callers where affected
- add or update API tests
- run the relevant .NET build and test suite
- regenerate or validate the Swift contract
- verify compatibility with the deployed service version policy

## WebUI And Deployment Impact

The initial native-client milestones do not replace or redeploy the WebUI.

Expected topology:

```text
macOS / iOS / iPadOS client
    -> trusted LAN or routed VPN
    -> TorrentCore.Service HTTP API

Windows browser
    -> trusted LAN or routed VPN
    -> TorrentCore.WebUI
    -> loopback TorrentCore.Service HTTP API
```

Deployment rules:

- do not expose Service or WebUI ports directly to the public internet
- keep the VPN route to the host LAN address
- use a stable LAN address or local hostname for each installation
- keep native application distribution separate from service launch-agent deployment
- do not add the native Mac application to TorrentCore launch-agent management
- retain WebUI deployment for Windows and recovery access

HTTPS and service authentication are deferred while the trusted LAN/VPN boundary is the accepted security boundary.
If direct public access becomes a requirement, stop and plan authentication, TLS, secret storage, WebUI protection, and
deployment changes before enabling it.

## Principal Risks

| Risk | Mitigation |
|---|---|
| Development actions affect the live installation | Use fakes by default, opt-in live configuration, read-only smoke tests first, and designated test torrents |
| Swift DTOs drift from .NET contracts | Reproducible OpenAPI or typed contract fixtures plus compatibility tests |
| Shared code becomes lowest-common-denominator UI | Share behavior and state, not entire screens |
| Polling creates duplicate requests or stale state | Central refresh coordination, cancellation, lifecycle awareness, and response ordering |
| Multi-item actions introduce unclear partial outcomes | Keep actions single-item unless a later milestone approves explicit batch semantics |
| HTTP is accidentally exposed publicly | LAN/VPN-only deployment rule and no router port forwarding |
| iOS background suspension conflicts with expectations | Promise foreground monitoring only unless a notification design is added |
| Native work breaks WebUI behavior | Keep service semantics authoritative and run .NET/WebUI regression verification for contract changes |
| Generator output requires manual edits | Reject generation or fix the source contract; never patch generated output |

## Completion Definition

The native Apple client initiative is complete when:

- the macOS, iPadOS, and iOS applications use one tested shared API and feature package
- each platform provides native interaction rather than a resized desktop screen
- service-domain behavior remains in `TorrentCore.Service`
- WebUI remains available for Windows and fallback administration
- normal development and previews require no local TorrentCore runtime
- live integration is explicit and safe
- limited Mac and TestFlight distribution are repeatable
- LAN and VPN operation are documented
- no native client depends on direct database or filesystem access
