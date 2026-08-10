# TorrentCore Scripts

This folder contains:

- repo-side deploy scripts
- host-side launch-agent management scripts
- shared launch-agent templates and environment example files
- `ServiceApp/`: the Arm64 background-app launcher, packager, signer, and verifier
- `MacOSApp/`: the signed native macOS UI app archive/export helper
- `ServiceAppDMG/`: the dated combined Service/native-UI DMG builder and manifest-free Service deployer

For the current runtime and deployment model, use [docs/deployment.md](../docs/deployment.md).

Important rule:

- deploy scripts run from a repo machine
- runtime control scripts run on the actual TorrentCore host
- `--restart` is only valid when the deploy command runs on the same host as the runtime
- Service-app DMGs never deploy or control WebUI, install the native UI only through a manual drag to `/Applications`,
  and retain the legacy scripts until the final cleanup slice
