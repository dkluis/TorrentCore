#!/bin/zsh

set -euo pipefail

SCRIPT_DIR="${0:A:h}"
REPO_ROOT="${SCRIPT_DIR:h:h}"
TEAM_ID="5GRR76N48V"
NOTARY_PROFILE="TorrentCore-notary"
SIGNING_IDENTITY=""
RELEASE_DATE="$(date '+%Y.%m.%d')"
INSTALLATION=""
CPU=""
RELEASE_NAME=""
OUTPUT_DIR="/Volumes/CA-Desktop-HD-2/Development/Deployments/DMGs"
PACKAGE_ROOT=""
STAGE_ONLY=false
ALLOW_DIRTY=false
CHECK_ONLY=false

fail() {
    print -ru2 -- "[TorrentCore Service app DMG] ERROR: $*"
    exit 1
}

usage() {
    cat <<'EOF'
Usage: release-service-app-dmg.zsh [options]

Options:
  --installation <Dick|Tom>   Required deployment environment name.
  --cpu <arm|intel>           Required CPU choice. Intel is reserved and currently refused.
  --release-name <name>       Required release purpose, for example ServiceApp.InitialDeploy.
  --date <YYYY.MM.DD>          Artifact date. Defaults to today.
  --output-dir <path>          DMG output directory.
  --package-root <path>        Staged package directory. Required with --stage-only.
  --signing-identity <name>    Exact Developer ID Application identity.
  --notary-profile <name>      Defaults to TorrentCore-notary.
  --stage-only                 Build/sign the app and stage mounted-DMG contents without creating a DMG.
  --allow-dirty                Permit a marked dirty development package; never valid for a release DMG.
  --check                      Validate release credentials and tools without building.
EOF
}

while (( $# > 0 )); do
    case "$1" in
        --installation) INSTALLATION="${2:-}"; shift 2 ;;
        --cpu) CPU="${2:-}"; shift 2 ;;
        --release-name) RELEASE_NAME="${2:-}"; shift 2 ;;
        --date) RELEASE_DATE="${2:-}"; shift 2 ;;
        --output-dir) OUTPUT_DIR="${2:-}"; shift 2 ;;
        --package-root) PACKAGE_ROOT="${2:-}"; shift 2 ;;
        --signing-identity) SIGNING_IDENTITY="${2:-}"; shift 2 ;;
        --notary-profile) NOTARY_PROFILE="${2:-}"; shift 2 ;;
        --stage-only) STAGE_ONLY=true; shift ;;
        --allow-dirty) ALLOW_DIRTY=true; shift ;;
        --check) CHECK_ONLY=true; shift ;;
        --help|-h) usage; exit 0 ;;
        *) fail "Unknown argument: $1" ;;
    esac
done

case "${INSTALLATION:l}" in
    dick) INSTALLATION="Dick" ;;
    tom) INSTALLATION="Tom" ;;
    *) fail "--installation must be Dick or Tom." ;;
esac
case "${CPU:l}" in
    arm) CPU="arm"; RUNTIME="osx-arm64" ;;
    intel) fail "Intel Service-app generation is recognized but not supported in the current Arm proof slice." ;;
    *) fail "--cpu must be arm or intel." ;;
esac
[[ "$RELEASE_NAME" =~ '^[A-Za-z][A-Za-z0-9.-]*$' ]] ||
    fail "--release-name is required and may contain only letters, numbers, dots, and hyphens."
[[ "$RELEASE_DATE" =~ '^[0-9]{4}\.[0-9]{2}\.[0-9]{2}$' ]] || fail "Invalid release date: $RELEASE_DATE"
if [[ "$ALLOW_DIRTY" == true && "$STAGE_ONLY" != true ]]; then
    fail "--allow-dirty is permitted only with --stage-only."
fi
if [[ "$STAGE_ONLY" == true && -z "$PACKAGE_ROOT" ]]; then
    fail "--package-root is required with --stage-only."
fi

for command_name in codesign ditto file hdiutil security shasum spctl xcrun; do
    command -v "$command_name" >/dev/null 2>&1 || fail "Required command is unavailable: $command_name"
done
xcrun --find notarytool >/dev/null || fail "notarytool is unavailable."
xcrun --find stapler >/dev/null || fail "stapler is unavailable."

