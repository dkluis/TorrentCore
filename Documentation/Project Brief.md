# TorrentCore Project Brief

## Purpose

TorrentCore is a standalone torrent engine product built with C# 14 and .NET 10.

It is intentionally separate from TVMaze.

TorrentCore owns:
- torrent engine runtime
- queueing and policy
- persistence
- management API
- dedicated management UI
- category definitions and category-aware download routing
- file finalization behavior for incomplete vs completed content
- seeding policy and stop conditions

Queueing rule:
- TorrentCore should accept and persist incoming magnet requests even when runtime capacity is full.
- Active-resolution and active-download limits control concurrency, not admission.
- Torrents that cannot start immediately because capacity is full should wait in TorrentCore-managed queues until slots open.
- The first concurrency controls are global per host: active metadata resolutions and active downloads.
- These limits should be operator-managed through TorrentCore rather than pushed down into TVMaze.

TVMaze does not own TorrentCore internals. TVMaze is only one client of TorrentCore.

## Why It Is Separate From TVMaze

The boundary decision from the TVMaze discussion was:
- keep TorrentCore in a separate repo
- keep TorrentCore independently deployable
- allow TorrentCore to evolve on its own release cadence
- avoid coupling TorrentCore to TVMaze DB entities, contracts, or service internals

This repo should reuse architectural practices from TVMaze, not TVMaze runtime dependencies.

## Source Context That Led To This Repo

Original source documents were in the TVMaze repo:
- `/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TVMaze/Documentation/Torrent Engine Service Summary.md`
- `/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TVMaze/Documentation/Handoff Avalonia Chat.md`
- `/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TVMaze/Documentation/Torrent Engine New Chat Starter.md`

Important context carried forward:
- separate engine service process
- existing TVMaze Avalonia app should stay a lightweight integration surface
- multi-host management is expected
- magnet links only for v1
- download-focused behavior that can expand into more general torrent-engine operation
- strong API boundary between UI and engine internals

## Product Shape

Primary components:
- `TorrentCore.Service`
- `TorrentCore.Web`
- `TorrentCore.Contracts`
- `TorrentCore.Client`
- `TorrentCore.Core`
- `TorrentCore.Persistence.Sqlite`

Planned later if needed:
- `TorrentCore.Avalonia`

## First UI Decision

The first rich management UI is `TorrentCore.Web`.

Reasoning:
- better fit for remote multi-host administration
- lower friction for initial admin tooling
- avoids building two full management clients at once

Avalonia remains a valid later addition if a native desktop operator app is still desired.

## Reuse From TVMaze

Reuse these practices:
- contract-first boundary between clients and service
- fail-fast startup validation
- per-host service mindset
- pragmatic controller/service layering
- concise DTO-focused API surface
- no `ILogger` if a project-specific persistent logging path is introduced later

Do not directly reuse these TVMaze runtime pieces:
- `TvmazeApiComplete`
- `Tvmaze.Contracts`
- `Entities_Lib`
- `DB_Lib_EF`
- TVMaze MariaDB schema
- TV-specific domain entities

## Ownership Boundary

TorrentCore owns:
- engine lifecycle
- metadata resolution for magnets
- torrent state
- queue policy
- admission and concurrency control for bursty incoming magnet submissions
- download paths
- category routing and callback integration
- incomplete-file handling such as `.part` suffix behavior
- completion and seeding policy
- host capabilities
- logs and diagnostics
- persistence schema
- host authentication and authorization

TVMaze may do only lightweight interaction:
- choose an engine host
- add a magnet
- view torrent summaries relevant to a TVMaze workflow
- pause, resume, remove
- surface completion or failure state
- rely on completed files no longer carrying the configured incomplete suffix
- deep-link to the dedicated TorrentCore UI for advanced work

## v1 Scope

Keep v1 narrow:
- one service host
- one web admin UI
- add magnet
- resolve metadata
- list torrents
- inspect torrent detail
- pause, resume, remove
- simple queueing rules
- accept-now, run-when-capacity-allows queue behavior for bursts of incoming magnets
- configurable incomplete-file finalization behavior with `.part` compatibility
- configurable seeding stop policy
- category-aware torrent routing for `TV`, `Movie`, `Audiobook`, and `Music`
- shared callback invocation compatibility with the existing TVMaze completion callback app
- survive restart
- host-local SQLite persistence
- basic health and diagnostics

## Non-Goals For v1

- building both rich Web and Avalonia admin apps immediately
- TVMaze-specific workflows embedded in the engine
- direct MonoTorrent types crossing the API boundary
- premature plugin/config abstraction

## Logging

This repo should not assume TVMaze logging infrastructure exists.

If persistent logging is added:
- use a TorrentCore-owned logging service
- keep logs in TorrentCore storage
- do not couple to TVMaze `Logs` table or DB entity stack

## Current Scaffold Intent

The initial scaffold is a boundary-first starter:
- minimal service API
- minimal client library
- minimal web admin shell
- documentation for continuation in a new Rider chat

## Immediate Next Milestones

1. Replace placeholder health-only flow with engine host info and service capabilities.
2. Define v1 torrent DTOs and request/response contracts.
3. Add service configuration and startup validation.
4. Add SQLite persistence shape.
5. Add first magnet workflow end to end.
6. Add explicit incomplete-file and seeding-policy behavior rather than relying on engine defaults.
7. Add operator-managed global concurrency controls for metadata-resolution and download queue execution.
8. Add Intel Mac deployment packaging and operational scripts for service/web start, stop, restart, and publish/copy deployment.
9. Add category-aware routing and shared completion-callback compatibility without coupling TorrentCore to TVMaze paths or engine internals.
