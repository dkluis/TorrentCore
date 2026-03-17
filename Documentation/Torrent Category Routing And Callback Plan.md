# Torrent Category Routing And Callback Plan

## Status

Phases 1 through 3 are implemented.

Implemented in this slice:

- persisted `torrent_categories` table
- seeded default categories:
  - `TV`
  - `Movie`
  - `Audiobook`
  - `Music`
- read-only categories API
- `CategoryKey` on add/list/detail torrent contracts
- category-aware add routing to the category download root
- host-level callback invocation settings added to runtime settings
- Web UI category administration in `Settings`
- Web UI callback settings administration in `Settings`
- Web UI category selection and filtering on the `Torrents` page
- per-torrent callback routing data persisted at add time
- completion callback invocation using the existing shared TVMaze callback entrypoint
- Transmission-compatible callback environment generation on first completion edge
- callback invocation diagnostics in the activity log
- regression coverage for callback environment values and restart-no-duplicate behavior

Not implemented yet:

- Avalonia category/callback management

## Goal

Add TorrentCore-owned category management so torrents can be submitted and operated with category-aware behavior for:

- `TV`
- `Movie`
- `Audiobook`
- `Music`

This category model must support:

- category-specific download roots
- category selection at torrent submission time
- operator management through TorrentCore UI
- completion callback invocation through the existing shared TVMaze callback app

TorrentCore should not invent a second callback stack. It should call the same callback entrypoint that Transmission
already uses so multiple torrent clients can share one operational callback setup.

## Product Decisions

- TorrentCore owns category definitions, routing, and category administration.
- TVMaze should pass a stable category key, not a filesystem path.
- TorrentCore should resolve the effective download directory from the category configuration.
- TorrentCore should invoke the existing shared callback app/script using Transmission-compatible environment variables.
- TorrentCore should not persist callback attempt state or build a callback worker model for this slice.
- Callback firing must be edge-based:
  - invoke once when a torrent first transitions into completed/finalized state
  - do not re-invoke on every later synchronization pass
- Callback executable management is external to TorrentCore. TorrentCore only needs enough configuration to call the
  already-installed callback entrypoint.

## Category Model

TorrentCore should persist a category definition table with stable keys.

Recommended fields:

- `Key`
- `DisplayName`
- `CallbackLabel`
- `DownloadRootPath`
- `Enabled`
- `InvokeCompletionCallback`
- `SortOrder`

Design rules:

- `Key` is the stable service/API identifier used by TVMaze and TorrentCore.
- `CallbackLabel` is the label sent to the shared callback app through `TR_TORRENT_LABELS`.
- `DownloadRootPath` is resolved by TorrentCore at add time and stored on the torrent.
- Editing a category later affects future torrents only. Existing torrents keep their resolved routing data.
- Compatibility rule for this phase:
  - if `CategoryKey` is omitted, TorrentCore keeps using the current global `DownloadRootPath`
  - this avoids silently misrouting existing manual add flows before category pickers are added to the UI

Example defaults:

- `TV`
- `Movie`
- `Audiobook`
- `Music`

The label can differ from the key if needed for compatibility. For example:

- key: `Audiobook`
- label: `Audio Book`

## Torrent Model Changes

Torrent submission should become category-aware.

Required changes:

- add `CategoryKey` to `AddMagnetRequest`
- persist `CategoryKey` on the torrent
- resolve and persist the effective `DownloadRootPath`/save routing at add time
- expose category in torrent list/detail contracts and UI surfaces

TorrentCore should continue to own the durable routing decision after submission.
TVMaze should not send raw download paths.

## Callback Contract

TorrentCore should invoke the existing callback entrypoint with the same environment shape that Transmission uses today.

Timing requirement:

- invoke the callback only after the final payload path is visible at `Path.Combine(TR_TORRENT_DIR, TR_TORRENT_NAME)`
- do not treat the engine's first completed or seeding edge as sufficient by itself
- when partial files are enabled, wait until the final name is visible and the incomplete-suffix file is not the only payload
- TVMaze checks the source path immediately and will return a duplicate or missing-source outcome if invoked before final visibility

Finalization readiness rule:

