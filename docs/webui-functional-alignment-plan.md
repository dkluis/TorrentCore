# WebUI Functional Alignment Plan

## Status

This is the active sliced implementation plan for bringing `TorrentCore.WebUI` back into functional alignment with
the current Service and the service-facing portions of the native macOS operator UI.

No slice is implemented merely because it appears in this document. This plan does not authorize deployment or
mutation of a live TorrentCore installation.

## Outcome

Update the supported WebUI so an operator can:

- inspect current VPN validation and torrent-processing state on the Dashboard;
- configure every current VPN egress setting;
- configure the metadata-resolution time slice, automatic-reset stuck threshold, and performance timing summaries;
- run the protected log, history, and orphan-log cleanup operations from Settings; and
- see the Torrents and History pages blocked while the Service explicitly reports that torrent processing is
  unavailable; and
- ship the Service and WebUI together as signed Arm64 app bundles in the existing combined DMG while retaining the
  native macOS UI as the existing drag-to-Applications app.

The WebUI remains a thin client over the existing Service and `TorrentCore.Client` contracts. Engine, persistence,
cleanup, VPN, and recovery policy remain Service-owned.

## Related Sources Of Truth

- [Architecture](architecture.md)
- [Development](development.md)
- [Deployment](deployment.md)
- [Operator settings](operator-settings.md)
- [Testing](testing.md)
- [Troubleshooting](troubleshooting.md)
- [VPN egress plan](torrentcore-service-app-vpn-egress-plan.md)

Current code and active documentation remain authoritative if this plan becomes stale.

## Confirmed Decisions

### Scope

- This effort covers only the service-facing gaps listed in this plan.
- Do not add smaller macOS parity refinements such as copy buttons, cross-page navigation, broader auto-refresh,
  Add Magnet validation changes, or category-load warnings.
- Do not change the behavior of an existing WebUI input unless that component is directly changed by a planned slice.
- Add Magnet behavior is unchanged. A VPN-degraded page overlay may make the WebUI Add Magnet action unavailable even
  though the Service API continues to accept and persist magnets while degraded.
- Do not add a WebUI test project, WebUI component tests, browser tests, or new WebUI test cases.
- Existing repository build and test commands remain required verification; this exclusion applies to adding WebUI
  tests, not to running the current suite or release-time bundle, signing, static-asset, LaunchAgent, and health
  verification.
- CA-Desktop is the only current VPN-enhanced production Service. CA-Server is neither running nor current enough for
  this work and must not be used as a verification target.
- Do not perform intermediate live WebUI settings mutations. Run one explicitly approved production acceptance against
  CA-Desktop at the end of the complete WebUI alignment process, with the exact temporary changes and restoration
  checklist agreed immediately before that test.
- The final production acceptance does not implicitly authorize destructive cleanup. Any live log, history, or orphan
  cleanup operation and its exact cutoff must receive separate explicit approval in the final checklist.

### Deployment

- The managed Service and WebUI are always packaged and deployed together by the DMG installer. Do not add a
  Service-only or WebUI-only selection to this path.
- Keep the existing native `TorrentCore.app` at the DMG root with the `/Applications` link. It remains outside the
  installer and is replaced manually through the normal macOS drag-to-Applications workflow.
- The initial supported deployment remains Arm64 only. Intel packaging remains deferred.
- Keep the existing WebUI LaunchAgent label `com.torrentcore.webui`.
- Package the WebUI as `TorrentCoreWebUI.app` with bundle identifier `com.conadv.torrentcore.webui`, launcher
  `Contents/MacOS/TorrentCoreWebUI`, and installed path
  `~/Applications/TorrentCore/TorrentCoreWebUI.app`.
- Follow the established TVMazeWeb and TorrentCore Service separation: immutable runtime, static web content,
  packaged defaults, and deployment resources live inside the signed app; `~/TorrentCore/WebUI` remains the mutable
  working and configuration directory.
- Preserve the existing WebUI working directory during upgrade. In particular,
  `~/TorrentCore/WebUI/Config/service-connection.json` must survive byte-for-byte.
- Never stage or publish a developer machine's `Config/service-connection.json`. A fresh installation leaves that
  machine-local override absent and uses the WebUI's existing packaged configuration fallback until an operator saves
  a connection.
- Reuse the existing TorrentCore Developer ID team, signing identity discovery, notarization profile, hardened-runtime
  policy, DMG layout, and target-user installation model.
