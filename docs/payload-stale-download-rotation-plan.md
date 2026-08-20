# Payload-Stale Download Rotation Plan

Status: Slices 0 and 1 completed on August 20, 2026. Payload-clock evaluation and automatic yielding have not started.

This plan adds fair rotation for active downloads that receive no payload while other work is waiting. The same
workstream also corrects History recency so records are found and ordered by their latest lifecycle change instead of
their original submission. It depends on the durable ordering and shared admission policy documented in
[architecture.md](architecture.md).

## Outcome

An active incomplete download that has made no downloaded-byte progress for the configured interval may yield its slot
to waiting runnable work. The yielded torrent remains runnable, keeps its partial files and history, and retries later.
Peer connections and reported transfer rate do not count as payload progress.

## Agreed Policy

- Use a separate live runtime setting with a 30-minute default and an allowed range of 1 through 60 minutes.
- Determine progress only from an increase in downloaded payload bytes.
- Do not yield while no other eligible runnable work is waiting.
- Yield only enough stale downloads to admit waiting work.
- When several active downloads are eligible, yield the one with the oldest no-progress start first, using torrent id
  as the stable tie-breaker.
- Preserve the combined active-work ceiling and all metadata-reservation rules.
- Automatic yield is not operator Pause and does not create Held intent.
- A yielded download goes behind all ordinary work that has not already been automatically yielded, including
  unresolved magnets.
- Automatically yielded downloads retry in oldest-yielded order.
- Priority work remains ahead of ordinary and automatically yielded work. Held work remains governed by the Hold
  release rule.
- Re-admission gives a yielded download a new full no-progress interval.
- The active no-progress clock continues across Service restart.
- No-progress time accumulates whenever the download is active, even when no work is waiting. Expiration alone does not
  cause a yield; if eligible work later begins waiting, the already-stale download may yield immediately.
- Partial payload, recovery history, completion state, and yield history survive restart.
- Existing cold-download recovery and abandonment remain separate policies. Connections may continue to affect cold
  recovery without resetting the payload-only rotation clock.

## Durable State

Migration 22 represents:

- the start of the current active no-payload-progress interval
- the most recent automatic download-yield time
- whether the torrent is currently waiting in the automatically yielded class

The implementation must be able to distinguish an ordinary queued/resumed torrent from one waiting because automatic
download rotation yielded it. A historical last-yield timestamp alone is insufficient because a later operator Resume
returns the item to ordinary order while retaining history.

State transitions:

- first admission to an active download starts the no-progress clock
- any downloaded-byte increase restarts the clock
- time spent queued, held, or operator-paused does not advance an active-attempt clock
- automatic yield clears the active clock, records yield time, and marks the queued item automatically yielded
- re-admission clears the waiting-yield marker and starts a new active clock
- operator Pause clears the active clock and automatic-yield queue marker
- Resume, Resume Next, and Resume on Hold apply their explicit queue intent rather than restoring an old automatic-yield
  position
- completion, error, or removal clears active rotation eligibility without changing historical activity logs

## Selection And Admission

On each serialized reconciliation:

1. Project waiting work through the shared queue policy.
2. If no work is currently eligible to replace an active download, perform no rotation.
3. Find active runnable downloads whose downloaded bytes have not increased for the configured interval.
4. Yield at most the number needed to admit eligible waiting work.
5. Re-run the shared admission policy so priority, ordinary, automatically yielded, and held work are handled in the
   agreed order.

The scheduler must not use instantaneous speed, peer count, tracker results, or connection activity as substitutes for
downloaded-byte growth. A small positive byte increase is progress under the agreed zero-growth rule.

## Runtime Setting And Diagnostics

The runtime setting is `DownloadNoProgressTimeSliceMinutes`, separate from metadata time slicing and cold recovery. It
applies live. Once payload-clock evaluation is implemented, updating it re-evaluates eligibility on the next
reconciliation without restarting the Service.

Summary/detail diagnostics and the WebUI must make automatic rotation observable without presenting it as Pause:

- active no-progress duration or start time
- automatically yielded/waiting state
- last automatic yield time
- applicable queue position and wait reason

Use the existing persistent logging service. One event is written when a download yields, including torrent id,
downloaded bytes, no-progress duration, configured interval, prior state, replacement torrent when known, and resulting
queue disposition. Do not log every no-progress tick.

## History Last-Updated View

The native macOS UI and WebUI History tables currently filter by submission date. That hides records submitted on an
earlier day even when completion, callback feedback, removal, abandonment, or another lifecycle update occurred during
the selected range.

Agreed behavior:

- replace the History table's Submitted column with Last Updated
- apply the From and Through filters to `last_updated_at_utc` for every history record and outcome
- keep the existing inclusive Service-local calendar-date semantics for From and Through
- default the History table to Last Updated descending so the newest lifecycle changes appear first
- apply the same behavior in the native macOS UI and WebUI
- continue using the existing persisted `torrent_history.last_updated_at_utc`; this change requires no new history
  timestamp or history-table migration

## Final Policy Decisions

- Select the eligible active download with the oldest no-progress start first, with torrent id as the stable
  tie-breaker.
- Continue the active no-progress clock across Service restart, matching metadata time-slice durability. Ordinary
  queued downtime does not count.
- Accumulate no-progress time while active even when nobody is waiting. Yield only when eligible runnable work is
  waiting, but allow immediate yield when that work arrives after the interval has already expired.
- Validate the setting from 1 through 60 minutes, default it to 30 minutes, and do not use zero as a disable value.
- When migration 22 upgrades an existing active download, leave its new no-progress start null and begin a fresh full
  interval at the first post-upgrade active observation. Do not infer payload timing from general activity timestamps.

## Sliced Delivery Plan

### Slice 0: Shared Safety Gate And Characterization

