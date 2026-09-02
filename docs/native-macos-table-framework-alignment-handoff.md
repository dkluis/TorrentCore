# Native macOS Table And Connection Safety Alignment Handoff

Status: complete — operator accepted on 2026-09-02

Date: 2026-09-02

## Completion Result

All five implementation slices are complete. The operator visually accepted the debug macOS UI on CA-Desktop running
macOS 26.6.2 against the deployed CA-Server Test service and closed this workstream. Future findings will be handled as
patches. This acceptance did not authorize or perform packaging or production deployment.

The completed result includes the connection-environment warning bar, shared table behavior for Torrents, History,
Logs, Peers, and Trackers, the accepted defaults and filter presentation, persisted resizable main-table inspectors,
and resizable Peer and Tracker windows without row selection or inspectors. The macOS UI version is 1.0.0 build 16.

During operator testing, History lookup required exact Torrent ID filtering before the service result limit was
applied. The operator separately authorized that additive query field and the corresponding Service, C# client,
OpenAPI, Swift client, and focused test updates. No available TorrentCore data or service behavior was otherwise
expanded.

Final verification passed all 360 .NET tests, all 38 shared Swift package tests, the macOS build-for-testing and unit
target, and an unsigned iOS Simulator build of the shared client changes.

## Objective

Align TorrentCore's macOS operator tables with the accepted TVMaze native table and trailing-overlay behavior. Also
add the accepted connection-environment classification and persistent color warning bar so the operator can always
distinguish a Production connection from a Test connection.

This is a native-client presentation change plus one additive device-local profile field, not a new UI architecture.
TorrentCore already has the required connection creation, editing, testing, persistence, activation, profile-switch
isolation, refresh, stale-state, capability, and API behavior. Preserve that implementation. TorrentCore service
behavior, HTTP contracts, SQLite, WebUI behavior, and service configuration do not change except for the separately
authorized exact Torrent ID History query described in the completion result.

## Scope And Fixed Boundaries

In scope:

- the macOS Torrents, History, Logs, Peers, and Trackers table surfaces;
- device-local Production/Test classification for each saved connection profile;
- the macOS Connection view and a persistent environment warning bar across the main window;
- shared macOS-only table, sort, export, pagination, notice, and trailing-overlay helpers;
- small unit tests for new isolated profile and table-support logic;
- operator-controlled acceptance against the real TorrentCore installation and data on CA-Server; and
- current native-client documentation after operator acceptance.

Out of scope:

- `TorrentCore.Service`, public API contracts, SQLite, and `TorrentCore.Client`, except for the separately authorized
  exact Torrent ID History query;
- `TorrentCore.WebUI`;
- the mobile client;
- Dashboard, native Settings, Service Settings forms, and the editable category-maintenance grid, except that the
  environment bar remains visible above those macOS surfaces;
- service-reported environment discovery, hostname/address inference, or environment-specific service behavior;
- new dependencies, AppKit table replacement, or a generic screen-generation framework;
- multi-row torrent actions or bulk service operations;
- changes to the existing global refresh interval choices, auto-refresh ownership, filters, destructive confirmations,
  capability checks, cross-navigation, or stale/offline behavior; and
- new or expanded fixture datasets, fake service behavior, automated native UI acceptance tests, or agent-controlled
  live mutations;
- version changes, commits, releases, DMGs, or deployment unless separately authorized.

Torrent actions remain single-selection because TorrentCore's current service and accepted native workflow are
single-item. TVMaze's Active Magnets multi-selection was a product-specific decision and is not copied here.

## Current Assessment

The existing macOS client already provides:

- native SwiftUI `Table` controls;
- persisted `TableColumnCustomization` for Torrents, History, Logs, Peers, and Trackers;
- local paging and sortable columns;
- a non-reflowing trailing inspector for Torrents, History, and Logs;
- authoritative service capability checks for torrent actions;
- correct last-known/stale handling and profile-switch isolation;
- one global foreground-only auto-refresh policy; and
- non-reflowing inspectors that already leave their table geometry intact.

The alignment gaps are presentation consistency rather than missing backend functionality:

- pagination code and page-size lists are duplicated across five surfaces;
- only one sort field and direction are persisted for the three main tables;
- Peers and Trackers do not persist sort or page-size choices;
- there is no explicit multi-field Sort editor;
- column visibility/reset behavior is not exposed consistently through a Columns menu;
- there is no standard selected-row/all-row export;
- the three main trailing inspectors use fixed widths instead of a persisted resizable width;
- selection can remain attached to a row that is no longer on the displayed page; and
- action feedback and table toolbar placement vary by screen.

