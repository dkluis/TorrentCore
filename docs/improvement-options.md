# Improvement Options

This document records improvement options identified during the August 19, 2026 production load evaluation. Queue
diagnostics, Make Next, and Hold have since been implemented; their current behavior is defined by the code and
the active documentation, especially [architecture.md](architecture.md) and
[operator-settings.md](operator-settings.md).

## Evaluation Context

- TorrentCore accepted 54 magnets during the morning, including a 45-magnet cohort submitted between 07:06 and 07:15
  local time.
- The configured combined ceiling of six active downloads and metadata resolutions behaved as intended and should
  not be increased automatically. The operator can change the limit when appropriate.
- Metadata time-slice rotation continued moving unresolved magnets through available metadata reservations.
- Synchronization timing remained healthy under the observed load. Performance Timing Summaries did not identify a
  sustained service-processing bottleneck.
- Several downloads connected to peers without making meaningful payload progress. Because current download recovery
  treats an open connection as useful activity, intermittent peer connections can clear the cold state even when
  downloaded bytes do not advance.
- Manually pausing unproductive downloads released combined capacity and allowed later magnets to resolve and begin
  downloading. At least one newly admitted download became productive.
- After a Service restart, durable torrent state, queue ordering inputs, metadata rotation clocks, and queue diagnostics
  remained intact, and queue processing continued. The apparent loss of queue information was a persisted native-table
  customization hiding the Wait column; unhiding it restored the existing values.

## Payload-Stale Download Rotation (Implemented And Validated)

Evaluate a download-yield mechanism analogous to metadata time slicing, but base eligibility on payload progress
rather than peer presence alone.

Desired properties:

- apply only while other runnable work is waiting
- do not yield a download that is making meaningful downloaded-byte progress
- do not treat a peer connection by itself as proof of useful download activity
- return an automatically yielded download to runnable queue state rather than recording operator pause intent
- preserve downloaded data, recovery history, and yield state across Service restarts
- leave a stale download active when no other runnable work is waiting
- preserve the configured combined active-work ceiling

The agreed interval is a separate live setting with a 30-minute default and a 1-through-60-minute range. Downloaded-byte
growth is the only progress signal: peer presence and reported speed do not reset it. The active clock survives Service
restart and accumulates even when nobody is waiting, but a download yields only when eligible runnable work is queued.
Yielded downloads wait behind ordinary work that has not already been automatically yielded, including unresolved
magnets, and retry oldest-yielded first. The existing long-cold recovery and abandonment windows serve recovery and
cleanup; they do not provide timely queue fairness during a large burst. See the completed delivery record in
[archive/payload-stale-download-rotation-plan.md](archive/payload-stale-download-rotation-plan.md).

The Service now persists and enforces that policy. Both Settings screens expose the live interval; torrent details in
both UIs expose the durable clock, last yield, and current automatic-retry status, while WebUI also provides a compact
sortable No Progress column. The August 20 copied-database cutover audit confirmed 21 successful yields across 15
torrents, six retry-tail readmissions followed by second yields, no rotation failures, and nine completed downloads
totaling 19.53 GiB.

## History Last-Updated Filtering (Implemented)

