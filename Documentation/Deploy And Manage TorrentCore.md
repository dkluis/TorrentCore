# Deploy And Manage TorrentCore

## Purpose

This document is the operator runbook for deploying and managing the current TorrentCore runtime on macOS.

It covers:
- publishing and deploying the Service and WebUI
- installing the `launchd` agents
- start, stop, restart, and status commands
- the expected file layout and runtime logs

## Runtime Model

TorrentCore is managed through per-user macOS `LaunchAgents`.

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

Important behavior:
- `install-launch-agents.zsh` installs the plists and starts the agents automatically
- you do not need to run a separate `start` command after a normal install
- `ManageTorrentCoreLaunchAgents.zsh` exists for explicit operator control after install

## Expected Target Layout

On the runtime machine:

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

## Environment File

Create a host-local runtime file at:

```bash
~/TorrentCore/Scripts/torrentcore.env
```

Typical ARM host example:

```bash
TORRENTCORE_ASPNETCORE_ENVIRONMENT=Production
TORRENTCORE_SERVICE_URLS=http://0.0.0.0:7033
TORRENTCORE_WEBUI_URLS=http://0.0.0.0:7053
TORRENTCORE_WEBUI_SERVICE_BASE_URL=http://127.0.0.1:7033/

TORRENTCORE_DEPLOY_BASE_INTEL=/Volumes/HD-Boot-CA-Server/Users/dick/TorrentCore
TORRENTCORE_DEPLOY_BASE_ARM=/Users/dick/TorrentCore
```

Deploy target rule:
- set `TORRENTCORE_DEPLOY_BASE_INTEL` and `TORRENTCORE_DEPLOY_BASE_ARM`
- do not set a shared `TORRENTCORE_DEPLOY_BASE` in `torrentcore.env`

## Clean ARM Deploy Test

This is the preferred full validation path on the ARM runtime host.

First clear any already-running processes or loaded agents:

```bash
pkill -f '/Users/dick/TorrentCore/Service/TorrentCoreService' || true
pkill -f '/Users/dick/TorrentCore/WebUI/TorrentCore.WebUI' || true

launchctl bootout gui/$(id -u) ~/Library/LaunchAgents/com.torrentcore.service.plist 2>/dev/null || true
launchctl bootout gui/$(id -u) ~/Library/LaunchAgents/com.torrentcore.webui.plist 2>/dev/null || true
```

Then deploy from the repo:

```bash
cd /Volumes/HD-Desktop-Dev-L5/Development/Source/C#/TorrentCore
./Scripts/deploy-all-arm.zsh
```

Then install the launch agents on the runtime host:

```bash
cd /Users/dick/TorrentCore/Scripts
./install-launch-agents.zsh all
./agentstatus.zsh
```

Because the install script bootstraps and kickstarts the agents, this is normally enough to move from deploy to running.

## Intel Deploy

Equivalent Intel deploy flow:

```bash
cd /Volumes/HD-Desktop-Dev-L5/Development/Source/C#/TorrentCore
./Scripts/deploy-all-intel.zsh
```

Then on the Intel runtime host:

```bash
cd ~/TorrentCore/Scripts
./install-launch-agents.zsh all
./agentstatus.zsh
```

## Deploy Script Reference

Available deploy scripts:

```bash
./Scripts/deploy-service-arm.zsh
./Scripts/deploy-webui-arm.zsh
./Scripts/deploy-all-arm.zsh

./Scripts/deploy-service-intel.zsh
./Scripts/deploy-webui-intel.zsh
./Scripts/deploy-all-intel.zsh
```

Optional same-host restart during deploy:

```bash
./Scripts/deploy-all-arm.zsh --restart
./Scripts/deploy-all-intel.zsh --restart
```

Use `--restart` only when the deploy script is running on the same machine that hosts the runtime.

## Install Script Reference

Install both agents:

```bash
cd ~/TorrentCore/Scripts
./install-launch-agents.zsh all
```

Install only the service:

```bash
./install-launch-agents.zsh service
```

Install only the WebUI:

```bash
./install-launch-agents.zsh webui
```

What install does:
- renders the plist files into `~/Library/LaunchAgents`
- validates them with `plutil`
- bootstraps them with `launchctl`
- kickstarts the selected agents immediately

## Start Stop Restart Commands

After install, use the management script for explicit control.

Start everything:

```bash
./ManageTorrentCoreLaunchAgents.zsh start all
```

Stop everything:

```bash
./ManageTorrentCoreLaunchAgents.zsh stop all
```

Restart everything:

```bash
./ManageTorrentCoreLaunchAgents.zsh restart all
```

Service-only control:

```bash
./ManageTorrentCoreLaunchAgents.zsh start service
./ManageTorrentCoreLaunchAgents.zsh stop service
./ManageTorrentCoreLaunchAgents.zsh restart service
```

WebUI-only control:

```bash
./ManageTorrentCoreLaunchAgents.zsh start webui
./ManageTorrentCoreLaunchAgents.zsh stop webui
./ManageTorrentCoreLaunchAgents.zsh restart webui
```

Behavior note:
- `stop` performs `bootout` and `disable`
- if you stop an agent before reboot, it should stay stopped after reboot/login
- if an agent is installed and enabled and you do not stop it first, it should start again at next user login

## Status Commands

Show current launch-agent status:

```bash
./agentstatus.zsh
```

Direct health checks:

```bash
curl http://127.0.0.1:7033/health
curl -I http://127.0.0.1:7053/
```

`agentstatus.zsh` reports:
- plist path
- current state
- pid when running
- last exit info when available
- configured program path

## Logs

Runtime logs:
- `~/TorrentCore/Logs/TorrentCore.Service.launchd.out.log`
- `~/TorrentCore/Logs/TorrentCore.Service.launchd.err.log`
- `~/TorrentCore/Logs/TorrentCore.WebUI.launchd.out.log`
- `~/TorrentCore/Logs/TorrentCore.WebUI.launchd.err.log`

Manager script logs:
- `~/TorrentCore/Logs/LaunchAgents.console.log`
- `~/TorrentCore/Logs/LaunchAgents.errors.log`

Crash reports when macOS terminates a process:
- `~/Library/Logs/DiagnosticReports/`

## Known Build Detail

The service now publishes as:
- executable: `TorrentCoreService`
- managed assembly: `TorrentCoreService.dll`

This is intentional. The earlier dotted executable name did not behave reliably under the tested launch-agent flow on the ARM host.

## Normal Operator Workflow

Typical day-to-day commands on the runtime host:

```bash
cd ~/TorrentCore/Scripts
./agentstatus.zsh
./ManageTorrentCoreLaunchAgents.zsh restart service
./ManageTorrentCoreLaunchAgents.zsh restart webui
```

Typical full refresh after code changes:

```bash
cd /Volumes/HD-Desktop-Dev-L5/Development/Source/C#/TorrentCore
./Scripts/deploy-all-arm.zsh

cd ~/TorrentCore/Scripts
./install-launch-agents.zsh all
./agentstatus.zsh
```