- Extend the current backup/history/manual-recovery mechanism to cover both managed bundles and LaunchAgents as one
  deployment unit. Do not introduce a separate rollback workflow.

### Contract Version Handling

- Match the native macOS client policy rather than enforcing a Service semantic-version minimum.
- The supported API contract version remains `ServiceApiContract.CurrentVersion`, currently `1`.
- Accept a missing `apiVersion` during the existing private-installation transition.
- Accept a reported API version less than or equal to the supported version.
- Reject a future API version greater than the supported version with an operator-facing compatibility error.
- Continue to report `serviceVersion` and `serviceBuild` as display information only; do not use either as a
  compatibility gate.
- Do not introduce API contract version `2` for this UI alignment work.

### Settings And Limits

- Runtime settings displayed by WebUI come from the Service's effective database-backed settings. WebUI does not
  substitute its own runtime-policy defaults when a current value has been loaded.
- Existing saved settings must survive saving any individual WebUI settings group.
- `MetadataResolutionTimeSliceMinutes` accepts `1` through `1440`.
- `AutomaticMetadataResetStuckThresholdSeconds` accepts `15` through `300`.
- VPN degraded interval, ready interval, request timeout, and engine suspension timeout must each be at least one
  second.
- VPN request timeout must be less than both check intervals.
- The VPN endpoint must be an absolute HTTPS URL with a host and without credentials or a fragment.
- Direct-ISP entries must be IPv4 CIDRs. The Service remains authoritative for canonicalization, deduplication, and
  the requirement for at least one CIDR while validation is enabled.
- Performance Timing Summaries changes only `runtime.tick.duration_summary` logging and does not change runtime
  scheduling.

### Cleanup

- Settings receives one Cleanup group containing Log Entries, History Records, and Orphaned Torrent Logs.
- Keep the existing Delete Orphaned Logs action on the Logs page unchanged; the Settings action is an additional
  operator entry point over the same Service operation.
- Use the same initial date-picker conveniences as macOS: seven days before today for logs and 30 days before today
  for history. These are not persisted runtime settings and do not alter Service retention policy.
- A cleanup date is required and cannot be in the future.
- The selected date is interpreted by the Service as Service-local midnight and used as an exclusive cutoff.
- Rows associated with torrent ids still present in the live torrent table remain protected.
- History eligibility uses the row's last-updated timestamp.
- Every destructive operation requires its own explicit confirmation and reports the returned deletion count.
- Service validation and deletion behavior remain authoritative.

### VPN-Degraded Pages

- Match the native macOS page-level behavior for an explicit
  `torrentProcessingAvailable == false` host status.
- Cover the Torrents and History page content with a processing-paused or processing-restarting overlay.
- Refresh is the only page action available through the overlay.
- Do not infer VPN degradation from `/api/health`, a host-status request failure, a missing VPN field, or a general page
  error.
- Preserve the last explicit degraded status across a later host-status refresh failure, matching the native client's
  last-known-state behavior.
- An explicit available status removes the overlay and restores the existing page behavior.
- The Torrents page keeps its existing refresh loop; its normal refresh also refreshes host status. Do not add a new
  History auto-refresh loop.

## Out Of Scope

- Service VPN coordinator, probe, execution-gate, and MonoTorrent lifecycle changes
- Service persistence or schema changes
- Public API shape changes or API version `2`
- New WebUI automated tests or test infrastructure
- Add Magnet input or validation changes
- Copy-to-clipboard actions
- New cross-page navigation actions
- General Dashboard, History, Logs, Peers, or Trackers refresh redesign
- General WebUI visual redesign
- Native Apple client changes
- Intel deployment support
- Automatic installation or replacement of the native macOS UI
- Executing a live deployment without separate operator authorization

## Current Gaps

- Dashboard reads host status but does not render its VPN fields.
- Torrents and History do not read host status and therefore cannot block on explicit processing unavailability.
- Settings omits all seven VPN policy values.
- Settings omits `RuntimeTickDurationSummaryEnabled`.
- Settings omits `MetadataResolutionTimeSliceMinutes` and
  `AutomaticMetadataResetStuckThresholdSeconds`.
- Settings has no date-based log or history cleanup controls.
- The WebUI adapter exposes orphan-log deletion but not the two maintenance cleanup operations already available in
  `TorrentCore.Client`.
