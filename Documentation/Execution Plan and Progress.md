# TorrentCore Execution Plan and Progress

## Purpose

This is the living delivery document for TorrentCore.

It records:
- execution phases
- testing strategy
- working assumptions
- concrete changes made in the repo
- progress against the current plan

## Source of Truth

This document follows:
- [Project Brief.md](/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TorrentCore/Documentation/Project%20Brief.md)
- [TVMaze Integration Boundary.md](/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TorrentCore/Documentation/TVMaze%20Integration%20Boundary.md)
- [Initial Scaffold Status.md](/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TorrentCore/Documentation/Initial%20Scaffold%20Status.md)

## Current Baseline

Status as of March 10, 2026:
- solution scaffolding exists and builds successfully
- current test suite passes
- Phase 0 contract work is complete
- service, client, and web now expose a stubbed torrent-management boundary backed by an in-memory application service
- Swagger UI is enabled for the service in development
- Phase 1 configuration and startup validation are implemented
- Phase 2 persistence foundation has started with SQLite-backed activity logging
- SQLite-backed torrent state persistence is implemented for the current fake engine slice
- tracked SQLite schema migrations are implemented
- startup torrent-state rehydration is implemented for the persisted fake engine slice
- a managed fake runtime now resolves metadata, applies simple queueing, and advances persisted download state
- a first MonoTorrent-backed engine slice is implemented behind the existing adapter boundary
- MonoTorrent host configuration, engine-ready diagnostics, and connection-failure log throttling are implemented
- MonoTorrent partial-file handling and configurable seeding-stop policy are implemented
- operator-managed global queue concurrency settings are implemented for active metadata resolutions and active downloads
- startup-configured global MonoTorrent network throttling controls are implemented for overall connection count, half-open connection count, download rate, and upload rate
- host status now includes a runtime-state breakdown so operators can see how many torrents are actively resolving metadata, queued for metadata, downloading, queued for download, seeding, paused, completed, or errored

Verified baseline:
- `dotnet build TorrentCore.sln`
- `dotnet test TorrentCore.sln`

Development API documentation:
- `https://localhost:7033/swagger`

