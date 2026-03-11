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

Unit:
- Minutes.

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
