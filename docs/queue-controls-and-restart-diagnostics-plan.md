# Queue Controls And Restart Diagnostics Plan

Status: Slices 0 through 7 completed on August 19, 2026. Slice 8 remains a separate cutover and recovery exercise.

This plan covers durable operator queue controls, explicit pause/resume behavior, and restart-safe queue diagnostics in
the native macOS client and the supported `TorrentCore.WebUI`. Payload-stale download rotation is specified separately in
[payload-stale-download-rotation-plan.md](payload-stale-download-rotation-plan.md).

## Outcomes

- A queued unresolved magnet or resolved download can be promoted with **Make Next**.
- Several Make Next requests form a durable ordered priority queue instead of replacing one another.
- A queued unresolved magnet or resolved download can be placed on **Hold** and automatically released only after no
  non-held runnable work remains queued.
- **Paused** remains a durable operator stop and is not replaced by Hold.
- Paused incomplete torrents can be resumed normally, resumed next, or resumed on hold.
- Queue positions, wait reasons, priority positions, and hold state remain accurate in the UI after Service restart.
- Every action preserves the configured combined active-work ceiling.

## Agreed Queue Semantics

### Capacity remains authoritative

- `MaxActiveDownloads` remains the combined ceiling for active downloads and metadata reservations.
- Existing metadata-reservation rules remain in force.
- Queue controls do not raise a configured limit.
- Active downloads are never displaced by Make Next. If every combined slot is a download, the promoted item waits at
  the head of the priority queue. The operator can pause an active download when immediate capacity is required.

### Durable order

- Each runnable queue entry has a durable ordinary queue order independent of its historical `AddedAtUtc` value.
- Existing live entries are backfilled deterministically using `AddedAtUtc` and torrent id.
- New magnets enter at the tail of ordinary order.
- Normal Resume enters at the tail at the time of the resume action; an old paused torrent does not jump ahead because
  of its original submission time.
- Queue positions are projections of durable ordering and current capacity. Position numbers do not need to be stored
  as mutable database values.

### Make Next

- Make Next applies to queued unresolved magnets and queued resolved downloads.
- Each request receives the next durable priority order. If one item is already priority 1, a later request becomes
  priority 2, then priority 3, and so on.
- Removing, pausing, holding, or admitting an earlier priority item causes the displayed positions behind it to close
  naturally without rewriting their relative order.
- When capacity is available, priority 1 starts. Later priority entries do not pass it.
- When capacity is full and at least one active metadata resolver can be displaced, the resolver closest to its normal
  metadata time-slice expiration yields and priority 1 starts immediately. This applies whether priority 1 needs
  metadata or is already resolved. Normally priority 1 is the item just selected; if earlier priority intent is
  already waiting, a later Make Next request remains priority 2 or later.
- The displaced resolver remains runnable and retains its durable metadata attempt/yield history.
- Make Next on a held item removes the hold and appends the item to the priority queue.
- Priority intent is consumed when the item is admitted; it is not permanent precedence for later retries.

### Hold

- Hold applies only to queued unresolved magnets and queued resolved downloads.
- Held work is excluded from admission while any non-held runnable work remains queued.
- Once no non-held runnable work remains queued, held items are automatically released in their existing durable
  ordinary order. Already active work may continue running.
- Automatic release consumes the hold. Work admitted from Hold does not become preemptible merely because new work is
  submitted later.
- Manual release removes the hold without changing the item's ordinary order.
- Holding a priority item removes its priority intent.
- Held entries remain visible with an explicit status and their order among held work.

### Pause and resume

- Paused means an indefinite, explicit operator stop. Paused torrents are excluded from admission, metadata rotation,
  download rotation, and automatic Hold release until an operator resumes them.
- Paused and Held are separate, mutually exclusive intents. Pausing clears pending priority or hold intent but
  preserves payload, metadata, recovery, and history data.
- For an incomplete paused torrent, the UI exposes three atomic choices:
  - **Resume**: make runnable at the tail of the ordinary queue without preempting active work.
  - **Resume Next**: make runnable and append to the durable priority queue; the normal Make Next displacement rules
    apply.
  - **Resume on Hold**: make runnable at the tail of ordinary order with Hold set.
- Existing normal resume behavior for terminal/error recovery and completed seeding lifecycle remains outside these
  queue-specific choices. Queue controls must not turn completed content into incomplete download work.

## Persistence Model

Use the next additive SQLite migration. The implementation may refine names to match existing conventions, but it
must durably represent these facts:

- ordinary queue order
- optional Make Next priority order
- held/not-held queue intent

The persistence operation that allocates ordinary or priority order must be serialized and atomic. It must not rely on
wall-clock timestamps being unique. Recovery must not replace these values from in-memory manager enumeration order.

Migration requirements:

- preserve all existing torrent and history rows
- backfill stable ordinary order from `AddedAtUtc`, then torrent id
- default priority and Hold to absent
- leave operator-paused torrents paused
- prove migration idempotence through the existing schema-migration test path

## Shared Queue Policy And Diagnostics

