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
- Phase 0 contract work has started
- service, client, and web now expose a stubbed torrent-management boundary backed by an in-memory application service
- persistence and real engine integration are not implemented yet

Verified baseline:
- `dotnet build TorrentCore.sln`
- `dotnet test TorrentCore.sln`

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

## Risks and Watch Items

- leaking engine-specific concepts into contracts too early
- defining persistence around implementation detail instead of domain state
- skipping restart and recovery testing until late in the project
- letting the web UI outrun the contract boundary
- adding TVMaze integration before TorrentCore behavior is stable

## Current Next Steps

1. Review the Phase 0 contract shape and confirm no additional v1 fields are required before freezing it further.
2. Begin Phase 1 startup options and validation work.
3. Define the SQLite persistence model and migration strategy for Phase 2.
4. Replace the in-memory application service with a persistence-backed fake-engine slice.
5. Expand API and UI coverage around torrent detail and action flows.

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

Assumptions:
- the source-of-truth boundary documents remain authoritative
- this document will be updated as implementation changes are made
- Phase 0 uses an in-memory service intentionally and does not yet imply persistence design
- v1 detail remains torrent-level only and does not yet include per-file DTOs
- magnet validation is intentionally limited to public-boundary checks, not engine-level parsing completeness

Progress:
- planning completed
- repository baseline reviewed
- build and test baseline verified
- initial Phase 0 implementation completed and verified by build and tests

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

In progress:
- awaiting review of the Phase 0 contract shape before moving deeper into startup validation and persistence

Next:
- Phase 1 configuration and startup validation
