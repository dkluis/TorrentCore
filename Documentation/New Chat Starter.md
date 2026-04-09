# TorrentCore New Chat Starter

## Status

- `TorrentCore.WebUI` is the supported operator client for ongoing work.
- `TorrentCore.Web` and `TorrentCore.Avalonia` are legacy/reference-only surfaces and are no longer receiving feature updates or support maintenance.
- Use this file only as a starter summary; current product direction should still be verified against:
  - [Project Brief.md](/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TorrentCore/Documentation/Project%20Brief.md)
  - [Execution Plan and Progress.md](/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TorrentCore/Documentation/Execution%20Plan%20and%20Progress.md)
  - [TorrentCore Web UI Restart Plan.md](/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TorrentCore/Documentation/TorrentCore%20Web%20UI%20Restart%20Plan.md)

Use these files as the source of truth for this repo:
- [Project Brief.md](/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TorrentCore/Documentation/Project%20Brief.md)
- [TVMaze Integration Boundary.md](/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TorrentCore/Documentation/TVMaze%20Integration%20Boundary.md)
- [Initial Scaffold Status.md](/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TorrentCore/Documentation/Initial%20Scaffold%20Status.md)

Continue from these agreed decisions:
- TorrentCore is a separate repo from TVMaze
- TorrentCore owns the engine, persistence, API, and dedicated admin UI
- TVMaze remains a lightweight integration client
- the supported rich admin UI is `TorrentCore.WebUI`
- `TorrentCore.Web` and `TorrentCore.Avalonia` are no longer active product targets

Current repo status:
- standalone solution and projects have been scaffolded
- the maintained operator surface is now `TorrentCore.WebUI`
- the next work should extend the real v1 torrent boundary, not re-argue repo structure

Useful original context from TVMaze:
- `/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TVMaze/Documentation/Torrent Engine Service Summary.md`
- `/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TVMaze/Documentation/Handoff Avalonia Chat.md`

Suggested opening prompt:

```md
Use these files as the source of truth for this repo:
- [Project Brief.md](/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TorrentCore/Documentation/Project%20Brief.md)
- [TVMaze Integration Boundary.md](/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TorrentCore/Documentation/TVMaze%20Integration%20Boundary.md)
- [Initial Scaffold Status.md](/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TorrentCore/Documentation/Initial%20Scaffold%20Status.md)

Continue with the agreed boundary:
- separate repo from TVMaze
- dedicated TorrentCore admin UI is primary
- TVMaze is a lightweight client only
- `TorrentCore.WebUI` is the supported operator UI
- legacy Web and Avalonia clients are not active delivery targets

Current task:
- [describe the next concrete implementation task here]
```
