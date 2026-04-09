# Torrent Engine New Chat Starter

## Status

This is a historical starter for the original repo/solution-boundary discussion.

- The supported operator client is now `TorrentCore.WebUI`.
- `TorrentCore.Web` and `TorrentCore.Avalonia` are legacy/reference-only and are no longer receiving feature updates or support maintenance.
- For current TorrentCore implementation work, prefer:
  - [Project Brief.md](/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TorrentCore/Documentation/Project%20Brief.md)
  - [Execution Plan and Progress.md](/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TorrentCore/Documentation/Execution%20Plan%20and%20Progress.md)
  - [TorrentCore Web UI Restart Plan.md](/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TorrentCore/Documentation/TorrentCore%20Web%20UI%20Restart%20Plan.md)

## Purpose
Use this as the starting context for a new chat about the torrent engine project boundary, repo structure, and solution structure.

Primary input document:
- [Torrent Engine Service Summary.md](/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TVMaze/Documentation/Torrent%20Engine%20Service%20Summary.md)

## Minimum Context To Provide

1. Goal
- Decide whether the torrent engine should be:
  - a separate repo
  - a separate solution in the TVMaze repo
  - part of `TVMaze.sln`

2. Current State
- Is there already working code?
- If yes:
  - where it currently lives
  - whether it already runs
  - whether it already has its own API, service host, DB, or contracts

3. Constraints
- Should it be deployable independently from TVMaze?
- Will anything other than TVMaze use it?
- Does it need its own release cadence?
- Should it remain generic beyond TV/media use cases?
- Will it run on separate machines from TVMaze?

4. Reuse Expectations
- What should be reused from TVMaze?
  - architectural practices
  - logging style
  - API patterns
  - DB patterns
  - error-handling patterns
- What should stay independent?

5. Preference Tension
- State explicitly that you are open to:
  - separate repo
  - separate solution
  - selective reuse of TVMaze practices without tight coupling

6. Desired Output
- discussion only
- recommendation only
- phased plan
- proposed repo/solution structure
- concrete project layout

## Helpful Extra Context
Include these only if they are known:
- expected clients of the torrent engine
- whether `TorrentCore.WebUI` is the only supported operator client for the scope being discussed
- whether the engine owns its own DB schema
- whether it should expose a public/internal API
- whether it will be run locally only or across multiple systems

## Paste-Ready Opening Message

```md
I want to decide whether the torrent engine should stay fully separate from TVMaze, including possibly a separate repo, while still reusing some architectural practices from TVMaze.

Primary context doc:
- [Torrent Engine Service Summary.md](/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TVMaze/Documentation/Torrent%20Engine%20Service%20Summary.md)

Current state:
- [Fill in current code/project status here]

Constraints:
- [Fill in deployment, reuse, and ownership constraints here]

What I want from you:
- Recommend whether this should be:
  - separate repo
  - separate solution in the same repo
  - or part of `TVMaze.sln`
- Give the tradeoffs.
- Propose a practical starting structure.
- Call out what should be reused from TVMaze and what should remain independent.
```

## Short Version
If I want the minimum possible starter, I should provide:
- the torrent engine summary doc
- current state of the code
- whether I want independent deployment
- whether non-TVMaze consumers are expected
- whether I want recommendation only or a concrete structure proposal