- The WebUI connection probe treats any successful `/api/health` response as reachable and does not yet apply the
  native client's service-name and future-API-version checks.
- The WebUI is still deployed as a flat published runtime rather than as its own signed app bundle.
- The combined Service-app DMG installs only the Service; it does not yet package, install, or verify WebUI.
- WebUI static-asset lookup is not yet made independent of its external working directory.
- `dotnet publish` can include the Git-ignored machine-local `Config/service-connection.json`, allowing a release
  machine's saved endpoint to overwrite the target machine's endpoint.

## Sliced Delivery Plan

### Slice 0: Compatibility And API Adapter Foundation

Status: completed on August 13, 2026.

#### Work

- Update the WebUI connection probe path to decode `/api/health` sufficiently to verify the expected
  `TorrentCore.Service` identity and apply the confirmed API-version policy.
- Return a clear connection error for an unexpected service or future unsupported API version.
- Keep missing, current, and older supported API-version behavior aligned with the native client.
- Add `CleanupLogsAsync` and `CleanupHistoryAsync` to `ITorrentCoreApiAdapter` and
  `TorrentCoreApiAdapter`, delegating to the existing `TorrentCore.Client` methods.
- Do not change endpoint persistence, URL normalization, timeout, or connection-form input behavior.

#### Acceptance

- A matching Service with missing API version remains connectable.
- API version `1` and lower remain connectable.
- A future API version is rejected before the WebUI treats the endpoint as usable.
- A successful health response from a non-TorrentCore service is rejected.
- Both date-based cleanup operations are callable through the WebUI adapter without adding a new Service endpoint.
- Existing connection settings and saved endpoints remain unchanged.

#### Verification

- Build the WebUI project.
- Run the existing client and Service contract tests that cover the touched contracts.
- Do not add WebUI tests.

#### Implemented Result

- The shared connection probe now decodes successful health responses before declaring an endpoint reachable.
- Service identity must exactly match `TorrentCore.Service`.
- Missing, null, current, and older numeric API versions are accepted; a version greater than
  `ServiceApiContract.CurrentVersion` is rejected with the native-client compatibility message.
- Invalid health JSON and nonnumeric API versions return operator-facing probe failures.
- The WebUI adapter exposes the existing client log and history date-cleanup operations through its established
  `ServiceCallResult` error boundary.
- Endpoint parsing, normalization, persistence, timeout, candidate fallback, and connection-form behavior were not
  changed.
- `dotnet build src/TorrentCore.WebUI/TorrentCore.WebUI.csproj --no-restore --maxcpucount:1
  --disable-build-servers`: succeeded with zero warnings and zero errors.
- Existing OpenAPI and maintenance-cleanup contract tests: all five selected tests passed.
- `dotnet test TorrentCore.sln --no-build --no-restore --maxcpucount:1 --disable-build-servers`: all 286 existing
  tests passed.
- No WebUI tests or test infrastructure were added.

### Slice 1: Runtime Settings Parity

Status: completed on August 13, 2026. Live mutation acceptance is intentionally deferred to the final one-time
CA-Desktop production verification.

#### Work

- Add `MetadataResolutionTimeSliceMinutes` and
  `AutomaticMetadataResetStuckThresholdSeconds` to Queue & Recovery.
- Add a VPN Egress settings group containing:
  - validation enabled;
  - validation endpoint;
  - direct-ISP IPv4 CIDRs;
  - degraded check interval seconds;
  - ready check interval seconds;
  - request timeout seconds; and
  - engine suspension timeout seconds.
- Add a Diagnostics settings group containing Performance Timing Summaries.
- Extend settings snapshots, dirty detection, group blocking, discard, save request construction, returned-value
  reconciliation, status text, and help content for the new fields.
- Preserve the current one-dirty-group-at-a-time workflow.
- Ensure every settings update carries or preserves the current effective values for settings outside the edited
  group.
- Apply the confirmed numeric and relationship limits in the UI while retaining Service error handling as the final
  authority.
- Preserve the current restart-required behavior: these new controls apply live and must not be presented as
  restart-required engine settings.

#### Acceptance

- All seven VPN values load from the Service and survive edit, save, refresh, discard, and saving another group.
- CIDR input supports all current Service values without dropping multiple entries.
- Invalid endpoints, CIDRs, nonpositive durations, and an invalid request-timeout relationship cannot be silently
  saved.
