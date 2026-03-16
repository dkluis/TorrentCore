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

## API Boundary Rule

TVMaze must talk to TorrentCore only through stable HTTP contracts or an intentionally versioned client library.

TVMaze must not reference TorrentCore engine internals directly.
