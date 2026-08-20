# Deployment

## Runtime Model

TorrentCore is currently deployed on macOS through per-user `LaunchAgents`.

Managed components:

- `TorrentCoreService`
- `TorrentCore.WebUI`

Current source Service, WebUI, and native macOS UI version: `0.8.0`, build `15`.

Launch agent labels:

- `com.torrentcore.service`
- `com.torrentcore.webui`

Installed plists:

- `~/Library/LaunchAgents/com.torrentcore.service.plist`
- `~/Library/LaunchAgents/com.torrentcore.webui.plist`

Managed app launchers:

- `~/Applications/TorrentCore/TorrentCoreService.app/Contents/MacOS/TorrentCoreService`
- `~/Applications/TorrentCore/TorrentCoreWebUI.app/Contents/MacOS/TorrentCoreWebUI`

The immutable framework-dependent runtimes live inside those bundles. Mutable configuration and state remain under
`~/TorrentCore/Service` and `~/TorrentCore/WebUI`; retained legacy runtime files there are inactive after app cutover.

## Target Layout

```text
~/TorrentCore/
├── Service/
│   ├── appsettings.json
│   └── version.json
├── WebUI/
│   ├── appsettings.json
│   ├── Config/
│   │   └── service-connection.json   # optional machine-local override
│   └── version.json
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

## Direct-Distribution Service DMG

The Service/WebUI deployment DMG remains framework-dependent and preserves the current per-user layout. Release
construction Developer ID-signs both .NET apphosts with Hardened Runtime, a secure timestamp, and the required JIT
entitlement. The apphosts also receive the library-validation exception required for Developer ID-signed executables to
load Microsoft's separately signed shared .NET runtime. Native Mach-O dependencies such as `libe_sqlite3.dylib` are
signed separately before the final DMG is signed, notarized, stapled, and verified. This does not make the deployment
self-contained and does not change the target directory layout or LaunchAgent commands.

The signed deployment payload keeps `Service/TorrentCoreService` and `WebUI/TorrentCore.WebUI` as the public
LaunchAgent paths. Each path now contains a component-specific native supervisor launcher with a unique Mach-O UUID;
the original framework-dependent apphost remains beside it with an `.apphost` suffix and retains the required .NET
entitlements. The launchers forward termination signals and return the helpers' exit status without changing working
directories or configuration lookup.

`api/host/status` identifies a Service build with both `serviceVersion` and optional `serviceBuild`. The build value is
the full Git commit embedded in the Service assembly. The WebUI and native macOS dashboard show its first 12 characters
so an operator can correlate the active process with the deployment snapshot's `release.gitSha`.

The Dick `torrentcore.2026.08.05.Dick.MetadataAdmissionRecovery` Intel deployment DMG was accepted and stapled on
August 5, 2026 under notarization submission `b4404683-2c07-4939-abe7-63c627ac886f`. Its SHA-256 checksum is
`a8c8b6be577efa5ab3c2dbc3f0972cc97686f77b7638206121d2463a6704e5f1`. The final copied artifact passed code-signature,
stapler-ticket, disk-image, Gatekeeper, full internal-checksum, distinct-main-UUID, and helper-entitlement verification.

The 0.5.1 Dick `torrentcore.2026.08.05.Dick.ConnectionLeakHotfix` Intel deployment DMG was accepted and stapled on
August 5, 2026 under notarization submission `ca9d691f-dcf4-40ff-b573-2577f35ace86`. Its SHA-256 checksum is
`cf4d0d59a9e9a48b7234cd3213942837b2dae49f8694f7a25f42c45d45fe0502`. The final copied artifact passed the same
signature, stapler-ticket, disk-image, Gatekeeper, internal-checksum, distinct-main-UUID, and entitlement verification.

The direct-email acceptance path must retain macOS quarantine. On the target Mac, mount the attachment and complete the
normal Terminal-based `plan`, `dry-run`, `apply`, and `verify` sequence without clearing extended attributes. The Tom
manifest keeps the former xattr compatibility policy disabled while this release workflow is proven.

`Config/service-connection.json` is machine-local state. WebUI publish, bundle, and DMG construction exclude it, and
the combined deployer verifies that an existing target file is unchanged byte-for-byte. A fresh target receives no
connection override and continues to use the established Service endpoint fallback.

## Arm64 Service App Bundle

`Scripts/ServiceApp/build-macos-service-app.zsh` publishes and packages the background-only Arm64
`TorrentCoreService.app`. The immutable framework-dependent Service runtime, packaged defaults, and static publish
content live under `Contents/Resources/Runtime`; app-specific deployment resources live under
`Contents/Resources/Deployment`. The launcher executes the helper from the bundle but uses `~/TorrentCore/Service` as
the working/configuration directory. An existing `Service/appsettings.json` is never overwritten; a fresh installation
receives the packaged default. The installed `Service/version.json` is updated to the app release metadata. SQLite,
MonoTorrent cache, download data, category paths, and callback state remain at their existing external locations.

The bundle identity is `com.conadv.torrentcore.service`, while the LaunchAgent label remains
`com.torrentcore.service`. The embedded installer associates those identities, binds the Service to
`http://0.0.0.0:7033` by default, and changes only the Service LaunchAgent. Do not run it merely to build or inspect an
app, because it boots out and replaces the active Service agent. Slice 8 owns the supported DMG install/rollback path.