- Metadata time slice is constrained to `1...1440` minutes.
- Automatic-reset stuck threshold is constrained to `15...300` seconds.
- Performance Timing Summaries loads and saves as a live boolean setting.
- Existing settings controls retain their present input and save behavior.

#### Verification

- Build the WebUI project.
- Run the existing Service runtime-settings and OpenAPI contract tests.
- Defer live load, dirty, discard, save, and returned-value reconciliation to the final explicitly authorized
  CA-Desktop production acceptance. Do not use CA-Server or perform an intermediate live mutation.
- Do not add WebUI tests.

#### Implemented Result

- Queue & Recovery now loads and edits the metadata-resolution time slice and automatic-reset stuck threshold with the
  confirmed `1...1440` minute and `15...300` second limits.
- VPN Egress is an independent live settings group containing all seven current policy values. Direct-ISP CIDRs use
  the macOS comma-separated input format and preserve multiple Service-returned entries.
- Diagnostics is an independent live settings group containing Performance Timing Summaries.
- Both new groups participate in the existing one-dirty-group workflow, including blocking, pending-change prompts,
  discard, save state, status text, and returned-value reconciliation.
- Every runtime update request carries the loaded/edited values for the two queue controls, all VPN values, and the
  diagnostics flag, including when an unrelated runtime group is saved.
- WebUI validation blocks invalid metadata bounds, VPN endpoints, IPv4 CIDRs, nonpositive durations, missing enabled
  CIDRs, and request timeouts that are not shorter than both check intervals. Service validation remains final.
- VPN and diagnostics saves use the existing live-setting success path and are not presented as restart-required
  engine changes.
- Help content matches the current macOS operational descriptions and limits.
- `dotnet build src/TorrentCore.WebUI/TorrentCore.WebUI.csproj --no-restore --maxcpucount:1
  --disable-build-servers`: succeeded with zero warnings and zero errors.
- Existing runtime-settings, options-validation, and OpenAPI contract tests: all 33 selected tests passed.
- `dotnet test TorrentCore.sln --no-build --no-restore --maxcpucount:1 --disable-build-servers`: all 286 existing
  tests passed on the final run. An unrelated cold-download abandonment timing test failed on the first full run, then
  passed both in isolation and in the complete rerun.
- No WebUI tests or test infrastructure were added.
- Live save/discard verification was not run. By operator decision, it belongs to the one-time final CA-Desktop
  production acceptance after the remaining WebUI alignment slices are complete.

### Slice 2: Settings Cleanup Operations

Status: completed on August 13, 2026. No live cleanup was performed.

#### Work

- Add a Cleanup group to Settings after the current configuration groups.
- Add Log Entries cleanup with a date selector, scope explanation, confirmation, busy state, error handling, and
  returned deletion count.
- Add History Records cleanup with the corresponding last-updated eligibility explanation and behavior.
- Add Delete Orphan Logs using the existing adapter operation.
- Initialize log and history selectors to the confirmed seven-day and 30-day convenience dates.
- Reject missing or future dates before submission while preserving Service-side validation.
- Prevent concurrent cleanup or settings mutations through the page's existing busy-state conventions.
- Keep the current Logs-page orphan cleanup action unchanged.

#### Acceptance

- Each cleanup action has a separate destructive confirmation describing its exact scope.
- No cleanup request is sent for a missing or future date.
- Log and history cleanup requests send the selected calendar date without converting it to a browser-local instant.
- Results show the Service-returned deleted-record count; zero is reported as a successful no-op.
- Failure feedback retains the Service's operator-safe problem detail.
- Live-torrent protection and cutoff conversion remain wholly Service-owned.

#### Verification

- Build the WebUI project.
- Run the existing maintenance cleanup and OpenAPI contract tests.
- Use non-mutating visual inspection by default. Exercise cleanup against a live Service only with explicit operator
  approval and a designated disposable target.
- Do not add WebUI tests.

#### Implemented Result

- Settings now contains one wide Cleanup group after the configuration groups with Log Entries, History Records, and
  Orphaned Torrent Logs operations.
- Log and history selectors initialize to seven and 30 days before today, remain clearable, reject missing or future
  dates, and construct `DateOnly` requests without instant or time-zone conversion.
- Each action has its own destructive confirmation using the macOS scope wording. Log and history confirmations state
  the selected date, Service-local midnight, exclusive eligibility, and live-torrent protection.