TorrentCore's existing Connection view already saves named device-local profiles, changes the active profile, clears
remote state when the active profile changes, and shows profile/address/connectivity in a persistent bottom status
bar. What it does not store or show is the operator's Production/Test classification. Profile name alone is not an
adequate safety indicator, especially when multiple profiles can point to the same physical installation through
different addresses.

Do not rewrite or replace this connection workflow. The only connection-profile behavior added by this handoff is the
operator-selected environment value needed to color and label the warning bar. Existing connection testing,
save/connect, use-connection, deletion, uniqueness validation, persistence location, service-client creation,
activation, refresh, cancellation, and remote-state clearing remain as implemented.

## Accepted Connection Environment Rules

Follow the established TVMaze macOS behavior rather than designing a TorrentCore-specific variation.

1. Add a device-local connection environment with exactly three stored values: Unclassified, Production, and Test.
2. Store the environment on each `TorrentCoreConnectionProfile`. Classification belongs to the saved connection, not
   to an address or a service response. Multiple profiles may therefore be classified the same way even when they use
   different names or addresses for one physical installation.
3. Decode profiles created by earlier client versions as Unclassified. Preserve their ID, name, address, timestamps,
   active selection, and all other preferences. Do not infer an environment from the profile name, hostname, IP
   address, port, or returned service data.
4. Add an Environment picker to the Connection form and show each profile's classification in the connection list.
   New or edited profiles cannot be saved until Production or Test is selected. Existing Unclassified profiles remain
   visible and usable so the migration never strands a saved installation; their gray warning bar remains until the
   operator explicitly classifies and saves them.
5. Add a 34-point, full-width environment bar directly below the macOS title/toolbar area and above all app content:
   Production is blue with `checkmark.shield.fill`; Test is red with `exclamationmark.triangle.fill`; Unclassified or
   no profile is gray with `questionmark.circle.fill`. The text is the uppercase environment, a bullet, and the active
   profile name (or `No Connection`).
6. Port the proven TVMaze AppKit titlebar-accessory implementation; a visually similar SwiftUI bar is not sufficient.
   Use an `NSViewRepresentable` with a coordinator that owns one `NSTitlebarAccessoryViewController`, sets its
   `layoutAttribute` to `.bottom`, and attaches it with `NSWindow.addTitlebarAccessoryViewController`. The accessory
   contains a fixed-height `NSView` whose edge-constrained `NSHostingView` hosts the SwiftUI bar, reports the correct
   intrinsic height, updates its root view when the active profile changes, follows the window width, and detaches its
   controller during dismantling or window changes. Do not place the bar in the content stack or as a visual overlay.
   It must reserve its own titlebar-accessory height and never cover, tint, shift unpredictably, or make the first row
   of app content inaccessible on macOS 26 or macOS 27.
7. Keep the existing bottom connection status bar. The top bar answers which environment/profile is selected; the
   bottom bar continues to show profile name, service address, and Connected/Offline/Connecting state. Offline status
   does not remove or recolor the environment classification.
8. Update the environment bar immediately when the active profile changes or an active profile is reclassified. Do
   not alter the existing generation/cancellation or `resetRemoteState()` implementation; the bar simply observes the
   same active profile that the existing connection workflow already uses.
9. Give the bar one combined accessibility label in the form `Test environment, Profile Name` and the stable
   identifier `main.environmentBar`. Give the picker the stable identifier `connection.environment`.
10. This classification is a local operator safety aid only. Do not add a Service setting, HTTP/OpenAPI field,
    database value, environment-dependent fallback, or different Production/Test behavior.

Keep shared feature-session calls source-compatible outside the macOS form: creating a profile without an explicit
environment produces Unclassified, while updating a profile without an explicit environment preserves its existing
classification. This is parameter threading around the existing methods, not new connection orchestration.

## Accepted Table Rules For TorrentCore

Apply these rules to every in-scope table unless a surface-specific section below says otherwise.

1. Continue using SwiftUI `Table`; do not introduce `NSTableView` or another dependency.
2. Use a shared page-size list of 25, 50, 100, and 250 rows. Default new/reset state to 25 rows.
3. Persist each table's page size, ordered multi-field sort, and `TableColumnCustomization` using table-specific
   `AppStorage` keys.
