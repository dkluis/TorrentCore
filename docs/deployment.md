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
| `TorrentCore.Service` | `7033` |
| `TorrentCore.WebUI` | `7053` |

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

## Service API And Swagger

The service exposes Swagger UI and the OpenAPI v1 document in Development, Integration, and Production:

```text
http://<torrentcore-host>:7033/swagger
http://<torrentcore-host>:7033/swagger/v1/swagger.json
```

Swagger exposes existing mutation operations through an interactive UI. It does not provide authentication or TLS.
Keep the service bound to the trusted LAN/VPN boundary and do not expose it directly to the public internet.

## MonoTorrent Refactor Cutover

Before deploying the refactored MonoTorrent integration:

- confirm TorrentCore reports zero active torrents
- confirm no downloads are active on the host
- inventory any remaining `.!mt` artifacts for manual disposition
- back up the TorrentCore database and MonoTorrent cache

The cutover does not migrate active torrents or partially downloaded payloads. Existing history may remain.

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
curl http://127.0.0.1:7033/api/health
curl -I http://127.0.0.1:7053/
```

## Native macOS App Release

The native app is released separately from the Service and WebUI deployment. It is a direct-download,
Developer ID-signed and Apple-notarized DMG for Apple Silicon Macs running macOS 26 or later. Building or installing
the app does not deploy, restart, or modify the Service or WebUI.

Current release identity:

| Item | Value |
|---|---|
| App bundle identifier | `com.conadv.TorrentCore.mac` |
| Apple Developer Team ID | `5GRR76N48V` |
| Current version | `0.3.1` |
| Current build | `5` |
| Default DMG | `/Volumes/CA-Desktop-HD-2/Development/Deployments/DMGs/TorrentCore-macOS-App-0.3.1.dmg` |

The first 0.1.0/build 1 artifact was accepted by Apple and stapled on July 26, 2026. Its SHA-256 checksum is
`adda66f813b45ea54afee388f991635bd0c221fd3c182e2e0fd95a533aa0a82c`.
The 0.2.0/build 2 upgrade candidate was accepted and stapled the same day under notarization submission
`f6dd6d0f-fa7e-4b5c-9260-2387f7cdecfd`. Its SHA-256 checksum is
`26fd5d1b3d4ce2d1a92834f56aea68a8842b75a9a8ed061c994e535dc2e78bd2`.
Installation over 0.1.0 passed operator upgrade acceptance on July 26, 2026.
The 0.2.1/build 3 macOS 27 compatibility hotfix was accepted and stapled under notarization submission
`72257d2e-d315-40dc-a315-71530bfdd9af`. Its SHA-256 checksum is
`6ab13ffc94fdef761b39a31f575bbff7b3636c1e5f8cde6c84c6ff8b1653079d`.
The 0.3.0/build 4 UI-refinement update was accepted and stapled on July 27, 2026 under notarization submission
`cea84cc3-1f89-49fa-9766-8c12dd6cd597`. Its SHA-256 checksum is
`eec8762805329edbe626b425484e57ef20b0c8335836aa60ca7422dc611e3f27`.
Installation over 0.2.x worked normally on a separate Apple Silicon macOS 26 system. On CA-Dick-MBA running macOS 27,
saving a connection exposed a repeatable SwiftUI/AppKit split-view constraint abort that persisted after downgrading
to 0.2.1 because the saved profile and UI state were retained. Launching Dashboard while bypassing window restoration
recovered the installation without deleting the saved connections. Acceptance of the subsequent stable-layout fix
remains pending.
The 0.3.1/build 5 stable-layout update was accepted and stapled on July 27, 2026 under notarization submission
`d0ee05c1-d3a0-4434-9314-94ba3f841cd5`. Its SHA-256 checksum is
`b4c746f3fa62c0cf47af52e9b3bc324de6239754ef4e4d58ae74a19ce14bb87d`. The copied DMG passed code-signature,
stapler-ticket, disk-image, Gatekeeper, and checksum verification. Separate-Mac installation-over-0.3.0 acceptance,
especially saved-connection startup on macOS 27, remains pending.

### One-Time Developer ID Setup

The release Mac needs a valid `Developer ID Application` identity for Team `5GRR76N48V`. An `Apple Development`
identity is sufficient for Xcode development builds but cannot sign a direct-download release.

Preferred Xcode setup:

1. Open Xcode **Settings > Accounts**.
2. Select `dkluis@icloud.com`, then the `Dick Kluis` team.
3. Open **Manage Certificates**.
4. Use the add button to create a **Developer ID Application** certificate.
5. Confirm that it appears in Keychain Access under **My Certificates** with its private key.

If Xcode cannot create it, use the Apple Developer
[Developer ID certificate procedure](https://developer.apple.com/help/account/certificates/create-developer-id-certificates/).
Create a certificate signing request in Keychain Access, choose **Developer ID Application** in the developer portal,
upload the request, download the certificate, and double-click it to install it in the login Keychain. A
`Developer ID Installer` certificate is not needed because TorrentCore uses a drag-to-Applications DMG rather than an
installer package.

Verify the result:

```bash
security find-identity -v -p codesigning
```

The output must contain a valid identity beginning with `Developer ID Application:` and ending with
`(5GRR76N48V)`. The private key remains in the Keychain on the release Mac. It is never embedded in or copied with the
app or DMG.

### One-Time Notarization Setup

Create an app-specific password for `dkluis@icloud.com` under **Sign-In and Security > App-Specific Passwords** at
[account.apple.com](https://account.apple.com/). Then run this command interactively:

```bash
xcrun notarytool store-credentials "TorrentCore-notary" \
  --apple-id "dkluis@icloud.com" \
  --team-id "5GRR76N48V"
