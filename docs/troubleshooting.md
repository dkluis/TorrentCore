# Troubleshooting

## Magnet Stuck In Metadata

Automatic recovery path:

1. after `Metadata Refresh Stale Seconds`, TorrentCore requests DHT and tracker refresh
2. after `Metadata Refresh Restart Delay Seconds`, TorrentCore escalates to stop/start
3. if the session still stays cold, TorrentCore can recreate the metadata session

Only torrents in active metadata-resolution slots are eligible. After an unsuccessful reset cycle, TorrentCore backs
off subsequent stale and escalation windows to `2x`, `4x`, and at most `8x`. Finding peer candidates does not clear
that backoff because repeated recovery announces can rediscover peers that never connect; an actual peer connection
does clear it.

Useful events to inspect:

- `torrent.metadata.refresh_requested`
- `torrent.metadata.restart_requested`
- `torrent.metadata.reset_requested`
- `runtime.recovery.announce_timed_out`
- `runtime.recovery.announce_failed`
- `runtime.connection.activity_summary`

Operator guidance:

- use `Refresh Metadata` for a fresh discovery attempt
- use `Reset Metadata` for the stronger recovery path without deleting and re-adding the torrent
- compare TorrentCore behavior with another client on the same host before changing global settings

## Downloading But No Peers

TorrentCore also treats zero-peer download stalls as a stale-recovery case.
Only active downloads are eligible. Each restart that remains cold applies the same bounded progressive backoff, and
any connected peer, positive rate, or downloaded-byte progress clears it.
After `Long-Cold Threshold Minutes`, recovery switches to one action per `Long-Cold Recovery Interval Minutes` and
alternates refresh and restart. The defaults are two hours and one hour, respectively.
After `Abandon Cold Download After Hours` of continuous inactivity, TorrentCore removes the torrent, deletes its
partial payload and torrent-scoped logs, skips the completion callback, and retains the removal reason in history.
The default is 72 hours; `0` disables abandonment.
The History page shows a persistent abandonment summary when retained abandoned downloads exist. Use its
`View abandoned downloads` action or the `Abandoned` outcome filter; this search intentionally ignores submitted-date
fields because an abandoned torrent may have been submitted days earlier. The grid keeps the last engine state in the
State column and shows `Abandoned` separately in Outcome.

Useful checks:

- whether peer discovery occurs without ever reaching a connected peer
- whether TorrentCore logs `torrent.download.refresh_requested`
- whether TorrentCore logs `torrent.download.restart_requested`
- whether another client succeeds over IPv4 on the same host while IPv6 route failures appear in TorrentCore logs

If connection summaries continue reporting `NewPeersFound` but later report no connection, disconnection, or failure
events after `TooManyOpenConnections`, verify the deployed MonoTorrent package. TorrentCore pins `3.0.2` because
`3.0.3-alpha.unstable.rev0049` can leak its host-wide open-connection count on failed encryption or handshake paths.
Per-torrent stop/start recovery does not reset that host-wide counter; a complete Service process restart does.

## Completion Callback Problems

Remember:

- TorrentCore does not fire the callback on the engine's first internal completed edge alone
- finalization must be visible at the downstream payload path first
- filename visibility alone does not mean an active transfer is ready for downstream processing

If callback behavior looks wrong, check:

- current callback settings
- category callback enablement
- callback state on the torrent
- final payload path visibility
- callback dispatch versus finalization timeout

## Intermittent Slow Or Unresponsive Operations

Inspect persistent activity logs for:

- `runtime.operation.slow`
- `runtime.tick.duration_summary`
- `runtime.recovery.action_completed`
- `runtime.metadata.reset_completed`
- `runtime.metadata.reset_failed`
- `runtime.metadata.reset_recreate_retry`
- `runtime.callback.dispatch_completed`
- `runtime.connection.activity_summary`
- `runtime.monotorrent.cache_audit`
- `runtime.tick.failed`