4. Preserve native click-to-sort headers. Also provide an explicit Sort popover that can add, remove, reorder, and set
   the direction of multiple sort fields and restore the documented default sort.
5. Show sort direction and priority in sorted column headers, matching the accepted TVMaze behavior.
6. Provide a Columns menu with per-column visibility, Show All Columns, Restore Default Columns, and Reset Table
   Layout. A required identity/name column cannot be hidden.
7. Keep default-visible columns narrow enough to fit the supported 1,000-point minimum window. Put diagnostic or
   secondary fields in available-but-hidden columns rather than forcing horizontal scrolling in the default layout.
8. Use one shared pagination bar that reports the displayed range, row count, page size, Previous, Next, and page
   number. Clamp the page after filtering, sorting, refresh, or page-size changes.
9. Clear a selection and close its inspector when the selected row is no longer on the displayed page or no longer
   exists in the authoritative result.
10. Export either the selected row or every filtered/sorted result, not merely the current page. Write a timestamped
    file to Downloads using the established quoted `##`-delimited format. Diagnostic identifiers and underlying DTO
    fields belong in exports even when they are not default-visible columns.
11. Keep row context menus only as shortcuts to actions that remain discoverable in the toolbar or inspector.
12. Use a standard resizable trailing overlay for Torrents, History, and Logs. It must float over the table without
    changing the table frame, use a persisted per-surface width, clamp the width to 340 through 720 points, and include
    an obvious close control.
13. Preserve current loading, empty, unavailable, stale, and processing-paused states. A failed load must never be
    shown as an empty result.
14. Preserve current filter semantics and reset the page to the first page when filters change.
15. Keep the existing app-wide Add Magnet, Refresh, and Auto Refresh controls. Add table-specific Sort, Columns, Export,
    selection actions, and Inspector controls contextually; do not duplicate global refresh ownership inside tables.
16. Continue honoring `CanPause`, `CanResume`, and every other service capability. No presentation helper may infer
    service policy.

## Shared macOS Support

Add `clients/apple/Apps/TorrentCoreMac/TorrentCoreMacTableSupport.swift` and register it with the macOS target.

Keep this support concrete and small. It should contain the TorrentCore-named equivalents of:

- paging, maximum-page, range-label, and content-width helpers;
- a codable ordered sort descriptor and sort storage;
- the reusable multi-sort editor;
- the shared pagination bar;
- Downloads export with filename sanitation, newline handling, and quoted `##` fields; and
- a small success/warning/error notice model suitable for table actions and exports.

Do not create a generic table view that owns screen-specific rows, filters, columns, or actions. Each screen remains
responsible for its own table item, column metadata, default visibility, sort comparators, export fields, and operator
actions.

Update `TorrentCoreMacComponents.swift` so `torrentCoreTrailingOverlay` accepts a width binding and supplies the same
drag-to-resize behavior and width limits. Retain the existing non-reflowing overlay behavior.

Use the existing SwiftUI `TableColumnCustomization` persistence first. Verify column order, visibility, and width
restoration on macOS 26 and macOS 27. If width restoration still fails, stop and obtain operator approval before adding
an AppKit introspection bridge or replacing the table implementation.

## Surface Requirements

| Surface | Default sort | Default-visible columns | Available secondary columns | Selection and actions |
| --- | --- | --- | --- | --- |
| Torrents | State ascending, then Progress descending | Name, State, Progress, Download, Peers, Category, Queue #, Priority #, Held # | Reason, Torrent ID and other already-received summary diagnostics useful for export | Single selection. Preserve Pause, Resume, queue actions, metadata actions, callback retry, Peers, Trackers, History, Logs, and confirmed removal in the inspector/contextual controls. |
| History | Last Updated descending | Last Updated, Name, Category, State, Outcome, Progress, Callback | Downloaded, Total, Removed, Removal Reason, identifiers, magnet, and callback diagnostic fields | Single selection. Preserve Show in Torrents, copy actions, and the existing detail request. |
| Logs | When descending | When, Level, Category, Event, Message | Log ID, Torrent ID, Service Instance ID, Trace ID, and Details JSON | Single selection. Preserve Show Torrent, Show History, and confirmed orphan-log deletion. |
| Peers | Endpoint ascending | Endpoint, Client, Direction, Connected, Seeder, Down, Up | Downloaded, Uploaded, Encryption and all other already-received peer fields | No new mutation and no row inspector. Keep Refresh and Done; add Sort, Columns, and Export. |
| Trackers | Tier ascending, then Tracker ascending | Tier, Tracker, Active, Status, Announce, Scrape | Since Announce, Announce OK, Since Scrape, Scrape OK, Failure, Warning and all other already-received tracker fields | No new mutation and no row inspector. Keep Refresh and Done; add Sort, Columns, and Export. |