- Cleanup uses the page's busy state to prevent concurrent cleanup, refresh, or settings/category saves. An unsaved
  settings group blocks access to Cleanup through the existing pending-group workflow.
- Success feedback reports the exact Service deletion count, including zero as a successful no-op. Failure feedback
  preserves the adapter's operator-safe Service problem detail.
- The orphan action delegates to the existing guarded Service operation. The existing Logs-page action was not changed.
- Help content matches the current macOS cleanup descriptions; cutoff conversion and record protection remain
  Service-owned.
- `dotnet build src/TorrentCore.WebUI/TorrentCore.WebUI.csproj --no-restore --maxcpucount:1
  --disable-build-servers`: succeeded with zero warnings and zero errors.
- Existing maintenance-cleanup and OpenAPI contract tests: all five selected tests passed.
- `dotnet test TorrentCore.sln --no-build --no-restore --maxcpucount:1 --disable-build-servers`: all 286 existing
  tests passed.
- No WebUI tests or test infrastructure were added, and no cleanup request was sent to CA-Desktop.

### Slice 3: Dashboard VPN Status

Status: completed on August 13, 2026. The operator completed the end-of-coding UI check against the production
Service before deployment work began.

#### Work

- Add a VPN Connection panel to the existing Dashboard without replacing its current lifecycle and work summaries.
- Present:
  - validation enabled or disabled;
  - connection phase;
  - torrent-processing active or paused;
  - connection reason;
  - current observed public IPv4;
  - last check;
  - preserved last successful check;
  - next automatic retry;
  - ready check interval;
  - degraded check interval; and
  - the Service-provided operator message.
- Use operator-friendly phase and reason labels consistent with the native macOS UI.
- Treat missing additive VPN fields as unavailable information rather than as a degraded state.
- Keep technical failure detail out of the primary operator panel; the Service host status and DB logs remain its
  diagnostic sources.
- Keep the Dashboard's current manual refresh behavior unchanged.

#### Acceptance

- Ready, checking, degraded, activating, validation-disabled, and unavailable-field states render without hiding the
  existing Dashboard.
- The operator message comes from host status when supplied.
- A direct-ISP result shows its reason and observed address without describing the result as a validated VPN provider.
- Last successful check remains visible after a later failed check.
- No Dashboard action mutates Service VPN state.

#### Verification

- Build the WebUI project.
- Run the existing host-status, VPN coordinator, and OpenAPI contract tests.
- Manually inspect representative current host-status payloads without adding WebUI tests.

#### Implemented Result

- Dashboard retains its existing lifecycle, pipeline, callback, and attention summaries and adds one wide VPN
  Connection panel sourced only from the existing host-status response.
- The panel presents validation, connection phase, torrent-processing availability, connection reason, observed
  public IPv4, last check, preserved last successful check, next automatic retry, both check intervals, and the
  Service-provided operator message.
- Phase, reason, validation, processing, interval, and unavailable-value presentation follows the native macOS
  terminology. The Service's technical failure summary is not repeated in the primary operator panel.
- Missing additive VPN fields render as unavailable and do not make the Dashboard itself degraded or hide any
  existing content. Dashboard refresh remains manual and no VPN mutation action was added.
- `dotnet build src/TorrentCore.WebUI/TorrentCore.WebUI.csproj --no-restore --maxcpucount:1
  --disable-build-servers`: succeeded with zero warnings and zero errors.
- The selected existing host-status, VPN coordinator, degraded-admission, and OpenAPI contract tests all passed (18
  tests). No WebUI tests or test infrastructure were added.
- `dotnet test TorrentCore.sln --no-build --no-restore --maxcpucount:1 --disable-build-servers`: all 286 existing
  tests passed.
- During the end-of-coding check, the operator connected the locally running current-source WebUI to the production
  Service and reported that the screens and UI implementation looked correct. This live check used the production
  state available at that time and does not claim that every synthetic VPN transition state was exercised.

### Slice 4: VPN-Degraded Torrents And History

Status: completed on August 13, 2026. The operator completed the end-of-coding UI check against the production
Service before deployment work began.

#### Work

- Add a shared WebUI processing-paused overlay following the native macOS operator wording and reason mapping.
- Refresh host status as part of Torrents and History page refreshes.
- On Torrents, include host-status refresh in the existing periodic loop without changing its interval controls.
- Show the restarting presentation when the Service reports activation in progress.
- When the last explicit host status reports `torrentProcessingAvailable == false`, cover page content and expose only
  Refresh.
