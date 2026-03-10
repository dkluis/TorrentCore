# TorrentCore New Chat Starter

Use these files as the source of truth for this repo:
- [Project Brief.md](/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TorrentCore/Documentation/Project%20Brief.md)
- [TVMaze Integration Boundary.md](/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TorrentCore/Documentation/TVMaze%20Integration%20Boundary.md)
- [Initial Scaffold Status.md](/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TorrentCore/Documentation/Initial%20Scaffold%20Status.md)

Continue from these agreed decisions:
- TorrentCore is a separate repo from TVMaze
- TorrentCore owns the engine, persistence, API, and dedicated admin UI
- TVMaze remains a lightweight integration client
- the first rich admin UI is Web
- Avalonia may be added later as a separate TorrentCore client

Current repo status:
- standalone solution and projects have been scaffolded
- a minimal health contract, API, client, and web shell exist
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
- Web is the first rich UI

Current task:
- [describe the next concrete implementation task here]
```
