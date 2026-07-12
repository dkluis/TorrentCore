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
- Incomplete content is distinguished from completed content by explicit policy and engine-observed file state, not by guesswork.

## Category Routing And Callback Rules

- Category keys are stable API identifiers such as `TV`, `Movie`, `Audiobook`, and `Music`.
- Clients submit category keys, not raw filesystem paths.
- TorrentCore resolves the effective download root and callback routing at add time.
- Category edits affect future torrents only. Existing torrents keep their persisted routing values.
- If `CategoryKey` is omitted, TorrentCore currently falls back to the host-global `DownloadRootPath`.

Completion callback rules:

- TorrentCore reuses the shared TVMaze-style callback entrypoint instead of inventing a second callback stack.
- TorrentCore invokes the callback only after the downstream-visible final payload path is ready.
- TorrentCore does not treat the engine's first internal completed edge as sufficient by itself.
- When partial files are enabled, callback invocation waits until the final payload is visible and partial-suffix files are no longer the active payload.
- TorrentCore may expose the validated final payload path through `TORRENTCORE_FINAL_PAYLOAD_PATH`.
- TorrentCore does not delete partial files or final payload files during callback finalization.
- Downstream systems must not infer payload readiness by independently scanning download paths or filenames.

See [docs/decisions/current-decisions.md](decisions/current-decisions.md) for the extracted appendix with the current durable routing, callback, and history rules.