- Keep the overlay in place if a later host-status request fails and the last explicit state was unavailable.
- Remove the overlay only after an explicit available status.
- Do not infer degradation when the field is missing or before any host status has loaded.
- Do not add History auto-refresh or change existing table, filter, selection, action, Add Magnet, or dialog input
  behavior.

#### Acceptance

- Torrent and History content cannot be operated through the overlay while processing is explicitly unavailable.
- Add Magnet is not reachable through the degraded Torrents page overlay.
- Refresh remains available and updates both page data and host status.
- Torrents automatically recovers from the overlay through its existing refresh loop after an explicit available
  status returns.
- History recovers after its existing manual Refresh returns an explicit available status.
- An absent VPN field does not disable either page.
- General Service or page errors continue through existing error handling and are not mislabeled as VPN degradation.

#### Verification

- Build the WebUI project.
- Run the existing VPN degraded-admission, host-status, and OpenAPI contract tests.
- Manually inspect ready, degraded, activating, recovered, missing-field, and host-refresh-failure states without adding
  WebUI tests.

#### Implemented Result

- Torrents and History now request host status as part of their existing page refreshes. Torrents includes that request
  in its existing periodic loop without changing the loop, enabled preference, or interval choices; History remains
  manual-only.
- One shared processing-availability boundary makes the covered content inert and presents only Refresh when the last
  successful explicit host status reports `torrentProcessingAvailable == false`.
- Activating uses the native Restarting Torrent Processing presentation. Other unavailable phases use Torrent
  Processing Paused, the Service operator message or native fallback, and the native operator-facing reason mapping.
- A failed host-status request leaves the last explicit processing state untouched while continuing through the
  page's existing error handling. Missing or null processing availability does not create or clear a last-known
  explicit state; only explicit `false` pauses and explicit `true` restores the page.
- The shared circuit state also disables the existing app-bar Add Magnet command while the operator is on Torrents or
  History and processing is explicitly unavailable. The command's behavior on every other page and in every other
  state is unchanged.
- Existing filters, tables, selection, dialogs, torrent actions, Add Magnet input behavior, History refresh behavior,
  and Torrents auto-refresh controls were not otherwise changed.
- `dotnet build src/TorrentCore.WebUI/TorrentCore.WebUI.csproj --no-restore --maxcpucount:1
  --disable-build-servers`: succeeded with zero warnings and zero errors.
- The selected existing VPN degraded-admission, host-status, VPN coordinator, and OpenAPI contract tests all passed
  (18 tests). No WebUI tests or test infrastructure were added.
- `dotnet test TorrentCore.sln --no-build --no-restore --maxcpucount:1 --disable-build-servers`: all 286 existing
  tests passed.
- During the end-of-coding check, the operator connected the locally running current-source WebUI to the production
  Service and accepted the rendered screens and UI implementation. The production state available during this check
  did not establish coverage of every synthetic ready, degraded, activating, recovered, missing-field, and
  refresh-failure transition.

### End-Of-Coding Operator Check

Completed on August 13, 2026, before bundled deployment work:

- The current-source WebUI served successfully on localhost and connected to the production TorrentCore Service after
  the operator selected the current endpoint through the existing Service Connection UI.
- The operator reviewed the rendered screens and reported that the screens and UI implementation looked correct.
- One runtime-settings update completed successfully against the production Service and subsequent settings reads
  succeeded.
- The operator ran the orphaned-torrent-logs cleanup from WebUI; the Service returned success.
- This was a focused coding-acceptance check. It does not claim execution of every synthetic VPN state or the broader
  final settings mutation/restoration checklist described for Slice 6.

### Slice 5: Bundled WebUI And Combined Managed Deployment

Status: implementation complete August 13, 2026. Corrected persistent release-package staging is pending operator
inspection before a replacement DMG is built.

#### Work

- Add an Arm64 `TorrentCoreWebUI.app` builder using the established TVMazeWeb bundle layout:
  - the native launcher at `Contents/MacOS/TorrentCoreWebUI`;
  - the framework-dependent WebUI runtime, `wwwroot`, and static-web-assets manifest under
    `Contents/Resources/Runtime`;
  - immutable configuration defaults under `Contents/Resources/Defaults`;
  - app-specific installation resources under `Contents/Resources/Deployment`; and
  - component version metadata under `Contents/Resources/version.json`.
