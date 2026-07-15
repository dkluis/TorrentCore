# Operator Settings

## Queue And Concurrency

### Max Active Metadata Resolutions

- maximum number of torrents actively resolving magnet metadata
- extra unresolved magnets stay queued
- applies live

### Max Active Downloads

- maximum number of torrents actively downloading
- resolved torrents above the limit stay queued
- applies live

Queue diagnostics currently expose:

- open metadata and download slots
- counts for resolving, metadata-queued, downloading, download-queued, seeding, paused, completed, and errored torrents
- per-torrent wait reason and queue position when applicable

## Metadata Recovery

### Metadata Refresh Stale Seconds

- idle window before TorrentCore issues a DHT announce and forced tracker announce
- used for cold metadata sessions and zero-peer download stalls
- applies live

### Metadata Refresh Restart Delay Seconds

- additional delay before TorrentCore escalates stale recovery to stop/start
- used for both metadata stalls and zero-peer download stalls
- applies live

### Long-Cold Threshold Minutes

- continuous zero-peer and zero-progress duration before an active download enters long-cold recovery
- defaults to `240`
- useful peer or transfer activity restarts this timer
- applies live

### Long-Cold Recovery Interval Minutes

- minimum delay between automatic actions after a download enters long-cold recovery
- defaults to `60`
- actions alternate between peer refresh and restart, so restart normally occurs every two intervals
- applies live

## Engine Settings

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

- engine encryption mode
- engine max connections
- engine max half-open connections
- engine max download rate
- engine max upload rate

Live settings currently include:

- queue concurrency
- metadata and long-cold download recovery windows
- logging throttle settings
- seeding policy
- completed-torrent cleanup policy
- callback settings
- category settings