Use the logged subsystem and operation fields to distinguish synchronization-gate waits, MonoTorrent lifecycle work,
callback execution, and storage phases before restarting the service. Recovery and connection summaries retain torrent
context after torrent-scoped activity logs are pruned.
Torrent list and detail reads do not wait for the MonoTorrent lifecycle gate. During a manager transition they may
briefly show persisted state instead of a live projection; repeated read timeouts therefore indicate pressure outside
that lifecycle gate or an older deployment.
For broad snapshot-phase delays, compare `torrent_snapshot_read`, `torrent_snapshot_projection`,
`torrent_finalization_visibility_probe`, `torrent_snapshot_write`, and `torrent_history_write` before attributing the
whole phase to SQLite.
Final-payload visibility checks use deduplicated per-torrent background probes. A slow
`torrent_finalization_visibility_probe` can delay one torrent's callback readiness, but it must not extend serialized
engine synchronization or pause state updates for other torrents.
Completion-time manager stops are also deduplicated background work. A slow `completion_manager_stop` delays only that
torrent's callback handoff; callback dispatch remains blocked until MonoTorrent has stopped accessing the payload.
Inspect `runtime.completion.manager_stop_completed` for duration and outcome. Failures also appear as
`runtime.completion.manager_stop_failed` and retry after a short cooldown.
`torrent.seeding.stopped_policy` records the first durable application of the configured seeding stop policy. Its
details may show `EngineStopReady: false` while the background manager stop is still finishing; use the runtime stop
events for the eventual stop outcome. Repeated policy events for one torrent indicate a deployment older than schema
migration 19 or a persistence failure.
Automatic metadata reset is single-flight across the host and runs outside serialized engine synchronization. Inspect
`runtime.metadata.reset_completed` for duration and outcome. `runtime.metadata.reset_failed` means the old manager
could not complete its reset and was restored when possible. `runtime.metadata.reset_recreate_retry` means removal
succeeded but creating the replacement failed; TorrentCore retries every five seconds while other torrents continue.
`runtime.metadata.reset_timed_out` means the configured stuck threshold elapsed; the affected manager remains
quarantined because MonoTorrent stop work cannot be forcibly cancelled. `runtime.metadata.reset_circuit_opened` marks
the fixed five-minute breaker window, and `runtime.metadata.reset_suppressed` records automatic resets withheld by an
active reset, retry cooldown, or open breaker. After cooldown, `runtime.metadata.reset_half_open` marks the one allowed
probe. `runtime.metadata.reset_late_completion` confirms the quarantined operation finally ended, while
`runtime.metadata.reset_circuit_closed` confirms a successful half-open probe restored normal scheduling.
Forced recovery announces do not hold serialized synchronization. Tracker announces are limited to ten seconds and
duplicate recovery announces for the same torrent are suppressed while one remains active.
Recovery action details include the recovery cycle, backoff multiplier, and effective timing windows. A high attempt
count with `LongColdMode=true` indicates a persistently cold torrent on the slower configured cadence rather than an
engine-wide synchronization stall.
Runnable downloads retain their accumulated cold duration across automatic stop/start transitions. Time spent queued
for an active-download slot is suspended and does not advance the long-cold threshold.
During a large magnet burst, `MaxActiveDownloads` is also the combined ceiling for active metadata resolutions and
downloads. A metadata resolution consumes a future download reservation because MonoTorrent automatically starts the
same manager after metadata arrives. Seeing fewer active metadata resolutions than `MaxActiveMetadataResolutions` is
therefore expected when resolved downloads already claim capacity; the remaining magnets should report a metadata-slot
wait reason. Available metadata and download slot diagnostics already include these reservations.
If unresolved magnets remain queued behind long-running resolvers, check `torrent.metadata.resolution_yielded`. Each
event means the configured metadata-resolution time slice expired and the resolver moved behind waiting work. Rotation
occurs only when another runnable unresolved magnet is waiting, so a single difficult magnet continues discovery.
Repeated yields across a full queue are expected for very cold swarms; changing the live time-slice setting affects
current attempts without resetting their persisted start times.
The cache audit treats files older than 90 days as review candidates only; TorrentCore does not automatically delete
them because cached metadata can accelerate a later re-add of the same torrent.

## Queue Information Is Missing In The Native Mac App

The native torrent table remembers column customization. If queue numbers and wait reasons remain visible in the
WebUI but appear absent in the native app, unhide the native table's **Wait** column before investigating Service
recovery. The Wait column renders both the reason and its queue number. Hiding it changes only the native presentation;
the Service continues scheduling the entries and returning the diagnostics through list and detail endpoints.

## Unexpected Service Exit

Inspect `~/TorrentCore/Logs/TorrentCore.Service.launchd.err.log` for a timestamped
`TorrentCore.Service unhandled process exception` marker. The marker records the UTC occurrence time, process id,
termination flag, exception type, and message. The runtime may append a second untimestamped copy of the stack trace.
Compare the marker with `service.startup.ready` in the persistent activity logs to identify the replacement service
instance started by launchd.

If the stack trace ends in `MonoTorrent.Client.PeerExchangeManager.OnAdd`, confirm that `Allow Peer Exchange (PEX)` is
disabled in Service Settings. Saving a different PEX value sets `Restart Required`; restart TorrentCore.Service so all
recovered and newly added torrent managers receive the saved value. With PEX disabled, tracker, DHT, and local peer
discovery remain available.

## Remove And Delete Data Fails

TorrentCore unregisters a torrent from MonoTorrent before deleting payload files. This keeps a transient macOS
`Resource busy` file-system error from failing the remove request while the torrent is still registered. Payload
cleanup is confined to the configured download root and retries transient I/O failures five times over approximately
21 seconds.

If all attempts fail, inspect the persistent activity log for `torrent.data_cleanup.failed`. The entry records the
torrent id, candidate paths, and final error. TorrentCore has already removed the torrent from active tracking at that
point, so resolve the external file-system condition and remove the remaining payload manually.

## Torrent Processing Is Paused For The VPN