The builder requires the `net10.0/osx-arm64` restore graph to exist. Restore it once, then build an unsigned proof:

```bash
dotnet restore src/TorrentCore.ServiceHost/TorrentCore.Service.csproj --runtime osx-arm64
./Scripts/ServiceApp/build-macos-service-app.zsh \
  --output-bundle /private/tmp/TorrentCoreService-proof/TorrentCoreService.app
```

For a Developer ID proof, add the exact keychain identity:

```bash
./Scripts/ServiceApp/build-macos-service-app.zsh \
  --output-bundle /private/tmp/TorrentCoreService-signed/TorrentCoreService.app \
  --signing-identity 'Developer ID Application: Dick Kluis (5GRR76N48V)'
```

The signed verifier checks the complete nested code tree, Team ID, Hardened Runtime, timestamps, .NET JIT and shared
runtime entitlements, native dependencies, architecture, bundle metadata, launcher/helper UUID separation, and absence
of mutable TorrentCore data. Run signing verification outside the filesystem sandbox as required by repository policy.

## Arm64 WebUI App Bundle

`Scripts/WebUIApp/build-macos-webui-app.zsh` publishes and packages the background-only Arm64
`TorrentCoreWebUI.app`. Its runtime and bundled static assets live under `Contents/Resources/Runtime`, immutable
configuration defaults under `Contents/Resources/Defaults`, and LaunchAgent installation resources under
`Contents/Resources/Deployment`. The launcher uses `~/TorrentCore/WebUI` as the external working/configuration
directory. `Program.cs` resolves `wwwroot` from the bundled runtime so static files do not depend on that external
directory.

The bundle identity is `com.conadv.torrentcore.webui`; the existing LaunchAgent label remains
`com.torrentcore.webui`. The installer retains `TORRENTCORE_WEBUI_URLS`, `TORRENTCORE_WEBUI_SERVICE_BASE_URL`, and the
optional saved Service Connection override. The static-assets verifier launches the bundle with an empty temporary
working directory and compares a served fingerprinted CSS route byte-for-byte with the bundled source.

## Combined Service/WebUI And Native-UI DMG

The combined DMG builder and managed-app deployer live under `Scripts/ServiceAppDMG`; they have no runtime dependency on the
TVMaze repository and require no machine `live.json` manifest. Generation requires an installation target (`Dick`,
`Tom`, or `Shared`) and a CPU choice. Dick and Tom retain installation-specific artifact names. Shared packages are
reusable and omit the installation segment from the artifact name. No hostname catalog is packaged: installation paths
come from the current user's home, and apply requires the existing `~/TorrentCore` and `~/TorrentCore/Service` structure. Releases contain
the Service and WebUI payloads under `payload/osx-arm64`, the signed native UI at the mounted root as `TorrentCore.app`,
and an `Applications` link to `/Applications`. The deployer refuses non-Arm64 hosts and never installs or controls the
native UI.

Artifacts follow the established naming convention, for example
`TorrentCore-torrentcore.2026.08.13.Dick.WebUIAlignment` for a specific installation or
`TorrentCore-torrentcore.2026.08.14.deploy-patches` for Shared. A release is always staged first as a complete,
persistent directory under `Deployments/TorrentCore-Deployments/<installation>`. The saved directory contains `README.md`,
`README.pdf`, `Runbook.md`, `Runbook.pdf`, `plan.zsh`, `dry-run.zsh`, `backup.zsh`, `apply.zsh`, `verify.zsh`,
`release.json`, the two managed app payloads, and the native UI app. It remains in place after DMG construction.