- Make WebUI content and web-root discovery resolve from the bundled runtime rather than from
  `~/TorrentCore/WebUI`, while keeping that external directory as the process working/configuration directory.
- Exclude `Config/service-connection.json` from build and publish inputs, staged runtime content, bundle defaults,
  checksums, and the final DMG.
- Preserve the complete existing `~/TorrentCore/WebUI` directory during upgrades, including a byte-for-byte check of
  `Config/service-connection.json` when present. Leave retained legacy runtime files inactive, consistent with the
  current Service cutover approach.
- Give the WebUI bundle its confirmed stable identity, its own main Mach-O UUID, and the existing WebUI LaunchAgent
  association. Reuse the Service app's Developer ID team, signing/notarization infrastructure, .NET helper
  entitlements, and external-working-directory launcher pattern.
- Add a release-time static-content verifier equivalent to TVMazeWeb: start the staged bundle with an empty temporary
  working directory, resolve a fingerprinted static route from the bundled manifest, request it over loopback, and
  prove the response is nonempty and byte-identical to its bundled source.
- Extend `Scripts/ServiceAppDMG` release construction so one Arm64 DMG contains:
  - `payload/osx-arm64/TorrentCoreService.app`;
  - `payload/osx-arm64/TorrentCoreWebUI.app`;
  - the existing root-level native `TorrentCore.app`; and
  - the existing `/Applications` link for manual native-UI installation.
- Expand release metadata and package verification to record and validate both managed app bundles independently,
  including paths, identifiers, versions, checksums, architectures, signatures, entitlements, timestamps, UUIDs, and
  nested native code.
- Expand the existing `plan`, `dry-run`, `apply --confirm`, `verify`, history, backup, and manual-recovery commands so
  Service and WebUI are always handled together. Preflight both source bundles before stopping either LaunchAgent;
  replace each installed bundle atomically as part of the same apply; install both LaunchAgents; and verify both
  processes before recording a successful apply.
- Install WebUI at `~/Applications/TorrentCore/TorrentCoreWebUI.app`, retain
  `~/Applications/TorrentCore/TorrentCoreService.app`, and keep their mutable state under the existing
  `~/TorrentCore/WebUI` and `~/TorrentCore/Service` directories.
- Preserve the WebUI's current configured bind URL, Service endpoint fallback, environment override behavior, and
  LaunchAgent label. This packaging slice does not redesign WebUI connection input or runtime settings.
- Keep the native macOS UI outside managed installation, backup, LaunchAgent control, and managed-runtime
  verification while retaining its existing release signing and DMG verification. Document its existing manual
  replacement only after managed Service and WebUI verification succeeds.
- Keep `--cpu intel` explicitly refused and do not add an x64 WebUI bundle in this slice.

#### Acceptance

- The DMG installer has one managed deployment scope: Service and WebUI together. Neither can be selected or updated
  independently through this installer.
- Both app bundles pass complete Developer ID, hardened-runtime, timestamp, Team ID, entitlement, architecture,
  nested-code, UUID-separation, notarization, stapler, Gatekeeper, and DMG-integrity verification.
- WebUI serves its bundled static assets when launched with an empty external working directory, and the served bytes
  match the staged publish output.
- No `service-connection.json` from the source or release machine exists anywhere in the publish, app bundle, or DMG.
- An existing target `service-connection.json` is unchanged byte-for-byte after apply and after use of the existing
  manual recovery path. A fresh target works through the current fallback without receiving a release-machine URL.
- Existing external Service and WebUI configuration and data remain in place; packaged defaults are used only where
  the established app-bundle installers use defaults for missing files.
- A confirmed apply installs and starts both LaunchAgents, then verifies Service API health/version metadata and WebUI
  root-page/static-content reachability before declaring the combined release successful.
- VPN Disabled, Ready, and Degraded remain valid Service installation outcomes when API health succeeds; WebUI must be
  reachable in each outcome.
- The root-level native `TorrentCore.app` remains a separately signed, notarized manual-drag application and is not
  copied by `install.zsh`.
- The release remains Arm64-only and rejects Intel hosts or payload selection.
- No WebUI test project, component test, browser test, or WebUI test case is added.

#### Verification

- Build the Service app and WebUI app as unsigned Arm64 proof bundles.
- Run the WebUI bundle structure and static-asset serving verifier from an empty temporary working directory.
- Run publish-content checks proving `Config/service-connection.json` is absent.
- Run installer dry-run fixtures covering an existing saved connection and a fresh WebUI working directory without
  adding WebUI tests.
