# Operator Settings

## VPN Egress Validation

TorrentCore can gate MonoTorrent processing on a scheduled public-IPv4 check while keeping the Service API available.
The implementation and rollout sequence are recorded in the
[VPN egress plan](torrentcore-service-app-vpn-egress-plan.md). Validation is disabled by default, so upgrading an
existing installation does not change torrent processing until an operator enables it.

### Enable VPN Egress Validation

- defaults to `false`
- when enabled, keeps the Service API available while preventing torrent processing until the first check succeeds
- turning it on starts an immediate check without preemptively pausing already-running torrents; a failed check performs
  the pause
- turning it off immediately attempts to restart paused processing without another VPN check
- applies live and does not require a Service restart

### Validation Endpoint

- defaults to `https://api.ipify.org?format=json`
- must be an absolute HTTPS URL with a host and without embedded credentials or a fragment
- applies live; the effective setting is loaded from SQLite on every settings read

### Direct ISP IPv4 CIDRs

- defaults to `47.0.0.0/8`
- accepts one or more IPv4 CIDRs, canonicalizes host addresses to their network address, and removes duplicates
- rejects IPv6 CIDRs and requires at least one range while validation is enabled
- CIDRs identify direct ISP egress; an observed address outside these ranges is validated egress, not proof of a
  particular VPN provider
- multiple CIDRs are supported so every known direct ISP range for a machine can be represented
- applies live

### Check Intervals And Timeout

- degraded check interval defaults to `60` seconds
- ready check interval defaults to `240` seconds
- request timeout defaults to `10` seconds
- all values must be positive, and the request timeout must be shorter than both intervals
- intervals are measured after the previous check and engine transition complete
- interval and policy edits apply on the next scheduled check without resetting the current countdown
- routine checks do not pause torrents or block operator pages while the result is pending

### Engine Suspension Timeout

- defaults to `10` seconds
- limits local MonoTorrent background-work draining and teardown after VPN validation fails
- must be positive and applies live
- does not impose a separate timeout on activation or durable recovery; failures remain degraded and retry later

### ExpressVPN Automatic Recovery

Automatic recovery is disabled by default and is effective only when VPN egress validation is enabled on a macOS
MonoTorrent host. It never changes ExpressVPN split-tunnel settings, regions, protocols, or application rules.

- `Disabled` performs no ExpressVPN reads, launches, disconnects, or connects
- `DirectIspOnly` permits recovery only after two consecutive checks detect a configured direct-ISP IPv4 range
- `AnyValidationFailure` permits recovery after two consecutive eligible failures, including endpoint and response
  failures
- MonoTorrent must be fully suspended before TorrentCore reads or changes ExpressVPN; a suspension failure blocks every
  provider action
- provider status and command success are diagnostic only; MonoTorrent starts again only after TorrentCore validates a
  non-ISP public IPv4 address

The recovery delay defaults to `180` seconds. It is both the startup grace period and the minimum interval between
disconnect/connect attempts. The unavailable launch delay defaults to `300` seconds and is measured before each of at
most two attempts to ask macOS to launch ExpressVPN. Reconnect and launch attempts have separate fixed limits of two.
After either category is exhausted, TorrentCore remains suspended and continues its normal degraded public-IP checks,
so a later manual recovery can still restore processing. All three settings apply live and reject unknown modes or
nonpositive delays.

The internal probe requires a successful JSON response shaped as `{ "ip": "address" }`, accepts IPv4 only, and limits
the body to 16 KiB. It distinguishes HTTP status, DNS, connection, TLS, HTTP protocol, and other HTTP failures for DB
diagnostics. The first completed outcome is logged; a later row is written only when the outcome or endpoint-failure
reason changes. Normal Service-shutdown cancellation is not logged.

When the coordinator closes the gate, magnet submission retains its normal validity, duplicate, category, and
download-root checks and persists accepted magnets without creating MonoTorrent managers. Torrent list/detail and
history reads remain available; engine-dependent reads and torrent mutations return
`503 vpn_egress_not_validated`. This is a host-level condition and does not rewrite individual torrent queue state.

