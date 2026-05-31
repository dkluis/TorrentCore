# Deployment

## Runtime Model

TorrentCore is currently deployed on macOS through per-user `LaunchAgents`.

Managed components:

- `TorrentCoreService`
- `TorrentCore.WebUI`

Launch agent labels:

- `com.torrentcore.service`
- `com.torrentcore.webui`

Installed plists:

- `~/Library/LaunchAgents/com.torrentcore.service.plist`
- `~/Library/LaunchAgents/com.torrentcore.webui.plist`

Published executables:

- `~/TorrentCore/Service/TorrentCoreService`
- `~/TorrentCore/WebUI/TorrentCore.WebUI`

## Target Layout

```text
~/TorrentCore/
├── Service/
│   └── TorrentCoreService
├── WebUI/
│   └── TorrentCore.WebUI
├── Scripts/
│   ├── install-launch-agents.zsh
│   ├── ManageTorrentCoreLaunchAgents.zsh
│   ├── agentstatus.zsh
│   ├── torrentcore.env
│   └── ...
└── Logs/
```

## Deploy Scripts

Run from the repo root on a machine that has the repo, `dotnet`, `rsync`, and access to the target path:

```bash
./Scripts/deploy-service-intel.zsh
./Scripts/deploy-webui-intel.zsh
./Scripts/deploy-all-intel.zsh

./Scripts/deploy-service-arm.zsh
./Scripts/deploy-webui-arm.zsh
./Scripts/deploy-all-arm.zsh
```

Rules:

- deploy scripts are repo-side publish-and-copy scripts
- runtime scripts should be executed on the machine actually hosting TorrentCore
- `--restart` is only valid when the deploy command runs on the same host as the runtime

Platform defaults:

| Target | Runtime | Mode |
|---|---|---|
| Intel Mac | `osx-x64` | framework-dependent |
| Arm Mac | `osx-arm64` | framework-dependent |

## Launch-Agent Management

Install both agents:

```bash
cd ~/TorrentCore/Scripts
./install-launch-agents.zsh all
```

Install only one:

```bash
./install-launch-agents.zsh service
./install-launch-agents.zsh webui
```

Explicit control:

```bash
./ManageTorrentCoreLaunchAgents.zsh start all
./ManageTorrentCoreLaunchAgents.zsh stop all
./ManageTorrentCoreLaunchAgents.zsh restart all
./agentstatus.zsh
```

`install-launch-agents.zsh` renders the plists, validates them, bootstraps them, and starts the selected agents.

## Environment Overrides

Create a host-local runtime file:

```bash
~/TorrentCore/Scripts/torrentcore.env
```

Useful overrides include:

- `TORRENTCORE_DEPLOY_BASE_INTEL`
- `TORRENTCORE_DEPLOY_BASE_ARM`
- `TORRENTCORE_ASPNETCORE_ENVIRONMENT`
- `TORRENTCORE_SERVICE_URLS`
- `TORRENTCORE_WEBUI_URLS`
- `TORRENTCORE_WEBUI_SERVICE_BASE_URL`

Real deployed hosts should use `torrentcore.env` instead of relying on built-in script defaults.

## Network Model

TorrentCore is currently intended for a trusted local network.

Rules:

- no public internet exposure
- no TLS/certificate management in this slice
- the service API must be reachable from the WebUI host
- the WebUI may be LAN-exposed

Default ports:

| Component | Default HTTP Port |
|---|---:|
| `TorrentCore.Service` | `5078` |
| `TorrentCore.WebUI` | `5131` |

Repo defaults stay on `localhost` for normal development. LAN binding is a deploy-time concern.

## WebUI Service Restart

`TorrentCore.WebUI` exposes a `Restart Service` action on the `Service Connection` page.

Rules:

- the browser does not call `launchctl` directly
- the service schedules its own restart
- a short outage window is expected
- manual browser refresh may still be needed if reconnect does not settle cleanly

Use the WebUI restart action for normal remote restarts.
Use the local runtime scripts when you need explicit host-side control.

## Logs And Status

Runtime logs:

- `~/TorrentCore/Logs/TorrentCore.Service.launchd.out.log`
- `~/TorrentCore/Logs/TorrentCore.Service.launchd.err.log`
- `~/TorrentCore/Logs/TorrentCore.WebUI.launchd.out.log`
- `~/TorrentCore/Logs/TorrentCore.WebUI.launchd.err.log`

Script logs:

- `~/TorrentCore/Logs/LaunchAgents.console.log`
- `~/TorrentCore/Logs/LaunchAgents.errors.log`

Basic checks:

```bash
./agentstatus.zsh
curl http://127.0.0.1:7033/health
curl -I http://127.0.0.1:7053/
```
