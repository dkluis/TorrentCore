# Native Apple Client Development Plan

## Status

This document is an implementation plan, not a statement of current product support.

`TorrentCore.WebUI` remains the supported operator UI until the native macOS client reaches its release milestone and
the active architecture documentation is deliberately updated.

Milestones 0 through 3 were completed on July 23, 2026, and Milestone 4 was completed on July 24, 2026.
Milestone 5 is in progress; its automated hardening stage was completed on July 25, 2026, release construction was
completed on July 26, 2026, and Stage 5D acceptance passed on Apple Silicon systems running macOS 26 and macOS 27.
Off-site routed-VPN verification remains environmentally deferred.

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
- Keep the limited-release accessibility scope proportional: verify keyboard and focus behavior, readable native text and
  contrast, useful control labels, and no color-only state. A comprehensive VoiceOver pass is not required.

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
- The macOS app has one main operator window. Each visible live-data feature owns a structured, cancellable polling
  task using the global foreground auto-refresh interval; leaving that feature cancels only its task. Dashboard,
  Torrents, History, Logs, Peers, and Trackers use this policy. Add Magnet categories, Connection, and Service Settings
  perform independent one-time loads when presented and remain manually refreshable.
- Service settings remain distinct from device-local macOS Settings. One service-settings group can be dirty at a
  time, with Save/Revert and Save/Discard/Cancel navigation protection. Callback API-key text is transient form state
  and is neither persisted nor logged by the Mac client.
- Service enum values are presented with readable selectors while retaining their exact API tokens. Fields that do not
  apply to the selected policy remain visible but disabled, and client-side validation mirrors the service rules before
  Save.
- Native field and action help uses compact information buttons with hover summaries and anchored macOS popovers. Help
  content lives in the shared feature package so later iOS and iPadOS presentation can reuse it without sharing
  platform-specific UI.
- Existing categories are maintained together in a full-width inline editable grid, with horizontal scrolling reserved
  for widths where the complete grid cannot fit. Save submits only changed rows sequentially through the existing
  single-category API; category creation and deletion are not invented by the native client.
- Service restart requires confirmation and polls for recovery for about 30 seconds.
- All actions remain single-torrent operations. Multi-selection and bulk APIs remain deferred because no concrete
  operator need was established.
- Destination changes synchronously enter a loading state and immediately request only the newly visible context.
  Empty-state messaging is reserved for successful zero-row responses; connectivity and request failures remain
  unavailable states.
- The main macOS toolbar is customizable through the standard system command. Add Magnet and Refresh are permanent
  reorderable items, and torrent actions are contextual customizable items. Connection status is permanently visible
  in a noninteractive content-area status bar outside toolbar customization. Refresh is owned by the main navigation
  toolbar rather than an inspector. One contextual Inspector item and View-menu command operate the active Torrents,
  History, or Logs inspector and remain available for toolbar customization.

### Milestone 5: macOS Hardening And Limited Release

Status: in progress. Stage 5A automated hardening completed July 25, 2026. Stage 5B manual verification is in progress;
Stage 5C release construction completed July 26, 2026. Stage 5D separate-Mac acceptance is complete on both planned
macOS versions. Off-site routed-VPN verification remains environmentally deferred.

Turn feature parity into a supportable application.

Work:

- complete the agreed limited accessibility review: keyboard navigation, focus, readable native text and contrast,
  useful control labels, and no color-only state
- test slow, interrupted, denied, and changing network conditions
- test service restart, service-instance changes, and stale responses
- test large torrent, history, peer, tracker, and log collections
- verify no sensitive callback or connection data is written to diagnostic logs
- add release build, signing, notarization, packaging, and installation instructions
- add a repeatable opt-in live integration checklist
- document recovery through the WebUI when the native client is unavailable

Implementation stages:

- **5A — automated hardening:** complete. Deterministic shared tests cover read and mutation timeout meaning, denied and
  interrupted network failures, late responses from an old feature context, restart recovery retries, service-instance
  replacement, and the agreed maximum fixture collections of 100 torrents, 500 history rows, 5,000 log rows, 250
  peers, and 50 trackers. The signed fixture UI suite covers Command-1 through Command-6 navigation, initial Add Magnet
  focus, torrent and history paging, and the log result-limit notice.
- **5B — limited accessibility and manual failure verification:** in progress. The agreed visual review, app-local
  System/Light/Dark appearance choices, Command-1 through Command-6 navigation, inspector controls, Add Magnet focus,
  and the main read screens passed operator verification on CA-Desktop. The Add Magnet sheet performs one category
  load when opened, quietly revalidates cached categories, rejects only clearly invalid magnet syntax locally, and
  presents structured TorrentCore problem details. The guarded disposable add/observe/pause/resume/remove sequence
  passed against CA-Desktop on July 25, 2026. A controlled service-only outage also passed: cached data remained
  coherent and read-only, mutations were disabled, Refresh remained available, and the open context recovered without
  strange or stale cross-instance data after the service restarted. Off-site routed-VPN verification remains
  environmentally deferred.
- **5C — release construction:** complete July 26, 2026. Version 0.1.0/build 1, Team ID, Arm64/macOS 26 release
  settings, automatic Developer ID export options, deterministic DMG naming, and the fail-fast
  archive/export/sign/notarize/staple/verify script are configured. Installation, upgrade, client-only uninstall,
  WebUI recovery, certificate, and Keychain credential procedures are documented. Apple accepted notarization
  submission `09e87103-e918-471f-a6e3-daf16558b46e`; the copied deployment artifact passed signature, stapler,
  disk-image, and Gatekeeper verification. The subsequent 0.2.0/build 2 UI-refinement upgrade candidate was accepted
  under submission `f6dd6d0f-fa7e-4b5c-9260-2387f7cdecfd`; its copied DMG passed the same release checks and subsequently
  passed installation-over-0.1.0 upgrade acceptance. The 0.2.1/build 3 macOS 27 compatibility hotfix was accepted under
  submission `72257d2e-d315-40dc-a315-71530bfdd9af` and passed the same release checks. The 0.3.0/build 4 UI-refinement
  update was accepted under submission `cea84cc3-1f89-49fa-9766-8c12dd6cd597`; its copied DMG passed signature,
  stapler-ticket, disk-image, Gatekeeper, and checksum verification. Separate-Mac upgrade acceptance for 0.3.0 remains
  pending.