Queue ordering and diagnostics must come from one testable policy used by the MonoTorrent adapter, persisted/fake
adapter, list/detail projections, and queue reconciliation. This prevents the UI from reporting an order different
from the scheduler's order.

The projection must distinguish:

- `Priority #N`
- ordinary metadata queue position
- ordinary download queue position
- `Held` and position among held work
- waiting for metadata capacity
- waiting for download capacity
- paused by operator

Priority position is global across unresolved and resolved priority entries, while ordinary queue position continues
to describe the applicable metadata or download lane. Every position is recalculated after admission, pause, hold,
removal, completion, settings changes, and recovery.

The post-restart disappearance observed on August 19 must first be reproduced through the real persistence and
recovery path. The fix must be made at the layer proven to lose the diagnostic state; the plan does not assume whether
the defect is recovery projection, API mapping, client mapping, or WebUI refresh state.

## Public Boundary And UI

Preserve the existing no-body `POST /api/torrents/{id}/resume` behavior as normal Resume. Add explicit mutation
operations for Make Next, Hold, release Hold, Resume Next, and Resume on Hold. Exact route names may follow the current
controller conventions, but each operation remains atomic and returns structured `404`, `409`, and service-unavailable
errors through the existing problem-details path.

Summary and detail contracts must expose enough state for a thin client to render behavior without reconstructing
policy, including:

- queue and priority positions
- Hold state and held order
- wait reason
- capabilities for Make Next, Hold, release Hold, Resume, Resume Next, and Resume on Hold

The WebUI adds row and selected-item actions only when the corresponding capability is true. It shows priority, held,
and applicable queue/wait information in both normal refresh and post-restart recovery. Reversible queue actions do
not require a destructive confirmation dialog, but failures remain visible through the existing action-error path.

Successful and failed mutations use the existing persisted activity-log service, not `ILogger`. Events include the
torrent id, action, prior queue intent, resulting queue intent, and any displaced metadata resolver.

## Sliced Delivery Plan

### Slice 0: Repository Safety Gate And Baseline

Status: completed on August 19, 2026.

#### Recorded Baseline

- Baseline source commit: `f0f254011f49f8f23ffe1d1fe26840992c75ee60`.
- Service version: `0.7.0`.
- WebUI version: `0.7.0`.
- Public API contract version: `1`.
- Normalized committed OpenAPI SHA-256:
  `5b296aad58910c50ca0a601bad618200024e1468bb08f92f3377bf04d6de726a`.
- `dotnet build TorrentCore.sln`: succeeded with zero warnings and zero errors.
- `dotnet test TorrentCore.sln --no-build`: all 319 tests passed with no failures or skips.
- All previously outstanding recovery documentation and the approved queue/load planning documents were committed and
  pushed to `origin/main` before implementation began.

#### Work

- Inspect staged, unstaged, and untracked files before any implementation change.
- Remind the operator to commit and push all outstanding commits and staged work.
- Stop until the operator confirms the intended work is committed and pushed, or explicitly identifies changes that
  must remain outside the implementation commit.
- Record the baseline commit, Service/API version, build result, test count, and normalized OpenAPI baseline.

#### Acceptance

- No existing work is silently included, overwritten, reset, or discarded.
- The implementation starts from an operator-confirmed baseline.
- `dotnet build TorrentCore.sln` and `dotnet test TorrentCore.sln` pass before behavior changes.

### Slice 1: Reproduce And Repair Restart Queue Diagnostics

Status: completed on August 19, 2026. The reported disappearance was traced to the native macOS torrent table's
persisted customization hiding the Wait column. The Service, list/detail API mappings, WebUI, and native mappings all
retained the diagnostics. Unhiding Wait restored the native display, so no runtime or client behavior was changed.
Restart characterization now covers unresolved and resolved queues at the combined active-work ceiling.

#### Work

- Add a persistence/recovery characterization containing queued unresolved and resolved torrents with active work at
  the configured ceiling.
- Prove list and detail diagnostics immediately before restart and after constructing a fresh Service/adapter over the
  same database.
- Trace the Service DTO, client adapter, and WebUI refresh mapping to locate the observed loss.
- Repair only the proven diagnostic/recovery defect before adding new queue controls.

#### Acceptance

- Existing queue numbers and metadata/download wait reasons match before and after restart.
- Recovery continues dispatching the same durable order.
- The regression fails against the old behavior and passes with the repair.
- No queue-control schema or behavior is introduced in this slice.

### Slice 2: Durable Queue Intent And Migration

Status: completed on August 19, 2026.

#### Work

- Add durable ordinary order, priority order, and Hold state to torrent snapshots and SQLite mappings.
- Add transactional allocation for ordinary and priority order.
- Backfill existing rows deterministically and update persistence round-trip fixtures.
- Define state-transition helpers so priority and Hold cannot coexist and Paused cannot retain either intent.

#### Acceptance

- Existing databases migrate without losing torrent, payload, callback, category, recovery, or history data.
- Ordinary, priority, Hold, and Paused intent round-trip through a fresh store instance.
- Repeated migrations are safe.
- Same-time priority requests still retain request order.

