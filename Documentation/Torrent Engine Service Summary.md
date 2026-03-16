# Torrent Engine Service Project Summary

## Purpose

Build a **torrent engine service** that you control, using **C# 14 / .NET 10**, with the existing **Avalonia macOS UI** expanded to manage it.

This is **not** a full traditional desktop torrent client design where the UI directly hosts the engine.  
The preferred architecture is:

- **separate engine service process**
- **existing Avalonia UI as a controller/management console**
- ability to manage **multiple engine hosts**
- **magnet links only**
- **download-focused behavior**
- strong separation between UI, service API, and engine internals

---

## Current Constraints and Preferences

- Existing **Avalonia macOS UI** already exists and can be extended
- UI should manage torrent engines running on **multiple systems**
- A **two-process architecture** is preferred and matches existing app patterns
- Only interested in:
  - **magnet links**
  - **downloading**
  - no emphasis on upload/seeding workflows
- Wants a system that is **owned and controlled**
- Existing experience with **Transmission RPC**, but this new system should use an engine the user controls more directly
- Candidate engine library: **MonoTorrent**
- Target stack:
  - **C# 14**
  - **.NET 10**
  - **Avalonia UI**
  - likely **SQLite** for persistence on each engine host

---

## Recommended Architecture

## High-Level Shape

### UI Application
Use the existing Avalonia macOS UI as a:

- host manager
- torrent dashboard
- command surface
- monitoring console

The UI should **not** directly depend on MonoTorrent types.

### Engine Service
Build a standalone **Torrent Engine Service** that:

- hosts MonoTorrent
- owns torrent session lifecycle
- owns queueing and policy decisions
- persists state locally on the engine host
- exposes a public API for the UI

### Shared Contracts Layer
Create a contracts project containing:

- DTOs
- enums
- request/response models
- event payloads
- API-facing state models

### Persistence
Each engine host should persist its own:

- torrent session state
- resume/checkpoint data
- host configuration
- tags/categories if owned by the service
- logs/history

The UI should persist only:

- host connection profiles
- UI preferences/layout/filtering
- cached summaries if useful

---

## Key Architectural Decision Changes Based on Updated Requirements

Originally, a local embedded client would have been reasonable.  
That changed because of the clarified requirements:

- existing Avalonia UI already exists
- engine should be a separate process
- multiple engine hosts are expected
- UI should manage local and remote hosts
- magnet-only flow simplifies add UX
- policy and orchestration belong in the service, not in the UI

Because of that, the correct product shape is closer to:

- **torrent engine daemon/service**
- plus
- **Avalonia management UI**

not a monolithic desktop torrent client.

---

## Recommended Technical Approach

## 1. Engine Service as a Standalone Process

Build a .NET service process such as:

- `TorrentEngine.Service`

Responsibilities:

- MonoTorrent integration
- torrent lifecycle management
- metadata resolution for magnets
- queue enforcement
- download path management
- health monitoring
- persistence
- logging
- remote API

This service should continue running independently of the UI.

---

## 2. API Boundary Between UI and Service

The UI should only talk to the service through a stable API.

Do **not** expose MonoTorrent types directly over the wire.

Define service contracts like:

- `TorrentDto`
- `TorrentDetailDto`
- `EngineHostStatusDto`
- `AddMagnetRequest`
- `QueuePolicyDto`
- `ServiceErrorDto`

This allows:

- a stable UI contract
- easier versioning
- testability
- possible engine replacement later
- isolation from MonoTorrent-specific model churn

---

## 3. Transport Choice

Recommended transport:

- **REST API** for commands and queries
- **WebSocket or SignalR** for event streaming/live updates

Reasoning:

- easier debugging than gRPC for this use case
- simpler tooling
- works well with Avalonia
- sufficient performance for a 20-torrent target
- easier future expansion to other admin clients

---

## 4. Multi-Host Model

The UI should treat each engine instance as a separate **host/node**.

Suggested model:

- `EngineHost`
- `ConnectionProfile`
- `HostHealth`
- `HostCapabilities`
- `TorrentSummary` scoped to a host

Recommended first version behavior:

- UI can store multiple hosts
- UI actively manages **one selected host at a time**
- optional aggregate/all-host view can come later

This keeps the first version simpler.

---

## Core Functional Scope

## v1 Scope Recommendation

Keep v1 narrow and useful.

Recommended v1:

- one engine service on one machine
- one UI host connection
- add magnet
- resolve metadata
- list torrents
- show progress/speed/state
- pause
- resume
- remove
- auto-stop on completion
- survive restart
- basic logs and errors

Recommended v2:

- multiple hosts
- host switching improvements
- richer queue rules
- file priorities
- notifications
- advanced diagnostics
- per-host dashboards

---

## Magnet-Only Implications

Because the design is **magnet only**:

### Simplified
- no `.torrent` file import flow
- single add workflow
- less UI complexity

### New Requirements
- metadata acquisition is a first-class concern
- before metadata arrives, file list/details may be unavailable
- queue logic may need to wait until metadata is known
- UI needs to represent metadata resolution clearly

Suggested states:

- `AddedMagnet`
- `ResolvingMetadata`
- `MetadataReady`
- `MetadataFailed`
- `Queued`
- `Downloading`
- `Paused`
- `Completed`
- `Error`

This state model should be explicitly designed.

---

## Download-Focused Behavior

The requirement is effectively **download-first**, not general torrent seeding behavior.

Important note:
BitTorrent downloading usually still involves some upload while downloading.  
So the practical product behavior should be:

- allow protocol-required upload while downloading
- **auto-stop immediately on completion**
- do not intentionally continue seeding after completion

Recommended default policy:

