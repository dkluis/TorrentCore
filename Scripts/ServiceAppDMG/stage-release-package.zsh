#!/usr/bin/env zsh

set -euo pipefail

SCRIPT_DIR="${0:A:h}"
REPO_ROOT="${SCRIPT_DIR:h:h}"
DEPLOYMENTS_ROOT="/Volumes/CA-Desktop-HD-2/Development/Deployments"
TEAM_ID="5GRR76N48V"
INSTALLATION=""
CPU=""
RELEASE_NAME=""
RELEASE_DATE="$(date '+%Y.%m.%d')"
NOTES=""
PACKAGE_DIR=""
PACKAGE_ROOT=""
SIGNING_IDENTITY=""
PDF_TOOL="pandoc"
PDF_ENGINE="tectonic"
SKIP_PDF=false
REQUIRE_PDF=false
CLEAN=false

fail() { print -ru2 -- "[TorrentCore package staging] ERROR: $*"; exit 1; }

usage() {
    cat <<'EOF'
Usage: stage-release-package.zsh --installation <Dick|Tom> --cpu arm --release-name <name> --notes <summary> [options]

Options:
  --date <YYYY.MM.DD>          Release date. Defaults to today.
  --package-dir <path>         Installation package parent directory.
  --package-root <path>        Exact persistent package directory.
  --signing-identity <name>    Exact Developer ID Application identity.
  --pdf-tool <command>         Defaults to pandoc.
  --pdf-engine <engine>        Defaults to tectonic.
  --skip-pdf                   Generate Markdown only.
  --require-pdf                Fail if either PDF cannot be generated.
  --clean                      Replace an existing package directory.
EOF
}

while (( $# > 0 )); do
    case "$1" in
        --installation) INSTALLATION="${2:-}"; shift 2 ;;
        --cpu) CPU="${2:-}"; shift 2 ;;
        --release-name) RELEASE_NAME="${2:-}"; shift 2 ;;
        --date) RELEASE_DATE="${2:-}"; shift 2 ;;
        --notes) NOTES="${2:-}"; shift 2 ;;
        --package-dir) PACKAGE_DIR="${2:-}"; shift 2 ;;
        --package-root) PACKAGE_ROOT="${2:-}"; shift 2 ;;
        --signing-identity) SIGNING_IDENTITY="${2:-}"; shift 2 ;;
        --pdf-tool) PDF_TOOL="${2:-}"; shift 2 ;;
        --pdf-engine) PDF_ENGINE="${2:-}"; shift 2 ;;
        --skip-pdf) SKIP_PDF=true; shift ;;
        --require-pdf) REQUIRE_PDF=true; shift ;;
        --clean) CLEAN=true; shift ;;
        --help|-h) usage; exit 0 ;;
        *) fail "Unknown argument: $1" ;;
    esac
done

case "${INSTALLATION:l}" in
    dick) INSTALLATION="Dick"; MACHINE="CA-Desktop"; TARGET_HOME="/Users/dick/TorrentCore" ;;
    tom) INSTALLATION="Tom"; MACHINE="vm"; TARGET_HOME="/Users/tomhyer/TorrentCore" ;;
    *) fail "--installation must be Dick or Tom." ;;
esac
case "${CPU:l}" in
    arm) CPU="arm"; RUNTIME="osx-arm64" ;;
    intel) fail "Intel package staging is not supported; this release is Arm64-only." ;;
    *) fail "--cpu must be arm." ;;
esac
[[ "$RELEASE_NAME" =~ '^[A-Za-z][A-Za-z0-9.-]*$' ]] || fail "--release-name is required and invalid."
[[ "$RELEASE_DATE" =~ '^[0-9]{4}\.[0-9]{2}\.[0-9]{2}$' ]] || fail "Invalid release date: $RELEASE_DATE"
[[ -n "$NOTES" ]] || fail "--notes is required so the package states what changed."
[[ "$SKIP_PDF" != true || "$REQUIRE_PDF" != true ]] || fail "--skip-pdf cannot be used with --require-pdf."

SOURCE_DIRTY=false
[[ -z "$(git -C "$REPO_ROOT" status --porcelain)" ]] || SOURCE_DIRTY=true
if [[ "$SOURCE_DIRTY" == true ]]; then
    fail "The TorrentCore source tree is dirty. Commit the intended package source before staging."
fi

RELEASE_ID="torrentcore.$RELEASE_DATE.$INSTALLATION.$RELEASE_NAME"
ARTIFACT_STEM="TorrentCore-$RELEASE_ID"
if [[ -z "$PACKAGE_ROOT" ]]; then
    [[ -n "$PACKAGE_DIR" ]] || PACKAGE_DIR="$DEPLOYMENTS_ROOT/TorrentCore-Deployments/$INSTALLATION"
    mkdir -p "$PACKAGE_DIR"
    PACKAGE_DIR="${PACKAGE_DIR:A}"
    PACKAGE_ROOT="$PACKAGE_DIR/$ARTIFACT_STEM"
else
    mkdir -p "${PACKAGE_ROOT:h}"
    PACKAGE_ROOT="${PACKAGE_ROOT:h:A}/${PACKAGE_ROOT:t}"
fi
if [[ -e "$PACKAGE_ROOT" ]]; then
    [[ "$CLEAN" == true ]] || fail "Package root already exists: $PACKAGE_ROOT"
    rm -rf "$PACKAGE_ROOT"
fi

for command_name in security ditto python3; do command -v "$command_name" >/dev/null || fail "Required command is unavailable: $command_name"; done
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

