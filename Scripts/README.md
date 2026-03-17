# TorrentCore Scripts

This folder contains:
- runtime control scripts intended to live on the deployed host under `~/TorrentCore/Scripts`
- deploy scripts intended to run from the repo on the development machine and sync to either the Intel or Arm deployment target

## Execution Model

These scripts have two roles:
- `deploy-*.zsh` scripts are publish-and-copy scripts
- `start/stop/restart-*.zsh` scripts are host-local runtime control scripts

Important rule:
- deploy scripts can be run from any machine that has the repo, `dotnet`, `rsync`, and write access to the target path
- runtime start/stop/restart scripts should be run on the machine that is actually running TorrentCore
- deploy scripts are intentionally not copied into the target host `~/TorrentCore/Scripts` directory

Current limitation:
- if you run a deploy script from one machine against another machine's mounted share, the `--restart` flag is not a true remote restart
- in that case the deploy should be run without `--restart`, and the target host should run its own local restart scripts afterward

## Runtime Script Layout

Expected sibling layout on the target host:
- `~/TorrentCore/Service`
- `~/TorrentCore/WebUI`
- `~/TorrentCore/Scripts`

The runtime scripts infer those sibling paths automatically from their own location.

What is copied to the target host `Scripts` directory:
- `start-service.zsh`
- `stop-service.zsh`
- `restart-service.zsh`
- `start-webui.zsh`
- `stop-webui.zsh`
- `restart-webui.zsh`
- `lib/torrentcore-common.zsh`
- `torrentcore.env.example`
- `README.md`

What is not copied to the target host:
- `deploy-*.zsh`

## Runtime Files

The scripts create:
- `Scripts/run/service.pid`
- `Scripts/run/webui.pid`
- `Scripts/logs/service.log`
- `Scripts/logs/webui.log`

## Runtime Commands

From `~/TorrentCore/Scripts` on the target host:

```bash
./start-service.zsh
./stop-service.zsh
./restart-service.zsh

./start-webui.zsh
./stop-webui.zsh
./restart-webui.zsh
```

## Deploy Commands

From the repo root on the development machine:

```bash
./Scripts/deploy-service-intel.zsh
./Scripts/deploy-webui-intel.zsh
./Scripts/deploy-all-intel.zsh

./Scripts/deploy-service-arm.zsh
./Scripts/deploy-webui-arm.zsh
./Scripts/deploy-all-arm.zsh
```

Optional restart during deploy:

```bash
./Scripts/deploy-all-intel.zsh --restart
```

Use `--restart` only when the deploy script is being run on the same host that will run TorrentCore.

For cross-machine deploy over a mounted share:
1. run the deploy script without `--restart`
2. log onto the target host
3. run the target host's local restart scripts

Example on the target host:

```bash
cd ~/TorrentCore/Scripts
./restart-service.zsh
./restart-webui.zsh
```

## Environment Overrides

Copy [torrentcore.env.example](/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TorrentCore/Scripts/torrentcore.env.example) to `Scripts/torrentcore.env` and edit the values you need.

# Completion Callback Contract

## Purpose
TorrentCore invokes an external callback script when a torrent completes downloading.
This allows integration with downstream systems (e.g., TVMaze) without coupling TorrentCore to their internals.

## Invocation Trigger
The callback is invoked **once** when a torrent transitions to completed state:
- `CompletedAtUtc` changes from null to a value
- `State` is `Completed` or `Seeding`
- `InvokeCompletionCallback` is true
- `CompletionCallbackLabel` is non-empty

## Environment Variables
The callback script receives the following environment variables:

| Variable | Source | Example |
|----------|--------|---------|
| `TR_TORRENT_HASH` | InfoHash | `abc123...` |
| `TR_TORRENT_NAME` | Torrent name | `Show.S01E01.1080p` |
| `TR_TORRENT_DIR` | Download root path | `/downloads/tv` |
| `TR_TORRENT_LABELS` | Category callback label | `TV` |
| `TVMAZE_API_COMPLETE_URL` | Optional API override | `https://api.example.com` |
| `TVMAZE_API_COMPLETE_API_KEY` | Optional API key override | `secret123` |

Note: `TR_TORRENT_ID` is always `"0"` (reserved for future use).

## Exit Code Contract
- **0**: Success - callback processed the completion
- **Non-zero**: Failure - callback encountered an error

Failed callbacks are logged but **not retried automatically**.

## Timeout
Callbacks must complete within `CompletionCallbackTimeoutSeconds` (configurable, default TBD).
Timed-out processes are killed and logged as warnings.

## Failure Handling
- Launch failures, timeouts, and non-zero exits are logged to the activity log
- No automatic retries
- Operators can manually re-invoke via [TBD: API endpoint or UI action]

## Category Mapping
Each category defines its own `CallbackLabel`:
- `TV` → `"TV"`
- `Movie` → `"Movie"`
- `Audiobook` → `"Audiobook"`
- `Music` → `"Music"`

The label is passed to the callback script via `TR_TORRENT_LABELS` for routing logic.

Useful overrides:
- `TORRENTCORE_DEPLOY_BASE`
- `TORRENTCORE_DEPLOY_BASE_INTEL`
- `TORRENTCORE_DEPLOY_BASE_ARM`
- `TORRENTCORE_ASPNETCORE_ENVIRONMENT`
- `TORRENTCORE_SERVICE_URLS`
- `TORRENTCORE_WEBUI_URLS`
- `TORRENTCORE_WEBUI_SERVICE_BASE_URL`
- `TORRENTCORE_PUBLISH_CONFIGURATION`
- `TORRENTCORE_PUBLISH_RUNTIME`
- `TORRENTCORE_PUBLISH_RUNTIME_INTEL`
- `TORRENTCORE_PUBLISH_RUNTIME_ARM`

## Remote Web Access

The default script bindings are loopback-only:
- service: `http://127.0.0.1:7033`
- web UI: `http://127.0.0.1:7053`

That means the Intel Mac itself can open the UI, but another machine cannot.

To allow browser access from another machine on the network, set this in `Scripts/torrentcore.env` on the Intel Mac:

```bash
TORRENTCORE_WEBUI_URLS=http://0.0.0.0:7053
TORRENTCORE_WEBUI_SERVICE_BASE_URL=http://127.0.0.1:7033/
```

If you also want remote Swagger/API access, set:

```bash
TORRENTCORE_SERVICE_URLS=http://0.0.0.0:7033
```

After changing those values, restart the affected process.
