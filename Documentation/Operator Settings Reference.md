# TorrentCore Operator Settings Reference

## Purpose

This document explains the operator-facing runtime and engine settings in plain language.

It is intentionally practical:
- what the setting means
- what the unit is
- what it affects
- whether it applies live or requires restart

## Queue And Concurrency Settings

## Queue Diagnostics

TorrentCore now exposes queue diagnostics at two levels:
- host-level queue counts and available slots
- per-torrent wait reason and queue position

Per-torrent wait reason currently means:
- `PendingMetadataDispatch`: the torrent is queued and should be picked up for metadata work as soon as the scheduler dispatches it
- `WaitingForMetadataSlot`: the torrent is queued behind the current metadata-resolution capacity
- `PendingDownloadDispatch`: the torrent is queued and should be picked up for download work as soon as the scheduler dispatches it
- `WaitingForDownloadSlot`: the torrent is queued behind the current active-download capacity
- `PausedByOperator`: the torrent is not running because it was explicitly paused
- `BlockedByError`: the torrent is not progressing because it is currently in an error state

Queue position:
- is only populated when a torrent is in one of the queue states
- is ordered by TorrentCore's current queue rule: oldest added first, then torrent id as a stable tie-breaker

Host-level queue diagnostics currently include:
- open metadata slots
- open download slots
- counts for resolving, metadata-queued, downloading, download-queued, seeding, paused, completed, and errored torrents

### Max Active Metadata Resolutions

Meaning:
- The maximum number of torrents allowed to actively resolve magnet metadata at the same time.

Effect:
- New magnets are still accepted and persisted immediately.
- If this limit is reached, extra unresolved magnets wait in queue until a metadata slot opens.

Unit:
- Count of torrents.

Applies:
- Live.

### Max Active Downloads

Meaning:
- The maximum number of torrents allowed to actively download at the same time.

Effect:
- Resolved torrents above this limit remain queued until a download slot opens.

Unit:
- Count of torrents.

Applies:
- Live.

## Metadata Recovery

These settings control how TorrentCore tries to wake up a public torrent when MonoTorrent is cold:
- while it is stuck in `ResolvingMetadata` without any useful peer activity
- while it is already in `Downloading` but still has zero open peers and zero payload progress

### Metadata Refresh Stale Seconds

Meaning:
- How long TorrentCore waits before it considers a metadata-resolution session stale.

Practical interpretation:
- This is the idle window before TorrentCore sends an explicit metadata-discovery nudge.
- When the threshold is reached, TorrentCore asks MonoTorrent to do a DHT announce and a forced tracker announce for that torrent.
- TorrentCore currently reuses this same stale window for `Downloading` torrents that already have metadata but still show zero open peer sessions and zero payload progress.
- TorrentCore now treats only live peer connectivity as meaningful metadata activity; peer-discovery callbacks with zero open connections do not reset the stale window by themselves.
- TorrentCore now prefers plaintext outgoing peer handshakes first and keeps RC4 as fallback, which reduces the amount of metadata time spent burning the first connection attempt on encrypted negotiation before retrying the same peer in plaintext.
- Lower values make TorrentCore react sooner to a cold magnet, but also increase how often it prods trackers and DHT for weak swarms.

Unit:
- Seconds.

Applies:
- Live.

### Metadata Refresh Restart Delay Seconds

Meaning:
- How long TorrentCore waits after a stale-metadata refresh before it escalates to restarting the torrent manager.

Practical interpretation:
- TorrentCore first tries a non-destructive peer-discovery refresh.
- If the torrent still shows no meaningful metadata progress after this additional delay, TorrentCore performs a stop/start and immediately asks for fresh peers again.
- TorrentCore currently reuses this same restart window for `Downloading` torrents that still have zero open peer sessions and no payload progress after a download-stage DHT/tracker refresh.
- Lower values make recovery more aggressive, but can create extra churn for magnets that only need a little more time.

Unit:
- Seconds.

Applies:
- Live.

## Troubleshooting: Magnet Stuck In Metadata

If a public magnet stays in `ResolvingMetadata`, TorrentCore now has both automatic recovery and a manual operator nudge.

What TorrentCore does automatically:
- After `Metadata Refresh Stale Seconds`, TorrentCore requests a DHT announce and a forced tracker announce for the torrent.
- If the torrent still stays cold through `Metadata Refresh Restart Delay Seconds`, TorrentCore restarts the torrent manager and immediately refreshes peer discovery again.
- If the torrent still stays cold after that restart window, TorrentCore recreates the MonoTorrent manager from the saved magnet and immediately refreshes peer discovery on the new session.