### Slice 3: Shared Ordering And Diagnostic Policy

Status: completed on August 19, 2026.

#### Work

- Extract a pure ordering/admission projection shared by production, persisted/fake, and diagnostic paths.
- Model global priority order, applicable ordinary lane order, held order, and current combined-capacity constraints.
- Replace duplicated AddedAt-only ordering where it would conflict with durable operator intent.
- Add table-driven policy tests for mixed unresolved/resolved queues and capacity combinations.

#### Acceptance

- Scheduler and list/detail diagnostics return the same next eligible item and positions from the same inputs.
- Priority items precede ordinary queued work subject to metadata and combined-capacity rules.
- Held work is excluded until the agreed release condition is met.
- Existing behavior is unchanged when no priority or Hold state exists, except for the intentional normal-Resume tail
  ordering introduced later.

### Slice 4: Queue-Control Contracts And Client Operations

Status: completed on August 19, 2026.

#### Work

- Add Service/application/engine operations for Make Next, Hold, and release Hold.
- Add queue state and capability fields to summary/detail contracts.
- Extend `TorrentCore.Client`, WebUI adapters, OpenAPI coverage, and all existing callers.
- Add structured state-conflict validation and persistent action logs.

#### Acceptance

- Unsupported actions return deterministic `409` responses without partial mutations.
- Make Next on Held atomically clears Hold and assigns the next priority order.
- Hold on Priority atomically clears priority and retains ordinary order.
- API, client, and OpenAPI contract tests agree.

### Slice 5: Runtime Make Next And Metadata Displacement

Status: completed on August 19, 2026.

#### Work

- Consume priority work during serialized reconciliation.
- When necessary, yield the active metadata resolver closest to time-slice expiration.
- Preserve the displaced resolver's attempt/yield history and reconcile priority 1, whether unresolved or resolved, in
  the same serialized operation.
- Consume priority intent only on successful admission.

#### Acceptance

- A free compatible slot starts priority 1 without displacement.
- A full combined set containing a resolver yields exactly one qualifying resolver and starts the selected item.
- Six active downloads cause no automatic download stop; priority order waits visibly.
- Multiple Make Next requests start in request order as capacity becomes available.
- Restart between request and admission preserves the same priority order.

### Slice 6: Hold And Explicit Resume Modes

Status: completed on August 19, 2026.

#### Work

- Enforce Hold exclusion and automatic/manual release through the shared policy.
- Keep Pause as an indefinite operator stop and clear queue-control intent on pause.
- Keep normal Resume backward compatible while assigning a new ordinary tail position.
- Add atomic Resume Next and Resume on Hold operations for incomplete paused torrents.
- Preserve non-queue lifecycle behavior for paused completed/seeding and resumable error states.

#### Acceptance

- Held entries remain queued while any non-held runnable work is queued.
- Multiple held entries automatically release in their original durable order.
- Pause never auto-releases; all three incomplete-torrent resume choices survive restart.
- Normal Resume does not displace active work or regain its historical AddedAt position.
- Resume Next and Resume on Hold cannot transiently enter the wrong queue between writes.

### Slice 7: Native And WebUI Queue Controls

Status: completed on August 19, 2026. The native macOS client is the primary operator surface for these controls;
the WebUI provides the same actions as a secondary surface.

#### Work

- Add Make Next, Hold, release Hold, Resume, Resume Next, and Resume on Hold actions using Service capabilities in
  both the native macOS client and WebUI.
- Add priority, ordinary queue, held-order, and wait-reason indicators to rows and selected details.
- Preserve action state correctly across polling, selection refresh, Service restart, and temporary request failure.
- Add focused component/adapter tests using mixed queue fixtures.

#### Acceptance

- The operator can distinguish Paused, Held, Priority, ordinary queued, resolving, and downloading states.
- Actions disappear or disable when the Service says they are invalid.
- A restart does not remove queue information from the UI.
- The UI does not implement its own scheduler or infer capabilities from display text.

### Slice 8: Recovery Matrix, Documentation, And Cutover

#### Work

- Run restart tests at each important transition: ordinary queued, priority queued, held, displaced resolver, paused,
  and each resume mode.
- Test live setting reductions, completion, removal, and VPN lifecycle recovery against queue intent.
- Update architecture, database, operator settings, troubleshooting, and testing documentation with implemented facts.
- Move this plan to `docs/archive/` only after the active documentation becomes the source of truth.

#### Acceptance

- Full solution build and tests pass, including OpenAPI synchronization.
- A copied-database recovery exercise shows identical durable intent and correct recomputed positions.
- No active ceiling is exceeded and no active download is displaced by Make Next.
- Activity logs explain operator mutations and resolver displacement without per-tick log noise.

## Out Of Scope

- Changing the configured active-work limits automatically.
- Replacing or disabling PEX.
- General per-tick persistence optimization.
- Tracker-announce timeout enforcement.
- Metadata restart staggering.
- Changing callback outcome severity.
- Automatically preempting active downloads for an operator priority request.
