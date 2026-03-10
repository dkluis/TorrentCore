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
- persistence and real engine integration are not implemented yet

Verified baseline:
- `dotnet build TorrentCore.sln`
- `dotnet test TorrentCore.sln`

Development API documentation:
- `https://localhost:7033/swagger`

Current service configuration section:
- `TorrentCore:DownloadRootPath`
- `TorrentCore:StorageRootPath`
- if not overridden, the service now defaults downloads to a dedicated `~/TorrentCore/downloads` folder and internal storage to the user's local app-data area
- project-relative runtime folders were replaced as defaults because they are not appropriate for normal operator use

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
- establish the SQLite storage model and migration path

Planned outputs:
- persistence entities/schema
- migration strategy
- repository or persistence service abstractions as needed
- torrent state rehydration rules after restart

Exit criteria:
- state survives restart
- schema creation and upgrades are deterministic
- persistence tests run against real SQLite files

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

Requirement:
- use real SQLite-backed tests, not only in-memory substitutes

### Engine Adapter Tests

Cover:
- fake adapter behavior for deterministic vertical-slice tests
- MonoTorrent adapter behavior for real engine integration

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

## Phase 0 Contract Review Decisions

Reviewed and accepted on March 10, 2026:
- v1 remains torrent-level only
- per-file torrent detail is deferred to a later version
- labels and categories are deferred from v1
- remove remains split between remove-only and remove-with-data
- queue position is deferred from v1
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

## Current Next Steps

1. Begin Phase 2 persistence foundation work.
2. Define the SQLite storage model and migration strategy.
3. Replace the in-memory engine adapter with a persistence-backed fake-engine slice.
4. Expand API and UI coverage around torrent detail and action flows.
5. Prepare restart rehydration rules before real engine integration.

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

Assumptions:
- the source-of-truth boundary documents remain authoritative
- this document will be updated as implementation changes are made
- Phase 0 uses an in-memory service intentionally and does not yet imply persistence design
- v1 detail remains torrent-level only and does not yet include per-file DTOs
- magnet validation is intentionally limited to public-boundary checks, not engine-level parsing completeness
- sorting and filtering are UI/API behaviors for later work, not embedded DTO concerns
- the current engine implementation remains in-memory and is still a placeholder for later persistence and real engine work

Progress:
- planning completed
- repository baseline reviewed
- build and test baseline verified
- Phase 0 completed and verified by build and tests
- Phase 1 completed and verified by build and tests

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

In progress:
- none

Next:
- Phase 2 persistence foundation