```

Enter the app-specific password only at the secure prompt. Do not place it in the command, a script, an environment
file, or the repository. The command validates the credentials and saves them under the local Keychain profile
`TorrentCore-notary`. Do not add `--sync`; release credentials should remain local to the release Mac.

The profile is used only by `notarytool`. It is not embedded in the app, copied into the DMG, deployed to another Mac,
or available to TorrentCore Service/WebUI.

Validate both one-time prerequisites without building:

```bash
./Scripts/release-macos-app.zsh --check
```

### Construct A Release

Before constructing a release, confirm the working tree contains the intended commit and run the relevant native tests
from [testing.md](testing.md). Then run:

```bash
./Scripts/release-macos-app.zsh
```

The script:

1. validates the Developer ID identity and local notary Keychain profile
2. creates a Release archive with automatic signing
3. exports and verifies the Developer ID-signed `TorrentCore.app`
4. verifies the requested version and build and an Arm64-only executable
5. creates a compressed DMG containing `TorrentCore.app` and an `Applications` shortcut
6. signs the DMG, submits it with `notarytool --wait`, and staples the accepted ticket
7. verifies the ticket, disk image, and Gatekeeper assessment
8. copies the verified artifact to the configured deployment directory and prints its SHA-256 checksum

Intermediate archive, export, and disk-image staging files use a dedicated `/private/tmp/TorrentCore-release.*`
directory and are removed when the script exits. The script refuses to replace an existing same-version DMG. Use
`--overwrite` only when replacing that exact artifact is intentional.

For a later release, supply the new values explicitly:

```bash
./Scripts/release-macos-app.zsh --version 0.3.1 --build 5
```

The script accepts `--output-dir`, `--notary-profile`, and `--signing-identity` overrides. Run `--help` for the complete
command surface. A failed notarization produces no release in the deployment directory.

### Install, Upgrade, And Verify

On the destination Mac:

1. Open `TorrentCore-macOS-App-<version>.dmg`.
2. Drag `TorrentCore` onto the `Applications` shortcut.
3. Eject the disk image.
4. Launch TorrentCore from Applications.
5. Allow local-network access when macOS asks, then create or select the TorrentCore connection profile.

An upgrade uses the same steps and replaces `TorrentCore.app` in Applications. The stable bundle identifier preserves
device-local connection profiles, refresh preferences, and appearance settings. There is no automatic updater in this
release.

Optional command-line checks:

```bash
xcrun stapler validate "/path/to/TorrentCore-macOS-App-0.3.1.dmg"
spctl --assess --type open --context context:primary-signature --verbose=4 \
  "/path/to/TorrentCore-macOS-App-0.3.1.dmg"
```

### Uninstall And Recovery

To uninstall the app, quit TorrentCore and move `/Applications/TorrentCore.app` to the Trash. That leaves device-local
app settings available for a later reinstall.

For a complete client-only reset, also use Finder **Go > Go to Folder** to open
`~/Library/Containers/com.conadv.TorrentCore.mac` and move that exact container to the Trash. This removes native-client
profiles and preferences. Never remove `~/TorrentCore`, the TorrentCore database, logs, download directories, Service,
or WebUI as part of native-app uninstall or recovery.

If the native app is unavailable:

- use the existing WebUI at `http://<torrentcore-host>:7053`
- inspect the Service API at `http://<torrentcore-host>:7033/swagger` inside the trusted LAN/VPN boundary
- reinstall the current or previous notarized DMG without changing the Service deployment

The Mac app contains no .NET runtime, Service executable, WebUI files, TorrentCore database, or downloaded torrent
data.