- **5D — separate-Mac acceptance:** complete July 26, 2026. The signed, notarized, and stapled 0.1.0 DMG installed and
  worked normally on an Apple Silicon macOS 26 system. It also installed and launched normally without Gatekeeper
  bypass on CA-Dick-MBA running macOS 27; LAN connection to CA-Desktop and add, pause, and remove-with-data mutations
  passed there. These results cover both planned OS-version acceptance targets.

Stage 5A behavior:

- A reconnect checks the service instance identity. If it changed, all cached remote snapshots are discarded, device
  profiles and preferences are preserved, and only the open feature context is loaded again. Mutations remain disabled
  until that context is current.
- Requested service restart uses the same identity check and bounded recovery polling. Cancellation or a profile change
  aborts recovery instead of retrying against a different installation.
- The native client contains no diagnostic logging calls. No callback API key, magnet URI, saved endpoint, or full URL
  is written to a native-client diagnostic log.
- Verification passed with 26 shared Swift tests, six development-signed macOS fixture UI tests, an unsigned macOS
  build-for-testing, and an unsigned iOS Simulator build. No service or WebUI source changed.

Stage 5B behavior completed so far:

- Appearance is a device-local app preference with System, Light, and Dark choices. System remains the default.
- The main operator scene is single-window. Live-data views own their structured refresh tasks and share the global
  5/10/15-second preference without a session-owned polling loop, so presenting master data cannot cancel an unrelated
  request. Peer and tracker diagnostics use the same policy as the other live operational views; Add Magnet categories,
  Connection, and Service Settings remain independent one-time loads.
- The content-area status bar permanently shows the selected profile name, address, and textual connection state.
  It is noninteractive and intentionally outside toolbar customization.
- The service's existing `application/problem+json` error responses are described with that media type in OpenAPI, so
  the generated Swift client decodes the existing structured error body instead of reporting a content-type mismatch.
  This is contract-metadata correction only; the service runtime response and handwritten C# WebUI client behavior are
  unchanged.
- Add Magnet does a single category refresh when the sheet opens. Existing category values remain usable without a
  misleading continuous-refresh message, and a category-load failure still permits an Uncategorized add.
- Local validation requires only the `magnet:?` prefix and a nonempty `xt` query value. TorrentCore and MonoTorrent
  remain authoritative for duplicate, category, and complete magnet validation.
- During the controlled service-only outage, the app retained its last-known data without replacing it with a false
  empty state. After the local service restarted and its read-only live probe passed, the app reconnected and continued
  from the open context without operator intervention or inconsistent data.
- History rows show the completion callback Final Result from an additive summary-contract field, avoiding a
  per-row detail-request fan-out. Rows without feedback show an em dash.
- The History inspector always presents callback Summary, Received, Final Result, and Reason fields. Summary uses the
  feedback display message and falls back to Final Result; unavailable values show an em dash.
- The History inspector can copy the full stored magnet URI to the macOS pasteboard. This local-only action remains
  available during a service outage and is disabled only when the history record has no magnet URI.

Exit criteria:

- a signed and notarized release candidate installs cleanly on a separate macOS 26 system
- automated tests pass from a clean checkout
- live integration verification passes against a designated installation
- the release does not require a local .NET runtime

Only after this milestone should the active architecture documentation consider describing the Mac client as a
supported operator UI.

### macOS UI Refinement Workstream

Status: in progress. The operator chose to continue improving the macOS presentation before beginning iPad adaptation.
Preserve Service and WebUI behavior and keep reusable, non-UI behavior in the iOS-capable `TorrentCoreKit` package.

Accepted first refinement slice:

- replace the title-bar connection item with a noninteractive status bar at the bottom of the content area, showing
  connection name, service address, and textual connection status
- let destination content use the full width available to the right of the main sidebar and align content to that edge
- retain the Torrents table pattern and convert History and Logs from simulated grids to native sortable tables
- use dropdown filters for bounded values, free-text fields only for wildcard searches and exact identifiers, and
  native date controls for date ranges
- populate Log Category and Event Type dropdowns from loaded values while preserving an active selection
- use the full WebUI-equivalent History column set and add local 25/50/100/250-row pagination to Logs
- make Peer and Tracker headers sortable, add 10/25/50/100-row pagination, and size their pop-ups to show all columns
  without horizontal scrolling when display space permits
- size coded-value columns to remain readable, reserve flexible width for names and messages, keep numeric columns
  compact, and retain native column resizing
- add local Copy actions for Torrent ID and Service Instance ID in inspectors where those identifiers are present
- keep Dashboard metric layouts, forms, connection lists, sidebar lists, and the editable category-maintenance surface
  outside the sortable-table requirement

Next confirmed refinement:

- Peer and Tracker pop-up sections still open with horizontal scrolling instead of sizing to expose all columns when
  display space permits.
- User-adjusted Peer and Tracker column widths do not appear to persist. Confirm the native table customization
  behavior and implement durable width restoration without assuming that visibility customization also saves widths.

### Milestone 6: iPad Adaptation

Status: open but deferred. The operator does not currently own an iPad test device. Keep this milestone available for
later work rather than deleting or treating it as complete.

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
