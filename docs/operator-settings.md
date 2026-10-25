# Operator Settings

## Media Payload Validation

TorrentCore owns `RequiredVideoExtensions` (initially `.mkv,.mp4`) and `ProhibitedPayloadExtensions`
(initially `.exe,.iso,.7z,.zip`). Edit their comma-separated values under Seeding & Cleanup in Native macOS,
WebUI and WebUI2. The Service normalizes case and leading dots, rejects empty/invalid/overlapping lists, and
persists values in the existing `runtime_settings` table. TVMaze does not supply these lists.

The Service loads these lists at startup. Saved list edits remain pending until Service restart and use the existing
restart-required indicator. After restart, the loaded rules apply to existing and new TV/Movie torrents once metadata is resolved.
Other categories are unaffected. At least one configured video is required; `.srt` alone is insufficient with
the default video list. Any prohibited final extension rejects the entire torrent, even with video present.
Archive-only torrents with unlisted extensions still fail when they contain no configured video. No archive
extraction, media-content inspection, repeated filesystem scan, or per-torrent policy snapshot is added.

Rejection stops/removes the torrent and confirms data deletion before downstream notification. An intentionally
disabled completion callback leaves a finished local rejection (`NotificationNotRequired`); an enabled callback
with missing configuration or an unconfirmed reply remains pending. The same hash stays blocked while its
rejection history remains retained; ordinary cleanup releases the block. Existing `.exe` history remains valid.
Settings storage/validation failures block the processing pass and log a controlled failure, never authorize deletion.


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

### VPN Automatic Recovery

The native macOS **VPN Egress → VPN Recovery** section starts with **ExpressVPN** and **PIA** switches.
Exactly one is selected; selecting either deselects the other. Provider selection does not connect or disconnect a VPN.
Existing installations default to ExpressVPN and preserve their recovery mode and delays. The native client manages
provider selection; the WebUI has no provider selector. Its existing ExpressVPN-labelled settings still edit the shared
recovery policy and delays, and an older client's save does not change the selected provider.

Automatic recovery is disabled by default and is effective only when VPN egress validation is enabled on a macOS
MonoTorrent host. It never changes either provider's split-tunnel settings, regions, protocols, or application rules.

- `Disabled` performs no provider reads, launches, disconnects, or connects
- `DirectIspOnly` permits recovery only after two consecutive checks detect a configured direct-ISP IPv4 range
- `AnyValidationFailure` permits recovery after two consecutive eligible failures, including endpoint and response
  failures
- MonoTorrent must be fully suspended before TorrentCore reads or changes the selected provider; a suspension failure
  blocks every provider action
- provider status and command success are diagnostic only; MonoTorrent starts again only after TorrentCore validates a
  non-ISP public IPv4 address

The recovery delay defaults to `180` seconds. It is both the startup grace period and the minimum interval between
disconnect/connect attempts. The unavailable launch delay defaults to `300` seconds and is measured before each of at
most two attempts to ask macOS to launch ExpressVPN. Reconnect and launch attempts have separate fixed limits of two.
After either category is exhausted, TorrentCore remains suspended and continues its normal degraded public-IP checks,
so a later manual recovery can still restore processing. Shared mode and delay settings apply live and reject unknown
modes or nonpositive delays.

PIA uses `/Applications/Private Internet Access.app/Contents/MacOS/piactl`, with five-second command limits and
60-second state-transition limits. A disconnected controller uses connect-only recovery; connected-but-unvalidated
egress uses disconnect, confirmed Disconnected, connect, confirmed Connected, then the existing public-IP validation.
Interrupted, Connecting, Reconnecting, and DisconnectingToReconnect get up to 60 seconds to recover themselves before
a bounded reconnect attempt. If PIA reaches Connected during that grace period, egress is checked before any reconnect.
CLI failures and unknown output remain distinct from Disconnected. Two unsuccessful reconnect attempts exhaust
provider recovery, but ordinary degraded checks continue and can resume processing after external/manual recovery.

Enable PIA background mode once on the Service host with:

```sh
"/Applications/Private Internet Access.app/Contents/MacOS/piactl" background enable
```

TorrentCore does not enable background mode, launch the PIA GUI, log in, or reset PIA settings. If its controller is
unavailable, the native dashboard explains the prerequisite and processing stays suspended. The unavailable launch
delay applies only to ExpressVPN and is hidden while PIA is selected. The saved delay is retained when switching back.
Provider changes apply at a serialized coordinator boundary, clear the previous provider's recovery episode, and
require fresh eligible checks. An in-flight command may finish; no sequence mixes commands between providers.
The public API retains the legacy `ExpressVpn*` setting names for shared mode/delay compatibility and adds
`VpnRecoveryProvider` (`ExpressVPN` or `PIA`). Generic `Vpn*` recovery status fields describe the active provider;
legacy ExpressVPN status reports inactive while PIA is selected. Outage detection and automatic resumption remain
provider-independent and unchanged.

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
Generic recovery fields separately report the selected provider and mode, recovery phase, last observed controller state,
reconnect and launch attempt counts, next scheduled provider action, last provider action and outcome, and a concise
recovery message.
The current address is present only after a validated or direct-ISP result. Disabling validation clears the live check,
success, retry, address, and failure values without deleting historical logs.

The native macOS and WebUI Dashboards present the operator-facing status and configured intervals. Technical failure
text stays in host status and DB logs. Their Torrents and History pages block page actions while leaving Refresh
available whenever the Service explicitly reports `torrentProcessingAvailable == false`. A missing value does not
block either page.

The shared VPN policy values are editable through the runtime-settings API and both operator Settings screens;
provider selection is available in the native macOS screen. The WebUI applies the same limits described above, saves the VPN group independently, and reconciles the form with the
effective values returned by the Service.

## Performance Timing Summaries

`RuntimeTickDurationSummaryEnabled` controls `runtime.tick.duration_summary` DB log writes and optional
executable-rejection history Debug records (including admission hash checks).

- defaults to `false`
- applies live through the runtime-settings API and both operator Diagnostics settings groups
- when off, rejection-history Debug records are not written; existing entries remain subject to normal cleanup
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
- an active incomplete download becomes eligible after its monotonic durable completed-piece byte count has not
  increased for the full interval; peers and reported transfer speed do not restart this clock
- expiry does nothing when no eligible runnable work is waiting; when work is waiting, TorrentCore yields only enough
  oldest-stale downloads to use the available replacements
- automatic yield preserves partial payload and runnable intent, runs behind priority and ordinary never-yielded work,
  and retries with other automatic yields in oldest-yielded order
- Service/VPN engine recovery preserves an existing active clock; ordinary queued, Held, and Paused time does not count
- a failed manager stop ends rotation for that tick, and a failed or uncertain yielded-state write admits no
  replacement
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

### Download No-Progress Time Slice Minutes

- defaults to `30`; accepted range is `1` through `60`
- applies live and is configurable through the runtime-settings API and both operator Settings screens
- measures an active incomplete download from its last durable completed-piece byte increase; peers, reported speed,
  tracker activity, and recovery activity do not restart the clock
- when eligible work is waiting, TorrentCore yields only enough oldest-stale downloads to admit that work
- yielded downloads retain their partial payload and runnable intent, then retry after priority and ordinary
  never-yielded work
- the active clock, last automatic-yield time, and current automatic-retry class survive Service restart and are
  exposed in torrent summary/detail diagnostics

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
- the History page displays an abandonment alert and provides an Abandoned outcome filter; any selected date range is
  applied to Last Updated just as it is for every other outcome
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
