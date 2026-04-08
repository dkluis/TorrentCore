# TorrentCore Web UI Restart Plan

## Status

Active planning document.

Current status: `Phase 0 Complete`, `Phase 1 Complete`, `Phase 2 Complete`, `Phase 3 Pending`

Last updated: `2026-04-07`

Current checkpoint:

- ground rules are agreed for a full web UI restart
- target direction is a new parallel TorrentCore web UI project with TVMazeWeb-aligned design language and MudBlazor usage
- current `TorrentCore.Web` remains available as a reference implementation during restart delivery
- TVMazeWeb baseline conventions have been captured from live source files to guide Phase 1 implementation
- project naming direction is now `TorrentCore.WebUI`
- visual baseline should stay as close as practical to TVMazeWeb, with TorrentCore-specific favicon/brand assets
- side-by-side runtime hosting is not required because Avalonia remains available during web restart delivery
- `src/TorrentCore.WebUI` has been created and added to `TorrentCore.sln`
- `TorrentCore.WebUI` now has MudBlazor, TVMaze-aligned shell/theme baseline, client-boundary DI wiring, and initial route placeholders
- Phase 2 shared infrastructure is now in place:
  - `ITorrentCoreApiAdapter` wraps `TorrentCoreClient` calls with consistent result/error contracts
  - shared loading/error/empty rendering uses reusable `StateView` component patterns
  - shared toast/confirm patterns are centralized through `IOperatorFeedbackService`
  - shared card/grid/table composition wrappers are available for data-heavy pages
  - circuit-scoped page-state persistence (`IPageStateStore`) now backs list filter/sort/page behavior
- dashboard, torrents, logs, settings, and service-connection pages now consume the shared primitives
- torrents page now follows the locked “single grid + selected detail panel” pattern with 5-second auto-refresh and row-level action execution from the detail panel
- torrents and logs tables now use MudBlazor header-based sorting (`MudTableSortLabel`) instead of separate sort selectors
- shell app bar `Add Magnet` now opens a global add dialog and submits directly through shared API adapter/service feedback patterns
- selected torrent actions now include `Peers` and `Trackers` dialogs backed by dedicated service endpoints instead of overloading the main details panel
- peer diagnostics use a compact paged grid with live auto-refresh while the dialog is open
- tracker diagnostics use a paged read-only grid with manual refresh and no tracker-URL column in the operator view

## Purpose

This is the working plan and progress record for replacing the current TorrentCore web UI with a new browser operator
surface.

This is not a restyle of the existing pages.

This is a controlled restart with clear architecture, UX, and cutover rules.

## Inputs

Primary planning references:

- [TvmazeWeb.csproj](/Volumes/HD-Desktop-Misc-L5/Development/Source/C%23/TVMaze/ProdWebApps/TvmazeWeb/TvmazeWeb.csproj)
- [TVMaze Web UI Restart Plan.md](/Volumes/HD-Desktop-Misc-L5/Development/Source/C%23/TVMaze/Documentation/Refactor%20Activities/UI/TVMaze%20Web%20UI%20Restart%20Plan.md)
- [Execution Plan and Progress.md](/Volumes/HD-Desktop-Misc-L5/Development/Source/C%23/TorrentCore/Documentation/Execution%20Plan%20and%20Progress.md)
- [TorrentCore.Web.csproj](/Volumes/HD-Desktop-Misc-L5/Development/Source/C%23/TorrentCore/src/TorrentCore.Web/TorrentCore.Web.csproj)
- [TorrentCoreClient.cs](/Volumes/HD-Desktop-Misc-L5/Development/Source/C%23/TorrentCore/src/TorrentCore.Client/TorrentCoreClient.cs)

## Problem Statement

The current TorrentCore web UI is functional, but it is no longer the right long-term shape for the operator experience
target:

- it is Bootstrap-first while the new desired direction is TVMazeWeb-style MudBlazor layout and interaction patterns
- major workflows are concentrated in large page files, making iterative UX changes slower and riskier
- layout and component structure do not match the desired shell/tab/grid standards from TVMazeWeb
- the desired next-generation operator surface requires both parity and expansion, not incremental page patching

Given the agreed target, continuing in-place is likely to cost more and produce a weaker result than a parallel restart.

## Agreed Direction

The following are fixed decisions:

1. Build a new UI as a parallel project; do not refactor the current web project in place.
2. Use the TVMazeWeb-style web stack: `Blazor Web App` with server interactivity and `MudBlazor`.
3. Treat current TorrentCore web pages as functional references only, not layout templates.
4. Keep the web app as a thin client over TorrentCore service contracts.
5. Match TVMazeWeb design language in shell, page composition, density, and grid interaction model.
6. Ship at least current TorrentCore web functionality at cutover, then expand from there.
7. Build responsive behavior from the first slices, not as later polish.
8. Use shared interaction patterns for loading, errors, confirmations, and action feedback.
9. Add web-specific tests for client boundary and component behavior.
10. Cut over only when acceptance criteria pass; old web runtime can be disabled during restart while Avalonia remains fallback.

## Working Assumptions

- Working project name is `TorrentCore.WebUI`.
- Existing `TorrentCore.Web` remains reference/maintenance-only during restart implementation.
- Existing `TorrentCore.Client` and `TorrentCore.Contracts` remain the primary service boundary for web UI calls.
- If a needed workflow is not available through current contracts, extend service/contracts instead of leaking logic into
  the UI.
- New UI delivery is workflow-based and slice-based, not one-shot parity.

## Non-Negotiable Guardrails

The replacement web UI must not:

- call MonoTorrent, DHT/tracker endpoints, TVMaze systems, or local process/file operations directly
- bypass TorrentCore service APIs for operator workflows
- embed business/recovery policy logic that belongs in service host
- mutate persistence through direct DB access

The replacement web UI must:

- use TorrentCore service APIs as the only operational gateway
- keep behavior aligned with current runtime and operator docs
- remain testable at HTTP client boundary and component/UI boundary
- preserve operator usefulness over visual novelty

## Primary Goal

Deliver a new browser operator surface for TorrentCore that matches TVMazeWeb-quality design/layout standards while
covering current TorrentCore functionality and enabling additional workflows without UI debt.

## Success Criteria

- a new web project exists and runs independently with no dependency on `TorrentCore.Web` runtime hosting
- shell and page patterns align with TVMazeWeb style and MudBlazor usage
- current functional coverage is preserved across:
  - dashboard/host visibility
  - torrent intake/list/detail/actions
  - logs and diagnostics browsing
  - runtime settings/category/callback management
  - service connection management
- responsive behavior is verified for phone/tablet/desktop widths for each completed slice
- web-specific tests cover critical request and interaction behavior
- old web UI can be retired without loss of required browser workflows

## Design Parity Target

Target parity is with TVMazeWeb design framework and interaction style, not literal page cloning.

Expected parity areas:

- MudBlazor-first component usage and theme structure
- compact operator-density defaults
- intentional shell/navigation composition
- consistent table/grid and actions model
- viewport-fit behavior for dense data screens
- consistent feedback model for async operations and failures

## Locked UI Standards (2026-04-04)

These are now mandatory defaults for all new/reworked TorrentCore.WebUI pages.

- Toast/snackbar placement uses top-right to match TVMaze behavior.
- MudBlazor runtime script must be loaded in `Components/App.razor` (`MudBlazor.min.js`).
- Dense data pages must be viewport-fit: browser window should not scroll during normal operation.
- Only grid/table containers scroll for dense views; pager must remain visible/clickable inside the table shell.
- Grid height must be based on available container space, not inferred from row count/page size.
- Default rows-per-page for operator tables is `25` unless a page has a documented exception.
- Table sorting should be provided through grid header sort controls (`MudTableSortLabel`) rather than external sort dropdowns.
- Do not hard-cap API retrieval counts in UI code for grid/list pages.
- Do not hard-cap API retrieval counts in service query handlers unless a cap is explicitly documented and approved.
- For logs specifically, the prior `500` query clamp is removed; retention is controlled by configured max log entries.

