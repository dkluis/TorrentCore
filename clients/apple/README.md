# TorrentCore Apple Clients

## Status

Milestones 0 through 4 are complete. Milestone 5 is in progress; its automated hardening stage passed shared, macOS
build, iOS Simulator build, and signed fixture UI verification on July 25, 2026. The initial Milestone 5B visual,
keyboard, app-local appearance, disposable mutation, and controlled service-outage/recovery checks passed on
CA-Desktop. Stage 5C produced the signed, notarized, and stapled 0.1.0 release DMG on July 26, 2026. Stage 5D acceptance
is complete: the DMG installed and worked normally on Apple Silicon systems running macOS 26 and macOS 27, including
normal Gatekeeper launch on CA-Dick-MBA, LAN connection to CA-Desktop, and add, pause, and remove-with-data mutations.
The signed, notarized, and stapled 0.2.0/build 2 app also passed installation-over-0.1.0 upgrade acceptance.
Off-site routed-VPN verification remains environmentally deferred.

The next active workstream is further macOS UI refinement. Its first operator-prioritized slice shipped in 0.2.0. The
signed, notarized, and stapled 0.2.1/build 3 compatibility hotfix avoided one direct split-view detail wrapper, but
later macOS 27 testing proved that it did not eliminate the underlying SwiftUI/AppKit split-view constraint abort. The
signed, notarized, and stapled 0.3.0/build 4 update adds WebUI-aligned operator presentation and right-side inspector
overlays that do not resize the underlying tables. It installed and worked normally on a separate Apple Silicon
macOS 26 system. On CA-Dick-MBA running macOS 27, saving a connection triggered the same split-view constraint abort;
the persisted layout then reproduced the abort in both 0.3.0 and downgraded 0.2.1. Opening Dashboard while bypassing
window restoration recovered the installation without deleting its saved connections. A main-branch follow-up
replaces the macOS root and nested maintenance split views with stable stack layout and adds compact-window saved-
connection regression coverage; separate-Mac acceptance of that follow-up remains pending. Milestone 6 iPad
Adaptation remains open but is deferred because no iPad test device is currently available.

That stable-layout follow-up is released as signed, notarized, and stapled 0.3.1/build 5. The release DMG passed local
signature, stapler-ticket, disk-image, Gatekeeper, and checksum verification. Separate-Mac installation-over-0.3.0
acceptance, especially launch and saved-connection use on macOS 27, remains pending.

The maintenance and filtering refinements are released as signed, notarized, and stapled 0.4.0/build 7. The updated
Service and macOS client passed live CA-Desktop testing through Xcode, and the copied release DMG passed signature,
stapler-ticket, disk-image, Gatekeeper, and checksum verification.

The native app-icon update is released as signed, notarized, and stapled 0.4.1/build 8. The copied release DMG passed
signature, stapler-ticket, disk-image, Gatekeeper, and checksum verification. Separate-Mac installation-over-0.4.0
acceptance remains pending.

The metadata-admission and recovery update is released as signed, notarized, and stapled 0.5.0/build 9. It adds the
metadata resolution time-slice and automatic-reset stuck-threshold controls to native Service Settings. The copied
release DMG passed signature, stapler-ticket, disk-image, Gatekeeper, and checksum verification.

Current source also displays the optional Service Git build identity on the macOS dashboard. Older Service versions
that omit the additive field continue to display `--` and remain connectable.

`TorrentCore.WebUI` remains the supported operator UI.

## Targets

| Target | Platform | Bundle identifier |
|---|---|---|
| `TorrentCoreMac` | macOS 26+, Apple Silicon | `com.conadv.TorrentCore.mac` |
| `TorrentCoreMobile` | iOS/iPadOS 26+ | `com.conadv.TorrentCore.mobile` |

Both application targets use the local `TorrentCoreKit` Swift package. macOS is implemented first; the mobile target
exists at the baseline so shared code is continuously buildable for iOS.

Both targets use the same approved TorrentCore app-icon artwork: an orange segmented transfer ring ending in a
download arrow around a dark central core. The macOS asset catalog supplies the complete native size set, while the
mobile catalog keeps one 1024-point source for future iOS/iPadOS adaptation.

## Development Model

A deployed TorrentCore runtime may coexist on the development Mac, but routine development, tests, and SwiftUI
previews use fakes and fixtures. A live service address is not compiled into the app or committed as executable
configuration; operator-approved integration hosts are documented only to support explicit integration work.

Live integration is opt-in:

```bash
export TORRENTCORE_INTEGRATION_BASE_URL='http://ca-desktop.local:7033'
swift test \
  --package-path clients/apple/Packages/TorrentCoreKit \
  --filter liveReadOnlyIntegrationProbe
```

The probe reads health, host status, dashboard lifecycle, torrents, optional torrent detail, and categories. It never
performs a mutation. Without the variable, the live test returns immediately and routine tests remain fixture-only.

`liveDisposableMutationSequence` is a separate, explicitly gated test. It runs only when all of these values are
supplied for an operator-approved disposable target:

- `TORRENTCORE_ALLOW_DISPOSABLE_MUTATION=1`
- `TORRENTCORE_INTEGRATION_BASE_URL`
- `TORRENTCORE_DISPOSABLE_MAGNET_URI`
- `TORRENTCORE_DISPOSABLE_INFO_HASH`
- `TORRENTCORE_DISPOSABLE_CATEGORY`, using the live enabled category display name

The test validates host capabilities and the magnet hash, resolves exactly one enabled category, and refuses to add
the torrent if that hash already exists. It then adds, observes, pauses, resumes, and removes only the returned torrent
ID with data deletion enabled. Failures after creation trigger best-effort cleanup of that same verified target. Do not
save a disposable magnet or a live endpoint in the repository.

## Contract And Client

`TorrentCoreAPI` uses pinned Swift OpenAPI Generator, runtime, and URLSession packages. The committed normalized
`openapi.json` is generated by an in-process .NET contract test. Generated Swift declarations remain internal and are
wrapped by the handwritten `TorrentCoreClient` facade and resilient public models.

Default request timeouts are 3 seconds for health, 15 seconds for reads, and 60 seconds for mutations. A mutation
timeout is reported as an uncertain outcome and must be followed by a refresh rather than an automatic retry.

On the first Xcode build, trust and enable Apple’s `OpenAPIGenerator` package plugin when Xcode asks. Command-line
automation may use `-skipPackagePluginValidation` with the pinned package versions.

## Connection Profiles And Refresh

Connection profiles are nonsecret, device-local client settings. `UserDefaultsTorrentCoreProfileStore` stores one
versioned JSON document under `TorrentCore.ClientPreferences.v2`; it is not written to iCloud. The document contains:

- named profiles with stable UUIDs, normalized base URLs, and created/updated timestamps
- the selected profile UUID
- one client-wide refresh interval
- one client-wide Auto Refresh on/off preference

On first load, a version 1 document is decoded with Auto Refresh enabled, validated, and copied to the version 2 key.
The legacy value is left in place as a recovery copy. Saved connections, active selection, and refresh interval are
preserved.

There is no compiled or automatically created server profile. Operators create profiles at runtime. Addresses accept
HTTP or HTTPS hostnames and IP addresses with optional ports; missing schemes become HTTP. Credentials, query strings,
fragments, and non-root paths are rejected. Duplicate normalized service addresses are not allowed.

The selectable refresh intervals are 5, 10, and 15 seconds, with 15 seconds as the default. Auto Refresh defaults on
and can be disabled without disabling manual Refresh. Each visible live-data view owns its cancellable refresh task and
uses the same global interval; changing tabs cancels only the view that disappeared. Backgrounded and suspended views
do not poll. Dashboard, Torrents, History, Logs, Peers, and Trackers all use this policy. Add Magnet categories,
Connection, and Service Settings issue independent one-time master-data loads when presented and remain manually
refreshable. They do not replace or cancel another feature's refresh context. When a torrent inspector is visible, the
Torrents view refreshes the list and selected detail together. Offline state retains last-known values in memory and
marks them stale. Service actions are disabled until refresh reconnects, and mutations are never retried automatically.

Reconnect also verifies the service instance identity. If the service has been replaced or restarted with a new
identity, all cached remote snapshots are cleared while device profiles and preferences remain intact. Only the open
feature context reloads, and mutations stay disabled until its authoritative state is current.

`TorrentCoreCredentialStoring` reserves a credential boundary and the Keychain service name
`com.conadv.TorrentCore.credentials`. The initial trusted-LAN/VPN model uses
`UnconfiguredTorrentCoreCredentialStore`, so it creates no Keychain items or permission prompts.

## macOS Operator UI

The macOS app has one main operator window and uses a native sidebar for Dashboard, Torrents, History, Logs, Service
Settings, and Connection. Each presented feature owns its own load or polling task while sharing connection and cached
state through `TorrentCoreFeatureSession`. The app remembers the last destination, but opens Connection when there is
no active saved connection.

- Connection manages named installations and keeps Test Connection separate from Save & Connect. Unreachable
  installations can still be saved for later LAN or VPN use. Its profile list and maintenance actions use stable
  non-split layout, keeping New Connection and Delete above the global status bar at the minimum supported window
  height.
- Dashboard shows service and engine identity, transfer totals, torrent states, queue capacity, startup recovery, and
  recent lifecycle events.
- Torrents uses a native sortable table with name, state, and category filters; local pagination matches the WebUI
  choices of 25, 50, 100, and 250 rows.
- Single selection drives fixed-width trailing overlay inspectors for Torrents, History, and Logs. The reusable
  overlay does not participate in table layout, so showing or hiding a current or future right-side panel preserves
  operator-resized columns. One standard toolbar and View menu command shows or hides the inspector for the active
  destination. Torrent details expose pause, resume, remove, remove-with-data, peer/tracker diagnostics, metadata
  recovery, callback retry, and cross-navigation.
- Add Magnet uses enabled service categories in service sort order and permits Uncategorized.
- Add Magnet and Refresh are permanent window-toolbar items. Add Magnet remains available from every destination while
  connected and returns to Torrents without selecting a row or opening the inspector after a successful add.
