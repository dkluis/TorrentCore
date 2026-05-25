# Torrent History Plan

## Status

Planning document only.

This document captures the agreed design for adding a durable torrent history table without changing the current purpose or behavior of the live `torrents` table.

Current execution status:

- Slices A-D complete
- Phase 6 complete
- Phase 1 complete
- Phase 2 complete
- Phase 3 complete
- Phase 4 complete
- Phase 5 complete
- Phase 7 complete

Last updated: `2026-05-25`

## Agreed Constraints

- `torrents` remains dedicated to live MonoTorrent state and restart/recovery persistence.
- No behavioral or semantic change to `torrents` is allowed as part of this work.
- Torrent history must use a separate table.
- History must not be derived from activity logs.
- History rows must not be deleted for now.
- A separate cleanup/retention policy may be added later, but it is explicitly out of scope for this slice.
- The history row should be inserted once and then kept up to date in place as lifecycle events happen.

## Chosen Design

Chosen option:

- dedicated history service/store with explicit workflow updates

This means:

- a new `torrent_history` table will be added through a SQLite migration
- a dedicated history persistence boundary will be introduced
- existing lifecycle flows will call that history boundary explicitly
- history is not a projection over `activity_logs`
- history is not a projection over the live `torrents` table

## Goal

Add a durable summary record for each torrent lifecycle so operators and future APIs can inspect a torrent after it has been removed from the active runtime table.

The history row should always contain the latest relevant known state for that torrent lifecycle.

## History Row Model

One row represents one torrent lifecycle.

Behavior:

- insert the row when the torrent is first added
- update that same row as significant torrent lifecycle events occur
- keep the row after the active torrent is removed from `torrents`

This is intentionally a summary-history model, not an append-only event log model.

## Scope For First Slice

Included:

- SQLite schema for `torrent_history`
- dedicated history store/service
- explicit history writes from lifecycle workflows
- history list/detail read API
- automated tests for lifecycle updates and retention of history rows

Not included:

- history retention cleanup
- event-sourced secondary history table
- reuse of `activity_logs` as the source of truth

Follow-up implementation note:

- a WebUI `History` page has now been added after the original API-first slice
- the page uses explicit local-date and text filters, a grid with browser-side sorting, and a selected-entry detail panel

## Estimated Effort

For the agreed first delivery scope of persistence plus read API, the expected implementation time is:

- best case: about 20 hours
- more realistic: about 24-30 hours
- practical calendar estimate: about 2-3 working days

The biggest variable is correctly wiring all lifecycle update points without missing edge cases.

## Field Set

The history row should include the fields requested during planning, plus the current operator-visible torrent details that are already valuable in the active UI.

### Identity And Routing

- `torrent_id`
- `name`
- `magnet_uri`
- `info_hash`
- `category_key`
- `download_root_path`

### Latest Operator State

- `latest_torrent_state`
- `latest_wait_reason`
- `latest_error_message`
- `latest_progress_percent`
- `latest_downloaded_bytes`
- `latest_uploaded_bytes`
- `latest_total_bytes`
- `latest_download_rate_bytes_per_second`
- `latest_upload_rate_bytes_per_second`
- `latest_tracker_count`
- `latest_connected_peer_count`

### Lifecycle Timestamps

- `submitted_at_utc`
- `metadata_resolved_at_utc`
- `download_started_at_utc`
- `download_completed_at_utc`
- `seeding_started_at_utc`
- `last_activity_at_utc`
- `last_updated_at_utc`
- `removed_at_utc`

### Callback State

- `invoke_completion_callback`
- `completion_callback_label`
- `latest_callback_status`
- `callback_started_at_utc`
- `callback_completed_at_utc`
- `callback_last_error`

### Removal Outcome

- `data_deleted`
- `removal_reason`
- `removed_by_cleanup_policy`

### Optional But Recommended In The Same Table

- `final_download_path`
- `service_instance_id_last_seen`

## Notes On Field Meaning

