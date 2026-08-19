# TorrentCore Architecture

## Purpose

TorrentCore is a standalone torrent engine product built with C# 14 and .NET 10.

It is intentionally separate from TVMaze.

## Current Product Shape

Primary components:

- `TorrentCore.Service`
- `TorrentCore.WebUI`
- `TorrentCore.Contracts`
- `TorrentCore.Client`
- `TorrentCore.Core`
- `TorrentCore.Persistence.Sqlite`

On Arm64 macOS, the Service can be represented by the background-only `TorrentCoreService.app`. Its native launcher is
the stable process identity seen by macOS and VPN split-tunneling controls; the framework-dependent .NET Service remains
the child runtime. The launcher executes that helper from the sealed bundle while using `~/TorrentCore/Service` for
host-local configuration and installed version metadata. Packaging changes process representation only and does not
move SQLite, cache, download, category, or callback state into the sealed app bundle.

The supported operator UI is `TorrentCore.WebUI`. On Arm64 macOS it is represented by the background-only
`TorrentCoreWebUI.app`; bundled runtime and static assets remain sealed while host-local configuration stays under
`~/TorrentCore/WebUI`. The managed DMG treats Service and WebUI as one install, backup, recovery, and verification
unit. The native `TorrentCore.app` remains a separate drag-to-Applications client.

Removed surfaces:

- `TorrentCore.Web`
- `TorrentCore.Avalonia`

## Ownership Boundary

TorrentCore owns:

- engine lifecycle
- magnet metadata resolution
- torrent state and persistence
- queue policy and admission
- download paths
- category definitions and routing
- completion callback invocation
- file finalization checks
- seeding policy and stop conditions
- logs and diagnostics
- host-local runtime settings

TVMaze may:

- choose a TorrentCore host
- submit a magnet with a stable category key
- show lightweight torrent state
- pause, resume, or remove through the public boundary
- treat TorrentCore's completion callback as the authoritative downstream-readiness signal
- deep-link into the dedicated TorrentCore UI

TVMaze must not own:

- primary torrent administration UX
- engine configuration
- deep queue policy
- storage-path policy
- category administration
- callback command configuration
- engine persistence
- engine diagnostics

## Service Boundary

External clients should talk to TorrentCore only through stable HTTP contracts or the versioned client library.

The service reports its additive native-client contract version through `apiVersion` on health and host-status
responses. Version `1` is the current contract. Clients may tolerate a missing value while older private installations
are being updated, but must reject a future version they do not understand.

Host status reports the Service semantic version separately from its build identity. `serviceVersion` comes from the
Service assembly version; optional `serviceBuild` is the full Git commit embedded by the .NET SDK in the assembly
informational version. Operator clients shorten the commit for display but retain the full value in the HTTP contract.
Keeping the build field optional lets updated clients continue to inspect older private Service installations.

History and log filter choices come from dedicated database-backed filter-options endpoints. They return distinct
values independently of the current grid query and row limit. Operator clients load these choices when a grid opens;
normal grid filtering and periodic row refreshes do not reload them.

The maintenance boundary exposes explicit date-based log and history cleanup operations. Each selected date is
interpreted as Service-local midnight and used as an exclusive cutoff; future dates are rejected. Both operations
protect rows associated with torrent ids still present in the live `torrents` table. Log cleanup can also remove old
service-level rows, while history eligibility is based on the row's last-updated timestamp. Successful operations
write a service audit log after deletion.

The WebUI stays a thin client over service contracts. It must not:

- call MonoTorrent directly
- mutate persistence directly
- bypass service APIs for operator workflows
- embed engine or recovery policy that belongs in the service host

The Service has one serialized VPN connection coordinator over its public-IPv4 probe, execution gate, and restartable
MonoTorrent lifecycle. The probe compares an observed address with the configured direct-ISP CIDRs: matching any
configured range means direct ISP access, while matching none means validated access without claiming a specific VPN
provider. When validation is enabled, startup leaves MonoTorrent inactive and the API available until the immediate
first check succeeds. Ready and degraded checks use their independent configured intervals and never overlap.

A routine check does not close the execution gate or publish degraded state. A failed check closes admission before
suspending and disposing MonoTorrent. A later successful degraded check recreates and recovers MonoTorrent before
reopening admission. Activation failure retries the engine directly at the degraded interval without repeating an
already-successful VPN check. Supported settings updates wake the coordinator for immediate enable/disable behavior;
other policy changes take effect on the next scheduled check without resetting the current countdown.

