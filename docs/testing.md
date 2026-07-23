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
- normalized OpenAPI contract generation for native clients

## Native Apple Contract

The committed Apple contract is
`clients/apple/Packages/TorrentCoreKit/Sources/TorrentCoreAPI/openapi.json`. The normal .NET suite compares it with a
normalized document produced by an in-process Development test host.

After an intentional public service-contract change, regenerate it with:

```bash
TORRENTCORE_UPDATE_OPENAPI=1 dotnet test \
  tests/TorrentCore.Service.Tests/TorrentCore.Service.Tests.csproj \
  --filter FullyQualifiedName~OpenApiContractTests
```

Then run the normal .NET suite and the Swift package tests. Live Apple integration remains opt-in through
`TORRENTCORE_INTEGRATION_BASE_URL` and is read-only unless the operator explicitly approves a mutation.

Milestone 2 shared-state tests cover device-local profile persistence, URL normalization, active profile isolation,
client-wide refresh preferences, open-context request routing, foreground/background behavior, last-known stale state,
single-item mutation refresh, and rejection of late responses from a previous profile.

Production Swagger availability is verified by `OpenApiContractTests` alongside normalized contract comparison.

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