When to use `Refresh Metadata` manually:
- The torrent is still in `ResolvingMetadata` after the automatic windows have already elapsed.
- The torrent detail view or list still shows no useful peer activity.
- You want to force a fresh discovery attempt immediately instead of waiting for the next automatic recovery window.

When to use `Reset Metadata` manually:
- `Refresh Metadata` has already been tried and the torrent is still stuck in `ResolvingMetadata`.
- The same magnet resolves immediately in another client on the same host, suggesting the current TorrentCore metadata session has gone stale.
- You want a stronger recovery than refresh or stop/start without deleting and re-adding the torrent record.

What to check:
- `torrent.metadata.refresh_requested` confirms a manual or automatic discovery refresh was issued.
- `torrent.metadata.restart_requested` confirms TorrentCore escalated from refresh to stop/start recovery.
- `torrent.metadata.reset_requested` confirms TorrentCore recreated the metadata session from the saved magnet.
- `torrent.download.refresh_requested` confirms TorrentCore nudged DHT and trackers for a torrent that already had metadata but was still in `Downloading` with zero useful activity.
- `torrent.download.restart_requested` confirms TorrentCore escalated that zero-peer download stall to a stop/start plus fresh DHT/tracker announce.
- `torrent.engine.peers_found` shows whether the swarm is returning candidate peers.
- `torrent.engine.peers_found` with `OpenConnections = 0` means peers were discovered but TorrentCore still had no live peer connections at that moment.
- `torrent.engine.peer_connected` and `torrent.engine.peer_disconnected` show whether MonoTorrent ever completed a full handshake, which peer/client it talked to, and which encryption mode the session used.
- `torrent.engine.connection_failed` helps distinguish "no peers discovered" from "peers discovered but connections failed."
- repeated `EncryptionNegiotiationFailed` or `HandshakeFailed` against discovered peers means the swarm exists but MonoTorrent is not turning those candidate peers into stable sessions.
- repeated IPv6 route failures can be noise when the host has IPv6 enabled but the active VPN path does not carry IPv6; successful IPv4 peer sessions can still be enough for a healthy download.

Operator guidance:
- `Refresh Metadata` is most useful for public magnets that appear stuck after a quiet period or after a weak first discovery pass.
- `Reset Metadata` is the stronger operator nudge and is the closest built-in equivalent to deleting and re-adding the same magnet.
- If your goal is only to recover a stuck metadata session, prefer `Reset Metadata` over `Delete Data` plus re-add so TorrentCore keeps the existing torrent record and logs.
- Weak or dead swarms may still never resolve metadata even after refresh and restart if no reachable peers exist.
- If another client on the same host resolves the same magnet faster, compare whether TorrentCore is reaching `peer_connected` at all before assuming the issue is only DHT or tracker discovery.
- If the same magnet resolves immediately in another client on the same host, compare TorrentCore's recent log events and current runtime settings before changing global limits again.

## Troubleshooting: Downloading But No Peers

If a torrent already resolved metadata, entered `Downloading`, and then sits with zero peers and no payload progress, TorrentCore now treats that as a second stale-recovery case.

What TorrentCore does automatically:
- After `Metadata Refresh Stale Seconds`, TorrentCore requests a DHT announce and a forced tracker announce for that torrent.
- If the torrent still shows zero open peer sessions and zero payload progress through `Metadata Refresh Restart Delay Seconds`, TorrentCore performs a stop/start and immediately asks for fresh peers again.
- If the download begins moving or any live peer session opens, the stale-recovery cycle is cleared and TorrentCore starts over from a clean slate on any later stall.

What to compare against another client:
- whether TorrentCore logs `torrent.engine.peers_found` without ever reaching `torrent.engine.peer_connected`
- whether TorrentCore reaches `torrent.download.refresh_requested` or `torrent.download.restart_requested`
- whether the other client is succeeding over IPv4 even while both clients log IPv6 route failures on the same VPN path

## MonoTorrent Engine Throttle Settings

These settings are global to the engine host, not per torrent.

### Engine Max Connections

Meaning:
- The maximum number of fully established peer connections the engine allows overall.

Practical interpretation:
- This is the cap on active open peer sessions across the host.
- A connection only counts here after the engine has successfully connected to a peer and the session is active.
- This is not a file count and not a torrent count. One torrent can use multiple peer connections, and the total is shared across all torrents.
- Higher values can improve swarm participation, but also increase CPU, memory, and socket usage.

Unit:
- Count of connections.

Applies:
- On service restart.

### Engine Max Half-Open Connections

Meaning:
- The maximum number of outbound connection attempts the engine allows to be in progress at the same time.