### `removed_at_utc`

Use `removed_at_utc` as the durable internal field name.

Reason:

- TorrentCore already uses `remove` as the lifecycle concept.
- `deleted` is ambiguous because payload file deletion and removal from TorrentCore tracking are different things.

If a later UI wants to label the column as `Deleted`, that can be a presentation choice rather than the storage name.

### `download_root_path`

For history, `download_root_path` is the only stored directory field.

This is the directory where the torrent content is being stored.

In the current production model:

- the download location and finished location are the same
- incomplete media is distinguished by MonoTorrent's `.!mt` suffix
- when the torrent completes, MonoTorrent removes that suffix

### `final_download_path`

`final_download_path` is the full path to the actual torrent payload when TorrentCore can know it confidently.

Examples:

- single-file torrent: the final media file path
- multi-file torrent: the torrent content directory path

This should remain `null` until TorrentCore genuinely knows the final file or directory path.

It should not be pre-populated during add flow from `download_root_path`.

### `last_updated_at_utc`

This should be updated whenever the history row is changed for a meaningful lifecycle update.

### `latest_*` fields

These should reflect the most recently known values at the time the row was last updated. They are summary fields, not a full audit trail.

### History Retention

No history deletion will be implemented in this slice.

Future retention options may be added later, but the base design assumes history is durable by default.

## Operator-Relevant Fields Confirmed From Current UI

The current active torrent detail view exposes data that should be reflected in history where practical:

- Name
- State
- Category
- Wait
- Progress
- Peers
- Trackers
- Downloaded
- Download rate
- Upload rate
- Info hash
- Added/submitted time
- Completed time
- Last activity
- Save path
- Magnet URI
- Total size

The history row should preserve these as durable summary data so the information remains available after removal from the active runtime.

## Lifecycle Update Matrix

### 1. Add Magnet

When a torrent is first added:

- create the history row
- populate identity/routing fields
- set `submitted_at_utc`
- set initial `latest_torrent_state`
- set initial latest metrics from the created torrent snapshot/result
- set `last_updated_at_utc`
- set `service_instance_id_last_seen`

### 2. Metadata Resolved

When metadata becomes available:

- set `metadata_resolved_at_utc` if not already set
- update `name`, `info_hash`, `latest_total_bytes`, tracker/peer counts if improved
- update `latest_torrent_state`
- update `last_activity_at_utc` if appropriate
- update `last_updated_at_utc`
- update `service_instance_id_last_seen`

### 3. Download Starts

On the first transition into active download work:

- set `download_started_at_utc` if not already set
- update latest rates, bytes, state, wait reason, and peer/tracker counts
- update `last_activity_at_utc`
- update `last_updated_at_utc`
- update `service_instance_id_last_seen`

### 4. Download Progress / State Changes

During normal state transitions such as queued, downloading, paused, seeding, completed, or error:

- update `latest_torrent_state`
- update `latest_wait_reason`
- update `latest_error_message`
- update latest metrics and counts
- update `last_activity_at_utc` when the active torrent state indicates real activity
- update `last_updated_at_utc`
- update `service_instance_id_last_seen`

### 5. Download Completion

When the torrent reaches completed payload state:

- set `download_completed_at_utc` if not already set
- update `latest_torrent_state`
- update final/latest bytes and size fields
- update `last_activity_at_utc`
- update `last_updated_at_utc`
- update `service_instance_id_last_seen`

### 6. Seeding Starts

When seeding begins:

- set `seeding_started_at_utc` if not already set
- update `latest_torrent_state`
- update upload metrics and peer counts
- update `last_activity_at_utc`
- update `last_updated_at_utc`
- update `service_instance_id_last_seen`

### 7. Callback Begins

When completion callback processing begins:

- set `callback_started_at_utc` if not already set for the current callback attempt semantics
- update `latest_callback_status`
- clear or replace `callback_last_error` as appropriate
- update `last_updated_at_utc`
- update `service_instance_id_last_seen`

