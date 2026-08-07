# TorrentCore Service App And VPN Egress Plan

## Status

This is the active sliced implementation plan for packaging `TorrentCoreService` as a macOS application bundle and
gating MonoTorrent execution on a Service-owned VPN egress check.

No slice is implemented merely because it appears in this document. Live deployment, ExpressVPN configuration, and
tests that mutate a real TorrentCore installation remain operator-controlled activities.

The first proof target is CA-Desktop on Apple Silicon. Intel packaging, WebUI bundling, and removal of legacy direct
deployment scripts are later slices after the Arm workflow is proven.

## Outcome

Deliver a signed, notarized, background-only `TorrentCoreService.app` that ExpressVPN can identify independently while
keeping the Service API available when VPN egress is unavailable.

When VPN validation is enabled:

- the Service starts and remains reachable even when the VPN is unavailable;
- magnets are accepted and persisted while degraded;
- no MonoTorrent engine, DHT, listener, manager, tracker, peer, or recovery activity runs while degraded;
- the Service checks its public IPv4 egress at setting-controlled intervals;
- direct-ISP, malformed, unexpected-address, timeout, and network-error results are failures;
- a successful check starts or recreates MonoTorrent, recovers durable torrent intent, and reconciles normal queues;
- a later failed check suspends and disposes MonoTorrent without converting runnable torrents to operator-paused;
- recovery requires no operator action.

## Related Sources Of Truth

- [Architecture](architecture.md)
- [Deployment](deployment.md)
- [Operator settings](operator-settings.md)
- [Testing](testing.md)
- [Troubleshooting](troubleshooting.md)
- [TVMaze deployment model](../../TVMaze/docs/deployment.md)
- [TVMaze macOS identity and packaging plan](../../TVMaze/docs/macos-local-network-identity-packaging-plan.md)
- Operator-supplied VPN egress recommendation: `/Users/dick/Desktop/vpn-egress-validation.md`

Current code and active documentation remain authoritative if this plan becomes stale.

## Confirmed Decisions

### Product And Runtime

- The proof changes `TorrentCoreService` only. It does not package or replace `TorrentCore.WebUI`.
- The Service and WebUI continue to be reachable from trusted LAN machines on ports `7033` and `7053` respectively.
- The Service binds to `http://0.0.0.0:7033`; local deployment health checks use `http://127.0.0.1:7033`.
- The trusted-LAN HTTP model remains unchanged; this work does not add authentication or TLS.
- IPv6 is link-local only on the operator's machines. The public egress validator must nevertheless reject non-IPv4
  responses instead of treating them as VPN success.
- ExpressVPN setup itself is outside implementation scope. The operator has established that split tunneling works once
  the Service is an application bundle.

### Application Identity

- Install root: `~/Applications/TorrentCore/`
- Bundle name: `TorrentCoreService.app`
- Display name: `TorrentCoreService`
- Main executable: `Contents/MacOS/TorrentCoreService`
- Bundle identifier: `com.conadv.torrentcore.service`
- Existing LaunchAgent label: `com.torrentcore.service`
- The LaunchAgent identifies the responsible app through `AssociatedBundleIdentifiers`.
- The app name contains no spaces, consistent with TVMaze background app bundles such as `TVMazeApiComplete.app`.

### Installed Data And Content

- Immutable Service executables, assemblies, native libraries, runtime metadata, generated static-asset metadata, and
  default `appsettings*.json` files live inside the sealed app bundle.
- `~/TorrentCore/Service` is not a permanent working-directory requirement after cutover.
- The first cutover backs up the legacy `~/TorrentCore/Service` directory, then removes it from the live layout after
  successful verification.
- `~/TorrentCore/Scripts/torrentcore.env`, logs, backups, deployment records, and the default download tree remain
  outside the bundle.
- SQLite state and the MonoTorrent cache remain under
  `~/Library/Application Support/TorrentCore/storage`.
- `~/TorrentCore/downloads` remains a functional out-of-box fallback only.
- Database-backed category paths and persisted per-torrent download paths remain authoritative and may point to
  separate mounted media volumes. Packaging and deployment must never rewrite them.
