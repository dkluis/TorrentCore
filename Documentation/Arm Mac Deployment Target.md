# TorrentCore Arm Mac Deployment Target

## Purpose

This document captures the arm64 deployment target alongside the existing Intel deployment.

The target is:
- a macOS Apple Silicon host
- .NET 10 already installed on that host
- service and web started through the same `zsh` runtime scripts used by the Intel deployment

## Deployment Layout

Default target base path on the Arm Mac host:
- `~/TorrentCore`

Planned runtime layout:
- `~/TorrentCore/Service`
- `~/TorrentCore/WebUI`
- `~/TorrentCore/Scripts`

The runtime start/stop/restart scripts are shared with the Intel deployment. Only the publish/deploy wrappers differ.
The deploy wrappers themselves remain repo-side only and are not copied into the target host `~/TorrentCore/Scripts` directory.

Execution rule:
- deploy can be run from any machine with the repo, `dotnet`, `rsync`, and write access to the Arm target path
- runtime start/stop/restart should be performed on the Arm host itself

## Publish Model

Current Arm deployment is:
- framework-dependent
- published for macOS arm64

Current publish target:
- `osx-arm64`

## Deploy Scripts

Arm deploy wrappers now live in:
- [Scripts](/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TorrentCore/Scripts)

Available commands:
- `./Scripts/deploy-service-arm.zsh`
- `./Scripts/deploy-webui-arm.zsh`
- `./Scripts/deploy-all-arm.zsh`

These wrappers:
- publish to `artifacts/publish/arm/...`
- sync the publish output to the Arm target base path
- sync only the shared runtime scripts into `~/TorrentCore/Scripts`

Optional restart:
- each deploy script supports `--restart`
- `--restart` is only valid when the deploy is being run on the Arm host itself
- for cross-machine share-based deploys, deploy without `--restart`, then run the local restart scripts on the Arm host

## Current Defaults

Current arm deploy defaults:
- deploy base: `~/TorrentCore`
- publish runtime: `osx-arm64`
- publish mode: framework-dependent

These are driven through:
- `TORRENTCORE_DEPLOY_BASE_ARM`
- `TORRENTCORE_PUBLISH_RUNTIME_ARM`

If needed, they can be overridden in:
- [torrentcore.env.example](/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TorrentCore/Scripts/torrentcore.env.example)

## Runtime Control

Once deployed, the arm host uses the same runtime commands as the Intel host:
- `./start-service.zsh`
- `./stop-service.zsh`
- `./restart-service.zsh`
- `./start-webui.zsh`
- `./stop-webui.zsh`
- `./restart-webui.zsh`

## Validation Notes

What is validated in-repo:
- arm deploy wrappers are syntax checked
- arm publish/sync works to a temp target directory
- the shared runtime scripts can start and stop the published arm service and web on the development machine

What should still be validated on the actual Arm deployment host:
- final chosen target path if not `~/TorrentCore`
- operator startup flow on that host
- any host-specific firewall/network expectations for remote browser access