The export definition is the full underlying DTO/reporting record for the surface, not only the columns currently
visible. Preserve TorrentCore's existing local-time formatting in the UI and include unambiguous ISO timestamps in
exports where timestamps exist.

## Implementation Slices

### Slice 1: Additive Connection Classification And Environment Bar — Complete

- Add the environment enum, one profile property, and backward-compatible decoding.
- Thread that one value through the existing feature-session add/update profile operations without changing their
  control flow, the profile store, active-profile selection, the service client, or the connection-test request.
  Preserve the existing environment when an update caller omits it.
- Add the Connection form picker and profile-list classification.
- Port the TVMaze `NSViewRepresentable`/coordinator/container/`NSHostingView` titlebar accessory into the TorrentCore
  macOS target and install it from the main window background while retaining the bottom status bar. Do not replace
  this with `safeAreaInset`, an overlay, or an extra content-stack row.
- Stop there. Do not reorganize the Connection view, replace its persistence, or refactor the feature session as part
  of this slice.

### Slice 2: Shared Support And Torrents — Complete

- Add the shared table-support file and macOS target membership.
- Upgrade the trailing overlay to persisted resizing without changing its overlay geometry.
- Convert Torrents from single-field sort persistence to ordered multi-sort persistence.
- Add the Torrents Sort, Columns, and Export controls.
- Standardize pagination and page clamping.
- Keep every current torrent action, confirmation, capability check, filter, context menu, and cross-navigation path.
- Preserve the app-wide toolbar's Add Magnet, Refresh, Auto Refresh, Pause, Resume, and Inspector behavior unless a
  small presentation adapter is required to expose Sort/Columns/Export. Do not redesign the shell.

This slice is the table-framework proof. Do not migrate the remaining tables until the operator has reviewed the
Torrents default layout, sorting, columns, export, pagination, and resizable overlay against CA-Server.

### Slice 3: History And Logs — Complete

- Replace their duplicated paging and single-sort persistence with the shared support.
- Add Sort, Columns, and selected/all Export controls.
- Make each trailing overlay resizable and persist its width independently.
- Preserve all current server filters, local search, Today default, result-limit notice, abandonment summary,
  cross-navigation, copy actions, and destructive cleanup confirmation.
- The operator-authorized exact Torrent ID History filter is the only query-contract exception; Logs contracts remain
  unchanged.

### Slice 4: Peers And Trackers — Complete

- Use the shared pagination, sort storage/editor, columns menu, and export.
- Persist page size, sort, and column customization per diagnostic table.
- Keep the current sheet presentation and foreground-only refresh behavior.
- Size the default-visible columns to avoid horizontal scrolling when the available display width permits it.
- Do not add row selection, an inspector, or service actions where none exist today.

### Slice 5: Focused Acceptance And Documentation — Complete

- Add only small unit tests for new isolated model and helper behavior. Do not add or expand fixture-driven UI tests.
- Present the debug Native UI to the operator for deliberate testing against CA-Server and its real Test data.
- Record the actual operator acceptance environment before changing native-client support status. This workstream did
  not change the existing support status.
- Update this handoff and `native-apple-client-development-plan.md` with the actual result after operator acceptance.
- Treat packaging and release as a separate operator-authorized step.

## Files Expected To Change

- `clients/apple/Packages/TorrentCoreKit/Sources/TorrentCoreFeatures/TorrentCoreConnectionProfile.swift`
- `clients/apple/Packages/TorrentCoreKit/Sources/TorrentCoreFeatures/TorrentCoreFeatureSession.swift`
- one focused pure-model compatibility test under
  `clients/apple/Packages/TorrentCoreKit/Tests/TorrentCoreKitTests/`; do not add a fake service workflow for it