GIT_SHA="$(git -C "$REPO_ROOT" rev-parse HEAD)"
BUILT_AT_UTC="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
SERVICE_APP_PATH="$PACKAGE_ROOT/payload/$RUNTIME/TorrentCoreService.app"
WEBUI_APP_PATH="$PACKAGE_ROOT/payload/$RUNTIME/TorrentCoreWebUI.app"
UI_APP_PATH="$PACKAGE_ROOT/TorrentCore.app"
mkdir -p "${SERVICE_APP_PATH:h}"

print -r -- "Staging TorrentCore release package..."
print -r -- "Package root: $PACKAGE_ROOT"
print -r -- "Release:      $RELEASE_ID"
print -r -- "Machine:      $MACHINE"
print -r -- "Runtime:      $RUNTIME"
print -r -- "Description:  $NOTES"

"$REPO_ROOT/Scripts/ServiceApp/build-macos-service-app.zsh" --output-bundle "$SERVICE_APP_PATH" --signing-identity "$SIGNING_IDENTITY"
"$REPO_ROOT/Scripts/WebUIApp/build-macos-webui-app.zsh" --output-bundle "$WEBUI_APP_PATH" --signing-identity "$SIGNING_IDENTITY"
"$REPO_ROOT/Scripts/WebUIApp/verify-macos-webui-static-assets.zsh" "$WEBUI_APP_PATH"
"$REPO_ROOT/Scripts/MacOSApp/build-signed-macos-ui-app.zsh" \
    --output-bundle "$UI_APP_PATH" --version 0.6.0 --build-number 11 \
    --git-sha "$GIT_SHA" --built-at-utc "$BUILT_AT_UTC" --signing-identity "$SIGNING_IDENTITY"

tree_sha256() {
python3 - "$1" <<'PY'
import hashlib, os, pathlib, sys
root=pathlib.Path(sys.argv[1]); digest=hashlib.sha256()
for path in sorted(root.rglob('*'), key=lambda item:item.relative_to(root).as_posix()):
    relative=path.relative_to(root).as_posix(); digest.update(relative.encode()); digest.update(b'\0')
    if path.is_symlink(): digest.update(b'L'+os.readlink(path).encode())
    elif path.is_file():
        digest.update(b'F')
        with path.open('rb') as handle:
            for chunk in iter(lambda:handle.read(1024*1024), b''): digest.update(chunk)
    elif path.is_dir(): digest.update(b'D')
    digest.update(b'\0')
print(digest.hexdigest())
PY
}
SERVICE_SHA="$(tree_sha256 "$SERVICE_APP_PATH")"
WEBUI_SHA="$(tree_sha256 "$WEBUI_APP_PATH")"
UI_SHA="$(tree_sha256 "$UI_APP_PATH")"

python3 - "$PACKAGE_ROOT/release.json" "$RELEASE_ID" "$RELEASE_DATE" "$INSTALLATION" "$MACHINE" "$TARGET_HOME" "$RUNTIME" "$NOTES" "$GIT_SHA" "$BUILT_AT_UTC" "$SOURCE_DIRTY" "$SERVICE_SHA" "$WEBUI_SHA" "$UI_SHA" <<'PY'
import json, pathlib, sys
(output, release_id, release_date, installation, machine, target_home, runtime, notes,
 git_sha, built_at, dirty, service_sha, webui_sha, ui_sha)=sys.argv[1:]
value={
 "schemaVersion":2, "product":"TorrentCoreManagedApps", "installation":installation,
 "machine":machine, "targetHome":target_home, "runtime":runtime, "releaseId":release_id,
 "releaseDate":release_date, "componentVersion":"0.6.0", "version":"0.6.0", "build":"1",
 "notes":notes, "gitSha":git_sha, "builtAtUtc":built_at, "sourceTreeDirty":dirty=="true",
 "managedApps":{
  "service":{"path":f"payload/{runtime}/TorrentCoreService.app", "runtime":runtime,
   "bundleIdentifier":"com.conadv.torrentcore.service", "version":"0.6.0", "build":"1", "sha256":service_sha},
  "webUi":{"path":f"payload/{runtime}/TorrentCoreWebUI.app", "runtime":runtime,
   "bundleIdentifier":"com.conadv.torrentcore.webui", "version":"0.6.0", "build":"11", "sha256":webui_sha}},
 "nativeUi":{"path":"TorrentCore.app", "version":"0.6.0", "build":"11", "runtime":runtime,
  "sha256":ui_sha, "installPath":"/Applications/TorrentCore.app", "installMode":"DragToApplications"},
 "protectedFiles":["Scripts/torrentcore.env", "WebUI/Config/service-connection.json"],
}
pathlib.Path(output).write_text(json.dumps(value,indent=2)+"\n",encoding="utf-8")
PY

doc_args=(--package-root "$PACKAGE_ROOT" --pdf-tool "$PDF_TOOL" --pdf-engine "$PDF_ENGINE")
[[ "$SKIP_PDF" != true ]] || doc_args+=(--skip-pdf)
[[ "$REQUIRE_PDF" != true ]] || doc_args+=(--require-pdf)
python3 "$SCRIPT_DIR/generate-package-docs.py" "${doc_args[@]}"

if find "$PACKAGE_ROOT" -path '*/Config/service-connection.json' -print -quit | grep -q .; then
    fail "Machine-local Config/service-connection.json was found in the staged package."
fi

print -r -- ""
print -r -- "TorrentCore release package staged."
print -r -- "Package root: $PACKAGE_ROOT"
print -r -- "Manifest:     $PACKAGE_ROOT/release.json"
print -r -- "README:       $PACKAGE_ROOT/README.md"
print -r -- "Runbook:      $PACKAGE_ROOT/Runbook.md"