Current service configuration section:
- `TorrentCore:EngineMode`
- `TorrentCore:EngineListenPort`
- `TorrentCore:EngineDhtPort`
- `TorrentCore:EngineAllowPortForwarding`
- `TorrentCore:EngineAllowLocalPeerDiscovery`
- `TorrentCore:EngineMaximumConnections`
- `TorrentCore:EngineMaximumHalfOpenConnections`
- `TorrentCore:EngineMaximumDownloadRateBytesPerSecond`
- `TorrentCore:EngineMaximumUploadRateBytesPerSecond`
- `TorrentCore:EngineConnectionFailureLogBurstLimit`
- `TorrentCore:EngineConnectionFailureLogWindowSeconds`
- `TorrentCore:UsePartialFiles`
- `TorrentCore:SeedingStopMode`
- `TorrentCore:SeedingStopRatio`
- `TorrentCore:SeedingStopMinutes`
- `TorrentCore:MaxActiveMetadataResolutions`
- `TorrentCore:DownloadRootPath`
- `TorrentCore:StorageRootPath`
- `TorrentCore:MaxActiveDownloads`
- `TorrentCore:RuntimeTickIntervalMilliseconds`
- `TorrentCore:MetadataResolutionDelayMilliseconds`
- `TorrentCore:DownloadProgressPercentPerTick`
- if not overridden, the service now defaults downloads to a dedicated `~/TorrentCore/downloads` folder and internal storage to the user's local app-data area
- project-relative runtime folders were replaced as defaults because they are not appropriate for normal operator use
- these settings are currently config-driven and exposed through host status for diagnostics; later they should be managed through the web UI
- MonoTorrent partial-file support currently uses the engine's native `.!mt` suffix when enabled
- runtime settings now persist operator overrides for seeding policy, cleanup policy, log throttling, active metadata-resolution concurrency, and active download concurrency
- engine-level MonoTorrent network throttling is currently startup-configured and exposed through diagnostics; moving those controls into live operator management should be handled in a later slice because they are engine-initialization settings
- the operator settings reference now explicitly explains engine connections, half-open connections, and how global upload/download rates are measured so throttle controls are understandable to operators
- the settings page now separates currently applied engine throttle values from saved restart-required throttle settings so operators do not see them as duplicate fields
- the dashboard host-status section now exposes queue and runtime-state counts so operators can understand whether torrents are waiting on metadata capacity, waiting on download slots, actively transferring, seeding, paused, completed, or in error
- the dashboard host-status section is now grouped into compact related cards so service, engine, queue/activity, and policy/storage information use screen width better instead of rendering as a single long vertical list
- the repo now includes an explicit Intel Mac deployment target document covering the target folder layout, mounted-share deployment path, and required `zsh` start/stop/restart/deploy scripts for the service and web UI
- torrent DTOs now expose per-torrent wait reason and queue position so the UI can explain whether an item is waiting for metadata capacity, download capacity, paused by an operator, or blocked by error
- host status now exposes available metadata/download slots plus aggregated current peer count and transfer rates so queue pressure and engine saturation are easier to understand
- the web UI is now split so the Dashboard is host-status focused while magnet submission and torrent lifecycle management live on a dedicated Torrents page
- the web UI now includes a dedicated torrent detail page with deeper runtime diagnostics, local-time timestamps, action controls, and recent per-torrent log history
- pausing a MonoTorrent torrent during metadata resolution now explicitly stops the manager so later sync passes do not project it back to `ResolvingMetadata`, and regression coverage now verifies paused state is preserved in both detail and list views
- the Torrents page now supports multi-select bulk actions for pause and remove using the existing torrent lifecycle API calls
- the earlier host-wide `Pause All` and `Resume All` controls were intentionally backed back out of the Torrents page after operator testing showed inconsistent behavior and poor responsiveness under real MonoTorrent load; row-level and selected-row lifecycle actions remain in place
- the Torrents page bulk toolbar now includes `Resume Selected` so multi-select pause/resume can be exercised and hardened without reintroducing host-wide pause-all/resume-all controls
- paused MonoTorrent torrents are now treated as operator-sticky during sync and queue reconciliation, so background metadata/download scheduling cannot project them back into `ResolvingMetadata` or other active states until the operator explicitly resumes them
- MonoTorrent stop handling now treats the engine's intermediate `Stopping` state as an in-progress stop instead of throwing through the background synchronization service during pause and synchronization flows
- MonoTorrent resume handling now waits for a paused manager to leave the engine's intermediate `Stopping` state before restarting, preventing rapid pause/resume flows from failing with a 500 while the manager is still winding down
- MonoTorrent queue reconciliation now re-reads current persisted torrent state before starting or reprojecting a manager, which prevents a fresh operator pause from being overwritten by a stale pre-pause scheduling snapshot
- pause/resume refactoring has started around a TorrentCore-owned desired-state model: torrent snapshots now persist `Runnable` vs `Paused` intent, read endpoints no longer trigger synchronization, MonoTorrent state-change events no longer write torrent state directly, and scheduler-owned synchronization now serializes pause/resume intent changes with manager start/stop decisions
- service/API regression coverage now explicitly verifies that repeated list/detail reads do not mutate a paused MonoTorrent torrent and that resuming a paused queued torrent under metadata-slot pressure leaves it queued with the correct wait reason instead of starting immediately

Note:
- one `MSB3026` copy warning occurred when build and test were run in parallel against the same output directories
- this does not indicate an application defect

## Delivery Principles

- TorrentCore remains a separate repo and deployable unit from TVMaze.
- TVMaze interacts only through stable HTTP contracts or a versioned client library.
- Torrent engine internals must not leak into external contracts.
- Web is the first rich admin UI.
- v1 stays intentionally narrow and is delivered through vertical slices.
- Each slice must be testable through the API and exercisable through the web UI.
- User-facing date/time values must be rendered in the operator's local time, not UTC.
- Incomplete content must be distinguishable from completed content through explicit TorrentCore policy, not inferred only from file size or Finder display.
- TorrentCore should accept and persist incoming magnet submissions even when runtime concurrency limits are full; queueing controls execution, not API admission.

## Phased Execution Plan

### Phase 0: Contract and State Model