- A cutover preflight refuses any mutable or writable path that would resolve inside the signed app bundle.

### Deployment

- The supported production path becomes a signed and notarized DMG; direct repo deployment is not used for the proof.
- Existing direct deployment scripts remain present until a final approved cleanup slice.
- The proof DMG contains only the Arm64 Service and the scripts required to install and manage its LaunchAgent.
- WebUI files, configuration, process state, and LaunchAgent are untouched by the proof DMG.
- The target package design supports architecture-specific payloads. The first proof contains only `osx-arm64`; a later
  release can contain both `osx-arm64` and `osx-x64` and select the host-compatible payload.
- TorrentCore deployment should not require a TVMaze installation `live.json` file. The package detects the host
  architecture, targets the current user's standard TorrentCore locations, and preserves host-local state.
- The Developer ID identity, Team ID, and notarization profile remain common across architectures. Each architecture
  still requires its own compile, signature, entitlement, UUID, launchd, and live-runtime verification.

### VPN Egress Policy

- The check uses a documented JSON public-IP endpoint, initially
  `https://api.ipify.org?format=json`.
- The endpoint is a database-backed setting and can be changed without rebuilding the Service.
- Direct-ISP ranges are database-backed CIDR settings rather than hard-coded application logic.
- The degraded retry interval and ready-state verification interval are separate database settings.
- Initial interval defaults are `60` seconds while degraded and `240` seconds while ready.
- A failed request, timeout, malformed response, non-IPv4 response, or address in a configured direct-ISP CIDR is a
  validation failure.
- The complete observed public IP and validation outcome may be stored in TorrentCore's database-backed activity logs.
- ExpressVPN Network Lock remains the immediate packet-level protection. Periodic application validation manages
  TorrentCore state but does not claim to eliminate the accepted interval between ready-state checks if Network Lock is
  disabled.

## Target Installed Layout

```text
~/Applications/TorrentCore/
└── TorrentCoreService.app/
    └── Contents/
        ├── Info.plist
        ├── MacOS/
        │   └── TorrentCoreService
        └── Resources/
            ├── Runtime/
            │   ├── TorrentCoreService
            │   ├── TorrentCoreService.dll
            │   ├── TorrentCoreService.deps.json
            │   ├── TorrentCoreService.runtimeconfig.json
            │   ├── TorrentCoreService.staticwebassets.endpoints.json
            │   ├── appsettings.json
            │   ├── appsettings.Development.json
            │   └── signed runtime dependencies
            ├── Deployment/
            └── version.json

~/TorrentCore/
├── Scripts/
│   └── torrentcore.env
├── Logs/
├── downloads/
├── .backups/
└── .deploy/

~/Library/Application Support/TorrentCore/
└── storage/
    ├── torrentcore.db
    └── monotorrent-cache/
```

The launcher sets the helper's content root to the sealed runtime directory. Absolute storage, category, callback, and
per-torrent paths continue to resolve outside the app.

## Runtime State Model

### Public Host State

| State | Service API | Magnet admission | MonoTorrent | Check cadence |
|---|---|---|---|---|
| `Ready` with validation disabled | Available | Accepted | Normal | None |
| `Degraded` / checking | Available | Accepted and queued | Not initialized | Degraded interval |
| `Ready` / validated | Available | Accepted | Normal | Ready interval |
| `Degraded` / validation failed | Available | Accepted and queued | Fully stopped and disposed | Degraded interval |

`/api/health` remains available while degraded. `/api/host/status` is the authoritative operational state and explains
whether degradation is caused by VPN validation.

### Enabled Startup

1. Start configuration, SQLite persistence, activity logging, controllers, and the HTTP listener.
2. Load effective database-backed VPN settings.
3. Publish a degraded/checking host state without initializing MonoTorrent.
4. Run the public IPv4 check asynchronously.
5. On success, create MonoTorrent, run normal persisted recovery, reconcile the queue, and publish ready state.
6. On failure, remain degraded, record one transition/event plus bounded repeat diagnostics, and retry at the degraded
   interval.

### Ready-To-Degraded Transition