Status: completed on August 20, 2026 from clean, pushed baseline
`4b8e81cd591b19dad0adce543ec51e20b3725254`. Six controllable-clock characterizations record connected-peer,
reported-rate, zero-peer/zero-rate, pause/resume, restart, and no-waiting-work behavior without adding a production
yield path.

#### Work

- Complete Slice 0 of the queue-controls plan before modifying source.
- Record current behavior for zero-byte downloads with peers, reported speed, no peers, pause/resume, restart, and no
  waiting work.
- Add a controllable clock and downloaded-byte observation fixtures without changing production scheduling.

#### Acceptance

- The operator has confirmed outstanding work is committed and pushed before coding starts.
- Characterization proves current recovery can treat connections as useful even without byte progress.
- No production rotation behavior exists yet.

### Slice 1: Setting And Durable Rotation State

Status: completed on August 20, 2026. `DownloadNoProgressTimeSliceMinutes` now defaults to 30, validates from 1 through
60, persists through the additive runtime-settings store, survives restart, and is mapped through the Service and
native client contracts. Migration 22 adds `download_no_progress_started_at_utc`,
`download_last_yielded_at_utc`, and `is_download_yielded`; existing rows begin null/null/false. Queue-intent
normalization clears active/yielded state for Pause, priority, Hold, and explicit Resume transitions while preserving
historical last-yield time.

#### Work

- Add the separate 30-minute live setting, with its 1-through-60 validation range, through options, runtime
  persistence, contracts, and effective settings mapping.
- Add the durable active no-progress, last-yield, and waiting-yield state to snapshots and SQLite.
- Extend state-store round-trip and migration coverage.

#### Acceptance

- Existing databases migrate without changing existing torrent intent or recovery history.
- The setting survives restart and applies live.
- Rotation state round-trips independently of `DownloadColdSinceUtc` and metadata timestamps.

### Slice 2: Payload-Progress Clock

#### Work

- Implement a pure transition function over prior snapshot, observed downloaded bytes, activity state, and current
  time.
- Integrate it with synchronized snapshot observation without using speed or peers as progress.
- Exclude queued, held, paused, completed, error, removed, and seeding states.

#### Acceptance

- Zero byte growth preserves the original active clock across ordinary synchronization ticks.
- Any positive byte growth restarts the interval.
- Peer connections and positive reported speed with no byte increase do not restart it.
- Pause and fresh re-admission receive the agreed clock behavior.

### Slice 3: Automatic Yield And Shared Admission

#### Work

- Select eligible stale downloads deterministically.
- Stop only enough selected managers to create capacity for currently eligible waiting work.
- Persist queued automatic-yield state before admitting replacements.
- Reconcile replacements and retries through the shared queue policy.

#### Acceptance

- No waiting work means no stop.
- Productive downloads never yield.
- Priority work is admitted first; ordinary never-yielded work precedes automatic retries; retries are oldest-yielded
  first; held work follows its release rule.
- A yield never changes DesiredState to Paused and never exceeds configured capacity.
- Multiple stale candidates yield only as many slots as can be used.

### Slice 4: Restart And Recovery Separation

#### Work

- Preserve active clock/yield state through a fresh Service and engine recovery according to the approved decision
  gates.
- Prove automatic recovery, long-cold cadence, abandonment, VPN suspension/recovery, manual Pause, and rotation do not
  reset or impersonate one another.
- Handle stop/start failures without leaving a torrent in a falsely yielded or doubly admitted state.

#### Acceptance

- Restart does not lose retry order or manufacture operator pause intent.
- A yielded torrent can later re-enter downloading with its partial payload intact.
- Recovery actions do not reset the payload clock without byte growth.
- Cancellation and manager-operation failures retain a recoverable durable state and structured diagnostics.

### Slice 5: API, Operator UIs, History Recency, Logging, And Setting Help

#### Work

- Expose rotation state and capabilities through summary/detail contracts and client mappings.
- Add the setting editor/help text to TorrentCore.WebUI.
- Display active no-progress and automatically yielded status in torrent rows/details.
- Change History range filtering to use `last_updated_at_utc` for every record.
- Replace Submitted with Last Updated in both History tables and make Last Updated descending the default order.
- Add yield activity events and focused WebUI/contract tests.

#### Acceptance

- Operators can distinguish automatically yielded, operator-paused, held, and ordinary queued torrents.
- UI state survives Service restart and polling refresh.
- A record submitted on an earlier day appears when its Last Updated value is inside the selected range, regardless of
  state or outcome.
- Both History tables show Last Updated and initially place the most recently updated record first.
- Setting validation errors use the existing structured error path.
- Logs explain each yield without creating per-tick noise.

### Slice 6: Load Matrix, Documentation, And Cutover

#### Work

- Exercise mixed loads containing productive downloads, connected zero-byte downloads, unresolved magnets, priority
  entries, ordinary resolved downloads, automatically yielded retries, held entries, and paused entries.
- Verify capacity changes above and below current activity.
- Update architecture, database, operator settings, troubleshooting, and testing docs with implemented behavior.
- Move this plan to `docs/archive/` only after active documentation becomes authoritative.

#### Acceptance

- Deterministic tests cover every ordering class and restart boundary.
- Full solution build and tests pass, including OpenAPI synchronization.
- A copied-database load exercise shows useful work advances without exceeding configured capacity or stopping a
  productive download.
- Existing metadata rotation, recovery, abandonment, callback, and completion behavior remains passing.

## Out Of Scope

- Treating low nonzero speed as stale.
- Using peer count as proof of payload progress.
- Abandoning or deleting payload because a download yields.
- Automatically raising active-work limits.
- Preempting a productive download for Make Next.
- Replacing the existing cold-recovery or abandonment policy.