Goal:
- define the v1 boundary before engine integration work begins

Planned outputs:
- `AddMagnetRequest`
- `TorrentSummaryDto`
- `TorrentDetailDto`
- `EngineHostStatusDto`
- `ServiceErrorDto`
- v1 torrent state enums and action/result contracts
- thin service layer boundary in the service project

Exit criteria:
- contracts compile across service, client, and web
- API shape is stable enough for vertical-slice implementation
- test coverage exists for serialization and validation behavior

### Phase 1: Configuration and Startup Validation

Goal:
- make service startup deterministic and fail fast on invalid configuration

Planned outputs:
- service options objects
- path and storage validation
- startup validation rules
- DI registration structure for application services and engine adapter
- predictable problem-details responses for invalid operations

Exit criteria:
- invalid configuration fails at startup with clear messages
- service composition is no longer controller-centric

### Phase 2: Persistence Foundation

Goal:
- establish the SQLite storage model and migration path, including operational logging persistence

Planned outputs:
- persistence entities/schema
- migration strategy
- repository or persistence service abstractions as needed
- torrent state rehydration rules after restart
- persisted diagnostic and activity log foundation
- persisted torrent-state foundation for the current fake engine slice

Exit criteria:
- state survives restart
- schema creation and upgrades are deterministic
- persistence tests run against real SQLite files
- operators can inspect persisted activity and error history through the service boundary

### Phase 3: First Vertical Slice with Fake Engine

Goal:
- validate the API and UI workflow before real engine complexity is introduced

Planned outputs:
- add magnet flow
- torrent list flow
- torrent detail flow
- fake or in-memory engine adapter for deterministic testing

Exit criteria:
- end-to-end flow works without MonoTorrent
- web UI can add and inspect torrents through the API

### Phase 4: Real Engine Integration

Goal:
- integrate MonoTorrent behind the internal engine abstraction

Planned outputs:
- metadata resolution
- torrent lifecycle integration
- pause/resume/remove operations
- restart recovery behavior
- operator-visible MonoTorrent runtime configuration and diagnostics
- configurable seeding stop policy
- explicit incomplete-file finalization behavior such as `.part` suffix handling

Exit criteria:
- no MonoTorrent types cross the public boundary
- core actions work consistently through service and web

### Phase 5: Web Admin v1

Goal:
- deliver a usable first management UI

Planned outputs:
- host status summary
- torrent list and detail pages
- add magnet workflow
- action controls
- robust error and loading states

Exit criteria:
- common admin actions are available without direct API calls

### Phase 6: TVMaze Integration

Goal:
- integrate only after the TorrentCore boundary is stable

Planned outputs:
- lightweight client usage from TVMaze
- host selection
- magnet submission
- summary/status viewing

Exit criteria:
- TVMaze remains a shallow consumer of TorrentCore

## Phase Gates

A phase is complete only when:
- implementation is covered by tests at the relevant layer
- failure behavior is explicit and repeatable
- restart behavior is validated where stateful behavior exists
- the slice is usable through the intended public boundary

## Test Strategy

### Unit and Contract Tests

Cover:
- DTO serialization
- request validation
- option validation
- state transition rules
- duplicate or invalid magnet handling

### Service and Domain Tests

Cover:
- queue behavior
- admission-vs-execution behavior when active metadata-resolution or download limits are reached
- lifecycle transitions
- recovery decisions
- service-layer orchestration independent of transport concerns

### API Integration Tests

Cover:
- controller and endpoint behavior through real HTTP requests
- status codes
- problem-details payloads
- persistence side effects

Recommended approach:
- `WebApplicationFactory`

### Persistence Tests

Cover:
- schema creation
- migrations
- locking behavior
- restart rehydration
- file finalization and rename behavior across restart

Requirement:
- use real SQLite-backed tests, not only in-memory substitutes

### Engine Adapter Tests

Cover:
- fake adapter behavior for deterministic vertical-slice tests
- MonoTorrent adapter behavior for real engine integration
- deterministic testing for log throttling and runtime diagnostics behavior without depending on external peer or network conditions
- seeding-policy transitions and incomplete-file rename/finalization behavior

### Web UI Tests