From clean committed source, stage the release package first:

```bash
./Scripts/ServiceAppDMG/stage-release-package.zsh \
  --installation Shared \
  --cpu arm \
  --release-name deploy-patches \
  --date 2026.08.14 \
  --notes "Apply the shared TorrentCore deployment packaging corrections." \
  --require-pdf
```

Inspect that saved directory before building its DMG. After approval, create the DMG from the exact saved package:

```bash
./Scripts/ServiceAppDMG/build-package-dmg.zsh \
  --package-root /Volumes/CA-Desktop-HD-2/Development/Deployments/TorrentCore-Deployments/Shared/TorrentCore-torrentcore.2026.08.14.deploy-patches
```

`release-service-app-dmg.zsh` remains the normal all-in-one driver when prior inspection is not required. It performs
those same two steps and never uses or deletes a temporary package root. Use `--installation Dick`,
`--installation Tom`, or `--installation Shared` with `--cpu arm` as appropriate. `--cpu intel` is explicitly refused
by the current Arm-only tooling.

The default DMG output directory is `/Volumes/CA-Desktop-HD-2/Development/Deployments/DMGs`. DMG construction signs
the DMG, submits it through the existing `TorrentCore-notary` profile, staples it, and performs signature, entitlement,
Gatekeeper, stapler-ticket, disk-image, checksum, and architecture verification outside the filesystem sandbox.
`TorrentCore.app/Contents/Resources/version.json` records native UI version, build, Git SHA, build time, and runtime.
The package `release.json` records both managed-app checksums, the short change description, and the manual
`/Applications/TorrentCore.app` target.

The current combined-package tooling defaults `TorrentCoreService.app`, `TorrentCoreWebUI.app`, and `TorrentCore.app`
to source version `0.8.0`, build `15`.

The Dick `torrentcore.2026.08.13.Dick.WebUIAlignment` Arm64 release was staged permanently under
`Deployments/TorrentCore-Deployments/Dick`, reviewed and approved by the operator, and then built from clean commit
`b43f55545f2e9367af7295ad0700466d13405774`. Apple accepted notarization submission
`8bd817c5-861a-4c14-9dbc-58304c026461`. The final stapled DMG SHA-256 is
`7d0e3844988ee1d74dde0e1ce9db7d4d4c668460dc1bd405db191242b756b746`; its signature, stapler ticket, disk-image
checksum, Gatekeeper assessment, mounted file checksums, required root helpers, all three app signatures, and
machine-local connection-file exclusion passed outside-sandbox verification.

The Dick `torrentcore.2026.08.19.Dick.QueueControls-Patch` Arm64 release was staged permanently under
`Deployments/TorrentCore-Deployments/Dick` from queue-controls source commit
`596ce3e5f94785dcaff0428676c2d5a19482b1ee`. Apple accepted notarization submission
`dd5a4890-41a3-4040-8426-4fad3de263d6`. The final stapled DMG SHA-256 is
`4e788c2e9fe6b7e39a875f1036fc3fb9fce8e6cd6b62ca15d9dcbfc9854ed0d9`; mounted layout, package checksums, all app
signatures, Gatekeeper assessment, and the stapler ticket passed outside-sandbox verification. Deployment and copied
production-database validation on August 20 confirmed version `0.8.0`/build `14`, schema migration 21, durable queue
intent, protected priority-attempt rotation and expiry, hold behavior, and enforcement of the combined active-work
ceiling without download preemption.

From the mounted DMG, begin with:

```bash
./plan.zsh
./dry-run.zsh
./backup.zsh
./apply.zsh
./verify.zsh
```

Plan and dry-run do not write files or deployment state and do not control launchd. `apply.zsh` supplies the required
confirmation argument. It verifies both source apps before stopping anything, creates one compressed backup
under `~/TorrentCore/.backups`, stages and replaces both apps, preserves every existing file under both working
directories, installs both LaunchAgents, and verifies Service API health/version plus WebUI reachability. VPN Disabled,
Ready, and Degraded are valid outcomes when API health is successful. Deployment state and history live under
`~/TorrentCore/DeploymentState`.