The coordinator's live host snapshot also records the last completed check, preserved last successful check, next
automatic retry, current observed public IPv4 when the latest result supplied one, and a sanitized failure summary.
State-change DB logs are driven by enabled/phase/reason transitions and include both previous and new values; timestamp
updates from unchanged routine results do not create state-change rows. `/api/health` remains independent of this
processing state and stays successful while the process and persistence boundary are healthy.

Synchronization timing summaries are an optional DB-log output, not part of engine scheduling. The synchronization
loop always runs. When `RuntimeTickDurationSummaryEnabled` is off or torrent processing is VPN-degraded, it does not
accumulate or write `runtime.tick.duration_summary`; re-enabling or returning to ready begins a new one-minute window.

## Engine Dependency

- TorrentCore pins MonoTorrent `3.0.2` as the production baseline.
- Do not use `3.0.3-alpha.unstable.rev0049`; its outgoing-connection retry path can leak the host-wide open-connection
  count after encryption or handshake failures and eventually suppress every new connection attempt.
- Evaluate later MonoTorrent prereleases as isolated upgrades with a sustained peer-churn test before changing the
  production baseline.

## Queueing And Lifecycle Rules

- TorrentCore accepts and persists incoming magnets even when runtime capacity is full.
- Active metadata-resolution and active-download limits control execution, not admission.
- A host-level execution gate separates durable magnet admission from MonoTorrent execution. Closing it immediately
  prevents new engine operations and drains work already admitted before closure. Magnet validation, category/root
  checks, SQLite persistence, and history creation remain available without creating a MonoTorrent manager.
- While the execution gate is closed, accepted magnets persist as queued runnable intent. Torrent list/detail and
  history reads remain available, while engine-dependent reads and torrent mutations return
  `503 vpn_egress_not_validated`. VPN degradation is not represented by rewriting each torrent's queue position,
  desired state, or wait reason.
- `IMonoTorrentLifecycle` serializes explicit activation and suspension. Activation creates a new `ClientEngine` from
  the latest effective SQLite settings and recovers durable torrents. Suspension drains admitted work, cancels and
  resets adapter-owned background coordinators, persists current progress while retaining torrent state and desired
  intent, zeroes live peer/rate values, disposes the engine, and clears every manager/runtime registry.
- VPN-triggered suspension does not call MonoTorrent's graceful stop path because that path performs final tracker
  announces. Normal Service shutdown may stop gracefully before disposal. Downloaded files and the external
  `monotorrent-cache` remain in place for later recovery.
- A degraded reboot never needs an in-memory engine state to survive. Persisted validation enablement forces a fresh
  check before activation. Magnets accepted while degraded remain in SQLite and recover after a later successful check.
- Every active metadata resolution reserves one future download slot. The effective metadata-resolution limit is the
  lower of the configured metadata limit and the download capacity not already claimed by resolved runnable torrents.
  This prevents MonoTorrent's automatic metadata-to-download transition from oversubscribing the download limit.
- Queue reconciliation releases excess metadata reservations before starting resolved queued downloads. The combined
  active metadata-resolution and download count is therefore not increased beyond `MaxActiveDownloads` by
  metadata-to-download handoffs, live setting reconciliation, or pause/resume queue reshuffling. When an operator
  lowers the limit below current activity, reconciliation stops the existing excess before admitting replacement work.
- Queued torrents wait inside TorrentCore until slots open.
- Runnable work has a durable ordinary queue order. New magnets and normal resumes enter at the tail; migration
  backfills older rows by added time and torrent id.
- Make Next adds a durable global priority order across unresolved and resolved queued work. Admission consumes the
  priority. It may yield one active metadata resolver closest to time-slice expiration, but it never displaces an
  active download or raises the combined active-work ceiling.
- Hold excludes queued work until no non-held runnable work remains queued. Automatic release consumes the hold and
  preserves ordinary order. Pause remains a separate indefinite operator state and clears priority and Hold intent.
- Incomplete paused work can Resume at the ordinary tail, Resume Next into priority order, or Resume on Hold. These
  transitions and their queue diagnostics survive Service restart.
- An unresolved magnet may occupy a metadata reservation for at most the configured metadata-resolution time slice
  while another unresolved magnet is waiting. Expired resolvers yield only enough slots to dispatch waiting work.
  Active non-expired attempts stay in place, never-tried magnets run before previously yielded magnets, and yielded
  magnets retry in oldest-yielded order. A lone unresolved magnet is not rotated.
