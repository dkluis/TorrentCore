# TorrentCore Scripts

This folder contains:
- runtime control scripts intended to live on the deployed host under `~/TorrentCore/Scripts`
- deploy scripts intended to run from the repo on the development machine and sync to the mounted Intel Mac share

## Runtime Script Layout

Expected sibling layout on the target host:
- `~/TorrentCore/Service`
- `~/TorrentCore/WebUI`
- `~/TorrentCore/Scripts`

The runtime scripts infer those sibling paths automatically from their own location.

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
```

Optional restart during deploy:

```bash
./Scripts/deploy-all-intel.zsh --restart
```

## Environment Overrides

Copy [torrentcore.env.example](/Volumes/HD-Desktop-Misc-L5/Development/Source/C#/TorrentCore/Scripts/torrentcore.env.example) to `Scripts/torrentcore.env` and edit the values you need.

Useful overrides:
- `TORRENTCORE_DEPLOY_BASE`
- `TORRENTCORE_ASPNETCORE_ENVIRONMENT`
- `TORRENTCORE_SERVICE_URLS`
- `TORRENTCORE_WEBUI_URLS`
- `TORRENTCORE_WEBUI_SERVICE_BASE_URL`
- `TORRENTCORE_PUBLISH_CONFIGURATION`
- `TORRENTCORE_PUBLISH_RUNTIME`

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
