#!/usr/bin/env zsh

set -euo pipefail

SCRIPT_DIR="${0:A:h}"
REPO_ROOT="${SCRIPT_DIR:h:h}"
DEPLOYMENTS_ROOT="/Volumes/CA-Desktop-HD-2/Development/Deployments"
TEAM_ID="5GRR76N48V"
NOTARY_PROFILE="TorrentCore-notary"
SIGNING_IDENTITY=""
PACKAGE_ROOT=""
DMG_PATH=""
VOLUME_NAME=""
CLEAN=false
CHECK_ONLY=false

fail() { print -ru2 -- "[TorrentCore package DMG] ERROR: $*"; exit 1; }

usage() {
    cat <<'EOF'
Usage: build-package-dmg.zsh --package-root <persistent-package-directory> [options]

Options:
  --dmg-path <path>            Defaults to Deployments/DMGs/<package-name>.dmg.
  --volume-name <name>         Defaults to the package directory name.
  --signing-identity <name>    Exact Developer ID Application identity.
  --notary-profile <name>      Defaults to TorrentCore-notary.
  --clean                      Replace an existing DMG.
  --check                      Validate release credentials and tools only.
EOF
}

while (( $# > 0 )); do
    case "$1" in
        --package-root) PACKAGE_ROOT="${2:-}"; shift 2 ;;
        --dmg-path) DMG_PATH="${2:-}"; shift 2 ;;
        --volume-name) VOLUME_NAME="${2:-}"; shift 2 ;;
        --signing-identity) SIGNING_IDENTITY="${2:-}"; shift 2 ;;
        --notary-profile) NOTARY_PROFILE="${2:-}"; shift 2 ;;
        --clean) CLEAN=true; shift ;;
        --check) CHECK_ONLY=true; shift ;;
        --help|-h) usage; exit 0 ;;
        *) fail "Unknown argument: $1" ;;
    esac
done