`/api/health` remains successful while VPN validation has paused torrent processing. Inspect `/api/host/status` and
check `vpnConnectionPhase`, `vpnConnectionReason`, `torrentProcessingAvailable`, `torrentProcessingMessage`,
`vpnLastCheckAtUtc`, `vpnLastSuccessAtUtc`, `vpnNextAutomaticRetryAtUtc`, `vpnObservedPublicIpv4`, and
`vpnFailureSummary`. The last-success value is deliberately retained after a later failure. A missing current address
means the latest check did not obtain a usable IPv4; a direct-ISP result shows the observed direct address.

- `DirectIsp` means the observed public IPv4 matched a configured direct-ISP CIDR.
- `TimedOut` or `EndpointFailure` means TorrentCore could not confirm the VPN through the configured endpoint.
- `EngineActivationFailed` means the VPN check succeeded but MonoTorrent could not restart; TorrentCore retries the
  engine directly at the degraded interval.
- `EngineSuspensionFailed` means execution admission is closed but engine teardown did not complete cleanly.

Magnets remain accepted and queued through the API while degraded. The native macOS and WebUI Torrents and History
pages show a processing-paused overlay with Refresh available after an explicit unavailable host status. The WebUI
Torrents page continues using its existing refresh loop; History remains manual-refresh only. A host-status request
failure preserves the last explicit unavailable state, while a missing or null availability value does not create a
degraded state. Recovery follows an explicit available host status after a successful degraded check. Do not restart
the Service merely to clear this state.

The WebUI Dashboard's VPN Connection section shows validation, phase, processing availability, operator reason,
observed address, check/retry timestamps, and configured intervals. Use host status or DB logs for the sanitized
technical failure detail.

For automatic ExpressVPN recovery, correlate these persistent activity events in timestamp order:

- `vpn.egress.validation_completed` records a changed validation outcome or endpoint-failure reason. Identical
  repeated results are intentionally suppressed, so two-check recovery eligibility may produce only one persisted
  timeout or endpoint-failure row.
- `engine.monotorrent.suspended` must precede every provider-changing action. If it is absent or suspension failed,
  TorrentCore must not disconnect, connect, or launch ExpressVPN.
- `vpn.expressvpn.controller_state_changed` records stable controller transitions such as `Connected` and
  `Disconnected`.
- `vpn.expressvpn.recovery_attempted` records the attempt number, trigger, prior controller state, provider-command
  outcomes, and final validated-egress disposition. A prior `Disconnected` state selects connect-only recovery; a
  prior `Connected` state selects the full disconnect/confirm/connect/confirm sequence.
- `vpn.expressvpn.launch_attempted` and `vpn.expressvpn.recovery_exhausted` distinguish an unavailable-controller launch
  path from reconnect exhaustion. Their absence means neither action was recorded; it does not by itself prove why an
  interrupted Service stopped.
- `vpn.egress.state_changed`, followed by `engine.monotorrent.ready`, confirms that validation succeeded before torrent
  processing resumed.

Compare `serviceInstanceId` values when an episode crosses a gap in activity. A changed value proves a new Service
instance, but the database alone cannot distinguish an OS reboot, a Service restart, sleep, or another external stop.
On a new instance, enabled validation must succeed before MonoTorrent activation; a provider recovery attempt that had
not started before the old instance ended does not consume an attempt.

The ready interval is detection latency, not immediate packet protection. ExpressVPN Network Lock remains responsible
for blocking traffic during the interval before TorrentCore performs its next check.

## Performance Timing Summaries Are Missing Or Unexpected

`runtime.tick.duration_summary` is disabled by default. Enable **Performance Timing Summaries** in either operator
Settings Diagnostics group when a performance investigation needs the one-minute records. Summaries are intentionally
suppressed while VPN validation has paused torrent processing. Changing this setting does not stop the runtime tick or
change other runtime diagnostics.

## Settings Cleanup Is Rejected

WebUI Settings and the native macOS client use the same Service cleanup operations. Each destructive action requires
its own confirmation. For Log Entries and History Records, select a non-future date; the Service interprets it as
local midnight and deletes only eligible rows strictly before that cutoff. Rows tied to live torrents remain
protected. Orphaned Torrent Logs removes only torrent-scoped rows whose torrent id is no longer live.

## Deployment And Runtime Checks

Useful runtime checks on the host:

```bash
cd ~/TorrentCore/Scripts
./agentstatus.zsh
curl http://127.0.0.1:7033/api/health
curl http://127.0.0.1:7033/api/host/status
curl -I http://127.0.0.1:7053/
```

Use `serviceVersion` plus `serviceBuild` from host status to identify the active Service. The version is the semantic
release identity; the optional build is the full Git commit and should match the prefix stored in
`~/TorrentCore/.deploy/installed.json`. A signed deployment uses a public supervisor process and a sibling `.apphost`
helper, so the helper—not the supervisor—normally owns ports `7033` and `7053`.

If the WebUI cannot reach the backend:

- recheck the persisted service endpoint
- verify the service health endpoint
- verify listen bindings and host firewall settings
- use the `Service Connection` page to test and save the intended endpoint
- confirm `/api/health` identifies `TorrentCore.Service`; a missing or API version up to `1` is accepted, while a
  future API version is rejected
