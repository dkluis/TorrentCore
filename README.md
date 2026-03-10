# TorrentCore

TorrentCore is a standalone torrent engine product kept separate from TVMaze.

Current repo scope:
- standalone service host
- standalone web admin UI
- shared contracts and client library
- SQLite persistence layer
- explicit handoff docs for continuation in Rider

Key boundary:
- TorrentCore owns torrent engine state, policy, persistence, and management UX
- TVMaze is only a lightweight client/integration surface

Start with these docs:
- `Documentation/Project Brief.md`
- `Documentation/TVMaze Integration Boundary.md`
- `Documentation/Initial Scaffold Status.md`
- `Documentation/New Chat Starter.md`
- `Documentation/Operator Settings Reference.md`

Repository legal documents:
- `LICENSE.md`
- `DISCLAIMER.md`