VPN suspension preserves each torrent's state and desired intent, resets live peer/rate values to zero, and disposes the
entire engine without MonoTorrent's graceful final tracker announcement. A normal Service shutdown may use graceful
stop before disposal. Snapshot-write failures do not leave the engine running: the last committed SQLite state,
downloaded files, and usable cache remain for a later automatic recovery attempt. If the machine reboots while
degraded, enabled validation must be checked again before MonoTorrent activates; the API and persisted magnets
remain available in the meantime.

`/api/host/status` reports `Degraded` while processing is unavailable. Its additive VPN fields include validation,
phase, reason, torrent-processing availability, operator message, last check, preserved last successful check, next
automatic retry, observed public IPv4, configured ready/degraded intervals, and a sanitized technical failure summary.
ExpressVPN fields separately report the selected recovery mode, recovery phase, last observed controller state,
reconnect and launch attempt counts, next scheduled provider action, last provider action and outcome, and a concise
recovery message.
The current address is present only after a validated or direct-ISP result. Disabling validation clears the live check,
success, retry, address, and failure values without deleting historical logs.

The native macOS and WebUI Dashboards present the operator-facing status and configured intervals. Technical failure
text stays in host status and DB logs. Their Torrents and History pages block page actions while leaving Refresh
available whenever the Service explicitly reports `torrentProcessingAvailable == false`. A missing value does not
block either page.

All ten VPN policy values are editable through the runtime-settings API and both operator Settings screens. The
WebUI applies the same limits described above, saves the VPN group independently, and reconciles the form with the
effective values returned by the Service.

## Performance Timing Summaries

`RuntimeTickDurationSummaryEnabled` controls only `runtime.tick.duration_summary` DB log writes.

- defaults to `false`
- applies live through the runtime-settings API and both operator Diagnostics settings groups
- leaves the synchronization timer, torrent processing, slow-operation diagnostics, and failure diagnostics unchanged
- when enabled, writes the existing one-minute summary only while torrent processing is available
- turning it on starts a fresh one-minute sample window
- turning it off or entering VPN-degraded processing discards the partial sample window; returning to ready starts a
  fresh window

## Queue And Concurrency

### Max Active Metadata Resolutions

- maximum number of torrents actively resolving magnet metadata
- extra unresolved magnets stay queued
- each active resolution reserves a future active-download slot, so the effective limit can be lower when resolved
  downloads are running or queued
- applies live

### Max Active Downloads

- maximum number of torrents actively downloading
- resolved torrents above the limit stay queued
- also acts as the hard ceiling for active downloads plus metadata resolutions; this prevents a burst of completed
  magnet resolutions from entering download state all at once
- applies live

### Metadata Resolution Time Slice Minutes

- maximum continuous time an unresolved magnet keeps a metadata/download reservation while another unresolved magnet
  is waiting
- defaults to `15`; accepted range is `1` through `1440`
- on expiry, the active resolver stops and moves behind never-tried magnets; yielded magnets later retry in
  oldest-yielded order
- a lone unresolved magnet continues resolving instead of being stopped and immediately restarted
- metadata refresh, restart, and automatic reset actions do not restart the time-slice clock
- applies live; configurable through the runtime-settings API/Swagger and both operator Settings screens

### Priority Metadata Attempts

- defaults to `3`; accepted range is `1` through `10`
- Make Next and Resume Next copy the current value into that torrent's durable priority intent
- an unresolved priority magnet receives a protected metadata time slice; lower-priority work and ordinary downloads
  do not take its reservation during that attempt
- after an unsuccessful attempt it moves to the priority tail; after the final attempt it moves to the ordinary
  metadata tail
- metadata success or resolved download admission consumes priority
- changing the setting affects future Make Next and Resume Next actions, not priority requests already in the queue
- configurable through the runtime-settings API/Swagger and both operator Settings screens

### Download No-Progress Time Slice Minutes