- `clients/apple/Apps/TorrentCoreMac/TorrentCoreMacTableSupport.swift` (new)
- `clients/apple/Apps/TorrentCoreMac/TorrentCoreMacComponents.swift`
- `clients/apple/Apps/TorrentCoreMac/TorrentCoreMacContentView.swift` for the environment bar and, only if needed, to
  expose contextual table toolbar items without redesigning the shell
- `clients/apple/Apps/TorrentCoreMac/TorrentCoreMacConnectionView.swift`
- `clients/apple/Apps/TorrentCoreMac/TorrentCoreMacTorrentsView.swift`
- `clients/apple/Apps/TorrentCoreMac/TorrentCoreMacHistoryView.swift`
- `clients/apple/Apps/TorrentCoreMac/TorrentCoreMacLogsView.swift`
- `clients/apple/Apps/TorrentCoreMac/TorrentCoreMacTorrentDiagnosticsView.swift`
- `clients/apple/TorrentCoreApple.xcodeproj/project.pbxproj` if explicit source registration is required
- focused pure-logic unit tests under `clients/apple/Tests/TorrentCoreMacTests/`
- `docs/native-apple-client-development-plan.md` after acceptance

The operator-authorized History Torrent ID query added the only .NET, Service, HTTP/OpenAPI, and generated-client
changes. WebUI, SQLite, and deployment files remain unchanged. Other shared Swift changes are limited to the additive
device-local connection classification, minimal feature-session parameter threading, and focused unit tests.

## Verification And Operator Acceptance

Follow the established TVMaze split: development-time automated coverage is limited to simple unit tests, while real
behavior and native presentation are validated by the operator through the dedicated Test environment. Do not build a
parallel fixture/fake acceptance environment for this work.

### Development Checks

Only add or run focused unit tests for isolated logic introduced by this handoff:

- an older saved profile without an environment decodes intact as Unclassified;
- the environment value survives a direct profile encode/decode round trip;
- table paging helper boundaries and range labels;
- sort storage round-trip, ordered multi-sort, and restored defaults;
- export escaping, field count, selected/all scope, and safe filename behavior;
- page clamping; and
- deterministic pure selection-reconciliation helpers, if implementation needs one.

Do not add automated macOS UI tests, enlarge TorrentCore fixtures, simulate operator workflows with fake service data,
or perform a live mutation from an automated command. A targeted unsigned macOS build may be used as a compile check;
it is not acceptance testing.

### Operator-Controlled Test Environment

The Native UI runs on the operator's Mac and connects to the real TorrentCore API and data on CA-Server. The operator
chooses the connection and controls all mutations. Never use the Production connection for test actions.

The operator validates:

- existing saved connections remain intact and can be explicitly classified as Production or Test;
- Save & Connect, Test Connection, Use Connection, delete, connection status, and profile switching still behave as
  they did before this additive change;
- the CA-Server Test profile shows the red Test bar, a Production profile shows the blue Production bar, and an
  unclassified legacy profile shows the gray warning bar;
- switching profiles changes the bar immediately and the existing profile-isolation behavior prevents old rows from
  appearing under the newly selected environment;
- the bar remains visible on every view and never obscures content at the supported minimum size or after resizing on
  macOS 26 and macOS 27;
- Torrents, History, Logs, Peers, and Trackers show real CA-Server data with the agreed columns, sorting, pagination,
  column persistence, exports, and overlays;
- existing Add Magnet, Refresh, Auto Refresh, selection, Pause, Resume, confirmations, context actions, and
  cross-navigation remain available; the operator chooses safe real Test data for any mutation; and
- saved table layout, sort, page size, and inspector width restore after the operator closes and reopens the app.

The operator decides when Test-environment evidence is sufficient and when packaging or production deployment may
begin. Implementation completion does not authorize either action.

## Completion Definition

The work is complete when every saved connection has an explicit operator-owned environment option, the persistent
bar correctly and safely identifies the active profile throughout the macOS app, all five table surfaces follow the
shared rules, their existing operator behavior is unchanged, the three main inspectors remain non-reflowing and are
resizable/persistent, focused unit tests pass, and the operator accepts the real CA-Server Test-environment results on
the supported macOS versions.

Any discovered need for a public API or Service change, new dependency, `NSTableView` bridge, bulk action, altered
refresh model, inferred environment, or WebUI change is outside this handoff. Stop and obtain operator approval before
expanding scope.