- Run the current solution build and existing test suite.
- From clean committed source, first save and inspect the standard persistent deployment directory with package docs,
  PDFs, five root helper scripts, release metadata, signed app payloads, and the native UI app.
- Only after operator approval of that directory, build the signed combined DMG from it and run signature, entitlement,
  architecture, notarization, stapler, Gatekeeper, checksum, LaunchAgent-definition, and payload verification outside
  the filesystem sandbox.
- Perform a live `plan`, `dry-run`, apply, and verification only during an explicitly authorized deployment window.

Completed implementation evidence:

- Both unsigned Arm64 proof bundles built and passed their structural verifiers.
- WebUI served a fingerprinted CSS asset from its signed bundle with an empty external working directory; the response
  was byte-identical to the bundled source.
- Solution build and all 286 existing tests passed; no WebUI tests were added.
- Replacement release evidence remains pending until the persistent Dick deployment directory is inspected and its
  DMG build is explicitly approved.
- No live `plan`, `dry-run`, apply, or installed-runtime verification was performed; those remain gated on explicit
  deployment authorization.

### Slice 6: Active Documentation And Final Verification

Status: pending; follows implementation of Slices 0 through 5.

#### Work

- Update [Operator settings](operator-settings.md) to remove the completed WebUI exclusions and describe the new
  controls.
- Update [Troubleshooting](troubleshooting.md) with WebUI Dashboard and degraded-page behavior.
- Update [Development](development.md) if the connection-version handling or current live-settings summary changes.
- Update [Deployment](deployment.md) with the supported combined DMG commands, bundle layout, preserved WebUI state,
  verification requirements, and unchanged manual native-UI installation path.
- Update [Architecture](architecture.md) only if an implementation decision changes a durable boundary; do not copy UI
  implementation detail into architecture documentation.
- Keep active docs short and move completed planning history to `docs/archive/` when this plan is fully delivered.
- Prepare the exact temporary setting changes, expected results, restoration values, and recovery checks for one
  operator-approved CA-Desktop production acceptance. Do not involve CA-Server.

#### Acceptance

- Active docs describe the implemented WebUI rather than the earlier macOS-only slices.
- No active document claims that the WebUI lacks a control delivered by this plan.
- Documentation uses repo-relative links and preserves the current docs structure.

#### Verification

- Run `dotnet build TorrentCore.sln`.
- Run `dotnet test TorrentCore.sln`.
- Run `git diff --check`.
- Verify repo-relative documentation links.
- Confirm no WebUI test project or WebUI test case was added.
- After explicit operator approval of the final checklist, run the one-time CA-Desktop production acceptance covering
  WebUI load, dirty-group blocking, discard, save, returned-value reconciliation, and restoration of the original
  settings. Record the observed results and final restored values.

## Delivery Order

1. Slice 0 establishes version handling and adapter access.
2. Slices 1 and 2 deliver Settings parity and may be implemented separately after Slice 0.
3. Slice 3 establishes Dashboard VPN presentation.
4. Slice 4 reuses that terminology for degraded-page behavior.
5. Slice 5 packages the aligned WebUI and makes Service plus WebUI the single managed DMG deployment unit.
6. Slice 6 reconciles active documentation and runs final verification.

Each implementation slice should remain reviewable and buildable on its own. Do not combine unrelated smaller parity
work into these slices.

## Completion Criteria

This plan is complete only when:

- all seven VPN policy values are editable in WebUI Settings;
- the two missing metadata controls and Performance Timing Summaries are editable;
- all three protected cleanup operations are available in Settings;
- the Dashboard presents the current VPN and processing state;
- Torrents and History block on explicit processing unavailability and recover on explicit availability;
- WebUI connection handling matches the native client's API-version policy;
- the Arm64 combined DMG packages, signs, installs, and verifies Service and WebUI together as app bundles;
- bundled WebUI static assets serve independently of the external working directory;
- machine-local WebUI connection state is excluded from releases and preserved across deployment;
- the native macOS UI retains its existing manual drag-to-Applications installation path;
- existing WebUI inputs outside this scope retain their behavior;
- no WebUI tests or smaller parity candidates were added;
- the current solution build and existing test suite pass; and
- active documentation reflects the delivered behavior.