- defaults to `30`; accepted range is `1` through `60`, with no disabled value
- persists as `DownloadNoProgressTimeSliceMinutes` and applies live without a Service restart
- existing active downloads start with a fresh full interval at their first post-upgrade active observation because
  older persistence cannot prove when payload bytes last increased
- Slice 1 persists the setting and rotation state; payload-clock evaluation and automatic download yielding are not
  active until the later rotation slices
- currently exposed through the runtime-settings API/Swagger and preserved by the native client mapping; operator
  Settings controls are delivered with the UI slice

Queue diagnostics currently expose:

- open metadata and download slots after metadata-to-download reservations are applied
- counts for resolving, metadata-queued, downloading, download-queued, seeding, paused, completed, and errored torrents
- per-torrent wait reason, ordinary queue position, priority position, and held position when applicable. Both torrent
  tables show these as separate Reason, Queue #, Priority #, and Held # columns; Reason is filterable, and each number
  column sorts by Reason ascending followed by the selected number direction

## Metadata Recovery

### Metadata Refresh Stale Seconds

- idle window before TorrentCore issues a DHT announce and forced tracker announce
- used for cold metadata sessions and zero-peer download stalls
- applies live

### Metadata Refresh Restart Delay Seconds

- additional delay before TorrentCore escalates stale recovery to stop/start
- used for both metadata stalls and zero-peer download stalls
- applies live

When refresh and restart do not recover an active metadata session, TorrentCore schedules at most one automatic
background reset across the host. Other torrents continue synchronizing while MonoTorrent stops, removes, and
recreates the affected manager. If removal succeeds but recreation fails, TorrentCore retries recreation every five
seconds until it succeeds or the service shuts down. Manual metadata reset remains synchronous.

### Automatic Metadata Reset Stuck Threshold Seconds

- elapsed reset time before TorrentCore reports the operation as stuck and opens the automatic-reset circuit breaker
- defaults to `30`; accepted range is `15` through `300`
- applies live to newly scheduled automatic resets
- configurable through the runtime-settings API/Swagger and both operator Settings screens
- a timed-out manager remains quarantined until the underlying MonoTorrent operation actually finishes
- the circuit breaker remains open for a fixed five minutes, then permits one half-open probe

### Long-Cold Threshold Minutes

- continuous zero-peer and zero-progress duration before an active download enters long-cold recovery
- defaults to `120`
- useful peer or transfer activity restarts this timer
- transient runnable queue states suspend the timer without discarding accumulated cold time
- applies live

### Long-Cold Recovery Interval Minutes

- minimum delay between automatic actions after a download enters long-cold recovery
- defaults to `60`
- actions alternate between peer refresh and restart, so restart normally occurs every two intervals
- applies live

### Abandon Cold Download After Hours

- continuous cold duration before TorrentCore stops tracking the download and deletes its partial payload
- defaults to `72`; set to `0` to disable automatic abandonment
- the completion callback is not invoked
- the durable history row is retained with the cleanup reason and deleted-data outcome
- history records the structured `ColdDownloadAbandonment` removal kind
- the History page displays an abandonment alert and provides an Abandoned outcome filter that is not constrained by submitted date
- torrent-scoped activity logs are deleted after successful removal; a service-scoped abandonment summary remains
- the cold timestamp is persisted across service restarts and excludes time waiting in the runnable queue
- applies live

## Engine Settings

### Allow Peer Exchange (PEX)

- permits connected peers to advertise additional peers in the same swarm
- supplements tracker, DHT, and local peer discovery; disabling PEX does not disable those sources
- defaults to disabled because MonoTorrent 3.0.2 PEX processing produced the observed unhandled queue exception
- requires service restart so every active and recovered torrent manager uses one consistent value

### Engine Encryption Mode

- controls plaintext-versus-encrypted peer preference
- current modes are `PlainTextPreferred`, `EncryptedPreferred`, and `EncryptedRequired`
- `EncryptedPreferred` is the current recommended default
- requires service restart

### Engine Max Connections

- global cap on fully established peer sessions
- requires service restart