[[ -n "$PACKAGE_ROOT" ]] || fail "--package-root is required."
[[ -d "$PACKAGE_ROOT" ]] || fail "Package root was not found: $PACKAGE_ROOT"
PACKAGE_ROOT="${PACKAGE_ROOT:A}"
PACKAGE_NAME="${PACKAGE_ROOT:t}"
[[ -f "$PACKAGE_ROOT/release.json" ]] || fail "Package release manifest is missing."
[[ -n "$VOLUME_NAME" ]] || VOLUME_NAME="$PACKAGE_NAME"
[[ -n "$DMG_PATH" ]] || DMG_PATH="$DEPLOYMENTS_ROOT/DMGs/$PACKAGE_NAME.dmg"
mkdir -p "${DMG_PATH:h}"
DMG_PATH="${DMG_PATH:h:A}/${DMG_PATH:t}"

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
    (( ${#identities[@]} == 1 )) || fail "Expected exactly one Developer ID Application identity for Team $TEAM_ID."
    SIGNING_IDENTITY="${identities[1]}"
fi
[[ "$SIGNING_IDENTITY" == Developer\ ID\ Application:*"($TEAM_ID)" ]] || fail "Signing identity is not for Team $TEAM_ID."
xcrun notarytool history --keychain-profile "$NOTARY_PROFILE" --output-format json >/dev/null || fail "Notary profile is missing or invalid: $NOTARY_PROFILE"
if [[ "$CHECK_ONLY" == true ]]; then
    print -r -- "TorrentCore package DMG prerequisites are ready."
    exit 0
fi

VALUES=("${(@f)$(python3 - "$PACKAGE_ROOT/release.json" <<'PY'
import json,sys
v=json.load(open(sys.argv[1],encoding='utf-8-sig'))
if v.get('product')!='TorrentCoreManagedApps': raise SystemExit('unexpected package product')
if v.get('runtime')!='osx-arm64': raise SystemExit('package is not Arm64')
if v.get('sourceTreeDirty') is not False: raise SystemExit('release package was not built from clean source')
print(v['managedApps']['service']['path']); print(v['managedApps']['service']['sha256'])
print(v['managedApps']['webUi']['path']); print(v['managedApps']['webUi']['sha256'])
print(v['nativeUi']['path']); print(v['nativeUi']['sha256'])
PY
)}")
SERVICE_APP="$PACKAGE_ROOT/${VALUES[1]}"
SERVICE_SHA="${VALUES[2]}"
WEBUI_APP="$PACKAGE_ROOT/${VALUES[3]}"
WEBUI_SHA="${VALUES[4]}"
UI_APP="$PACKAGE_ROOT/${VALUES[5]}"
UI_SHA="${VALUES[6]}"
SERVICE_VERIFIER="$REPO_ROOT/Scripts/ServiceApp/verify-macos-service-app.zsh"
WEBUI_VERIFIER="$REPO_ROOT/Scripts/WebUIApp/verify-macos-webui-app.zsh"
STATIC_VERIFIER="$REPO_ROOT/Scripts/WebUIApp/verify-macos-webui-static-assets.zsh"
[[ -d "$SERVICE_APP" && -d "$WEBUI_APP" && -d "$UI_APP" ]] || fail "The staged app payload is incomplete."
[[ -x "$SCRIPT_DIR/install.zsh" && -x "$SERVICE_VERIFIER" && -x "$WEBUI_VERIFIER" && -x "$STATIC_VERIFIER" ]] || fail "DMG installer resources are incomplete."

tree_sha256() {
python3 - "$1" <<'PY'
import hashlib,os,pathlib,sys
root=pathlib.Path(sys.argv[1]); digest=hashlib.sha256()
for path in sorted(root.rglob('*'),key=lambda item:item.relative_to(root).as_posix()):
 r=path.relative_to(root).as_posix(); digest.update(r.encode()); digest.update(b'\0')
 if path.is_symlink(): digest.update(b'L'+os.readlink(path).encode())
 elif path.is_file():
  digest.update(b'F')
  with path.open('rb') as h:
   for c in iter(lambda:h.read(1024*1024),b''): digest.update(c)
 elif path.is_dir(): digest.update(b'D')
 digest.update(b'\0')
print(digest.hexdigest())
PY
}
[[ "$(tree_sha256 "$SERVICE_APP")" == "$SERVICE_SHA" ]] || fail "Service app checksum differs from release.json."
[[ "$(tree_sha256 "$WEBUI_APP")" == "$WEBUI_SHA" ]] || fail "WebUI app checksum differs from release.json."
[[ "$(tree_sha256 "$UI_APP")" == "$UI_SHA" ]] || fail "Native UI checksum differs from release.json."
"$SERVICE_VERIFIER" --bundle "$SERVICE_APP" --require-signed
"$WEBUI_VERIFIER" --bundle "$WEBUI_APP" --require-signed
"$STATIC_VERIFIER" "$WEBUI_APP"
codesign --verify --deep --strict --verbose=2 "$UI_APP"
if find "$PACKAGE_ROOT" -path '*/Config/service-connection.json' -print -quit | grep -q .; then
    fail "Machine-local Config/service-connection.json was found in the package."
fi

if [[ -e "$DMG_PATH" ]]; then
    [[ "$CLEAN" == true ]] || fail "DMG already exists: $DMG_PATH"
fi
WORK_ROOT="$(mktemp -d /private/tmp/TorrentCore-package-dmg.XXXXXX)"
cleanup() { rm -rf "$WORK_ROOT"; }
trap cleanup EXIT
IMAGE_ROOT="$WORK_ROOT/$VOLUME_NAME"
WORK_DMG="$WORK_ROOT/${DMG_PATH:t}"
ditto --noqtn "$PACKAGE_ROOT" "$IMAGE_ROOT"
ditto --noqtn "$SCRIPT_DIR/install.zsh" "$IMAGE_ROOT/install.zsh"
ditto --noqtn "$SCRIPT_DIR/Open Terminal Here.command" "$IMAGE_ROOT/Open Terminal Here.command"
ditto --noqtn "$SCRIPT_DIR/Open README.command" "$IMAGE_ROOT/Open README.command"
mkdir -p "$IMAGE_ROOT/Tools"
ditto --noqtn "$SCRIPT_DIR/torrentcore-service-app-deploy.zsh" "$IMAGE_ROOT/Tools/torrentcore-service-app-deploy.zsh"
ditto --noqtn "$SCRIPT_DIR/torrentcore_service_app_deploy.py" "$IMAGE_ROOT/Tools/torrentcore_service_app_deploy.py"
ditto --noqtn "$SERVICE_VERIFIER" "$IMAGE_ROOT/Tools/verify-macos-service-app.zsh"
ditto --noqtn "$WEBUI_VERIFIER" "$IMAGE_ROOT/Tools/verify-macos-webui-app.zsh"
ditto --noqtn "$STATIC_VERIFIER" "$IMAGE_ROOT/Tools/verify-macos-webui-static-assets.zsh"
chmod +x "$IMAGE_ROOT/install.zsh" "$IMAGE_ROOT/"*.command "$IMAGE_ROOT/Tools/"*
ln -s /Applications "$IMAGE_ROOT/Applications"

cat > "$IMAGE_ROOT/README-FIRST.txt" <<EOF
TorrentCore deployment package

Release package:
  $VOLUME_NAME

Changes:
  $(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["notes"])' "$PACKAGE_ROOT/release.json")

Start here:
  1. Open README.pdf for the package summary.
  2. Open Runbook.pdf for the machine-specific manual deployment commands. If PDF output was skipped, open Runbook.md.
  3. Open Terminal in this mounted DMG root before running any manual command from the runbook.

Important:
  Use the helper scripts in the package root or the exact commands in the runbook.
EOF

(
    cd "$IMAGE_ROOT"
    find . -type f ! -name checksums.txt -print | LC_ALL=C sort | while IFS= read -r file; do shasum -a 256 "$file"; done
) > "$IMAGE_ROOT/checksums.txt"

hdiutil create \
    -volname "$VOLUME_NAME" \
    -srcfolder "$IMAGE_ROOT" \
    -format UDZO \
    -imagekey zlib-level=9 \
    "$WORK_DMG" >/dev/null
codesign --force --sign "$SIGNING_IDENTITY" --timestamp "$WORK_DMG"
codesign --verify --verbose=2 "$WORK_DMG"
xcrun notarytool submit "$WORK_DMG" --keychain-profile "$NOTARY_PROFILE" --wait
xcrun stapler staple -v "$WORK_DMG"
xcrun stapler validate -v "$WORK_DMG"
hdiutil verify "$WORK_DMG"
spctl --assess --type open --context context:primary-signature --verbose=2 "$WORK_DMG"
[[ ! -e "$DMG_PATH" ]] || rm -f "$DMG_PATH"
ditto --noqtn "$WORK_DMG" "$DMG_PATH"
codesign --verify --verbose=2 "$DMG_PATH"
xcrun stapler validate -v "$DMG_PATH"
hdiutil verify "$DMG_PATH"
spctl --assess --type open --context context:primary-signature --verbose=2 "$DMG_PATH"
shasum -a 256 "$DMG_PATH"
print -r -- "created: $DMG_PATH"
