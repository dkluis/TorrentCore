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

The supported operator UI is `TorrentCore.WebUI`.

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

The WebUI stays a thin client over service contracts. It must not:

- call MonoTorrent directly
- mutate persistence directly
- bypass service APIs for operator workflows
- embed engine or recovery policy that belongs in the service host

## Queueing And Lifecycle Rules

- TorrentCore accepts and persists incoming magnets even when runtime capacity is full.
- Active metadata-resolution and active-download limits control execution, not admission.
- Queued torrents wait inside TorrentCore until slots open.
- Queue order is oldest added first, with torrent id as a stable tie-breaker.
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
- Forced recovery announces run outside serialized synchronization, are deduplicated per torrent, and use a bounded tracker-announce window.

## Category Routing And Callback Rules

- Category keys are stable API identifiers such as `TV`, `Movie`, `Audiobook`, and `Music`.
- Clients submit category keys, not raw filesystem paths.
- TorrentCore resolves the effective download root and callback routing at add time.
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
- Serialized synchronization consumes completed probe and stop results and remains the sole owner of persisted
  callback-state mutations.
- Completed, stopped torrents leave the per-tick synchronization path after callback dispatch or terminal callback state.
- Waiting-for-feedback torrents are updated by the feedback API rather than periodic filesystem polling.
- Finalization visibility checks run only at the completion edge, while pending finalization, or during an explicit retry.

See [docs/decisions/current-decisions.md](decisions/current-decisions.md) for the extracted appendix with the current durable routing, callback, and history rules.
