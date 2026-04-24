# TorrentCore Scripts

This folder contains:
- runtime launch-agent scripts intended to live on the deployed host under `~/TorrentCore/Scripts`
- deploy scripts intended to run from the repo on the development machine and sync to either the Intel or Arm deployment target
- one ARM desktop deploy script for the Avalonia app that publishes and bundles a macOS `.app`

## Execution Model

These scripts have two roles:
- `deploy-*.zsh` scripts are publish-and-copy scripts
- `ManageTorrentCoreLaunchAgents.zsh`, `install-launch-agents.zsh`, and `agentstatus.zsh` are host-local runtime control scripts

Important rule:
- deploy scripts can be run from any machine that has the repo, `dotnet`, `rsync`, and write access to the target path
- runtime launch-agent scripts should be run on the machine that is actually running TorrentCore
- deploy scripts are intentionally not copied into the target host `~/TorrentCore/Scripts` directory

Current limitation:
- if you run a deploy script from one machine against another machine's mounted share, the `--restart` flag is not a true remote restart
- in that case the deploy should be run without `--restart`, and the target host should run its own local launch-agent install or restart command afterward

## Runtime Script Layout

Expected sibling layout on the target host:
- `~/TorrentCore/Service`
- `~/TorrentCore/WebUI`
- `~/TorrentCore/Scripts`

The runtime scripts infer those sibling paths automatically from their own location.

What is copied to the target host `Scripts` directory:
- `ManageTorrentCoreLaunchAgents.zsh`
- `install-launch-agents.zsh`
- `agentstatus.zsh`
- `com.torrentcore.service.plist`
- `com.torrentcore.webui.plist`
- `lib/torrentcore-common.zsh`
- `torrentcore.env.example`
- `README.md`

What is not copied to the target host:
- `deploy-*.zsh`

Desktop app note:
- `TorrentCore.Avalonia` is not managed through the launch-agent scripts
- its deploy script builds a macOS `.app` bundle for ARM Macs instead

## Runtime Files

The launch-agent installer creates:
- `~/Library/LaunchAgents/com.torrentcore.service.plist`
- `~/Library/LaunchAgents/com.torrentcore.webui.plist`
- `~/TorrentCore/Logs/TorrentCore.Service.launchd.out.log`
- `~/TorrentCore/Logs/TorrentCore.Service.launchd.err.log`
- `~/TorrentCore/Logs/TorrentCore.WebUI.launchd.out.log`
- `~/TorrentCore/Logs/TorrentCore.WebUI.launchd.err.log`
- `~/TorrentCore/Logs/LaunchAgents.console.log`
- `~/TorrentCore/Logs/LaunchAgents.errors.log`

The launch agents run the published executables directly:
- `~/TorrentCore/Service/TorrentCoreService`
- `~/TorrentCore/WebUI/TorrentCore.WebUI`

## Runtime Commands

From `~/TorrentCore/Scripts` on the target host:

```bash
./install-launch-agents.zsh all
./ManageTorrentCoreLaunchAgents.zsh start all
./ManageTorrentCoreLaunchAgents.zsh stop all
./ManageTorrentCoreLaunchAgents.zsh restart all
./agentstatus.zsh
```

Service-specific and WebUI-specific control:

```bash
./install-launch-agents.zsh service
./install-launch-agents.zsh webui

./ManageTorrentCoreLaunchAgents.zsh restart service
./ManageTorrentCoreLaunchAgents.zsh restart webui
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
./Scripts/deploy-avalonia-arm.zsh
```

Optional restart during deploy:

```bash
./Scripts/deploy-all-intel.zsh --restart
```

Use `--restart` only when the deploy script is being run on the same host that will run TorrentCore.
When `--restart` is used, the deploy now reinstalls the affected launch agents and restarts them through `launchctl`.

Desktop app deploy:

```bash
./Scripts/deploy-avalonia-arm.zsh
```