- resolve a candidate final path at `Path.Combine(TR_TORRENT_DIR, TR_TORRENT_NAME)`
- if that candidate is a file, require the final-name file to exist and the partial-suffix sibling not to exist
- if that candidate is a directory, recursively scan the torrent subtree and require that no files using the active partial-file suffix remain
- evaluate readiness on normal runtime ticks instead of blocking the engine loop in a long-running wait
- keep a configurable finalization wait timeout with a default target of 120 seconds; if readiness is still not reached, log the timeout and leave the torrent in a recoverable callback state instead of pretending the callback succeeded

Callback lifecycle state:

- TorrentCore should persist a generic callback lifecycle state on the torrent that is separate from the torrent's transfer state such as `Queued`, `Downloading`, `Seeding`, or `Completed`
- TorrentCore should not persist TVMaze-specific downstream ownership states such as "Submitted to TVMaze" or "On TVMaze"
- recommended persisted callback states:
  - `PendingFinalization`
  - `Invoked`
  - `Failed`
  - `TimedOut`
- recommended persisted callback timestamps/details:
  - `CompletionCallbackPendingSinceUtc`
  - `CompletionCallbackInvokedAtUtc`
  - `CompletionCallbackLastError`
- `PendingFinalization` should resume across restart on normal runtime ticks until the finalization check succeeds or the timeout is reached

Primary environment variables:

- `TR_TORRENT_ID`
- `TR_TORRENT_HASH`
- `TR_TORRENT_NAME`
- `TR_TORRENT_DIR`
- `TR_TORRENT_LABELS`

Recommended TorrentCore mapping:

- `TR_TORRENT_ID`
  - optional compatibility value such as `0`
- `TR_TORRENT_HASH`
  - torrent info hash
- `TR_TORRENT_NAME`
  - torrent display name
- `TR_TORRENT_DIR`
  - resolved category download root
- `TR_TORRENT_LABELS`
  - category `CallbackLabel`

Optional override environment variables should also be supported when configured:

- `TVMAZE_API_COMPLETE_URL`
- `TVMAZE_API_COMPLETE_API_KEY`

Those align with the existing shared callback app behavior in `TransmissionDoneCallback`.

## Host-Level Callback Settings

TorrentCore should not manage the callback app itself, but it does need host-local settings describing how to invoke it.

Recommended host settings:

- `CompletionCallbackEnabled`
- `CompletionCallbackCommandPath`
- `CompletionCallbackArguments`
- `CompletionCallbackWorkingDirectory`
- `CompletionCallbackTimeoutSeconds`
- `CompletionCallbackApiBaseUrlOverride`
- `CompletionCallbackApiKeyOverride`

Scope rule:

- these are host settings, not per-category settings
- per-category control is only whether a category invokes the callback and which callback label it sends
- normal operator setup should usually only require:
  - enabling callback invocation
  - setting the full callback launcher script path
  - keeping a timeout value
- `CompletionCallbackArguments`, `CompletionCallbackWorkingDirectory`, and the TVMaze API override fields are advanced-only fields for unusual launcher scenarios, not the normal operational path

## UI Scope

TorrentCore Web UI should be the first admin surface for this functionality.

Required Web UI capability:

- category list/editor under settings/admin
- enable/disable category
- edit display name
- edit callback label
- edit download root path
- choose whether completion callback is enabled for the category
- configure host-level callback command/path settings
- select category during Add Magnet
- show category in torrent list, detail, and filters

Avalonia can follow after the Web UI slice is stable.

## API Surface

Recommended additions:

- `GET /api/categories`
- `GET /api/categories/{key}`
- `PUT /api/categories/{key}`
- optional later:
  - `POST /api/categories`
  - `POST /api/categories/{key}/validate`
  - `POST /api/host/completion-callback/test`

Torrent endpoints should gain category support through the existing add/list/detail contracts.

## Delivery Order

### Phase 1 - Contracts and schema

Goal:

- add category and callback configuration shape to TorrentCore contracts and persistence

Scope:

- category persistence schema
- host callback settings persistence/config shape
- `CategoryKey` on add/list/detail contracts
- seeded default categories

Exit criteria:

- category definitions are persisted and queryable
- torrents can store a category key and resolved routing data

