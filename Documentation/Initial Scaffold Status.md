# Initial Scaffold Status

## Created In This Chat

- new git repo: `TorrentCore`
- solution: `TorrentCore.sln`
- projects:
  - `src/TorrentCore.Contracts`
  - `src/TorrentCore.Core`
  - `src/TorrentCore.Persistence.Sqlite`
  - `src/TorrentCore.Client`
  - `src/TorrentCore.Service`
  - `src/TorrentCore.Web`
  - `tests/TorrentCore.Service.Tests`

## Current Technical Baseline

- central build settings through `Directory.Build.props`
- minimal health contract in `TorrentCore.Contracts`
- minimal HTTP client in `TorrentCore.Client`
- minimal health controller in `TorrentCore.Service`
- minimal Blazor admin shell in `TorrentCore.Web`
- project references wired through the solution

## Intentional Decisions

- separate repo from TVMaze
- Web is the first rich management UI
- Avalonia is deferred, not rejected
- TVMaze remains a lightweight integration client only

## What Has Not Been Built Yet

- MonoTorrent integration
- real torrent contracts
- SQLite schema
- auth/security model
- logging implementation
- advanced web admin UI
- TVMaze integration work in the TVMaze repo

## Recommended First Tasks

1. Define the v1 API contracts:
   - `AddMagnetRequest`
   - `TorrentSummaryDto`
   - `TorrentDetailDto`
   - `ServiceErrorDto`
   - `EngineHostStatusDto`
2. Add service configuration and startup validation.
3. Add a thin service layer instead of putting behavior in controllers.
4. Add SQLite persistence models and a migration strategy.
5. Build the add-magnet and list-torrents vertical slice.