Deliver the History recency correction with payload-stale download rotation. Replace Submitted with Last Updated in
the native macOS and WebUI History tables, apply From and Through to `last_updated_at_utc` for every history record,
and default to Last Updated descending. This lets an item submitted on an earlier day appear when it completes, receives
callback feedback, is removed, is abandoned, or is otherwise updated during the selected range. The existing history
timestamp is sufficient; no additional history column is needed. The delivery details and acceptance criteria are in
[archive/payload-stale-download-rotation-plan.md](archive/payload-stale-download-rotation-plan.md#history-last-updated-view).

## Restart-Safe Queue Diagnostics

Keep queue-position numbers and wait-reason text visible after Service recovery.

Observed behavior:

- queued torrent rows, original submission timestamps, desired states, metadata attempt timestamps, and metadata yield
  timestamps survived restart
- post-restart reconciliation continued yielding resolvers and starting replacement magnets
- the native UI's persisted column customization had hidden the former Wait column; current UIs expose separate
  Reason, Queue #, Priority #, and Held # columns
- activity logs record transitions but do not store a current numbered queue snapshot

Required outcome:

- queue position and wait reason remain accurate and visible before and after restart
- UI indicators must survive restart from the operator's perspective, whether implemented through durable storage or
  reliable reconstruction from durable queue state
- recovery must not rewrite queue priority, hold state, operator pause intent, or metadata time-slice history

## Make Next Queue Control (Implemented)

Native macOS and WebUI actions let an operator promote a queued unresolved magnet or resolved download.

Agreed behavior:

- when a metadata resolver is active, return the resolver closest to normal time-slice expiration to the runnable
  queue and start the selected item immediately
- yielding for this operator action must preserve the displaced resolver's durable attempt and yield history
- when all combined slots are occupied by downloads, do not interrupt a download; assign the selected magnet priority
  position 1 and start it when the next slot becomes available
- the operator may manually pause a download when immediate capacity is needed
- priority state and its UI indication must survive Service restart
- this action does not raise or bypass the configured combined active-work ceiling
- later Make Next requests become priority 2, priority 3, and so on without replacing earlier requests
- Make Next on a held item removes Hold before assigning priority
- unresolved priority work keeps a protected metadata reservation for the configured number of attempts; unsuccessful
  attempts rotate to the priority tail and final expiry moves to the ordinary tail

## Hold In Queue Control (Implemented)

Native macOS and WebUI hold controls defer queue entries until ordinary queued work has been admitted.

Agreed behavior:

- held entries are excluded from normal admission while any non-held runnable entries remain queued
- a held entry becomes eligible automatically when no non-held runnable entries remain queued; other entries may by
  then be downloading, completed, paused, or removed
- the operator can remove a hold manually
- hold state and its UI indication survive Service restart
- holding an entry does not remove it, discard downloaded data, or erase metadata attempt and yield history
- held entries remain visible in the queue with an explicit hold status
- holding a priority item removes it from the priority queue
- held items retain their durable ordinary order

Paused remains separate from Held. Pause is an indefinite operator stop, while Hold is runnable work deferred until
ordinary queued work is admitted. An incomplete paused torrent can Resume at the ordinary queue tail, Resume Next, or
Resume on Hold. The implemented policy is documented in [architecture.md](architecture.md); the completed delivery
record is in
[archive/queue-controls-and-restart-diagnostics-plan.md](archive/queue-controls-and-restart-diagnostics-plan.md).

## Persistence Scalability

Evaluate reducing unchanged per-tick persistence work. The current synchronization path reads and updates each
nonterminal manager every second, including queued managers whose durable state may not have changed.

The observed load remained healthy, but one 45-manager snapshot-persistence phase took approximately 2.86 seconds.
Change detection, less frequent persistence for inactive queue entries, or grouped writes could reduce linear database
work and WAL checkpoint pressure at larger queue sizes without reducing runtime observation frequency.

## Synchronization Outliers

Evaluate staggering metadata restart work that currently occurs synchronously during recovery processing. Multiple
approximately two-second restart operations can combine into a roughly four-second synchronization tick. The observed
ticks remained below the five-second slow-synchronization threshold, so this is a resilience option rather than an
identified production failure.

## Recovery Tracker Announce Timeout

Evaluate enforcing the configured recovery tracker-announce timeout as a real wall-clock bound. Observed tracker
announces continued for approximately 43 to 45 seconds despite receiving a ten-second cancellation token.

These operations run in deduplicated background work and did not block synchronization, but their extended lifetime
can retain resources and makes the timeout diagnostic misleading. The final payload-rotation load audit observed 197
slow tracker announcements: 119 failed after averaging 41.5 seconds and 78 succeeded after averaging 38.5 seconds; the
maximum was 88.2 seconds. Rotation still occurred at the configured boundary, nine downloads completed, and only one
two-second synchronization-gate wait was slow. Treat this as non-blocking unless it recurs with healthy current
torrents, causes sustained CPU or memory growth, delays queue reconciliation, or continues after stale work is gone.

## Diagnostic Quality

Potential diagnostic improvements:

- include manager count and major synchronization-phase summaries in minute timing records so sub-threshold outliers
  can be attributed without relying on coincidence
- distinguish successful callback dispatch from warning outcomes; observed callback dispatches completed successfully
  but were stored at warning level because the successful outcome string did not match the logging helper's expected
  value
- expose enough queue-diagnostic state to verify priority, hold, and automatic-yield behavior without depending only
  on transient UI rendering

## Current Non-Issues And Operator Choices

- The combined active-work ceiling is intentional protection against overloading Service processing.
- PEX remains intentionally enabled because it has materially improved magnet resolution in production. The evaluated
  database contained no `TooManyOpenConnections` evidence or PEX-related process failure.
- Performance Timing Summaries are optional and currently disabled. Slow-operation and failure diagnostics remain
  active independently.
- The prior 1 KiB/s global upload cap was changed to unlimited and applied through a Service restart. Subsequent
  observed download throughput improved substantially, although the available evidence does not isolate upload policy
  from refreshed peer sessions or swarm differences.