### 8. Callback Completes / Fails / Times Out

When callback processing finishes or transitions to a terminal/visible state:

- set `callback_completed_at_utc` when the callback attempt is complete
- update `latest_callback_status`
- update `callback_last_error`
- update `final_payload_path` when known
- update `last_updated_at_utc`
- update `service_instance_id_last_seen`

### 9. Manual Remove

When an operator removes a torrent from active tracking:

- set `removed_at_utc`
- set `removal_reason`
- set `removed_by_cleanup_policy = false`
- set `data_deleted` based on the request
- update final/latest state fields
- update `last_updated_at_utc`
- keep the history row

### 10. Automatic Cleanup Remove

When cleanup removes a completed torrent from active tracking:

- set `removed_at_utc`
- set `removal_reason`
- set `removed_by_cleanup_policy = true`
- set `data_deleted = false` for the current cleanup behavior unless future policy changes that contract
- update `last_updated_at_utc`
- keep the history row

## Implementation Structure

Recommended components:

- history record/model for persistence mapping
- history store interface in the core boundary or equivalent service-facing abstraction
- SQLite history store implementation
- history service that owns insert/update rules
- service/API contracts for history list/detail reads

The history service should be the only place that decides:

- when a timestamp is first stamped
- whether a field is first-write-wins or latest-write-wins
- how removal fields are populated
- how callback status fields are normalized

## Phased Delivery Plan

This work should be delivered in incremental slices with review points between them.

### Phase 0 - Lock The Model

Goal:

- finalize the history table contract before implementation starts

Deliverables:

- approved field list
- approved timestamp semantics
- approved removal semantics
- approved first-slice API scope

Review points:

- confirm `removed_at_utc` naming
- confirm one row per torrent lifecycle
- confirm no cleanup/deletion of history rows
- confirm read API shape is list plus detail

Estimated time:

- about 1-2 hours

### Phase 1 - Schema And Persistence Boundary

Goal:

- create the history table and its dedicated persistence boundary

Deliverables:

- SQLite migration for `torrent_history`
- history record/model
- history store interface
- SQLite history store implementation
- migration/initialization test coverage

Review points:

- review column names and nullability
- review indexes
- review persistence boundary shape

Estimated time:

- about 3-5 hours

Status:

- `Complete`

Delivered:

- SQLite migration `11` adds `torrent_history`
- `torrent_id` is the row primary key for the current lifecycle
- full agreed initial column set is present in the schema
- dedicated history store boundary is implemented
- SQLite history store implementation is in place
- migration/store coverage exists

### Phase 2 - Row Creation On Torrent Add

Goal:

- start creating history rows when torrents are submitted

Deliverables:

- create history row on add
- populate identity, routing, initial state, and submitted timestamp
- tests for row creation

Review points:

- verify initial field values
- verify no change to `torrents` semantics
- verify duplicate/add edge behavior

Estimated time:

- about 2-4 hours

Status:

- `Complete`

Delivered:

- add-magnet flow now creates a history row
- the inserted row captures the agreed Slice A identity/routing/latest-state baseline
- no semantic or behavioral change was made to the live `torrents` table
- add-flow coverage verifies the history row is created

### Phase 3 - Core Lifecycle Updates

Goal:

- keep history rows current through normal torrent progression

Deliverables:

- update history on metadata resolution
- update history on download start
- update history on progress/state transitions
- update history on completion
- update history on seeding start
- tests for timestamp stamping and latest-field refresh

Review points:

- confirm first-write versus latest-write timestamp rules
- verify state transitions do not miss edge cases
- verify latest metrics update correctly

Estimated time:

- about 6-10 hours

Status:

- `Complete`

Delivered:

- history observation now updates rows from the existing runtime/synchronization snapshot flow
- missing history rows are auto-created when an active torrent is observed
- metadata resolution stamps `metadata_resolved_at_utc` when the torrent exits `ResolvingMetadata`
- download start stamps `download_started_at_utc`
- completion stamps `download_completed_at_utc`
- seeding stamps `seeding_started_at_utc`
- latest summary fields refresh only when meaningful values changed
- `last_updated_at_utc` only advances when the history row is actually written
- `final_download_path` is no longer populated during add flow

