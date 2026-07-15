# Development

## Repo Layout

- `src/TorrentCore.Contracts`: DTOs and API contracts
- `src/TorrentCore.Client`: client library
- `src/TorrentCore.Core`: service-facing core abstractions
- `src/TorrentCore.Persistence.Sqlite`: SQLite persistence and migrations
- `src/TorrentCore.ServiceHost`: service host and runtime orchestration
- `src/TorrentCore.WebUI`: supported operator UI
- `tests/TorrentCore.Service.Tests`: automated test suite
- `Scripts/`: deployment and launch-agent scripts

## Local Workflow

Normal verification commands:

```bash
dotnet build TorrentCore.sln
dotnet test TorrentCore.sln
```

The service exposes Swagger only in development:

- `https://localhost:7033/swagger`

## Runtime Configuration Model

`TorrentCore.Service` binds configuration from the `TorrentCore` section and validates it on startup.

Important groups:

- engine mode, ports, encryption, and connection limits
- queue and concurrency limits
- storage and download roots
- seeding and cleanup policy
- metadata recovery windows
- completion callback settings
- activity-log retention

Live settings currently include:

- queue concurrency
- seeding policy
- completed-torrent cleanup policy
- connection-failure log throttling
- callback settings
- category settings

Restart-required engine settings currently include:

- engine max connections
- engine max half-open connections
- engine max download rate
- engine max upload rate
- engine encryption mode

The WebUI shows both saved values and currently applied engine values so restart-required changes are visible to operators.

## Logging And Diagnostics

TorrentCore uses a project-owned persistent logging path.

Rules:

- do not couple logging to TVMaze infrastructure
- keep service-level diagnostics in TorrentCore storage
- use runtime and activity-log behavior already implemented in the service
- connection-failure warnings are throttled to avoid log floods
- completed-torrent log pruning, when enabled, applies only to torrent-scoped activity-log rows and never to payload data

Duration diagnostics:

- `runtime.operation.slow` identifies slow synchronization, gate-wait, MonoTorrent, callback, and storage phases
- `runtime.tick.duration_summary` records minute-scale synchronization timing baselines without logging every tick
- `runtime.recovery.action_completed` records each automatic recovery action, attempt number, duration, outcome,
  recovery cycle, bounded backoff timing, and long-cold cadence state
- `runtime.callback.dispatch_completed` records callback process-launch duration independently from callback feedback state
- `runtime.connection.activity_summary` aggregates per-torrent peer and connection churn over one-minute windows
- individual peer discovery, connection, and disconnection events are aggregated instead of persisted separately
- `runtime.monotorrent.cache_audit` inventories metadata, fast-resume, unmatched, and aged cache files at startup without deleting them
- slow storage diagnostics distinguish snapshot reads, projection, state writes, and history writes
- `torrent_finalization_visibility_probe` measures slow background filesystem visibility checks independently from engine synchronization
- slow-operation details include subsystem, operation, duration, threshold, outcome, and torrent context when available
- runtime diagnostics store torrent context in event details rather than the torrent-scoped log column so completed-torrent log pruning preserves them

## WebUI Service Connection State

`TorrentCore.WebUI` uses a fallback service base URL from configuration and can persist a tested override at runtime.

Rules:

- the persisted endpoint is host-global for that WebUI host
- the UI tests `/api/health` before saving a new endpoint
- the saved endpoint is runtime state and should not be tracked in git
- source-controlled defaults should remain environment-neutral

## Scripts Boundary

- deploy scripts run from repo machines
- runtime control scripts run on the actual TorrentCore host
- `--restart` is only valid when the deploy command runs on the same host as the runtime

See [docs/deployment.md](deployment.md) for the full runtime and deployment model.
