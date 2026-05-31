# Troubleshooting

## Magnet Stuck In Metadata

Automatic recovery path:

1. after `Metadata Refresh Stale Seconds`, TorrentCore requests DHT and tracker refresh
2. after `Metadata Refresh Restart Delay Seconds`, TorrentCore escalates to stop/start
3. if the session still stays cold, TorrentCore can recreate the metadata session

Useful events to inspect:

- `torrent.metadata.refresh_requested`
- `torrent.metadata.restart_requested`
- `torrent.metadata.reset_requested`
- `torrent.engine.peers_found`
- `torrent.engine.peer_connected`
- `torrent.engine.peer_disconnected`
- `torrent.engine.connection_failed`

Operator guidance:

- use `Refresh Metadata` for a fresh discovery attempt
- use `Reset Metadata` for the stronger recovery path without deleting and re-adding the torrent
- compare TorrentCore behavior with another client on the same host before changing global settings

## Downloading But No Peers

TorrentCore also treats zero-peer download stalls as a stale-recovery case.

Useful checks:

- whether peer discovery occurs without ever reaching a connected peer
- whether TorrentCore logs `torrent.download.refresh_requested`
- whether TorrentCore logs `torrent.download.restart_requested`
- whether another client succeeds over IPv4 on the same host while IPv6 route failures appear in TorrentCore logs

## Completion Callback Problems

Remember:

- TorrentCore does not fire the callback on the engine's first internal completed edge alone
- finalization must be visible at the downstream payload path first
- partial-suffix files must no longer be the active payload

If callback behavior looks wrong, check:

- current callback settings
- category callback enablement
- callback state on the torrent
- final payload path visibility
- callback timeout versus finalization timeout

## Deployment And Runtime Checks

Useful runtime checks on the host:

```bash
cd ~/TorrentCore/Scripts
./agentstatus.zsh
curl http://127.0.0.1:7033/health
curl -I http://127.0.0.1:7053/
```

If the WebUI cannot reach the backend:

- recheck the persisted service endpoint
- verify the service health endpoint
- verify listen bindings and host firewall settings
- use the `Service Connection` page to test and save the intended endpoint