That script:
- publishes `src/TorrentCore.Avalonia`
- creates a macOS `.app` bundle
- syncs raw publish output to `TORRENTCORE_AVALONIA_PUBLISH_TARGET`
- syncs the `.app` bundle to `TORRENTCORE_AVALONIA_APP_TARGET`
- when the deploy target is local to the current ARM machine, also mirrors the `.app` bundle to `/Applications` by default
- optionally syncs a second `.app` bundle copy to `TORRENTCORE_AVALONIA_APP_MIRROR_TARGET`

For cross-machine deploy over a mounted share:
1. run the deploy script without `--restart`
2. log onto the target host
3. run the target host's local launch-agent install or restart command

Example on the target host:

```bash
cd ~/TorrentCore/Scripts
./install-launch-agents.zsh all
./ManageTorrentCoreLaunchAgents.zsh restart all
```

## Environment Overrides

Copy [torrentcore.env.example](/Volumes/HD-Desktop-Dev-L5/Development/Source/C#/TorrentCore/Scripts/torrentcore.env.example) to `Scripts/torrentcore.env` and edit the values you need.

First-start rule:
- every deployed host should create its own `~/TorrentCore/Scripts/torrentcore.env`
- do not rely on the script built-in defaults for a real deployed host
- if `torrentcore.env` is missing, the launch-agent installer logs a warning and falls back to the built-in loopback defaults

Recommended host profile for a LAN-accessible Web UI with a local-only service:

```bash
TORRENTCORE_SERVICE_URLS=http://127.0.0.1:7033
TORRENTCORE_WEBUI_URLS=http://0.0.0.0:7053
TORRENTCORE_WEBUI_SERVICE_BASE_URL=http://127.0.0.1:7033/
```

If you also want remote API access or remote Avalonia clients to hit that host's service directly:

```bash
TORRENTCORE_SERVICE_URLS=http://0.0.0.0:7033
```

Useful overrides:
- `TORRENTCORE_DEPLOY_BASE_INTEL`
- `TORRENTCORE_DEPLOY_BASE_ARM`
- `TORRENTCORE_ASPNETCORE_ENVIRONMENT`
- `TORRENTCORE_SERVICE_URLS`
- `TORRENTCORE_WEBUI_URLS`
- `TORRENTCORE_WEBUI_SERVICE_BASE_URL`
- `TORRENTCORE_LAUNCH_AGENT_TARGET_DIR`
- `TORRENTCORE_LAUNCH_AGENT_LOG_DIR`
- `TORRENTCORE_SERVICE_LAUNCH_LABEL`
- `TORRENTCORE_WEBUI_LAUNCH_LABEL`
- `TORRENTCORE_PUBLISH_CONFIGURATION`
- `TORRENTCORE_PUBLISH_RUNTIME`
- `TORRENTCORE_PUBLISH_RUNTIME_INTEL`
- `TORRENTCORE_PUBLISH_RUNTIME_ARM`
- `TORRENTCORE_AVALONIA_PUBLISH_TARGET`
- `TORRENTCORE_AVALONIA_APP_TARGET`
- `TORRENTCORE_AVALONIA_APP_MIRROR_TARGET`
- `TORRENTCORE_AVALONIA_SYSTEM_APPLICATIONS_TARGET`
- `TORRENTCORE_AVALONIA_SYSTEM_APPLICATIONS_MIRROR`

## Remote Web Access

The built-in script defaults are loopback-only for the service and loopback-only unless overridden for the Web UI:
- service: `http://127.0.0.1:7033`
- web UI: `http://127.0.0.1:7053`

Those defaults are only a fallback. Real deployed hosts should use `torrentcore.env`.

After changing network bindings in `torrentcore.env`, rerun `./install-launch-agents.zsh all` so the regenerated plists carry the updated environment.

Deploy target rule:
- configure `TORRENTCORE_DEPLOY_BASE_INTEL` and `TORRENTCORE_DEPLOY_BASE_ARM`
- do not set a shared `TORRENTCORE_DEPLOY_BASE` in `torrentcore.env`
- the deploy scripts derive `TORRENTCORE_DEPLOY_BASE` from the selected target internally