1. Serialize the transition against engine synchronization, queue reconciliation, actions, and automatic recovery.
2. Close the global execution gate before beginning teardown so no replacement work starts.
3. Stop background engine operations and flush manager snapshots.
4. Stop managers without changing durable `Runnable` versus `Paused` intent.
5. Stop and dispose the MonoTorrent `ClientEngine`, including DHT/listener facilities.
6. Clear only in-memory engine state and publish degraded state.
7. Continue API reads, settings, logs, history, category management, and persistence-only magnet admission.

### Degraded-To-Ready Transition

1. Serialize one engine-start attempt.
2. Recreate MonoTorrent using the latest effective engine settings.
3. Recover managers from durable torrent snapshots and cached metadata/fast-resume state.
4. Preserve queue order, metadata time-slice history, cold-recovery timing, completion state, callback state, category
   routing, and operator pause intent.
5. Reconcile capacity and start only the work allowed by normal queue policy.
6. Publish ready state and resume the ready-state check cadence.

Failed teardown or recreation must remain visible as degraded and retryable. It must not produce a second engine
instance or require an operator restart.

## Planned Database-Backed Settings

Names may be refined to match the existing runtime-settings naming pattern, but their separate semantics must remain.

| Setting | Initial default | Applies live | Purpose |
|---|---:|---|---|
| `VpnEgressValidationEnabled` | `false` | Yes | Preserves existing behavior until enabled per installation |
| `VpnEgressValidationEndpoint` | `https://api.ipify.org?format=json` | Yes | Documented JSON public-IP endpoint |
| `VpnEgressDirectIspCidrs` | Decision gate | Yes | One or more direct-ISP IPv4 CIDRs |
| `VpnEgressDegradedCheckIntervalSeconds` | `60` | Yes | Retry cadence while checking or degraded |
| `VpnEgressReadyCheckIntervalSeconds` | `240` | Yes | Revalidation cadence while ready |
| `VpnEgressRequestTimeoutSeconds` | Decision gate | Yes | Per-request timeout bounded below both intervals |

The setting validator must reject unsupported schemes, invalid absolute endpoints, credentials in endpoint URLs,
invalid CIDRs, IPv6 CIDRs for this policy, nonpositive intervals, and a timeout that is not safely below the applicable
check interval.

## Decision Gates Remaining Inside The Plan

These do not block documenting or scaffolding earlier independent slices, but the owning slice cannot complete without
the decision.

1. Confirm the initial direct-ISP CIDR value. The supplied recommendation uses `47.0.0.0/8` but prefers a narrower
   range if known.
2. Confirm the request-timeout default. The supplied recommendation uses 10 seconds.
3. Confirm degraded mutation semantics beyond magnet admission. Recommended behavior:
   - list, detail, history, logs, settings, and category operations remain available;
   - add magnet persists a queued runnable torrent without creating a manager;
   - pause/resume update durable desired state without creating a manager;
   - removal operates from persistence/filesystem state without starting MonoTorrent;
   - explicitly engine-dependent metadata refresh/reset and tracker actions return a structured unavailable response.
4. Approve the final `NSLocalNetworkUsageDescription` text for the Service bundle.

## Sliced Delivery Plan

### Slice 0: Baseline And Safety Fixtures

Status: completed on August 6, 2026.

#### Work

- Record the clean build/test baseline and current Service/OpenAPI contract.
- Add fixture helpers for controllable clocks, HTTP egress responses, and engine lifecycle observation.
- Capture the current CA-Desktop layout as test fixtures without copying machine-local secrets or live databases.
- Define persistent activity event names for validation checks and state transitions.
- Define the app-bundle and DMG verification inventory before packaging code changes begin.

#### Acceptance

- Normal tests remain deterministic and require neither Internet access nor ExpressVPN.
- Test fixtures can represent VPN success, direct ISP, IPv6, malformed JSON, timeout, cancellation, and endpoint failure.
- No production behavior changes.

#### Recorded Baseline

- Baseline Git commit: `348f2dc` (`Plan TorrentCore VPN-gated service app`).
- Service version: `0.5.1`.
- Public API contract version: `1`.
- Committed normalized OpenAPI SHA-256:
  `ba2fe9554cb98a1dffd96c4f143fdff8b54d4cb713b31f05704f02abdfb5a629`.
