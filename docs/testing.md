# Testing

## Standard Commands

```bash
dotnet build TorrentCore.sln
dotnet test TorrentCore.sln
```

## Test Scope

The current suite focuses on service, persistence, engine-adapter, and client-boundary behavior.

Representative coverage areas:

- API behavior
- SQLite schema migration and persistence
- torrent state persistence and restart recovery
- history-store and history-service behavior
- callback finalization and callback lifecycle processing
- category routing
- seeding policy
- metadata recovery and connection policy behavior
- data-path cleanup
- client options and path defaults

## Testing Rules

- use real SQLite-backed tests for persistence behavior
- keep deterministic coverage for engine-adapter and runtime-policy logic where possible
- preserve regression coverage around callback timing, restart recovery, and history updates
- validate implementation at the relevant layer instead of relying on one large end-to-end path

## Operational Verification

Normal documentation cleanup work should at least re-run:

```bash
dotnet build TorrentCore.sln
dotnet test TorrentCore.sln
```

If a docs-only change ever coincides with config or script changes, verify those command surfaces separately.