- stop torrent automatically at 100%
- no ratio-based seeding target in v1
- no explicit upload-focused controls in the UI initially

---

## Queueing and Policy Decisions

Queueing must live in the **engine service**, not in the UI.

The UI can configure policies, but the service enforces them.

Examples of service-owned policy:

- max active downloads
- max simultaneous metadata resolutions
- stalled detection
- low disk handling
- retry policy
- auto-stop on completion
- per-host limits

If queue logic lives in the UI, the system becomes fragile whenever the UI is closed.

---

## Persistence Strategy

## On Each Engine Host
Persist locally:

- active torrents
- magnet metadata state
- resume/checkpoint state
- service settings
- download paths
- optional tags/categories
- operation history
- logs

Suggested storage:

- **SQLite** for app/service metadata
- service-owned state files where needed
- structured logs

## In the UI
Persist only:

- host connection definitions
- tokens/credentials references
- UI state and preferences
- last selected host
- filter/sort/layout preferences

The UI should **not** be the source of truth for engine state.

---

## File System and Path Handling

Because this is multi-host capable, paths must be treated as **remote host data**.

Rules:

- the UI must not assume remote paths are locally accessible
- “open folder” should only work when the path is actually local and available
- move/rename operations should be executed by the service
- the service should own path validation and disk checks

This should be designed up front to avoid remote-path confusion in the UI.

---

## Security and Connectivity

Because the service may run on multiple machines, security matters from day one.

Recommended initial model:

- per-host API token
- token stored securely by the UI, ideally through platform-secure storage
- initially intended for trusted LAN / private network / Tailscale-style use
- TLS can be added later if needed
- do not leave the API unauthenticated

This should not be designed as a public internet-facing API initially.

---

## Hosting Model on macOS

Preferred service style:

- background service / daemon-like process
- runs without the UI being open
- user-level startup behavior is likely sufficient

Potential options:

- launch at login
- LaunchAgent-style background run
- manual service start for development

Key requirement:
the service must remain independently recoverable and operable without the UI.

---

## Service API Design Guidance

Design the API before implementing too much engine logic.

### Suggested Command Endpoints
- add magnet
- pause torrent
- resume torrent
- remove torrent
- move data
- force recheck
- set category/tag
- set download path
- configure host limits/policies

### Suggested Query Endpoints
- host status
- torrent list
- torrent detail
- tracker status
- file list
- transfer stats
- storage/disk status
- service settings

### Suggested Event Stream Topics
- torrent added
- metadata resolved
- metadata failed
- progress changed
- state changed
- completion reached
- error occurred
- host health changed

The UI should use:
- event stream for real-time changes
- periodic refresh for reconciliation

Do not rely on polling only.

---

## Engine Boundary Guidance

Even though MonoTorrent is the likely engine, the service should define its own internal abstraction.

Recommended internal interface categories:

- session management
- torrent lifecycle
- metadata resolution
- file selection/priorities
- rate limits
- health snapshots
- persistence coordination
- queue policy enforcement

MonoTorrent should remain isolated inside the engine/service implementation.

---

## Risks and Things to Watch

Main risks are not whether C#/.NET can do this.  
The main risks are:

- scope creep
- queue/state complexity
- metadata resolution edge cases
- remote host/path assumptions leaking into the UI
- weak contract separation between UI and service
- crash-safe resume/state persistence
- overbuilding multi-host aggregation too early

The biggest implementation discipline point:

**do not let MonoTorrent internals leak into the UI model or service contract model.**

---

## Suggested Project Structure

Possible solution layout:

- `TorrentEngine.Contracts`
- `TorrentEngine.Service`
- `TorrentEngine.Core`
- `TorrentEngine.Infrastructure`
- `TorrentEngine.MonoTorrentAdapter`
- `TorrentEngine.Service.Tests`
- existing Avalonia UI project extended to consume the service

### Notes
- `Contracts` = DTOs, enums, API payloads
- `Core` = domain/state machine/policies/interfaces
- `Infrastructure` = SQLite, filesystem, config, auth, logging
- `MonoTorrentAdapter` = concrete engine integration
- `Service` = ASP.NET Core host + API + orchestration

---

## First Milestone Recommendation

A very good first milestone is:

- `TorrentEngine.Service`
- `TorrentEngine.Contracts`
- health endpoint
- add-magnet endpoint
- torrent-list endpoint
- event stream endpoint
- existing Avalonia UI consumes those endpoints

Once that works, the project has a real backbone.

---

## Recommended Immediate Design Artifacts Before Major Coding

Before building too much, create these 4 documents/artifacts:

1. **Service API contract**
2. **Torrent state machine**
3. **Engine host persistence model**
4. **UI host/connection model**

These are more important right now than visual UI design.

---

## Practical v1 Build Sequence

1. Define contracts
2. Define torrent lifecycle state machine
3. Build service host skeleton
4. Implement health/status endpoint
5. Implement add-magnet flow
6. Implement metadata resolution handling
7. Implement torrent list/detail queries
8. Implement pause/resume/remove
9. Add persistence and restart recovery
10. Connect existing Avalonia UI
11. Add event streaming
12. Add logging and diagnostics

---

## Bottom-Line Recommendation

Given the clarified requirements, the best architecture is:

- **existing Avalonia macOS UI as the controller**
- **separate .NET 10 / C# 14 torrent engine service**
- **MonoTorrent behind a private adapter**
- **REST + WebSocket/SignalR API**
- **magnet-only workflow**
- **download-first with auto-stop on completion**
- **per-host persistence**
- **multi-host capable design, but one selected host at a time in v1**

This is a strong, controllable, and extensible architecture that matches the user’s existing application patterns.