- `dotnet build TorrentCore.sln --no-restore`: succeeded with zero warnings and zero errors.
- `dotnet test TorrentCore.sln --no-build --no-restore`: all 206 pre-slice tests passed.
- The test baseline required the normal unsandboxed VSTest host because its local coordination socket is denied by the
  filesystem/network sandbox. The suite itself did not contact the Internet or ExpressVPN.

Reusable fixtures now provide a manual `TimeProvider`, scripted HTTP responses, and engine-instance lifecycle
observation. The sanitized CA-Desktop fixture records only known paths, architecture, and LAN bindings; it deliberately
omits host-local environment values, saved WebUI endpoint values, live database/cache contents, and external-volume
category paths.

#### Persistent Activity Event Inventory

VPN activity uses category `vpn` and these stable event types:

| Event type | Persistence rule | Required detail fields when implemented |
|---|---|---|
| `vpn.egress.validation_completed` | One result for each completed validation attempt, subject to the existing activity-log retention limit | trigger, outcome, sanitized endpoint authority, duration, and observed IP when parseable |
| `vpn.egress.state_changed` | One row only when public VPN/engine state changes | previous state, new state, transition reason, validation outcome, and engine disposition |
| `vpn.egress.engine_transition_failed` | One row for each failed serialized engine start, stop, or dispose attempt | operation, instance identity when available, error, and retry disposition |

Cancellation caused by normal Service shutdown is represented by the validation outcome but must not manufacture a
degraded transition. Repeated unchanged validation failures may be summarized later, but the event names and core
detail meanings above remain stable.

#### App Bundle And DMG Verification Inventory

Every proof release must verify all of the following before live installation:

- the DMG contains the Arm64 Service payload and Service deployment scripts only, with no WebUI payload or saved
  machine-local configuration;
- the install target is `~/Applications/TorrentCore/TorrentCoreService.app` and the bundle identifier is
  `com.conadv.torrentcore.service`;
- `Info.plist` is valid, the display and executable names are `TorrentCoreService`, the app is background-only, and the
  approved local-network usage text is present;
- the main launcher and .NET helper are Arm64 Mach-O files with distinct expected roles, every native dependency is
  signed, and the framework-dependent helper retains its required JIT and library-validation entitlements;
- the sealed runtime contains the required Service executable, assemblies, dependency/runtime metadata, generated
  static-asset endpoint metadata, default appsettings files, deployment resources, and `version.json`;
- the app contains no database, MonoTorrent cache, logs, downloads, `torrentcore.env`, WebUI connection file, or
  category/per-torrent mutable paths;
- the Service launcher sets the content root to `Contents/Resources/Runtime` while storage and default downloads still
  resolve to their established external paths;
- the installed LaunchAgent uses the bundled executable, keeps label `com.torrentcore.service`, declares
  `AssociatedBundleIdentifiers` for `com.conadv.torrentcore.service`, preserves LAN binding, and does not replace or
  restart the WebUI agent;
- package checksums and release Git/build identity are internally consistent;
- Developer ID verification, notarization acceptance, stapler validation, Gatekeeper assessment, and disk-image
  verification all pass outside the filesystem sandbox;
- a mounted-DMG plan/dry-run proves existing mutable state would be preserved before any apply step is authorized.

### Slice 1: Persisted VPN Settings

Status: pending.

#### Work

- Add additive SQLite/runtime-setting support for the VPN settings.
- Extend effective settings, update requests, validation, persistence, API contracts, and settings help.
- Preserve validation-disabled behavior by default.
- Update all .NET and Apple-client callers for additive contract fields.
- Regenerate and verify the committed OpenAPI contract after the public contract is approved.

#### Acceptance

- Existing databases migrate without changing torrent, category, callback, or engine settings.
- Settings round-trip through SQLite and the API.
- Live interval/endpoint/CIDR changes affect the next scheduling decision without restarting the Service.
- Invalid endpoints, CIDRs, intervals, and timeouts return structured validation errors.

### Slice 2: Public IPv4 Egress Probe

Status: pending.