### Phase 4 - Callback Lifecycle Updates

Goal:

- capture callback history cleanly

Deliverables:

- update history on callback start
- update history on callback success
- update history on callback failure
- update history on callback timeout/finalization timeout
- tests for callback fields and retry semantics

Review points:

- confirm `latest_callback_status`
- confirm callback started/completed timestamp semantics
- confirm callback error behavior on retries

Estimated time:

- about 3-5 hours

Status:

- `Complete`

Delivered:

- history now mirrors the existing callback lifecycle state directly
- `callback_started_at_utc` is stamped when callback lifecycle enters `PendingFinalization`
- `callback_completed_at_utc` is stamped when callback reaches a terminal visible state
- `latest_callback_status` is latest-write-wins
- `callback_last_error` is latest-write-wins
- callback retry resets the active attempt timestamps and error state for the new attempt
- retry paths now update history immediately instead of waiting only for a later sync

### Phase 5 - Removal And Cleanup Retention

Goal:

- preserve history after active torrent removal

Deliverables:

- update history on manual remove
- update history on remove-with-data
- update history on automatic cleanup remove
- keep history row permanently
- tests for retention and removal flags

Review points:

- verify `removed_at_utc`
- verify `data_deleted`
- verify `removed_by_cleanup_policy`
- verify row survives active deletion

Estimated time:

- about 3-5 hours

Status:

- `Complete`

Delivered:

- manual remove now stamps `removed_at_utc`, `data_deleted`, `removal_reason`, and `removed_by_cleanup_policy`
- remove-with-data now records successful payload deletion in history
- automatic completed-torrent cleanup now stamps removal history and preserves the durable history row
- focused coverage now verifies:
  - manual remove retention
  - remove-with-data retention
  - automatic cleanup retention

### Phase 6 - Read API

Goal:

- make torrent history inspectable through the service boundary

Deliverables:

- history list DTO
- history detail DTO
- application service methods
- controller endpoints
- client methods if included in the same slice
- API tests

Review points:

- confirm endpoint shape
- confirm sort/filter baseline
- confirm detail payload is sufficient for future UI work

Estimated time:

- about 3-5 hours

Status:

- `Complete`

Delivered:

- `GET /api/history` now returns history rows with explicit UI-oriented filters:
  - `torrentName`
  - `categoryKey`
  - `state`
  - `removed`
  - `fromDate`
  - `toDate`
  - `take`
- `GET /api/history/by-torrent/{torrentId}` now returns a single history detail row
- API default ordering is `submitted_at desc`
- browser-side sorting remains the intended interaction model for later UI work
- `fromDate` and `toDate` are local-date filters, not UTC filters
- API responses map timestamps to local time for user-facing history reads
- string filters now use case-insensitive contains matching by default:
  - `torrentName`
  - `categoryKey`
  - `state`

### Phase 7 - Hardening And Gaps

Goal:

- close lifecycle gaps before any later UI work

Deliverables:

- review all update call sites
- add missing edge-case tests
- verify restart/recovery does not break history updates
- small documentation updates as needed

Review points:

- confirm no missed transition paths
- confirm no dependency on `activity_logs`
- confirm no `torrents` behavior change

Estimated time:

- about 2-4 hours

Status:

- `Complete`

Completed validation and fixes:

- persisted startup recovery now updates history when it normalizes active torrent state
- explicit pause, resume, refresh-metadata, and reset-metadata action paths now update history consistently in both persisted and MonoTorrent adapters
- history row creation is now idempotent so add-flow writes and sync observation writes cannot race into a duplicate-row host failure
- MonoTorrent stop handling now tolerates shutdown/disposal during teardown so regression tests do not fail on a disposed internal gate

Phase 7 focused verification:

- `dotnet build TorrentCore.sln`
- `dotnet test tests/TorrentCore.Service.Tests/TorrentCore.Service.Tests.csproj --filter "FullyQualifiedName~TorrentHistoryServiceTests|FullyQualifiedName~SqliteTorrentHistoryStoreTests|FullyQualifiedName~FakeRuntime_HistoryRow_TracksCoreLifecycleMilestones|FullyQualifiedName~AddMagnet_CreatesTorrentHistoryRow|FullyQualifiedName~PersistedRecovery_NormalizesHistoryState_OnStartup|FullyQualifiedName~FakeRuntime_PauseAndResumeWhileDownloading_PreservesPausedStateUntilResumed|FullyQualifiedName~MonoTorrentEngine_RefreshMetadata_RequestsDiscoveryRefresh_AndWritesEngineLog|FullyQualifiedName~MonoTorrentEngine_ResetMetadataSession_RecreatesManager_AndWritesEngineLog"`

## Delivery Slices

For execution and review, the phases group into four practical slices.

### Slice A

- Phase 1
- Phase 2

Outcome:

- the table exists and rows are created on add

Estimated time:

- about half a day

Status:

- `Complete`

### Slice B

- Phase 3

Outcome:

- history rows stay current through core torrent lifecycle changes

Estimated time:

- about one day

Status:

- `Complete`

### Slice C

- Phase 4
- Phase 5

Outcome:

- callback and removal history is complete and durable

Estimated time:

- about half a day to one day

### Slice D

- Phase 6
- Phase 7

Outcome:

- read API is available and the implementation is hardened

Estimated time:

- about half a day to one day

Status:

- `Complete`

## Recommended Review Cadence

Stop for explicit review after:

1. Phase 1
2. Phase 3
3. Phase 5
4. Phase 6

Those checkpoints provide meaningful validation without stopping after every tiny change.

## Progress Tracking

This document should be kept current as implementation proceeds.

At minimum, update it when:

- a phase starts
- a phase completes
- a scope or field decision changes
- an important lifecycle rule changes
- a later retention or UI follow-up is added

Suggested phase status tracking:

- `Pending`
- `In Progress`
- `Complete`

## API Direction

First slice should include read APIs for history.

Recommended surface:

- history list endpoint
- history detail endpoint by `torrent_id`

Implemented Phase 6 surface:

- `GET /api/history`
- `GET /api/history/by-torrent/{torrentId}`

Implemented filter shape:

- `torrentName`
- `categoryKey`
- `state`
- `removed`
- `fromDate`
- `toDate`
- `take`

Implemented filter behavior:

- `torrentName`: case-insensitive contains
- `categoryKey`: case-insensitive contains
- `state`: case-insensitive contains
- `removed`: exact boolean match
- `fromDate` / `toDate`: inclusive local-date filtering against submitted date

The API returns a default server ordering, and later UI sorting is expected to happen locally in the browser after refresh.

## Testing Requirements

The first implementation should include automated coverage for:

- creating a history row on add
- updating the row as metadata resolves
- stamping `download_started_at_utc`
- stamping `download_completed_at_utc`
- stamping callback start and callback terminal status
- preserving the history row after manual remove
- stamping `data_deleted` correctly when remove-with-data is used
- stamping cleanup removal fields correctly for automatic completed-torrent cleanup
- confirming history does not depend on `activity_logs`

## Out Of Scope Follow-Ups

Possible later work, but not part of this plan:

- history retention/cleanup policy
- history UI page in `TorrentCore.WebUI`
- secondary event-level history table
- export/reporting workflows
- auth/authz around history endpoints once the broader v1 auth model is defined

## Summary

The agreed direction is:

- add a separate `torrent_history` table
- keep one summary row per torrent lifecycle
- update that row explicitly through a dedicated history service as events happen
- preserve the history row after the live torrent is removed
- include the relevant operator-facing torrent information directly in the history row
- ship the first slice as persistence plus read API, with UI deferred