Application rule:

- If any future page violates these standards, fix the shared shell/CSS/component pattern first, then page-specific code.

## Logs Filtering Rules (2026-04-05)

These rules are now explicit for `TorrentCore.WebUI` logs behavior:

- The logs page always requests all available rows from the service (`take = int.MaxValue`), with no server-side filter parameters.
- All logs filtering is local in the browser/UI.
- `Torrent Id` filtering must only apply when the field contains a valid GUID.
- Empty `Torrent Id` means no torrent-id filter (do not interpret empty as `NULL`).
- `From` and `To` date fields apply only when they contain parseable date/time values.
- `Refresh` applies the currently entered filter values.
- `Clear` resets all filter inputs and query-string filter context.

## Torrent Diagnostics Dialog Rules (2026-04-07)

These rules are now explicit for selected-torrent diagnostics in `TorrentCore.WebUI`:

- The `Selected Torrent` action area owns deeper peer/tracker inspection through `MudDialog` popups, not through more rows in the detail card.
- `Peers` opens a compact paged grid with these default columns:
  - endpoint
  - client
  - direction
  - connected
  - seeder
  - down rate
  - up rate
  - downloaded bytes
  - uploaded bytes
  - encryption
- The peers dialog auto-refreshes while open so live swarm changes can be inspected without leaving the selected torrent context.
- `Trackers` opens a paged read-only grid with tier/tracker position plus active/status/announce/scrape diagnostics.
- Tracker URL is intentionally not shown in the first WebUI diagnostics slice; operator view stays focused on state/health rather than raw announce strings.
- Dialog grids still follow the same table rules as full pages:
  - header-based sorting
  - default 25 rows per page
  - pager always visible inside the table shell
  - no browser-window scrolling required for normal use

## TVMaze Baseline Conventions (Captured)

These conventions are now treated as the implementation baseline for TorrentCore web restart.

Theme and shell baseline:

- MudBlazor theme is defined in layout, not a standalone theme file.
- palette is warm/light with dark app bar and drawer, matching TVMazeWeb shell contrast model.
- typography uses:
  - serif headings (`Iowan Old Style` fallback chain)
  - sans-serif body (`Avenir Next` fallback chain)
- default border radius is `12px`.
- shell uses `MudLayout` + `MudAppBar` + responsive `MudDrawer` + `MudMainContent`.
- app bar includes a global `Add Magnet` action:
  - button on desktop
  - icon action on smaller breakpoints
- drawer is compact-width and icon-and-title navigation focused.

Styling baseline:

- CSS tokens are centralized in `wwwroot/app.css` under `:root` custom properties (`--tvz-*` family).
- dense operator styling is primarily CSS-driven (compact paddings, small table typography, compact controls).
- layout relies on viewport-fit work areas using `100dvh` minus section offsets for data-heavy screens.

Data layout and table baseline:

- TVMazeWeb currently uses `MudTable` broadly for operational tables.
- no `MudDataGrid` usage is currently present in the referenced TVMazeWeb project.
- table pattern conventions:
  - `Dense=true`, `Bordered=true`, `Striped=true`, `Hover=true` in most operator tables
  - row click for selection on interactive tables
  - sticky/contained table shell with pager kept visible
  - compact table cell padding and uppercase narrow header labels via CSS
- tab pattern conventions:
  - top-level `MudTabs` for major workflow partitions
  - nested `MudTabs` for detail sub-workflows within selected context

Responsive baseline:

- breakpoint bands in shared CSS are:
  - mobile: `< 48rem`
  - tablet/desktop transition: `>= 48rem`
  - large desktop: `>= 72rem`
  - extra large desktop: `>= 90rem`
