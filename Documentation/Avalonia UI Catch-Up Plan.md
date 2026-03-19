## Goal

Bring `TorrentCore.Avalonia` up to date with the current operator capabilities in the Web UI while preserving the existing desktop design language:

- dark left navigation rail
- neutral content canvas with dense control cards
- right-side inspector rail
- desktop-first operator workflows instead of browser-style responsive layouts

This is a parity plan, not a visual port of the Web UI.

Current status:

- Slice 1 (`Settings` parity) is now implemented
- Slice 2 (`Torrents` parity) is now implemented
- Slice 3 (`Torrent Detail` parity) is now implemented
- `Dashboard` plus `Logs` remain pending

## Design Guardrails

Keep these Avalonia concepts in place:

- the current three-column shell in `MainWindow.axaml`
- the warm neutral palette with slate navigation from `App.axaml`
- card-based sections instead of long form pages or Bootstrap-like grids
- command-oriented toolbars for operator actions
- the inspector rail as a persistent desktop-only context surface

Do not copy these Web patterns directly:

- mobile card/table breakpoints
- long single-column forms with HTML field stacks
- Web-specific inline link/action patterns when a desktop command bar or card action reads better

## Current Gap Summary

The current Avalonia client already covers connection bootstrap, settings parity, torrents parity, and torrent detail parity. The remaining parity gaps are:

1. `Dashboard`
- no callback lifecycle rollup card

2. `Logs`
- missing the Web-style auto-refresh behavior that fits the operator monitoring use case
- no direct desktop shortcut from a log entry into torrent detail

## Planned UI Direction

### Shell And Shared Patterns

Keep the existing shell, but make it more useful for operations:

- make the inspector rail contextual by section instead of static
- keep shared loading, error, and success banners visually consistent across screens
- add optional auto-refresh toggles to screens that benefit from live monitoring
- preserve the current desktop density instead of spacing the UI like the Web client

Inspector usage by screen:

- `Dashboard`: service target, last refresh, callback counts, and queue saturation summary
- `Torrents`: selected count, active filters, add-category default, and callback retryable count
- `Torrent Detail`: category, callback state, final payload path, and latest callback event summary
- `Logs`: active filters, last refresh, auto-refresh status
- `Settings`: runtime summary, restart-required indicator, and category/callback save guidance

### Slice 1: Settings Parity

This is the highest-value catch-up slice because it unlocks desktop-side administration of the current live runtime model.

Status:

- implemented

Planned changes:

- extend `SettingsViewModel` to load and save both runtime settings and categories
- add a `Shared Callback Settings` section
- keep `Arguments`, `WorkingDirectory`, API override fields, and API key override under an `Advanced Overrides` expander
- add a `Categories` section using one card per category
- keep category cards dense and desktop-oriented rather than reproducing the Web layout

Recommended Avalonia presentation:

- retain the current wrap-panel card layout for runtime settings
- add a dedicated callback card group under the existing runtime settings sections
- render categories as stacked cards with a compact header row:
  - key
  - enabled toggle
  - invoke callback toggle
  - display name
  - callback label
  - download root
  - sort order

Exit criteria:

- Avalonia can manage all runtime callback settings already supported by the Web UI
- Avalonia can manage category routing definitions already supported by the Web UI

### Slice 2: Torrents Parity

Status:

- implemented

Planned changes:

- add category selection to the add-magnet form
- default that selection to `TV` when the enabled seeded `TV` category exists
- add category and callback-state filters alongside the existing name/status/sort filters
- extend `TorrentListItemViewModel` to project category and callback lifecycle state
- add a `Retry Callback` per-row action when the torrent is eligible
- show category and callback state in each torrent card without losing the existing compact action flow

Recommended Avalonia presentation:

- keep the current stacked torrent cards instead of moving to a table
- add a small metadata strip below the title:
  - state
  - category
  - callback state
- keep primary actions in the existing horizontal action row
- add retry only when actionable to avoid dead controls

Exit criteria:

- Avalonia operators can add torrents into categories with the same routing behavior as Web
- callback retryable torrents are visible and actionable from the list

### Slice 3: Torrent Detail Parity

Status:

- implemented

Planned changes:

- show the torrent category in the identity/runtime area
- add a dedicated `Completion Callback` card
- add retry callback action in the top command bar and inside the callback card
- surface:
  - callback state
  - pending since
  - invoked at
  - final payload path
  - pending reason
  - last error
  - latest callback log summary
  - process id
  - exit code
  - command path
  - working directory
  - process timeout
  - finalization wait timeout

Recommended Avalonia presentation:

- keep the current multi-card wrap layout
- make callback diagnostics a first-class card rather than burying them in raw log JSON
- keep recent logs below the cards, but add a clearer callback event highlight when present

Exit criteria:

- Avalonia detail view exposes the same callback lifecycle and diagnostics that the Web detail view already exposes

### Slice 4: Dashboard And Logs Observability

Planned changes:

- add a `Completion Callbacks` card on the dashboard with:
  - pending finalization
  - invoked
  - failed
  - timed out
  - retryable count
- add optional auto-refresh to dashboard, torrents, torrent detail, and logs
- add an `Auto Refresh` toggle to logs and a `Last Refreshed` summary
- make log entries with a torrent id navigable to the torrent detail screen

Recommended Avalonia presentation:

- keep dashboard cards visually consistent with the existing host/engine/queue cards
- use the inspector rail for live operational counters instead of adding another dense banner row
- keep logs as stacked desktop rows/cards, but add one-click navigation for entries tied to a specific torrent

Exit criteria:

- Avalonia supports the same callback-state monitoring workflow the Web dashboard and torrents list now support
- logs work as a real desktop operations tool instead of a read-only event dump

## Recommended Implementation Order

1. `Settings`
- biggest parity gap
- highest operator value
- mostly straightforward client/UI work

2. `Torrents`
- category routing and callback retry become visible in the main operational screen

3. `Torrent Detail`
- finish the callback diagnostics story

4. `Dashboard` and `Logs`
- complete the monitoring/observability layer

5. shared shell and inspector refinement
- apply once the page-level data surfaces exist

## Shared Implementation Notes

- prefer adding shared formatting helpers for category labels, callback-state labels, and local-time rendering instead of duplicating string logic across viewmodels
- use the existing `TorrentCore.Client` boundary only; this plan does not require new service endpoints
- keep the right inspector rail useful by feeding it screen-specific summary data instead of static copy
- keep screen actions optimistic but reload-backed, matching the current Avalonia model

## Testing Strategy

Current gap:

- the repo does not yet have a dedicated Avalonia UI test harness

Recommended coverage for this slice:

- viewmodel tests for:
  - default `TV` category resolution
  - torrent category and callback-state filtering
  - retry callback visibility and command gating
  - settings load/save mapping for callback settings and categories
  - dashboard callback count aggregation
- build verification for `src/TorrentCore.Avalonia`
- manual validation against the live environment for:
  - category add flow
  - category editing
  - callback retry
  - callback diagnostics display
  - log-to-detail navigation

## Out Of Scope For This Catch-Up Pass

- replacing the Web UI
- introducing a second visual design system
- adding media artwork or consumer-style browsing
- adding service behavior that is not already exposed through current contracts