Status:

- complete
- implemented with a read-only `GET /api/categories` surface
- default categories are seeded on startup using subdirectories under the configured host download root

### Phase 2 - Category-aware submission and routing

Goal:

- make torrent submission resolve category routing inside TorrentCore

Scope:

- validate submitted `CategoryKey`
- resolve category `DownloadRootPath`
- persist category and effective path on the torrent
- expose category in list/detail

Exit criteria:

- adding a torrent with a category routes it to the configured category directory
- future category edits do not silently rewrite existing torrent routing

### Phase 3 - Completion callback invocation

Goal:

- call the existing shared callback app when a torrent reaches downstream-visible finalization

Scope:

- detect the first finalization-visible transition
- persist resolved callback label/invoke decision on the torrent at add time so later category edits affect only future torrents
- persist generic callback lifecycle state separately from torrent transfer state
- invoke configured callback command
- populate Transmission-compatible environment variables
- apply optional API override environment variables when configured
- apply a configurable finalization wait timeout and log timeout/failure outcomes
- provide a manual retry action for callback states that failed or timed out
- log callback invocation success/failure for diagnostics
- log the transition into pending finalization so operators can distinguish "completed" from "visible and invoked"

Out of scope:

- callback worker infrastructure
- a separate callback worker or ledger subsystem beyond the minimal per-torrent callback lifecycle fields
- downstream-specific acknowledgement states owned by TVMaze or another callback consumer
- replacing the existing TVMaze callback app

Exit criteria:

- completed torrents invoke the shared callback entrypoint once after the final payload path is visible
- the existing callback app can consume TorrentCore-originated completions without modification
- pending finalization survives restart without losing whether the callback is still waiting, timed out, or already invoked
- operators can retry failed or timed-out callbacks through the public boundary without touching the database
- operators can see callback lifecycle state, retryability, and finalization timing controls in the supported UI

Status:

- complete
- callback configuration, routing, Transmission-style environment generation, finalization-gated invocation, restart-safe callback lifecycle persistence, and manual retry are implemented
- service-level coverage verifies single-file and multi-file finalization waits, timeout behavior, callback failure persistence, manual retry, and no-duplicate-after-restart behavior

### Phase 4 - Operator UI

Goal:

- make categories and callback settings manageable without editing files

Scope:

- category settings UI
- host callback settings UI
- category-aware magnet submission UI
- category list/detail/filter presentation in torrent views

Exit criteria:

- operators can manage categories and callback behavior from TorrentCore UI

Status:

- partially complete
- Web UI now supports:
  - category editing
  - callback settings editing
  - category selection during magnet add, with `TV` preselected when that seeded category is enabled
  - category filtering/display in the torrent list
  - richer torrent-detail callback diagnostics, including the final payload path, pending reason, and the latest callback event/process metadata
- Avalonia catch-up is still pending

## Test Expectations

Required tests:

- category validation and lookup
- default category seeding
- add-torrent request with valid and invalid category keys
- category-aware download-root resolution
- category shown correctly in list/detail DTOs
- finalization visibility detection triggers callback once
- callback invocation populates the expected Transmission-style environment values
- callback-disabled categories do not invoke the callback
- pending finalization survives restart until the callback is invoked or timed out
- multi-file torrents do not invoke the callback while partial-suffix files remain anywhere in the final torrent subtree

Manual validation should also confirm that the shared callback app behaves the same when called by:

- Transmission
- TorrentCore

## Integration Boundary With TVMaze

TVMaze should remain a shallow client.

TVMaze may:

- choose a TorrentCore category key
- submit a magnet with that key
- show category in lightweight summaries if useful

TVMaze should not own:

- category download-root paths
- callback command configuration
- callback environment construction
- category administration

## Success Criteria

This workstream is complete when:

1. TorrentCore operators can manage the four initial categories through UI.
2. Torrent submission accepts a stable category key and resolves download routing inside TorrentCore.
3. Completed torrents invoke the existing shared callback app using Transmission-compatible environment variables.
4. TVMaze can target TorrentCore categories without sending raw filesystem paths.
5. No second callback stack or duplicated per-client callback setup is introduced.