### Engine Max Half-Open Connections

- global cap on in-progress outbound connection attempts
- requires service restart

### Engine Max Download Rate

- global receive-rate cap in bytes per second
- `0` means unlimited
- requires service restart

### Engine Max Upload Rate

- global send-rate cap in bytes per second
- `0` means unlimited
- requires service restart

## Logging Settings

### Connection Failure Burst Limit

- retained for settings and API compatibility
- individual failures are no longer persisted; failure counts and reasons appear in minute activity summaries

### Connection Failure Window Seconds

- retained for settings and API compatibility
- individual failure suppression is no longer needed because persistence is summary-only

## Lifecycle And Cleanup

### Seeding Stop Mode

- decides when completed torrents stop seeding
- applies live

### Seeding Stop Ratio

- ratio target for ratio-based seeding policy
- applies live

### Seeding Stop Minutes

- time target for time-based seeding policy
- applies live

### Completed Torrent Cleanup Mode

- controls whether TorrentCore automatically removes completed torrents from active tracking
- automatic cleanup never deletes payload data
- applies live

### Completed Torrent Cleanup Minutes

- completion-age window for automatic cleanup
- also used for optional completed-log pruning
- applies live

### Delete Log Entries For Completed Torrents

- deletes only torrent-scoped activity-log rows for completed torrents after the normal completion-age window
- does not delete payload data
- does not run while callback state is still pending, failed, or timed out
- applies live

## Protected Cleanup Operations

Both operator Settings screens expose three separately confirmed Service maintenance operations:

- Log Entries starts at seven days before the operator's current date and deletes eligible logs strictly before
  Service-local midnight on the selected date.
- History Records starts at 30 days before the operator's current date and deletes eligible history rows, based on
  last-updated time, strictly before Service-local midnight on the selected date.
- Orphaned Torrent Logs deletes torrent-scoped logs whose torrent id is no longer present in the live torrent table.

The initial dates are UI conveniences rather than persisted settings. A date is required and cannot be in the future.
Log and history cleanup preserve rows associated with live torrent ids. Orphan cleanup preserves Service-level logs
and logs for live torrents. The WebUI also retains the existing orphan-log action on the Logs page.

## Completion Callback Settings

### Enable Completion Callback Invocation

- enables or disables launching the configured shared callback entrypoint
- applies live

### Command Path

- full path of the callback executable or script
- applies live

### Arguments

- optional static command-line arguments
- applies live

### Working Directory

- optional working directory for callback launch
- applies live

### Process Timeout Seconds

- retained for settings and API compatibility
- callback dispatch no longer waits for process exit, so this value does not limit callback execution

### Finalization Wait Seconds

- timeout for waiting on downstream-visible final payload readiness before callback launch
- applies live

### API Base URL Override

- optional callback-environment API base URL override
- applies live

### API Key Override

- optional callback-environment API key override
- applies live

## Category Routing Settings

Category rules:

- categories control future torrent routing only
- existing torrents keep the routing values resolved at add time
- keep callback label and download root aligned with downstream expectations for the same category

Per-category settings:

- `Enabled`
- `Invoke Callback`
- `Display Name`
- `Callback Label`
- `Download Root`
- `Sort Order`

## Payload Readiness And Restart Semantics

Payload-readiness rules:

- MonoTorrent partial-file naming is disabled
- incomplete data may be visible at its final filename while a transfer is active
- downstream systems must use TorrentCore's completion callback rather than filename visibility as the readiness signal
- finalization verifies the engine-reported complete paths or final payload path before callback invocation

Restart-required settings currently include:

- peer exchange
- engine encryption mode
- engine max connections
- engine max half-open connections
- engine max download rate
- engine max upload rate

Live settings currently include:

- VPN validation enablement, endpoint, direct-ISP CIDRs, check intervals, and timeouts
- performance timing summary logging
- queue concurrency
- metadata and long-cold download recovery windows
- logging throttle settings
- seeding policy
- completed-torrent cleanup policy
- callback settings
- category settings