#### Work

- Implement an async, cancellable egress client using the configured JSON endpoint.
- Use a bounded `HttpClient` path and typed response parsing.
- Require `AddressFamily.InterNetwork`; reject IPv6 even if parsing succeeds.
- Implement tested IPv4 CIDR matching rather than string-prefix comparison.
- Classify success, direct ISP, invalid response, timeout, cancellation, DNS/connection failure, and unexpected failure.
- Record results through the existing persistent activity-log path; do not add a new logging subsystem.
- Avoid logging on every identical successful check; keep transition logs and bounded diagnostics useful.

#### Acceptance

- Unit tests cover every result class without Internet access.
- A non-`47.*` result is described as validated egress, not proof of a specific VPN provider.
- Cancellation during Service shutdown is not reported as a VPN failure.
- The probe itself does not initialize or call MonoTorrent.

### Slice 3: Persistence-Only Admission And Degraded Actions

Status: pending.

#### Work

- Separate durable magnet admission from MonoTorrent manager creation.
- Allow add operations to persist accepted torrents while the global execution gate is closed.
- Project these torrents as queued with a VPN-specific host wait reason without changing their runnable desired state.
- Implement the approved degraded pause/resume/remove semantics.
- Ensure reads and non-engine mutations never initialize MonoTorrent as a side effect.
- Keep normal oldest-added queue ordering and metadata reservation rules intact for later recovery.

#### Acceptance

- Magnets submitted during degraded state remain durable across full Service restart.
- No MonoTorrent engine or manager is created by admission, list/detail reads, or approved degraded mutations.
- After the gate opens, stored magnets enter the existing queue policy in deterministic order.
- Operator-paused torrents remain paused after VPN recovery.

### Slice 4: Restartable MonoTorrent Lifecycle

Status: pending.

#### Work

- Refactor the current shutdown-oriented adapter path into explicit idempotent initialize, recover, suspend, and dispose
  operations.
- Dispose `ClientEngine` and reset initialization state during suspension.
- Stop all adapter-owned background operations, reset coordinators, announce work, probes, and synchronization safely.
- Flush durable snapshots before manager disposal.
- Make repeated suspend/resume cycles single-flight and safe under cancellation and partial failure.
- Recreate engine settings from the latest effective database settings on every start.

#### Acceptance

- Tests prove no DHT/listener/manager activity exists after suspension completes.
- Repeated ready/degraded/ready cycles do not leak engines, managers, tasks, gates, sockets, or reservations.
- Recovery preserves torrent desired state, paths, progress, completion/callback state, time-slice history, and cold state.
- A failed teardown or start stays degraded and can retry without restarting the Service process.
- Service shutdown remains bounded and does not race the periodic validator.

### Slice 5: VPN State Orchestrator And Execution Gate

Status: pending.

#### Work

- Add a single host-level coordinator owning check scheduling, state transitions, and the MonoTorrent execution gate.
- Start degraded/checking when validation is enabled and ready immediately when it is disabled.
- Use the degraded and ready intervals independently.
- On ready-state failure, close admission-to-execution first, then suspend MonoTorrent.
- On degraded success, initialize/recover MonoTorrent and open normal execution only after recovery succeeds.
- Apply setting changes deterministically, including enable/disable transitions and interval changes.
- Prevent overlapping checks and overlapping engine transitions.

#### Acceptance

- The API is reachable before the first enabled validation succeeds.
- No torrent work runs in degraded mode.
- Recovery is automatic and requires no operator action.
- A slow check cannot overlap itself or resurrect a stale state after settings change or shutdown.
- The accepted ready interval bounds application detection; documentation does not overstate leak prevention.

### Slice 6: Host Status, Logs, And Operator Diagnostics

Status: pending.

#### Work

- Reuse `EngineHostStatus.Degraded` and add structured VPN-validation details to host status.
- Expose enabled state, phase, last check time, last success time, next check time, observed public IPv4 when available,
  configured intervals, and a safe failure summary.
- Add queue/wait diagnostics that distinguish VPN gating from capacity and operator pause.
- Keep `/api/health` successful while the process and persistence boundary are healthy.
- Update WebUI/native client decoding for additive fields even though the proof DMG does not deploy WebUI.
- Update active operator settings and troubleshooting documentation.

