# TorrentCore Intel Mac Deployment Target

## Purpose

This document captures the first real deployment target outside the current development machine.

The target is:
- a separate macOS Intel host
- .NET 10 already installed on that host
- service and web started through `zsh` scripts
- scripts launched at boot/login through macOS login items or equivalent operator startup flow

This is now a tracked product/deployment requirement, not an informal note.

## Deployment Layout

Target base path on the Intel Mac host:
- `~/TorrentCore`

Planned runtime layout on that host:
- `~/TorrentCore/Service`
- `~/TorrentCore/WebUI`
- `~/TorrentCore/Scripts`

Current real mounted path from the development machine:
- `/Volumes/HD-Boot-CA-Server/Users/dick/TorrentCore/Service`
- `/Volumes/HD-Boot-CA-Server/Users/dick/TorrentCore/WebUI`
- `/Volumes/HD-Boot-CA-Server/Users/dick/TorrentCore/Scripts`

Interpretation:
- build and publish can happen from the current machine
- deployment output is copied to the mounted Intel Mac share
- scripts are stored alongside the deployed runtime under the target `Scripts` directory

## First Deployment Model

Initial deployment will be:
- Intel-only
- framework-dependent
- published for macOS x64

Reasoning:
- the target machine already has .NET 10 installed
- this keeps the first deployment smaller and simpler
- self-contained publishing can be added later if operationally needed

Initial publish target:
- `osx-x64`

## Required Scripts

The deployment target requires these `zsh` scripts:

Service scripts:
- `start-service.zsh`
- `stop-service.zsh`
- `restart-service.zsh`

Web UI scripts:
- `start-webui.zsh`
- `stop-webui.zsh`
- `restart-webui.zsh`

Deployment scripts:
- `deploy-service-intel.zsh`
- `deploy-webui-intel.zsh`
- `deploy-all-intel.zsh`

These now live in:
- [Scripts](/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TorrentCore/Scripts)

Important split:
- `start/stop/restart` scripts are target-host runtime scripts
- `deploy` scripts are repo-side scripts that publish from the current machine and sync to the mounted Intel Mac share

## Script Responsibilities

### Service Start Script

Should:
- start the published `TorrentCore.Service` app from `~/TorrentCore/Service`
- run under `zsh`
- write stdout/stderr to a predictable log file
- record a PID file so stop/restart can target the right process
- preserve the service working directory

### Service Stop Script

Should:
- stop only the TorrentCore service process
- use the PID file first if present
- avoid killing unrelated `dotnet` processes on the host

### Service Restart Script

Should:
- call the service stop and start flow safely

### Web UI Start Script

Should:
- start the published `TorrentCore.Web` app from `~/TorrentCore/WebUI`
- run under `zsh`
- write stdout/stderr to a predictable log file
- record a PID file so stop/restart can target the right process
- preserve the web working directory

### Web UI Stop Script

Should:
- stop only the TorrentCore web process
- use the PID file first if present
- avoid killing unrelated `dotnet` processes on the host

### Web UI Restart Script

Should:
- call the web stop and start flow safely

### Deploy Scripts

Should:
- publish `TorrentCore.ServiceHost` and `TorrentCore.Web` for `osx-x64`
- copy only the published runtime output to the target host share
- copy scripts into the target `Scripts` directory
- create target directories if they do not already exist
- avoid copying source, test output, and local development artifacts

## Directory Expectations

Service runtime directory:
- contains the published service output only
- should be safe to replace during deployment

Web UI runtime directory:
- contains the published web output only
- should be safe to replace during deployment

Scripts directory:
- contains all operational scripts
- now also contains:
  - `run/` for PID files
  - `logs/` for shell-level start/stop logs
  - optional `torrentcore.env` for operator overrides

Current script-created state files:
- `~/TorrentCore/Scripts/run/service.pid`
- `~/TorrentCore/Scripts/run/webui.pid`
- `~/TorrentCore/Scripts/logs/service.log`
- `~/TorrentCore/Scripts/logs/webui.log`

Current environment template:
- [torrentcore.env.example](/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TorrentCore/Scripts/torrentcore.env.example)

## Logging Expectations

The script layer should log enough to troubleshoot:
- process startup
- process shutdown
- startup failure
- publish/deploy failure
- target copy failure

This is separate from TorrentCore's own application/activity logs.

Current decision:
- script-layer stdout/stderr is written to `Scripts/logs`
- TorrentCore application logs continue to be managed by the application itself

## Process Management Expectations

The first script-based deployment should:
- support independent service and web lifecycle control
- support boot/login startup
- avoid requiring Rider or manual `dotnet run`
- avoid broad `pkill dotnet`-style process management

Current implementation:
- start scripts use `dotnet <published-dll>` from the published app directory
- PID files are checked before startup so duplicate starts are avoided
- stop scripts use the PID file first and verify the PID still matches the expected DLL before sending signals
- stop uses `TERM` first, then escalates to `KILL` only if the process does not exit

## Current Defaults

Current runtime defaults implemented in the scripts:
- service URL binding: `http://127.0.0.1:7033`
- web URL binding: `http://127.0.0.1:7053`
- web-to-service base URL: `http://127.0.0.1:7033/`
- ASP.NET Core environment: `Production`
- deploy target base: `/Volumes/HD-Boot-CA-Server/Users/dick/TorrentCore`
- publish runtime: `osx-x64`
- publish mode: framework-dependent

These can be overridden through `Scripts/torrentcore.env`.

Remote access note:
- the defaults are intentionally loopback-only
- to open the Web UI from another machine on the LAN, set `TORRENTCORE_WEBUI_URLS=http://0.0.0.0:7053`
- if remote Swagger/API access is also desired, set `TORRENTCORE_SERVICE_URLS=http://0.0.0.0:7033`
- the web UI can still keep `TORRENTCORE_WEBUI_SERVICE_BASE_URL=http://127.0.0.1:7033/` because the service is local to the Intel Mac

## Deploy Behavior

Current deploy behavior:
- publish service and/or web to `artifacts/publish/intel/...` in the repo
- sync publish output to the target share using `rsync --delete`
- sync the `Scripts` folder to the target share while preserving:
  - `Scripts/run`
  - `Scripts/logs`
  - `Scripts/torrentcore.env`

Deploy commands:
- `./Scripts/deploy-service-intel.zsh`
- `./Scripts/deploy-webui-intel.zsh`
- `./Scripts/deploy-all-intel.zsh`

Optional restart behavior:
- each deploy script supports `--restart`
- `deploy-all-intel.zsh --restart` stops web, stops service, syncs both runtimes plus scripts, then starts service and web again

## Validation Notes

What is already validated in-repo:
- the `zsh` scripts pass syntax validation with `zsh -n`
- Intel (`osx-x64`) publish and sync to a temp target directory works
- local start/stop validation was executed successfully using an `osx-arm64` override on the development machine because the Apple Silicon host cannot execute the Intel publish directly

Still to validate on the actual Intel Mac:
- running the published `osx-x64` service and web from the mounted-share deployment
- login-item boot/startup behavior
- restart behavior on the real host after deployment

## Recommended Delivery Order

1. Add publish/deploy scripts for `osx-x64`.
2. Add start/stop/restart scripts for service and web.
3. Validate deployment into the mounted Intel Mac share.
4. Validate service and web launch from scripts on the Intel Mac.
5. Validate restart and stop behavior.
6. Then decide whether login-item startup is sufficient or if `launchd` should replace it later.
