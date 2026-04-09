# Initial Scaffold Status

## Status

This is a historical scaffold snapshot from the first repo-construction chat.

- The supported operator client is now `TorrentCore.WebUI`.
- `TorrentCore.Web` and `TorrentCore.Avalonia` are legacy/reference-only and are no longer receiving feature updates or support maintenance.
- Treat the project list and decisions below as the initial baseline, not the current delivery target.

## Created In This Chat

- new git repo: `TorrentCore`
- solution: `TorrentCore.sln`
- projects:
  - `src/TorrentCore.Contracts`
  - `src/TorrentCore.Core`
  - `src/TorrentCore.Persistence.Sqlite`
  - `src/TorrentCore.Client`
  - `src/TorrentCore.ServiceHost`
  - `src/TorrentCore.Web`
  - `tests/TorrentCore.Service.Tests`

## Current Technical Baseline

- central build settings through `Directory.Build.props`
- minimal health contract in `TorrentCore.Contracts`
- minimal HTTP client in `TorrentCore.Client`
- minimal health controller in `TorrentCore.Service`
- the maintained Blazor operator shell is now `TorrentCore.WebUI`
- project references wired through the solution

## Intentional Decisions

- separate repo from TVMaze
- `TorrentCore.WebUI` is the supported rich management UI
- `TorrentCore.Web` and `TorrentCore.Avalonia` are not active delivery targets
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
