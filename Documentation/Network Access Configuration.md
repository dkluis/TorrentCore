# Network Access Configuration

## Purpose

This document defines the current TorrentCore network-access model for a trusted local network.

The implemented model is:
- `TorrentCore.Service` is the API host that must be reachable on the LAN
- `TorrentCore.Web` is a Blazor Server UI that can also be exposed on the LAN
- `TorrentCore.Avalonia` is a desktop client that calls the service over HTTP
- Web stores one global service endpoint for that Web host
- Avalonia stores one saved service endpoint per desktop app install/user profile
- both UIs test `/api/health` before saving a new endpoint

This slice is intentionally HTTP-only. HTTPS, certificates, and internet-facing hardening are out of scope here.

## Trusted-LAN Assumption

TorrentCore is currently intended for a trusted private network only.

Assumptions:
- no public internet exposure
- no TLS/certificate management in this slice
- no authentication boundary added here
- operators use LAN hostnames or LAN IP addresses

## Default Ports

Default HTTP ports:

| Component | Default HTTP Port |
|-----------|-------------------|
| `TorrentCore.Service` | `5078` |
| `TorrentCore.Web` | `5131` |

Repo defaults stay on `localhost` for normal development. Network-accessible binding is a deploy/run concern, not a source default.

## Binding for LAN Access

To make the service reachable from other machines, bind it to a network interface instead of loopback.

Common options:
- `http://0.0.0.0:5078`
- `http://192.168.68.80:5078`
- `http://torrentcore-service.local:5078`

To make the Web UI reachable from browsers on other machines, do the same for the Web host.

Common options:
- `http://0.0.0.0:5131`
- `http://192.168.68.81:5131`
- `http://torrentcore-web.local:5131`

Recommended deployment pattern:
- keep source-controlled `launchSettings.json` and appsettings defaults on `localhost`
- use `ASPNETCORE_URLS` or deploy-time config overrides for LAN binding

Examples:

```bash
export ASPNETCORE_URLS="http://0.0.0.0:5078"
dotnet TorrentCore.Service.dll
```

```bash
export ASPNETCORE_URLS="http://0.0.0.0:5131"
dotnet TorrentCore.Web.dll
```

The host firewall must allow inbound TCP traffic on the chosen service and Web ports.

## Runtime Endpoint Bootstrap

The two UIs no longer require a permanently correct service URL baked into source config.

Instead:
- both UIs start with a fallback `TorrentCoreService:BaseUrl`
- both UIs detect whether the configured service endpoint is reachable
- if the service is unreachable, the UI shows connection setup instead of assuming the backend is healthy
- the UI tests `/api/health` before saving a new endpoint
- once a tested endpoint is saved, it is reused on the next startup

## Web UI Behavior

`TorrentCore.Web` is a server app, so its service connection is host-global.

Behavior:
- the Web host uses `TorrentCoreService:BaseUrl` from appsettings as a fallback
- if a saved override exists, that override wins
- if the current endpoint is unreachable, normal app pages are gated and the operator is sent to `Service Connection`
- saving a new endpoint updates the Web host immediately after the health check passes

Persistence:
- saved file: `src/TorrentCore.Web/Config/service-connection.json` in repo/dev scenarios
- published host: the same relative `Config/service-connection.json` under the app content root

Operational meaning:
- every browser session that talks to that Web host shares the same backend service endpoint
- this is the correct model for the Web UI; it is not per browser or per session

## Avalonia Behavior

`TorrentCore.Avalonia` is a true client, so its service connection is local to that desktop app instance.

Behavior:
- Avalonia uses `Config/appsettings.json` as a fallback default
- if a saved override exists, that override wins
- if the current endpoint is unreachable at startup, Avalonia opens its connection setup view
- saving a new endpoint updates the client immediately after the health check passes

Persistence:
- saved file is written under the current user's local application-data area in a `TorrentCore.Avalonia` folder

Operational meaning:
- different desktop machines can point to different TorrentCore service hosts
- testing a local dev Avalonia build against a deployed LAN service is supported without changing committed defaults

## Development Against a Deployed Service

This model supports local development against a deployed service on the LAN.

Examples:
- local dev `TorrentCore.Web` -> remote deployed `TorrentCore.Service`
- local dev `TorrentCore.Avalonia` -> remote deployed `TorrentCore.Service`

Why it works:
- Web is Blazor Server, so the browser only talks to the Web host; the Web host makes the service API calls
- Avalonia is a direct HTTP client to the service
- service endpoint changes are runtime-configurable and persisted outside source-controlled defaults

Recommended approach:
- keep repo defaults pointed at `http://localhost:5078/`
- when you want to test against a deployed service, use the UI connection flow to save the deployed host URL

## Example Topologies

### Single Host

One machine hosts both server processes:

```text
Machine: 192.168.68.80
├── TorrentCore.Service  -> http://0.0.0.0:5078
└── TorrentCore.Web      -> http://0.0.0.0:5131

Remote browser:
└── http://192.168.68.80:5131

Remote Avalonia:
└── http://192.168.68.80:5078
```

Normal Web service target in this case:
- `http://localhost:5078/`

### Split Service and Web Hosts

The service and Web host run on different machines:

```text
Service host: 192.168.68.80
└── TorrentCore.Service  -> http://0.0.0.0:5078

Web host: 192.168.68.81
└── TorrentCore.Web      -> http://0.0.0.0:5131

Remote browser:
└── http://192.168.68.81:5131

Remote Avalonia:
└── http://192.168.68.80:5078
```

Normal Web service target in this case:
- `http://192.168.68.80:5078/`

## Operational Notes

- Prefer stable hostnames over raw IP addresses when the LAN supports it.
- If the service moves to a new machine or port, use the UI connection flow rather than changing source-controlled defaults.
- Restart is not required for the current runtime endpoint change flow; the updated endpoint is applied immediately after a successful health check.
- Changing listen bindings such as `ASPNETCORE_URLS` is still a host-process configuration change and therefore still requires restarting that server process.