#### Acceptance

- Operators can distinguish process health, VPN degradation, engine transition failure, and normal queue pressure.
- Persistent logs contain the full observed public IP as approved.
- Successful repeated checks do not flood activity logs.
- Older clients tolerate the additive fields according to the current API-version policy.

### Slice 7: Arm64 TorrentCoreService App Bundle

Status: pending.

#### Work

- Adapt the proven TVMaze app-like bundle and native supervisor pattern for TorrentCore.
- Package the framework-dependent helper and all immutable publish output under `Contents/Resources/Runtime`.
- Create a component-specific Arm64 launcher with a unique main Mach-O UUID.
- Preserve signal forwarding, child exit status, working/content-root behavior, and one-Service-instance semantics.
- Add stable bundle metadata, background-only identity, version/build identity, and approved Local Network description.
- Register the installed bundle with Launch Services.
- Update the Service LaunchAgent to use the bundled launcher and `AssociatedBundleIdentifiers` while retaining
  `com.torrentcore.service`.
- Keep `ASPNETCORE_URLS=http://0.0.0.0:7033` host-configurable and use loopback for local verification.

#### Acceptance

- The unsigned/static verifier confirms layout, metadata, runtime files, unique UUID, helper relationship, and plist
  association.
- The signed verifier confirms inside-out signatures, Team ID, Hardened Runtime, secure timestamp, .NET JIT and
  framework-dependent entitlements, native dependency signatures, and no mutable files inside the bundle.
- Launching the app from launchd preserves configuration, storage, categories, callback paths, and API behavior.
- The app is registered and available for operator selection in ExpressVPN.

### Slice 8: Generic Service-Only Arm DMG And Deployer

Status: pending.

#### Work

- Reshape the TorrentCore release metadata from one machine/runtime to a runtime payload collection.
- Initially stage only `payload/osx-arm64/TorrentCoreService.app`.
- Detect the host architecture and refuse a missing or mismatched payload.
- Remove the TorrentCore deployer's dependency on TVMaze machine `live.json` manifests.
- Target the current user's standard app, runtime-state, scripts, logs, backup, and LaunchAgent locations.
- Make the package Service-only: do not copy, stop, start, verify, or back up WebUI.
- Preserve `torrentcore.env`, the SQLite storage root, category paths, download data, and installed state.
- Back up the legacy Service directory into a compressed, non-discoverable artifact.
- Verify the new bundle before stopping the legacy Service.
- Replace the bundle atomically, register it, install only the Service LaunchAgent, and verify rollback material before
  deleting the live legacy Service directory.
- Keep plan, dry-run, confirmed apply, backup, verify, history, checksums, quarantine, signing, notarization, and
  stapling behavior.

#### Acceptance

- Plan and dry-run make no filesystem or launchd changes.
- Apply never touches WebUI.
- Apply preserves every approved host-local path and setting.
- A first install may complete in degraded state so the operator can configure ExpressVPN; degraded is not confused
  with a failed process installation.
- Operational verification separately requires a successful Service-owned egress result and ready engine state.
- Rollback can restore the legacy executable/LaunchAgent without rolling back or overwriting the database.

### Slice 9: WebUI Connection-State Packaging Fix

Status: pending; independent of the Service-only proof deployment.

#### Work

- Exclude `WebUI/Config/service-connection.json` from every publish and release payload.
- Treat the saved connection file as machine-local mutable state.
- Preserve it across any future WebUI directory or app-bundle replacement.
- Add staging, apply, backup, rollback, and verification regressions.

#### Acceptance

- A release payload contains no release-machine saved endpoint.
- An existing target endpoint survives replacement byte-for-byte.
- A fresh installation receives an intentional default without pretending it is saved operator state.
- The existing prohibition on another Tom Service/WebUI package can be removed only after its separate acceptance gate.

### Slice 10: CA-Desktop Arm Proof

Status: pending; requires explicit operator approval and a production window.

#### Preflight