- mobile pattern emphasizes stacked toolbar/actions and vertically flowing panel headers.
- larger breakpoints progressively increase grid columns and reduce viewport-offset values.

## Functional Parity Baseline

At minimum, the new UI must support all current core workflows:

- dashboard host status and runtime visibility
- add magnet, filter/sort, single and bulk torrent actions
- torrent detail diagnostics and lifecycle actions
- logs filtering and recent activity inspection
- runtime settings and category/callback administration
- service endpoint bootstrap and persistence

Post-parity expansion targets (candidate):

- stronger queue/recovery diagnostics UX
- better activity/event timeline visualization
- richer multi-torrent operations and operator workflows
- improved mobile-first operation on data-dense screens

## Delivery Strategy

Use a parallel-project migration:

- build and validate the new web project as a separate deployment unit
- port by workflow slice
- verify each slice against functional + responsive + test gates
- complete cutover only after acceptance checklist passes

Recommended route strategy during migration:

- keep current UI routes stable
- side-by-side runtime hosting is optional, not required
- if the current web UI is shut down during restart, Avalonia remains the operator fallback
- final cutover still requires the full acceptance checklist

## Proposed Delivery Order

1. Foundation + shared shell + shared interaction primitives
2. Dashboard
3. Torrents list + add + bulk actions
4. Torrent detail
5. Logs
6. Settings
7. Service connection + cross-cutting polish + cutover prep

Reason:

- this sequence delivers operator value quickly while forcing shared layout and interaction patterns early
- it addresses highest-traffic workflows first before less-frequent administration paths

## Phase Plan

### Phase 0 - Decision Capture And Scope Lock

Goal:

- lock direction, rules, and baseline scope before implementation

Tasks:

- capture agreed ground rules in this document
- define design parity target and functional parity baseline
- define delivery order and cutover strategy
- define acceptance and testing gates

Verify:

- this document is accepted as restart source of truth
- implementation can begin without re-debating boundary decisions

Status:

`Complete`

### Phase 1 - New Project Foundation

Goal:

- create new project with correct stack and boundary wiring

Tasks:

- create new web project in solution (`Blazor Web App`, server interactivity)
- add MudBlazor package and baseline theme configuration
- wire `TorrentCore.Client` and endpoint provider patterns
- establish base shell/navigation and error boundaries
- ensure no direct non-service operational dependencies

Verify:

- new project builds and runs
- project reaches TorrentCore service through typed client boundary
- shell renders with baseline responsive behavior

Status:

`Complete`

### Phase 2 - Shared UI Infrastructure

Goal:

- avoid per-page reinvention

Tasks:

- create shared service wrappers/adapters around client calls as needed
- define shared loading/error/empty/action-feedback components
- define shared confirm/alert/toast patterns
- define shared responsive grid/table/card patterns for data-heavy pages
- define shared page-state persistence strategy (filters/sorts/page)

Verify:

- first functional slices can reuse shared primitives
- no ad hoc request/error patterns in new pages

Status:

`Complete`

### Phase 3 - Dashboard Slice

Goal:

- deliver host visibility baseline in new design language

Tasks:

- implement dashboard cards/panels for host/runtime/queue/callback visibility
- implement periodic refresh behavior with explicit operator refresh control
- implement mobile/tablet/desktop responsive behavior

Verify:

- functional parity with current dashboard
- responsive review passed at phone/tablet/desktop widths

Status:

`Pending`

### Phase 4 - Torrents List Slice

Goal:

- deliver primary day-to-day workflow

Tasks:

- implement add magnet surface and category selection
- implement filters/sort, row actions, and bulk actions
- implement responsive grid/card behavior and selection behavior
- preserve and improve operator action feedback patterns

Verify:

- parity with current torrents list workflows
- key actions validated against real service

Status:

`Pending`

### Phase 5 - Torrent Detail Slice