After combined verify succeeds, quit an existing TorrentCore UI, drag the mounted `TorrentCore.app` onto the mounted
`Applications` link, select **Replace** when prompted, and launch the updated UI to confirm its Dashboard connects to
the Service. This manual drag is the only native UI installation path in the combined DMG. The managed installer does
not copy, replace, back up, roll back, or verify `/Applications/TorrentCore.app`.

Legacy Service and WebUI runtime files and current legacy scripts remain in place but inactive after app cutover. Do
not use the legacy `install-launch-agents.zsh` after app cutover because it still describes the retained flat-runtime
layout.

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
- the scheduler targets the supported `com.torrentcore.service` label; `XPC_SERVICE_NAME` is used only to verify that
  the managed helper is running under launchd because the native supervisor arrangement reports the helper name as `0`
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

## Logs And Status

Runtime logs:

- `~/TorrentCore/Logs/TorrentCore.Service.launchd.out.log`
- `~/TorrentCore/Logs/TorrentCore.Service.launchd.err.log`
- `~/TorrentCore/Logs/TorrentCore.WebUI.launchd.out.log`
- `~/TorrentCore/Logs/TorrentCore.WebUI.launchd.err.log`

Production console logging uses a `Warning` baseline for both hosts, so routine Service information and WebUI HTTP
client request traces do not flood the launchd files. Development retains an `Information` baseline. Framework console
entries include UTC timestamps. The Service also writes a timestamped stderr marker before an unhandled process
exception terminates it; launchd may append the runtime's untimestamped stack trace immediately afterward.
These thresholds do not change the Service's persistent SQLite activity-log levels.

Launchd appends to the configured files and does not truncate existing content when a new build is installed. Archive
an oversized historical file only while its agent is stopped; changing its path while the process is running leaves
the process writing through its existing open file handle.

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
| Current version | `0.5.1` |
| Current build | `10` |
| Default DMG | `/Volumes/CA-Desktop-HD-2/Development/Deployments/DMGs/TorrentCore-macOS-App-0.5.1.dmg` |

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
The 0.4.0/build 7 maintenance and filtering update was accepted and stapled on July 28, 2026 under notarization
submission `b55e898b-b73d-4ff0-b3ad-0b3a1563d373`. Its SHA-256 checksum is
`74e8325562a90bafa9a1a982b881c6d75a92c34166dc88acfb2e0c43e74459c8`. The copied DMG passed code-signature,
stapler-ticket, disk-image, Gatekeeper, and checksum verification.
The 0.4.1/build 8 native app-icon update was accepted and stapled on July 29, 2026 under notarization submission
`a53db386-2133-4910-be77-4354fea77089`. Its SHA-256 checksum is
`acb507af743172642d0440a59e61a46dd5c95bccb1606279ace35e7e57c7f835`. The copied DMG passed code-signature,
stapler-ticket, disk-image, Gatekeeper, and checksum verification.
The 0.5.0/build 9 metadata-admission and recovery update was accepted and stapled on August 5, 2026 under notarization
submission `d6626a01-99ce-4569-8814-951caecda675`. Its SHA-256 checksum is
`c4e85a26bf349146d69f6764d88e3f68e3f2d03569ce002993459c3dddbc09b3`. The copied DMG passed code-signature,
stapler-ticket, disk-image, Gatekeeper, and checksum verification.
The 0.5.1/build 10 native compatibility update recognizes the Service `serviceBuild` response property added after
build 9. The older app's strict generated decoder rejects that unknown property after its health-only test succeeds.
Apple accepted and the release workflow stapled build 10 on August 5, 2026 under notarization submission
`baffb509-1e96-4d8e-a56e-59a630de1bfe`. Its SHA-256 checksum is
`51f8704b3a1769b51f29d7f3b611384e888cf02c43a135d488c9458b92965458`. The release artifact passed app and DMG
code-signature checks, stapler-ticket validation, disk-image verification, and Gatekeeper assessment.

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
./Scripts/release-macos-app.zsh --version 0.5.1 --build 10
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
xcrun stapler validate "/path/to/TorrentCore-macOS-App-0.5.1.dmg"
spctl --assess --type open --context context:primary-signature --verbose=4 \
  "/path/to/TorrentCore-macOS-App-0.5.1.dmg"
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