- Confirm the Service has no active torrent work requiring uninterrupted transfer.
- Back up the SQLite database consistently and capture current LaunchAgent/app versions.
- Report effective storage, category, per-torrent, callback, and download paths without rewriting them.
- Confirm no mutable path resolves inside the target app.
- Build and verify the DMG outside the filesystem sandbox where required by repository policy.

#### Proof Sequence

1. Mount the quarantined signed/stapled DMG.
2. Run plan and dry-run.
3. Apply the Service-only Arm package.
4. Confirm `TorrentCoreService.app` identity, LaunchAgent, LAN API availability, and degraded/checking status before VPN
   approval if applicable.
5. Add `TorrentCoreService.app` to the approved ExpressVPN split-tunnel rule.
6. Wait for a Service-owned successful egress check and automatic MonoTorrent recovery.
7. With explicit disposable-mutation approval, prove a magnet submitted while degraded stays queued and starts only
   after recovery.
8. Exercise one controlled ready-to-degraded-to-ready cycle with Network Lock providing immediate protection.
9. Confirm no operator pause intent, paths, history, callback state, or queue order is lost.
10. Run final package, signature, launchd, API, host-status, log, and installed-snapshot verification.

#### Acceptance

- The Service remains reachable and accepts magnets without VPN egress.
- MonoTorrent has no activity while degraded.
- The next successful check restarts and recovers MonoTorrent automatically.
- LAN clients can reach the Service on port `7033`.
- The old Service directory is absent from the live layout and recoverable from compressed backup.
- The operator explicitly accepts the proof before Intel or WebUI packaging begins.

### Slice 11: Intel Payload And Dual-Architecture DMG

Status: deferred until Arm proof acceptance.

#### Work

- Publish and package an independently signed `osx-x64` Service bundle.
- Add the Intel payload to the same runtime-selecting DMG format.
- Reuse the same Developer ID identity and notarization profile while compiling and validating Intel Mach-O content
  separately.
- Prove architecture selection refuses cross-installation and never mixes helpers or native libraries.
- Validate the framework-dependent Intel runtime, launchd behavior, app identity, signatures, entitlements, UUID, and
  engine lifecycle on an approved Intel host.
- Leave VPN validation disabled where blanket VPN policy makes the application gate unnecessary unless the operator
  explicitly enables it.

#### Acceptance

- One DMG contains complete, independently verifiable Arm64 and x64 payloads.
- The installer selects exactly one matching runtime.
- Arm acceptance remains unchanged after adding Intel.
- Intel has its own live acceptance record before being treated as supported.

### Slice 12: WebUI App Bundle

Status: deferred and not implied by Service proof acceptance.

#### Work

- Package `TorrentCore.WebUI` as its own app bundle using the proven TVMazeWeb static-content pattern.
- Keep immutable `wwwroot`, CSS, JavaScript, assemblies, and defaults inside the bundle.
- Keep `service-connection.json` outside the sealed bundle.
- Give WebUI a separate stable bundle identity, launcher UUID, LaunchAgent association, signing verification, and
  component-level deployment/rollback path.
- Decide whether WebUI shares a combined multi-component DMG or receives its own release artifact only after the
  Service workflow is stable.

#### Acceptance

- WebUI static assets served from the bundle are byte-identical to staged publish output.
- Saved Service connection state survives upgrade and rollback.
- Service-only deployments remain able to leave WebUI untouched.

### Slice 13: Final Deployment Cleanup

Status: deferred until Arm, Intel, and required WebUI paths are proven.

#### Work

- Inventory the direct `deploy-*-arm.zsh`, `deploy-*-intel.zsh`, and combined deploy surfaces.
- Remove or archive them only with explicit operator approval.
- Remove obsolete flat-Service assumptions from scripts, docs, tests, and troubleshooting.
- Keep current active docs concise; move completed milestone history and this plan to `docs/archive/` when no longer
  active.
- Record the final supported DMG commands, installed layout, rollback workflow, and verification requirements.

#### Acceptance

- There is one documented production deployment path.
- No supported script can silently replace an app installation with the legacy flat runtime.
- Active docs describe current behavior; historical rollout detail is archived.

## Verification Matrix