Cover:
- page rendering
- empty/loading/error states
- add/list/detail flows
- basic browser-level smoke coverage for critical actions

### End-to-End Scenarios

Cover:
- add magnet
- metadata resolution
- pause/resume/remove
- restart recovery
- engine unavailable while API remains reachable

## Working Assumptions

Current assumptions unless superseded by later decisions:
- v1 is magnet-only
- one service host is sufficient for the first deliverable
- SQLite is host-local persistence for v1
- Web is the only rich admin client in the first release
- TVMaze integration happens after the TorrentCore v1 boundary is proven
- authentication and authorization are required, but the exact v1 model is still pending
- logging will be TorrentCore-owned and not coupled to TVMaze infrastructure
- startup recovery for the persisted fake engine normalizes active runtime states to `Queued` rather than pretending transfers survived a process restart
- the current fake runtime is intentionally deterministic and exists to exercise queueing, metadata resolution, and restart behavior before MonoTorrent is integrated
- the service now supports both `Fake` and `MonoTorrent` engine modes, with `MonoTorrent` as the default operator-facing mode and `Fake` retained for deterministic tests
- MonoTorrent runtime configuration is currently file/config driven, but the service boundary should stay stable so those settings can later move behind the web UI without redefining the API surface
- TorrentCore should evolve as a more general-purpose torrent engine, not only a narrowly TVMaze-oriented downloader
- downstream consumers may rely on `.part` suffix semantics to determine when content is safe to process
- finished torrents may legitimately remain in `Seeding` until a configured stop policy is reached

## Phase 0 Contract Review Decisions

Reviewed and accepted on March 10, 2026:
- v1 remains torrent-level only
- per-file torrent detail is deferred to a later version
- labels and categories are deferred from v1
- remove remains split between remove-only and remove-with-data
- queue position is deferred from v1
- bursty intake from TVMaze should be handled by accepting and persisting magnets immediately, then queueing execution behind runtime limits instead of rejecting requests when capacity is full
- initial list behavior will later support sorting by progress, status, and name
- initial list behavior will later support filtering by name and status
- torrent state names remain:
  - `ResolvingMetadata`
  - `Queued`
  - `Downloading`
  - `Seeding`
  - `Paused`
  - `Completed`
  - `Error`
  - `Removed`
- tracker count and connected peer count are included in the Phase 0 contract surface

## Risks and Watch Items

- leaking engine-specific concepts into contracts too early
- defining persistence around implementation detail instead of domain state
- skipping restart and recovery testing until late in the project
- letting the web UI outrun the contract boundary
- adding TVMaze integration before TorrentCore behavior is stable
- treating preallocated file size as proof of completion instead of using engine-verified completion and explicit finalization rules

## Current Next Steps

1. Add explicit incomplete-file handling with `.part` suffix compatibility.
2. Add configurable seeding stop policy covering immediate stop, ratio, time, ratio-or-time, and unlimited seeding.
3. Persist and test file finalization state across restart.
4. Surface seeding and finalization policy through service configuration and later through the web UI.
5. Continue expanding runtime diagnostics and operator controls around the real engine slice.

## Change Log

### 2026-03-10