Practical interpretation:
- These are connection attempts that have started but are not yet fully established.
- In practice, this is the number of peers the engine is currently trying to connect to but has not finished the network handshake with yet.
- A half-open connection is not downloading or uploading payload data yet. It is still in the "trying to establish the session" phase.
- This mainly affects how aggressively the engine fans out to new peers.
- Higher values can help the engine find working peers faster, but they can also create extra connection churn and more `connection_failed` activity when many peers are unreachable.

Unit:
- Count of in-progress outbound connection attempts.

Applies:
- On service restart.

### Engine Max Download Rate

Meaning:
- The maximum total download throughput allowed for the engine host.

Practical interpretation:
- This is a global ceiling across all torrents combined.
- It is not a per-torrent cap.
- `0` means unlimited.
- This is measured as network download throughput seen by the engine, not disk write speed and not final file growth on disk.
- In plain terms, it is the rate at which TorrentCore is receiving torrent payload data from peers across the whole engine.

Unit:
- Bytes per second.

UI note:
- The web UI displays this as MB/s for readability.

Applies:
- On service restart.

### Engine Max Upload Rate

Meaning:
- The maximum total upload throughput allowed for the engine host.

Practical interpretation:
- This is a global ceiling across all torrents combined.
- It is not a per-torrent cap.
- `0` means unlimited.
- This is measured as network upload throughput seen by the engine, not disk read speed.
- In plain terms, it is the rate at which TorrentCore is sending torrent payload data back to peers across the whole engine.

Unit:
- Bytes per second.

UI note:
- The web UI displays this as MB/s for readability.

Applies:
- On service restart.

## Logging Settings

### Connection Failure Burst Limit

Meaning:
- How many repeated engine connection-failure events are logged before TorrentCore starts suppressing additional identical warnings for a short time window.

Effect:
- Prevents the logs from filling with hundreds of repeated connection failures.

Unit:
- Count of log entries.

Applies:
- Live.

### Connection Failure Window Seconds

Meaning:
- The time window used together with the burst limit for connection-failure log suppression.

Effect:
- Controls how long identical connection-failure warnings are grouped before they are allowed to appear again.

Unit:
- Seconds.

Applies:
- Live.

## Lifecycle And Policy Settings

### Seeding Stop Mode

Meaning:
- The rule used to decide when a completed torrent should stop seeding.

Examples:
- unlimited seeding
- stop immediately
- stop after ratio
- stop after time
- stop after ratio or time

Applies:
- Live.

### Seeding Stop Ratio

Meaning:
- The target upload ratio used by ratio-based seeding policies.

Practical interpretation:
- `1.0` means upload an amount equal to the downloaded payload size.

Unit:
- Ratio.

Applies:
- Live.

### Seeding Stop Minutes

Meaning:
- The target seeding duration used by time-based seeding policies.

Unit:
- Minutes.

Applies:
- Live.

### Completed Torrent Cleanup Mode

Meaning:
- The policy controlling whether TorrentCore automatically removes completed torrents from its own tracking list.

Important rule:
- Automatic cleanup never deletes downloaded data.

Applies:
- Live.

### Completed Torrent Cleanup Minutes

Meaning:
- The delay before automatic completed-torrent cleanup runs when a time-based cleanup mode is active.
- TorrentCore also uses this same completion-age window for optional completed-log pruning when `Delete Log Entries For Completed Torrents` is enabled.

Unit:
- Minutes.

Applies:
- Live.

### Delete Log Entries For Completed Torrents

Meaning:
- Controls whether TorrentCore automatically deletes torrent-scoped activity logs after a torrent has completed successfully and aged past the current completed-cleanup minute window.

Important rules:
- This deletes only activity-log rows whose `torrent_id` matches that completed torrent.
- It does not delete downloaded data.
- It does not run while a completion callback is still pending, failed, or timed out.
- If automatic completed-torrent removal is also enabled, TorrentCore removes the torrent from tracking first and then clears that torrent's log history as part of the same cleanup pass.

Applies:
- Live.

## Partial Files

TorrentCore currently uses MonoTorrent partial-file behavior:
- incomplete files use `.!mt`
- completed files lose the `.!mt` suffix

## Restart Semantics

Some settings are live and some are engine-start settings.

Live now:
- queue concurrency
- seeding policy
- completed cleanup policy
- connection-failure log throttling

Restart required now:
- engine max connections
- engine max half-open connections
- engine max download rate
- engine max upload rate

The settings page shows both:
- desired saved values
- currently applied engine values

If they differ, TorrentCore shows that a service restart is required.

## Quick Mental Model

If you want a practical way to think about these four engine controls:
- `Engine Max Connections` is how many peer sessions are already open.
- `Engine Max Half-Open Connections` is how many new peer sessions are currently being attempted.
- `Engine Max Download Rate` is the global receive speed cap for torrent payload traffic.
- `Engine Max Upload Rate` is the global send speed cap for torrent payload traffic.