| Boundary | Required verification |
|---|---|
| Settings/persistence | SQLite migration, round-trip, validation, old-database compatibility |
| Egress probe | Success, configured direct CIDR, IPv6, malformed JSON, timeout, cancellation, endpoint failure |
| Admission | Degraded add persistence, restart durability, queue order, pause intent |
| Engine lifecycle | No activity after suspend, repeated recovery, partial failures, shutdown races |
| API/contracts | Health while degraded, structured host status, OpenAPI normalization, client compatibility |
| App bundle | Layout, metadata, UUID, registration, helper path, content root, immutable assets |
| Signing | Inside-out code signatures, Team ID, Hardened Runtime, timestamp, entitlements, native dependencies |
| Deployer | Plan/dry-run, architecture selection, Service-only scope, backup, atomic apply, rollback, preserved state |
| DMG | Checksums, signature, notarization acceptance, stapler ticket, disk image, Gatekeeper, quarantine path |
| Live Arm proof | LAN API, degraded admission, zero MonoTorrent activity, automatic ready recovery |

Routine implementation verification follows [docs/testing.md](testing.md):

```bash
dotnet build TorrentCore.sln
dotnet test TorrentCore.sln
```

Contract changes also require committed OpenAPI regeneration and Apple client tests. Script changes require syntax and
focused deployer regressions. DMG security validation must run outside the filesystem sandbox and is authoritative over
sandboxed signing results.

## Rollback Model

- Before first cutover, create a compressed backup of the complete legacy Service directory, relevant scripts/plist,
  installed snapshot, and configuration inventory.
- Do not place an unpacked backup `.app` where Launch Services can discover a duplicate bundle identity.
- Stop the exact `com.torrentcore.service` job before switching paths.
- A failed app apply restores the prior LaunchAgent and legacy runtime, then verifies the old process before reporting
  rollback success.
- Database migrations for VPN settings are additive. Rollback must be verified against the previous Service version;
  older code must safely ignore the additional runtime-setting rows.
- Never roll back or overwrite category paths, torrent state, history, callback state, downloads, or the storage root as
  part of executable rollback.

## Principal Risks And Controls

| Risk | Control |
|---|---|
| Public-IP provider outage suspends torrents | Intentional fail-closed behavior, configurable endpoint, visible degraded reason |
| Non-VPN proxy produces non-direct address | Report validated egress rather than asserting provider identity |
| Traffic escapes between ready checks if Network Lock is disabled | Document accepted interval; keep Network Lock as immediate protection |
| Add path initializes MonoTorrent while degraded | Separate persistence-only admission before enabling the gate |
| Engine teardown leaves DHT/listeners alive | Dispose the complete engine and test absence of activity |
| Recovery changes durable operator intent | Keep VPN suspension separate from `Paused`; verify desired state across cycles |
| Concurrent check/start/stop creates two engines | Single host lifecycle coordinator and single-flight transitions |
| Bundle replacement loses machine-local paths | External state, preflight inventory, protected files, atomic replacement, rollback |
| Backup creates duplicate discoverable app | Store app backups compressed |
| Strict clients reject additive host fields | Update contracts/callers and run normalized OpenAPI/Swift verification |
| Arm success hides Intel-specific runtime issue | Separate Intel compile, signing, launchd, and live acceptance |
| Sandboxed signing checks report false failures | Perform authoritative DMG security verification outside the sandbox |

## Definition Of Complete

The Service app and VPN-egress work is complete only when:

- CA-Desktop runs the signed/notarized `~/Applications/TorrentCore/TorrentCoreService.app` through the existing
  LaunchAgent label;
- the Service remains reachable and accepts durable magnets while VPN validation is degraded;
- MonoTorrent has no network or manager activity in degraded state;
- successful validation automatically initializes and recovers MonoTorrent;
- later failures automatically suspend it and later successes recover it repeatedly;
- settings, public status, persistent logs, contracts, clients, and operator docs describe the behavior accurately;
- the Service-only Arm DMG preserves all external state and passes package plus live acceptance;
- rollback is proven;
- the operator explicitly approves proceeding to Intel, WebUI bundling, or legacy deployment cleanup.
