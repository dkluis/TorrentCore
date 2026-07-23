# Current Decisions

## Supported Operator Surface

- `TorrentCore.WebUI` is the supported rich operator client.
- `TorrentCore.Web` and `TorrentCore.Avalonia` are not active delivery targets.

## Category Routing

- TorrentCore owns category definitions and category administration.
- Clients send stable category keys, not filesystem paths.
- TorrentCore resolves the effective download root at add time and persists that routing on the torrent.
- Category edits affect future torrents only.
- Default seeded categories are `TV`, `Movie`, `Audiobook`, and `Music`.
- If `CategoryKey` is omitted, TorrentCore currently falls back to the host-global `DownloadRootPath`.

## Completion Callback

- TorrentCore reuses the shared TVMaze-style callback entrypoint.
- TorrentCore launches the callback with Transmission-compatible environment variables.
- TorrentCore invokes the callback only after downstream-visible finalization is confirmed.
- Finalization checks do not treat the first internal completed edge as sufficient.
- Pending finalization, failure, timeout, and retryability are persisted separately from torrent transfer state.
- MonoTorrent partial-file naming is disabled; incomplete data may already use its final filename.
- TorrentCore does not delete final payload files as part of callback finalization.
- The callback is the authoritative downstream-readiness signal.
- Downstream systems do not infer readiness by independently scanning download paths or filenames.

## MonoTorrent Refactor Cutover

- Deploy the refactored MonoTorrent integration only after TorrentCore has zero active torrents and no active downloads.
- Do not migrate active MonoTorrent managers or partially downloaded payloads during cutover.
- Existing history may remain because it is separate from active torrent state.
- Inventory any existing `.!mt` artifacts before cutover; TorrentCore does not perform an automated migration.

## History Appendix

Durable history rules preserved from the completed history workstream:

- `torrents` remains the live runtime table
- `torrent_history` is a separate durable summary table
- one row represents one torrent lifecycle
- the row is inserted when the torrent is added and then updated in place
- history is not derived from activity logs
- history rows are retained after active torrent removal
- removal classification is stored as a structured kind rather than inferred from operator-facing reason text
- abandoned-download history can be retrieved independently of the torrent submission date

Important history fields:

- identity and routing
- latest operator-visible state and metrics
- lifecycle timestamps
- callback state
- removal outcome

Lifecycle update points that must remain represented in history:

- add magnet
- metadata resolved
- download starts
- state and progress changes
- download completion
- seeding starts
- callback begins
- callback completes, fails, or times out
- manual remove
- automatic cleanup remove