- Both remove paths require native destructive confirmation. A mutation timeout is shown as uncertain and is followed
  by authoritative refresh rather than automatic retry.
- History starts with Today, uses dropdowns for bounded category, state, and outcome filters, preserves abandonment
  visibility, and presents the full WebUI-equivalent column set in a native sortable table. Bounded results page
  locally at 25, 50, 100, or 250 rows. The inspector always presents callback Summary, Received, Final Result, and
  Reason values and can copy the full stored magnet URI, Torrent ID, and last-seen Service Instance ID. Local Copy
  actions remain available while the service is offline.
- Logs combine service-side filters and selectable recent-row limits with search over the loaded rows. Category and
  Event Type use loaded-value dropdowns; exact identifiers remain text fields. The native sortable table pages locally
  at 25, 50, 100, or 250 rows, and its inspector can copy Torrent and Service Instance identifiers. Orphaned-log
  cleanup requires confirmation.
- Peer and Tracker diagnostics use native sortable tables with 10, 25, 50, or 100 rows per page. Their pop-ups prefer
  enough width for the complete WebUI-equivalent diagnostic column sets when display space permits.
- Service Settings edits one group at a time with Save/Revert and guarded navigation. Closed server values use readable
  selectors for seeding stop mode, completed-torrent cleanup mode, and engine encryption mode. Dependent controls are
  disabled when their selected policy does not use them, and service validation rules are enforced before Save.
  Metadata Recovery includes the live metadata-resolution time slice and automatic-reset stuck threshold with the
  same ranges and defaults as the Service contract.
  Numeric settings show their current values in editable fields. Categories use one full-width inline editable grid,
  falling back to horizontal scrolling only when the tab is genuinely too narrow. Save submits only changed category
  rows sequentially through the existing single-category API.
- Service Settings includes a final Cleanup group. Log Entries defaults to seven days back, History Records defaults
  to 30 days back, and both dates recalculate whenever the group opens. Each destructive action confirms separately,
  rejects future dates, reports its deleted count, and preserves records tied to torrent ids still present in the live
  torrent table. History eligibility uses Last Updated. The existing orphan-log operation remains available on Logs
  and is also exposed in Cleanup.
- Compact information buttons beside native fields open anchored help popovers. The help content is shared through
  `TorrentCoreFeatures` for later iOS/iPadOS presentation, while the popover renderer remains macOS-specific.
- Dashboard, Torrents, History, Logs, Peers, and Trackers follow the global foreground refresh policy only while their
  context is visible. Add Magnet categories and Service Settings load once when presented and support manual refresh.
- A noninteractive status bar at the bottom of the content area permanently identifies the selected installation as
  profile name, service address, and textual connection state. Connection remains available through navigation.
- The Navigate menu maps Command-1 through Command-6 to the six sidebar destinations. Context menus and direct
  cross-navigation keep actions scoped to one torrent.
- Opening a destination immediately loads its visible service data. First loads show progress, successful empty
  responses show an empty state, and failures show an unavailable state.
- Standard macOS Settings and the customizable main toolbar share the same global Auto Refresh and interval
  preferences. Inspector is a contextual customizable item. Connection status remains permanently visible outside
  toolbar customization.

The app has no compiled live endpoint. `--torrentcore-ui-fixtures` and the large-collection variant
`--torrentcore-ui-large-fixtures` are reserved for Xcode UI testing and start an in-memory fixture service; neither
contacts nor mutates a deployed TorrentCore installation.

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
  -skipPackagePluginValidation \
  SYMROOT=/private/tmp/torrentcore-apple-mac-products \
  OBJROOT=/private/tmp/torrentcore-apple-mac-intermediates \
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
  -skipPackagePluginValidation \
  SYMROOT=/private/tmp/torrentcore-apple-mobile-products \
  OBJROOT=/private/tmp/torrentcore-apple-mobile-intermediates \
  CODE_SIGNING_ALLOWED=NO \
  build
```

Compile the macOS app and both test targets without launching an app:

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

Running `TorrentCoreMacUITests` launches an app and therefore requires a normal development-signed build. Do not run
UI tests against an unsigned app product. The explicit temporary build roots above also prevent compile-only
verification from replacing the normal signed Xcode product. The UI tests use only the in-memory fixture environment.

## Signing And Distribution

- Apple Developer Team ID: `5GRR76N48V`
- signing style: automatic
- macOS distribution: signed and notarized outside the Mac App Store
- mobile distribution: TestFlight

Signing identities and provisioning profiles remain machine/account state and are not committed.
The repeatable macOS release workflow, one-time Developer ID/notary setup, installation, upgrade, and client-only
uninstall procedures are documented in [deployment.md](../../docs/deployment.md#native-macos-app-release).

## Network Boundary

Initial clients connect over HTTP only inside a trusted LAN or a VPN that routes onto the LAN. Direct public-internet
access is outside scope. Both apps declare local-network use and narrowly allow local HTTP through
`NSAllowsLocalNetworking`. The WebUI remains deployed for Windows clients and recovery access.

See [the development plan](../../docs/native-apple-client-development-plan.md).
