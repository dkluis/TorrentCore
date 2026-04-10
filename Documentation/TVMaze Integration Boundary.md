# TVMaze Integration Boundary

## Dedicated Torrent UI Owns

- host registration and host switching
- full torrent list and detail views
- add magnet workflow
- queue and scheduling controls
- storage path settings
- incomplete-file suffix and finalization policy
- seeding ratio and seeding-time policy
- category or tag management
- engine configuration
- diagnostics, logs, and recovery actions
- capability and version views per host

## TVMaze Owns

- TV/media workflow context
- deciding when a torrent should be added from a TVMaze flow
- lightweight torrent summary views tied to TVMaze use cases

## TVMaze Is Allowed To Do

- select a TorrentCore host
- add a magnet with a stable category key
- show progress and simple state
- pause, resume, remove
- display completion or error state
- treat files without the configured incomplete suffix as ready for downstream processing
- provide a link or launch path into the dedicated TorrentCore UI

## TVMaze Should Not Own

- primary torrent admin UX
- engine configuration
- deep queue policy
- storage policy
- category administration and download-root routing
- completion callback command configuration
- incomplete-file lifecycle policy
- seeding policy
- host administration
- engine persistence
- engine diagnostics

## Category Rule

TVMaze should submit stable category keys such as:

- `TV`
- `Movie`
- `Audiobook`
- `Music`

TorrentCore should resolve category download roots and callback labels internally.

TVMaze should not submit raw download directories or callback command details.

Transition compatibility rule:

- if a client omits `CategoryKey`, TorrentCore currently falls back to the host's global `DownloadRootPath`
- that fallback exists to keep older/manual add flows working until all clients move to explicit category submission

## Shared Callback Rule

TorrentCore and Transmission should be able to call the same existing TVMaze completion callback entrypoint.

TorrentCore should emulate the expected Transmission-style environment variables when invoking that shared callback,
instead of requiring a second callback application or a TorrentCore-specific callback protocol.

Directory alignment rule:

- when TVMaze validates completion callbacks against `TransmissionRoute:{Category}` configuration, TorrentCore category
  `DownloadRootPath` must match that route for the same callback label/category
- otherwise TVMaze rejects the callback before downstream FileOps handling begins
- changing a TorrentCore category affects future torrents only because the resolved download root is persisted per
  torrent at add time

Possible later evolution:

- if shared download roots become too constraining, TVMaze and TorrentCore may later adopt a client-scoped acquisition
  root model so Transmission and TorrentCore can keep different physical directory structures
- that would need an explicit mapping layer at the shared completion boundary; it is not the current design

Important timing rule:

- do not fire the shared callback on the engine's first internal completed edge alone
- fire it only after the downstream-visible final path at `Path.Combine(TR_TORRENT_DIR, TR_TORRENT_NAME)` exists as a file or directory
- if incomplete-file mode is enabled, the callback must wait until the incomplete-suffix variant is no longer the only visible payload
- TorrentCore may also provide the exact validated final payload path through `TORRENTCORE_FINAL_PAYLOAD_PATH` so the shared callback can avoid reconstructing a single-file source path from name/root alone

Finalization check guidance:

- if `Path.Combine(TR_TORRENT_DIR, TR_TORRENT_NAME)` resolves to a file, TorrentCore should wait until the final-name file exists and the partial-suffix sibling is gone
- if it resolves to a directory, TorrentCore should recursively scan that subtree and wait until no partial-suffix files remain
- when MonoTorrent exposes exact per-file complete/incomplete paths for the active torrent, TorrentCore should prefer those engine-reported paths over a reconstructed `TR_TORRENT_NAME` guess when deciding whether a single-file payload is really finalized
- when MonoTorrent also reports that the file's current active path is already the complete path, a leftover `.!mt` sibling should be treated as stale cleanup residue rather than proof that the payload is still incomplete
- TorrentCore should persist a generic callback lifecycle state such as `PendingFinalization`, `Invoked`, `Failed`, or `TimedOut` so restart recovery does not confuse transfer state with callback state
- if the callback process itself fails or times out after starting, TorrentCore should not present that as another finalization-visibility timeout; if the source path disappears during the callback attempt, diagnostics should say that the callback may already have moved the payload
- TorrentCore should not persist TVMaze-specific outcome states; TVMaze ownership remains outside the TorrentCore boundary

TVMaze validates source existence immediately when it receives the callback. If TorrentCore invokes the callback too
early, TVMaze treats it as already handled or missing because the expected final path does not exist yet.

## API Boundary Rule

TVMaze must talk to TorrentCore only through stable HTTP contracts or an intentionally versioned client library.

TVMaze must not reference TorrentCore engine internals directly.
