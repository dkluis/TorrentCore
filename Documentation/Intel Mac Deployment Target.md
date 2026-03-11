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
- optionally a combined `deploy-all-intel.zsh`

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
- may also contain PID files and local script logs if that proves simplest

## Logging Expectations

The script layer should log enough to troubleshoot:
- process startup
- process shutdown
- startup failure
- publish/deploy failure
- target copy failure

This is separate from TorrentCore's own application/activity logs.

## Process Management Expectations

The first script-based deployment should:
- support independent service and web lifecycle control
- support boot/login startup
- avoid requiring Rider or manual `dotnet run`
- avoid broad `pkill dotnet`-style process management

## Open Decisions

Still to decide:
- exact log file locations for the script layer
- exact PID file locations
- whether deploy scripts should stop/start automatically or only publish/copy
- whether old published output should be removed before copy or replaced in place
- whether later deployment should become self-contained instead of framework-dependent

## Recommended Delivery Order

1. Add publish/deploy scripts for `osx-x64`.
2. Add start/stop/restart scripts for service and web.
3. Validate deployment into the mounted Intel Mac share.
4. Validate service and web launch from scripts on the Intel Mac.
5. Validate restart and stop behavior.
6. Then decide whether login-item startup is sufficient or if `launchd` should replace it later.