if [[ -z "$SIGNING_IDENTITY" ]]; then
    identities=()
    while IFS= read -r line; do
        if [[ "$line" =~ '"(Developer ID Application:.*\('"$TEAM_ID"'\))"' ]]; then
            identities+=("${match[1]}")
        fi
    done < <(security find-identity -v -p codesigning)
    (( ${#identities[@]} == 1 )) || fail "Expected exactly one valid Developer ID Application identity for Team $TEAM_ID."
    SIGNING_IDENTITY="${identities[1]}"
fi
[[ "$SIGNING_IDENTITY" == Developer\ ID\ Application:*"($TEAM_ID)" ]] || fail "Signing identity is not for Team $TEAM_ID."

print -r -- "[TorrentCore Service app DMG] Developer ID identity: $SIGNING_IDENTITY"
print -r -- "[TorrentCore Service app DMG] Validating notary profile: $NOTARY_PROFILE"
xcrun notarytool history --keychain-profile "$NOTARY_PROFILE" --output-format json >/dev/null ||
    fail "Notary profile is missing or invalid: $NOTARY_PROFILE"

if [[ "$CHECK_ONLY" == true ]]; then
    print -r -- "TorrentCore Service app DMG release prerequisites are ready."
    exit 0
fi

SOURCE_DIRTY=false
if [[ -n "$(git -C "$REPO_ROOT" status --porcelain)" ]]; then
    SOURCE_DIRTY=true
fi
if [[ "$SOURCE_DIRTY" == true && "$ALLOW_DIRTY" != true ]]; then
    fail "The TorrentCore source tree is dirty. Commit the intended release source before creating a DMG."
fi

RELEASE_ID="torrentcore.$RELEASE_DATE.$INSTALLATION.$RELEASE_NAME"
ARTIFACT_STEM="TorrentCore-$RELEASE_ID"
if [[ -z "$PACKAGE_ROOT" ]]; then
    PACKAGE_ROOT="$(mktemp -d "/private/tmp/$ARTIFACT_STEM.package.XXXXXX")"
else
    [[ ! -e "$PACKAGE_ROOT" ]] || fail "Package root already exists: $PACKAGE_ROOT"
    mkdir -p "$PACKAGE_ROOT"
    PACKAGE_ROOT="${PACKAGE_ROOT:A}"
fi

WORK_ROOT="$(mktemp -d /private/tmp/TorrentCore-ServiceApp-DMG.XXXXXX)"
cleanup() {
    rm -rf "$WORK_ROOT"
    if [[ "$STAGE_ONLY" != true ]]; then
        rm -rf "$PACKAGE_ROOT"
    fi
}
trap cleanup EXIT

APP_PATH="$PACKAGE_ROOT/payload/$RUNTIME/TorrentCoreService.app"
mkdir -p "${APP_PATH:h}" "$PACKAGE_ROOT/Tools"
"$REPO_ROOT/Scripts/ServiceApp/build-macos-service-app.zsh" \
    --output-bundle "$APP_PATH" \
    --signing-identity "$SIGNING_IDENTITY"

ditto "$SCRIPT_DIR/install.zsh" "$PACKAGE_ROOT/install.zsh"
ditto "$SCRIPT_DIR/Open Terminal Here.command" "$PACKAGE_ROOT/Open Terminal Here.command"
ditto "$SCRIPT_DIR/Open README.command" "$PACKAGE_ROOT/Open README.command"
ditto "$SCRIPT_DIR/torrentcore-service-app-deploy.zsh" "$PACKAGE_ROOT/Tools/torrentcore-service-app-deploy.zsh"
ditto "$SCRIPT_DIR/torrentcore_service_app_deploy.py" "$PACKAGE_ROOT/Tools/torrentcore_service_app_deploy.py"
ditto "$REPO_ROOT/Scripts/ServiceApp/verify-macos-service-app.zsh" "$PACKAGE_ROOT/Tools/verify-macos-service-app.zsh"
chmod +x "$PACKAGE_ROOT/install.zsh" "$PACKAGE_ROOT/"*.command "$PACKAGE_ROOT/Tools/"*.zsh "$PACKAGE_ROOT/Tools/"*.py

GIT_SHA="$(git -C "$REPO_ROOT" rev-parse HEAD)"
BUILT_AT_UTC="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
APP_SHA="$(python3 - "$APP_PATH" <<'PY'
import hashlib
import os
import pathlib
import sys

root = pathlib.Path(sys.argv[1])
digest = hashlib.sha256()
for path in sorted(root.rglob("*"), key=lambda item: item.relative_to(root).as_posix()):
    relative = path.relative_to(root).as_posix()
    digest.update(relative.encode("utf-8"))
    digest.update(b"\0")
    if path.is_symlink():
        digest.update(b"L")
        digest.update(os.readlink(path).encode("utf-8"))
    elif path.is_file():
        digest.update(b"F")
        with path.open("rb") as handle:
            for chunk in iter(lambda: handle.read(1024 * 1024), b""):
                digest.update(chunk)
    elif path.is_dir():
        digest.update(b"D")
    digest.update(b"\0")
print(digest.hexdigest())
PY
)"

python3 - "$PACKAGE_ROOT/release.json" "$RELEASE_ID" "$RELEASE_DATE" "$INSTALLATION" "$CPU" "$RUNTIME" "$RELEASE_NAME" "$GIT_SHA" "$BUILT_AT_UTC" "$APP_SHA" "$SOURCE_DIRTY" <<'PY'
import json
import pathlib
import sys

output, release_id, release_date, installation, cpu, runtime, release_name, git_sha, built_at, app_sha, dirty = sys.argv[1:]
value = {
    "schemaVersion": 1,
    "product": "TorrentCoreServiceApp",
    "releaseId": release_id,
    "releaseDate": release_date,
    "installation": installation,
    "cpu": cpu,
    "releaseName": release_name,
    "version": "0.6.0",
    "build": "1",
    "gitSha": git_sha,
    "builtAtUtc": built_at,
    "sourceTreeDirty": dirty == "true",
    "runtimes": {
        runtime: {
            "path": f"payload/{runtime}/TorrentCoreService.app",
            "sha256": app_sha,
        }
    },
}
pathlib.Path(output).write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")
PY

cat > "$PACKAGE_ROOT/README.md" <<EOF
# TorrentCore $RELEASE_NAME $RELEASE_DATE ($INSTALLATION, $CPU)

This Service-only package installs ~/Applications/TorrentCore/TorrentCoreService.app and updates only
com.torrentcore.service.

It does not install, stop, start, verify, back up, or otherwise change TorrentCore.WebUI. It preserves the existing
~/TorrentCore structure, including Service files, scripts, logs, deployment records, backups, and
Scripts/torrentcore.env.

Start with:

    ./install.zsh plan
    ./install.zsh dry-run

Only run ./install.zsh apply --confirm during an explicitly approved Service deployment window.
EOF

cat > "$PACKAGE_ROOT/Runbook.md" <<'EOF'
# TorrentCore Service App Runbook

1. Run `./install.zsh plan` and review every resolved path.
2. Run `./install.zsh dry-run`; it must report that nothing changed.
3. Run `./install.zsh apply --confirm` only after explicit approval.
4. Run `./install.zsh verify` after installation.
5. Use `./install.zsh history` to locate an apply record.
6. Review `./install.zsh rollback --dry-run --history <record>` before a confirmed rollback.

VPN Disabled, Ready, and Degraded are valid installation outcomes when API health is `ok`. A degraded VPN state does
not fail installation and does not require operator recovery of the Service process.
EOF

(
    cd "$PACKAGE_ROOT"
    find . -type f ! -name checksums.txt -print | LC_ALL=C sort | while IFS= read -r file; do
        shasum -a 256 "$file"
    done
) > "$PACKAGE_ROOT/checksums.txt"

if [[ "$STAGE_ONLY" == true ]]; then
    print -r -- "Staged signed TorrentCore Service app package: $PACKAGE_ROOT"
    exit 0
fi

mkdir -p "$OUTPUT_DIR"
OUTPUT_DIR="${OUTPUT_DIR:A}"
OUTPUT_DMG="$OUTPUT_DIR/$ARTIFACT_STEM.dmg"
[[ ! -e "$OUTPUT_DMG" ]] || fail "Release artifact already exists: $OUTPUT_DMG"
WORK_DMG="$WORK_ROOT/$ARTIFACT_STEM.dmg"

hdiutil create -fs HFS+ -srcfolder "$PACKAGE_ROOT" -volname "$ARTIFACT_STEM" -format UDZO "$WORK_DMG" >/dev/null
codesign --force --sign "$SIGNING_IDENTITY" --timestamp "$WORK_DMG"
codesign --verify --verbose=2 "$WORK_DMG"
xcrun notarytool submit "$WORK_DMG" --keychain-profile "$NOTARY_PROFILE" --wait
xcrun stapler staple -v "$WORK_DMG"
xcrun stapler validate -v "$WORK_DMG"
hdiutil verify "$WORK_DMG"
spctl --assess --type open --context context:primary-signature --verbose=2 "$WORK_DMG"
ditto "$WORK_DMG" "$OUTPUT_DMG"
codesign --verify --verbose=2 "$OUTPUT_DMG"
xcrun stapler validate -v "$OUTPUT_DMG"
hdiutil verify "$OUTPUT_DMG"
spctl --assess --type open --context context:primary-signature --verbose=2 "$OUTPUT_DMG"
shasum -a 256 "$OUTPUT_DMG"
print -r -- "TorrentCore Service app DMG complete: $OUTPUT_DMG"