Changes:
- created this living execution and progress document
- captured the agreed phased implementation plan
- captured the layered testing strategy
- recorded the current baseline verification results
- implemented the initial Phase 0 contract surface in `TorrentCore.Contracts`
- added host and torrent API endpoints backed by a thin in-memory application service
- updated the client and web shell to use the new Phase 0 boundary
- added API integration tests for host status, torrent listing, and magnet validation
- completed contract review decisions for v1 scope
- added tracker and connected-peer counts to torrent summary and detail contracts
- enabled Swagger UI for the service API in development
- added `LICENSE.md` using Apache License 2.0
- added `DISCLAIMER.md` covering warranty, liability, and lawful-use responsibility
- added Phase 1 service options for download and storage paths
- added fail-fast startup validation and directory initialization
- introduced an engine adapter registration boundary between the service layer and engine implementation
- changed invalid operation responses to predictable problem-details payloads
- added Phase 1 tests covering configuration validation and configured path behavior
- changed the default path strategy so downloads resolve to a user-facing location and internal storage resolves to a user app-data location
- changed the default download path again to avoid the user's `Downloads` folder as an unsafe cleanup target
- renamed the service project folder from `src/TorrentCore.Service` to `src/TorrentCore.ServiceHost` to avoid macOS Finder package semantics
- updated the Phase 2 plan to include persisted logging and activity diagnostics
- added the first SQLite-backed persistence slice for activity logging
- added a logs API endpoint and client support for reading recent logs
- wired service startup and torrent operations into the persisted activity log
- added log filtering, service-instance correlation, and max-entry retention controls
- replaced the in-memory torrent engine state with SQLite-backed torrent-state persistence
- added restart-persistence coverage for torrent state and duplicate detection across restarts
- added tracked SQLite schema migrations through a `schema_migrations` table
- moved schema creation responsibility out of the stores and into a dedicated migrator
- added startup recovery for persisted torrent state with explicit normalization rules for active runtime states
- exposed startup recovery status through the host-status contract
- added recovery activity-log events for normalized torrents and completed startup recovery
- added a managed fake runtime loop that resolves metadata, starts queued downloads, applies a single-active-download queue, and completes downloads over time
- added runtime configuration settings and validation for the managed fake runtime
- added engine-mode selection so the host can switch between the fake runtime and a real MonoTorrent-backed runtime
- added a first MonoTorrent-backed adapter with add/recover/pause/resume/remove and persisted state synchronization
- added host-status visibility for the active engine runtime
- added MonoTorrent runtime observability through persisted engine activity logs for state changes, peer discovery, and connection failures
- corrected persisted `DownloadedBytes` in MonoTorrent mode to be derived from synchronized torrent progress instead of session-only transfer counters
- added explicit MonoTorrent configuration for listen/DHT ports, port forwarding, local peer discovery, and repeated connection-failure log throttling
- exposed MonoTorrent runtime configuration through host status for diagnostics and future operator UI management
- added `engine.monotorrent.ready` startup logging with the active runtime configuration in the event details
- reduced repeated identical MonoTorrent connection-failure log noise by throttling after a configurable burst limit within a configurable time window
- corrected MonoTorrent recovery-path persistence so restarted torrents do not nest duplicate content directories and lose track of previously downloaded data
- added lightweight web refresh behavior so the home page keeps up with live torrent state changes without requiring a full browser reload

Assumptions:
- the source-of-truth boundary documents remain authoritative
- this document will be updated as implementation changes are made
- Phase 0 uses an in-memory service intentionally and does not yet imply persistence design
- v1 detail remains torrent-level only and does not yet include per-file DTOs
- magnet validation is intentionally limited to public-boundary checks, not engine-level parsing completeness
- sorting and filtering are UI/API behaviors for later work, not embedded DTO concerns
- the current engine implementation remains in-memory and is still a placeholder for later persistence and real engine work
- the current SQLite persistence implementation now covers both activity logging and fake-engine torrent state
- log timestamps continue to be stored in UTC, while future UI surfaces must render them in local time
- the current fake-engine behavior now persists torrent state in SQLite, but it is still not a real torrent runtime
- schema evolution is now tracked through explicit migration versions rather than ad hoc table creation
- persisted fake-engine recovery currently normalizes `ResolvingMetadata`, `Downloading`, and `Seeding` to `Queued` on startup and clears active transfer counters
- the current fake runtime now simulates metadata resolution and download progression using deterministic background processing and persisted snapshots
- the current MonoTorrent integration is the first real-engine slice and currently focuses on magnet add, host recovery, lifecycle commands, and persisted state synchronization without expanding the public DTOs
- MonoTorrent runtime events are now written to the persistent activity log under the `engine` category so operators can inspect real engine behavior through the existing logs API
- current MonoTorrent runtime settings are exposed through diagnostics now so a later web UI can manage them without reshaping the underlying service contract
- MonoTorrent restart recovery must preserve the engine save-path semantics instead of persisting a display-oriented content path, or completed downloads can regress after restart
- MonoTorrent's native partial-file suffix is `.!mt`, and downstream consumers may rely on that naming until a later UI-managed policy layer exists