- Metadata attempt and last-yield timestamps are durable. Recovery refresh, restart, and reset actions do not extend
  the active time slice; an operator pause releases the attempt clock.
- Automatic recovery applies only to torrents occupying active metadata-resolution or download slots; queued and
  terminal torrents do not run recovery actions.
- Repeated cold recovery cycles use bounded progressive backoff (`1x`, `2x`, `4x`, then `8x`) from the configured
  stale and restart-delay windows. Useful peer or transfer activity immediately restores the normal cadence.
- Downloads that remain continuously cold beyond the configured long-cold threshold switch to one recovery action per
  configured interval, alternating peer refresh and restart. Useful activity exits long-cold mode immediately.
- Automatic restart transitions preserve accumulated cold-recovery state; time spent waiting in a runnable queue is
  excluded from the cold duration.
- The continuous cold timestamp is persisted across service restarts. When the configured abandonment window expires,
  TorrentCore removes the torrent and partial payload without invoking completion processing, prunes torrent-scoped
  logs, and retains the cleanup reason in torrent history.
- History stores a structured cold-abandonment removal kind. The operator UI surfaces retained abandonments through a
  dedicated outcome filter and summary that do not depend on the original submission date.
- Incomplete content is distinguished from completed content by explicit policy and engine-observed file state, not by guesswork.
- Torrent list and detail reads do not wait behind serialized MonoTorrent lifecycle work. They project from a
  concurrency-safe manager snapshot and use persisted state when a manager is temporarily absent during a transition.
- Automatic metadata-session resets run as single-flight host-wide background work. The affected manager is detached
  with a brief registry mutation, while stop, remove, and recreation run outside both global engine gates. Other
  torrents continue synchronizing. A reset exceeding the configured stuck threshold remains quarantined until the
  underlying MonoTorrent operation actually finishes and opens a fixed five-minute circuit breaker. After cooldown,
  one half-open reset probe may run. Failed recreation retries every five seconds until success or service shutdown.
- Manual metadata-session reset remains a synchronous operator action.
- Forced recovery announces run outside serialized synchronization, are deduplicated per torrent, and use a bounded tracker-announce window.

## Category Routing And Callback Rules

- Category keys are stable API identifiers such as `TV`, `Movie`, `Audiobook`, and `Music`.
- Clients submit category keys, not raw filesystem paths.
- TorrentCore resolves the effective download root and callback routing at add time.
- Both the host default and category-specific download roots must be accessible before a magnet is accepted.
- Category edits affect future torrents only. Existing torrents keep their persisted routing values.
- If `CategoryKey` is omitted, TorrentCore currently falls back to the host-global `DownloadRootPath`.

Completion callback rules:

- TorrentCore reuses the shared TVMaze-style callback entrypoint instead of inventing a second callback stack.
- TorrentCore invokes the callback only after the downstream-visible final payload path is ready.
- A successful process start completes dispatch; TorrentCore does not wait for the callback process to exit.
- Downstream completion is reported independently through the callback feedback API and may arrive much later.
- TorrentCore does not treat the engine's first internal completed edge as sufficient by itself.
- Transient MonoTorrent progress values during starting, hashing, queued, or downloading states do not establish completion; completion timestamps require a completed or seeding lifecycle state.
- TorrentCore may expose the validated final payload path through `TORRENTCORE_FINAL_PAYLOAD_PATH`.
- TorrentCore does not delete final payload files during callback finalization.
- Downstream systems must not infer payload readiness by independently scanning download paths or filenames.
- Final-payload filesystem visibility probes run as deduplicated per-torrent background work and never hold serialized engine synchronization.
- Completion-time MonoTorrent stops run as deduplicated per-torrent background work when the seeding policy has been
  reached. Callback dispatch remains gated until both payload visibility and the manager stop succeed.
- Applying a seeding stop policy is a durable per-torrent transition. The service records
  `torrent.seeding.stopped_policy` only when that transition is first applied, not on later synchronization ticks or
  after restart.
- Serialized synchronization consumes completed probe and stop results and remains the sole owner of persisted
  callback-state mutations.
- Completed, stopped torrents leave the per-tick synchronization path after callback dispatch or terminal callback state.
- Waiting-for-feedback torrents are updated by the feedback API rather than periodic filesystem polling.
- Finalization visibility checks run only at the completion edge, while pending finalization, or during an explicit retry.

See [docs/decisions/current-decisions.md](decisions/current-decisions.md) for the extracted appendix with the current durable routing, callback, and history rules.
