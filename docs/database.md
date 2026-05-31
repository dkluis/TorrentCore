# Database

## Persistence Model

TorrentCore uses a host-local SQLite database managed by `TorrentCore.Persistence.Sqlite`.

Schema changes are applied through tracked migrations in `SqliteSchemaMigrator`.

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

### `torrents`

- live runtime state
- restart and recovery persistence
- current category routing and callback state for active torrents

### `runtime_settings`

- persisted host-local runtime settings

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
- history rows are not deleted by the current implementation
- future retention policy is separate from the current design

Important history fields include:

- identity and routing
- latest operator-visible state and metrics
- lifecycle timestamps
- callback state
- removal outcome

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
- runtime-setting storage semantics