Goal:

- deliver per-torrent diagnostics/action surface

Tasks:

- implement detail identity/status/transfer/diagnostics sections
- implement action controls (`pause/resume/remove`, metadata actions, callback retry where applicable)
- include relevant activity/log context and navigation affordances

Verify:

- parity with current torrent detail workflows
- action and diagnostics behavior validated end-to-end

Status:

`Pending`

### Phase 6 - Logs Slice

Goal:

- deliver diagnostics and troubleshooting workflows

Tasks:

- implement log filters and list/grid behavior
- support torrent-scoped and global diagnostic review paths
- support refresh and paging patterns appropriate for operator use

Verify:

- parity with current logs page coverage
- filters and pagination verified across expected volumes

Status:

`Pending`

### Phase 7 - Settings + Service Connection Slice

Goal:

- deliver runtime administration and connection management in new UI

Tasks:

- implement runtime settings sections (policies, throttles, recovery, callbacks, categories)
- implement service connection setup/test/save behavior
- align advanced sections with compact, high-density operator layout

Verify:

- parity with current settings + service-connection behavior
- saved settings and endpoint changes verified against live service

Status:

`Pending`

### Phase 8 - Hardening And Cutover

Goal:

- make cutover low-risk and reversible

Tasks:

- complete responsive/accessibility pass across implemented pages
- complete regression and component tests for critical workflows
- run operator acceptance checklist
- prepare cutover/rollback deployment steps

Verify:

- cutover checklist fully green
- rollback path documented and tested

Status:

`Pending`

## Testing Strategy

Required coverage:

- client-boundary tests for request shaping and error handling
- component tests for critical actions and interaction states
- end-to-end smoke checks for add/pause/resume/remove/settings/log workflows
- responsive review gates on phone/tablet/desktop for each completed slice

Minimum cutover test set:

- add magnet succeeds and returns promptly under load
- torrent actions behave correctly and reflect state updates
- key diagnostics are visible and actionable
- runtime settings updates persist and apply according to restart/live semantics
- connection bootstrap flow recovers correctly from unreachable endpoint

## Risks And Mitigations

Risk:

- scope expansion beyond parity before foundation is stable

Mitigation:

- enforce phase exit criteria and lock parity-first scope

Risk:

- inconsistent UX patterns across pages

Mitigation:

- prioritize shared interaction primitives in Phase 2 before heavy feature slices

Risk:

- regression during cutover

Mitigation:

- keep old UI live until acceptance gates pass, then perform controlled cutover with rollback

Risk:

- hidden API gaps for advanced UX

Mitigation:

- capture gaps early and extend contracts/service deliberately, not ad hoc in UI

## Information Needed To Finalize Visual Parity

These are optional refinement inputs; they are not blockers for starting Phase 1 because the core baseline is already
captured directly from TVMazeWeb:

- any additional TVMazeWeb pages you consider canonical for detailed interaction behavior beyond the current baseline
- explicit do/don't rules if you want to diverge from TVMazeWeb defaults in specific TorrentCore areas
- preferred state persistence policy per page (`restore` vs `reset`) for filters/tab/paging

## Decision Log

- `2026-04-03`: agreed to full restart, parallel project, TVMazeWeb design/framework parity, and parity-plus scope
- `2026-04-03`: locked 10 ground rules as non-negotiable restart constraints
- `2026-04-03`: created `TorrentCore.WebUI` foundation and completed Phase 1 baseline implementation
- `2026-04-03`: completed Phase 2 shared UI infrastructure and rewired page implementations to shared request/state/feedback primitives
- `2026-04-05`: fixed logs-page false row reduction where empty `Torrent Id` was incorrectly treated as `torrent_id IS NULL`; empty now means no torrent-id filter.
- `2026-04-05`: reaffirmed logs retrieval/filter boundary: fetch all rows from API, apply filters only in UI, and avoid implicit server-side narrowing.