Progress:
- planning completed
- repository baseline reviewed
- build and test baseline verified
- Phase 0 completed and verified by build and tests
- Phase 1 completed and verified by build and tests
- Phase 2 started with SQLite-backed logging persistence and API exposure

## Progress Log

### 2026-03-10

Completed:
- reviewed the initial project context documents
- reviewed the current scaffold in service, client, web, and tests
- validated that the solution builds and the initial tests pass
- documented the execution plan and testing approach
- defined the initial v1 torrent and host DTOs
- introduced torrent state and action contracts
- added service endpoints for host status, list, detail, add, pause, resume, and remove
- implemented an in-memory application service to exercise the contract boundary
- updated the web home page to display host status, add magnets, and list torrents
- added API integration tests and re-verified the solution with build and test
- completed contract review and folded the accepted decisions into the Phase 0 boundary
- added tracker and connected-peer counts to the torrent contract and list UI
- enabled Swagger/OpenAPI UI for the local service API
- added repository license and disclaimer documents
- added `TorrentCore` service configuration with download and storage path settings
- added startup validation and directory creation for configured service paths
- moved torrent state logic behind an engine adapter registration boundary
- changed invalid torrent operation failures to RFC 7807 problem-details responses
- added test coverage for options validation and configured service paths
- replaced project-relative runtime defaults with user-accessible and user-profile-based defaults
- changed the default managed-content location from `Downloads/TorrentCore` to `~/TorrentCore/downloads`
- renamed the service source folder to make it directly accessible through Finder without `.service` package confusion
- started Phase 2 with a persisted activity log table in SQLite
- added startup and torrent action activity logging
- added an API endpoint for recent logs and test coverage for log persistence behavior
- added filtered log queries by level, category, event type, torrent, time window, and service instance
- added service-instance correlation for startup and torrent activity events
- added configurable activity-log retention through `TorrentCore:MaxActivityLogEntries`
- added a persisted torrent-state table in SQLite
- replaced the in-memory engine adapter with a persistence-backed fake-engine adapter
- added tests for torrent-state survival across restart and duplicate detection across restart
- added a tracked SQLite migration runner and schema verification tests
- added verification that legacy activity-log schema is upgraded to the current shape
- added startup recovery state reporting in host status
- added restart-recovery logging and recovery normalization tests for persisted torrent state
- added managed fake-runtime processing and tests for automatic metadata resolution, queued download start, and completion
- added MonoTorrent package integration and test coverage for the real engine mode while preserving deterministic fake-mode API tests
- added MonoTorrent engine-event log coverage and strengthened real-engine synchronization fidelity
- added host-status exposure for MonoTorrent listen/DHT ports, port-forwarding, local peer discovery, and connection-failure log throttling settings
- added MonoTorrent engine-ready startup logging and deterministic test coverage for connection-failure log throttling
- fixed a MonoTorrent restart regression where persisted save paths could duplicate the torrent directory name and cause restarted torrents to redownload into nested paths
- added a manual and timed refresh path to the web home page so operator-visible torrent state does not stay stale after startup or runtime changes
- split persisted MonoTorrent path semantics so torrent rows now retain a recovery-oriented `download_root_path` separately from the display/content `save_path`
- broadened the documented product direction from a narrowly TVMaze-oriented downloader toward a more general-purpose torrent engine with reusable operator policies
- documented `.part`-based incomplete-file compatibility as a first-class requirement because downstream consumers already rely on that convention
- documented explicit seeding stop policies as part of the operator-facing runtime model
- enabled MonoTorrent native partial-file support so incomplete files use the engine's `.!mt` suffix and finished files lose that suffix automatically
- added configurable seeding stop policies covering unlimited seeding, immediate stop, ratio stop, time stop, and ratio-or-time stop
- persisted upload totals and seeding-start timestamps so seeding policies can survive restart instead of resetting on each process start
- surfaced partial-file and seeding-policy settings through host status for future UI management
- added a dedicated web logs page with filtering, local-time rendering, and refresh support so operators can inspect diagnostics without Swagger or direct SQLite access
- tightened the web logs page filter behavior so dropdown changes apply immediately, text filters use current typed values, and Enter/apply actions refresh deterministically
- fixed the web admin shell render mode so route pages are actually interactive in the browser instead of static server-rendered snapshots, which unblocked logs filtering and timed refresh behavior
- added persisted runtime settings in SQLite for live-editable seeding policy and engine connection-failure log throttling
- added host API endpoints and client support to retrieve and update effective runtime settings without editing appsettings by hand
- added a dedicated web settings page so operators can manage the supported live settings through the UI and have them survive restart
- fixed the torrent remove API so callers can omit the request body and still get the default safe behavior of removing the torrent record without deleting data
- fixed the real MonoTorrent remove path to stop an active manager before unregistering it so operator remove requests do not fail with a 500 while the torrent is still running
- added automatic completed-torrent cleanup policy as a TorrentCore-owned runtime setting, with the current mode set supporting timed removal from engine/DB tracking only
- enforced the product rule that automatic cleanup never deletes downloaded data; only an explicit remove API request with `DeleteData = true` is allowed to delete files
- added a background cleanup worker plus UI/runtime settings support so completed torrents can age out of the dashboard automatically without destructive deletion
- added dashboard-level torrent controls for pause, resume, remove, and delete-data actions so common operator lifecycle management no longer requires Swagger
- added basic dashboard filtering by name/status and client-side sorting by name, state, progress, and newest-added order
- fixed delete-data removal so TorrentCore now prunes empty torrent-specific directories left behind after MonoTorrent deletes files, while preserving the configured download root and any non-empty/shared directories
- tightened the dashboard so `Delete Data` is only offered for in-progress/error torrent states and not for completed or seeding torrents
- documented the intended future concurrency model for burst intake: TorrentCore should accept/persist new magnets immediately and queue metadata/download execution behind global runtime limits
- added live-editable runtime settings for `MaxActiveMetadataResolutions` and `MaxActiveDownloads`, with host-status and web-settings visibility of the effective queue/concurrency caps
- updated both the fake runtime and MonoTorrent runtime to treat those caps as execution limits instead of admission limits, so excess torrents wait in queue until slots open
- fixed the MonoTorrent remove path again so explicit remove requests are atomic against the scheduler and cannot be re-started mid-remove by a background synchronization tick
- added startup-configured MonoTorrent network throttling controls for overall connections, half-open connections, and global upload/download rate caps
- surfaced those active MonoTorrent throttle values through host status and the dashboard so operators can verify the current engine saturation limits
- added validation and API coverage for the new engine throttle settings
- fixed a SQLite torrent-store persistence bug where stale background updates could recreate a torrent row after delete/remove because updates were using `INSERT OR REPLACE` semantics instead of true update-only semantics
- added a regression test proving that delete followed by a stale later update does not recreate the torrent row
- fixed a MonoTorrent resume regression where a paused torrent could remain stuck in the paused state under the new scheduler instead of re-entering queue/download processing
- added a real-engine regression test proving pause/resume leaves the paused state
- extended runtime settings and the web settings page so MonoTorrent network throttle values can now be edited as persisted desired settings with explicit restart-required semantics
- separated desired engine throttle settings from currently applied engine throttle settings so the UI can show pending restart requirements instead of implying a live engine change that has not happened yet
- added startup initialization for applied engine settings so restart-required state clears correctly after a service restart in both fake and MonoTorrent modes
- added an operator settings reference document explaining queue limits, connection limits, half-open connections, and global upload/download rate settings in plain language

In progress:
- Phase 2 persistence foundation beyond activity logging

Next:
- add the next operator control slice for global engine throttling: rate caps, global peer/connection limits, and burst-friendly saturation control
- move the new MonoTorrent network throttling controls from startup config into an operator-managed experience with clear restart/apply semantics
- continue expanding MonoTorrent diagnostics so operators can understand why torrents are queued, resolving, downloading, seeding, or blocked
- continue building the operator-facing path so more current MonoTorrent configuration can move from config files into the web UI
- extend the UI from basic settings and actions into richer operator diagnostics and controls
- add Intel Mac deployment packaging and `zsh` operational scripts for service/web publish, deploy, start, stop, and restart
- add a proper torrent detail page with deeper diagnostics now that per-torrent wait reason and queue context are in the public contract
