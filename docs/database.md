# Database

## Persistence Model

TorrentCore uses a host-local SQLite database managed by `TorrentCore.Persistence.Sqlite`.

Schema changes are applied through tracked migrations in `SqliteSchemaMigrator`.

Runtime database connections use WAL journal mode, `synchronous=NORMAL`, and a bounded busy timeout. Torrent state,
history, category, and settings stores share one coordinated writer gate per database file. Activity logs deliberately
write outside that gate so diagnostic traffic cannot queue ahead of lifecycle persistence. Log retention runs at startup
and before log reads rather than after every insert.

Current core tables:

- `schema_migrations`
- `activity_logs`
- `torrents`
- `runtime_settings`
- `torrent_categories`
- `torrent_history`

## Table Responsibilities

### `activity_logs`

- persistent service-owned diagnostics
- torrent-scoped and service-scoped entries
- indexed by occurred time and torrent id
- manual date cleanup uses an exclusive Service-local midnight cutoff
- date cleanup removes old service-level rows and old torrent rows only when the torrent id is absent from `torrents`

### `torrents`

- live runtime state
- restart and recovery persistence
- persisted continuous-cold timestamp used by long-running download abandonment
- persisted metadata-attempt start and last-yield timestamps used for fair unresolved-magnet rotation
- durable ordinary queue order, optional priority order, remaining protected priority-metadata attempts, and Hold
  intent for restart-safe operator queue controls
- durable payload no-progress start, most recent automatic download-yield time, and current download-yield queue class
  for restart-safe stale-download rotation
- current category routing and callback state for active torrents

### `runtime_settings`

- persisted host-local runtime settings stored as additive key/value rows
- VPN egress settings use this existing table, including the CIDR list serialized as JSON; adding them requires no
  schema migration and leaves an existing database on validation-disabled defaults until an operator saves or enables
  the policy

### `torrent_categories`

- stable category definitions
- operator-managed category labels, enablement, callback behavior, and download roots

### `torrent_history`

- one durable summary row per torrent lifecycle
- retained after the active torrent is removed
- not derived from activity logs
- not a replacement for the live `torrents` table

## History Rules

- `torrents` remains dedicated to live state and restart persistence
- torrent history uses a separate table
- history rows are inserted once and updated in place
- manual date cleanup uses `last_updated_at_utc` and an exclusive Service-local midnight cutoff
- history cleanup never deletes a row whose torrent id remains in the live `torrents` table
- metadata resolution is recorded only after payload size becomes available; an info hash or a temporary queued state is not sufficient evidence
- download completion is recorded only from a completed or seeding lifecycle state, not from a transient 100-percent progress value
- migration 14 repairs an impossible stored completion that predates download start when a later valid seeding timestamp is available
- migration 15 clears premature metadata-resolution timestamps for live magnets that are still unresolved and have no download milestones
- migration 16 adds the live download cold timestamp used to preserve recovery and abandonment timing across service restarts
- migration 17 adds and backfills the structured history `removal_kind` used for reliable outcome filtering
- migration 18 adds nullable metadata-resolution attempt-start and last-yield timestamps to the live `torrents` table
- migration 19 adds the nullable live-torrent seeding-policy application timestamp used to keep policy events
  idempotent across synchronization ticks and service restarts
- migration 20 adds ordinary queue order, optional priority queue order, and Hold state to live torrents. Existing rows
  are backfilled by `AddedAtUtc` and torrent id; later tail allocations are serialized SQLite writes rather than
  timestamp-derived positions. Priority and Hold are mutually exclusive, and Paused rows retain neither intent.
- migration 21 adds the nullable remaining protected priority-metadata-attempt count. Existing priority rows receive
  the default allowance of three; nonpriority rows remain null.
- migration 22 adds nullable download no-progress and last-yield timestamps plus the non-null download-yielded queue
  marker. Existing rows receive null timestamps and a false marker, so an active download receives a fresh full
  interval from its first post-upgrade active observation without changing queue intent or cold-recovery history.
- `download_no_progress_started_at_utc` is present only for an active or recovery-suspended download attempt. Durable
  completed-piece growth restarts it; automatic yield and ordinary queue intent clear it.
- `is_download_yielded` identifies the current automatic-retry queue class, while
  `download_last_yielded_at_utc` retains historical yield time and orders retries. Successful readmission clears the
  marker without discarding the historical timestamp. Stop/persistence failure never writes a false yielded marker or
  admits replacement work ahead of the durable update.

Important history fields include:

- identity and routing
- latest operator-visible state and metrics
- lifecycle timestamps
- callback state
- removal outcome, including a structured removal kind independent of the operator-facing reason text

Extracted durable rules are summarized in [docs/decisions/current-decisions.md](decisions/current-decisions.md).

## Migration Rules

- preserve existing data during migration
- prefer additive schema changes when possible
- keep migration history explicit and tracked
- validate migrations through automated tests against real SQLite files

## Current Conflict Rule

If documentation conflicts with current code, preserve the current implementation.

This matters most for:

- callback lifecycle fields
- category routing persistence
- history row shape
- protected log and history cleanup
- runtime-setting storage semantics
