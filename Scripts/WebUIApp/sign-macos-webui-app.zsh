#!/bin/zsh

set -euo pipefail

SCRIPT_DIR="${0:A:h}"
ENTITLEMENTS="$SCRIPT_DIR/TorrentCoreWebUI.entitlements"
BUNDLE_PATH=""
SIGNING_IDENTITY=""
TEAM_ID="5GRR76N48V"
fail() { print -ru2 -- "[TorrentCoreWebUI signing] ERROR: $*"; exit 1; }

while (( $# > 0 )); do
    case "$1" in
        --bundle) BUNDLE_PATH="${2:-}"; shift 2 ;;
        --signing-identity) SIGNING_IDENTITY="${2:-}"; shift 2 ;;
        --team-id) TEAM_ID="${2:-}"; shift 2 ;;
        *) fail "Unknown argument: $1" ;;
    esac
done
[[ -d "$BUNDLE_PATH" ]] || fail "Bundle was not found: $BUNDLE_PATH"
[[ "$SIGNING_IDENTITY" == Developer\ ID\ Application:*"($TEAM_ID)" ]] || fail "Signing identity does not belong to Team $TEAM_ID."
HELPER="$BUNDLE_PATH/Contents/Resources/Runtime/TorrentCore.WebUI"
MAIN="$BUNDLE_PATH/Contents/MacOS/TorrentCoreWebUI"
[[ -x "$HELPER" && -x "$MAIN" ]] || fail "Bundle executables are incomplete."

while IFS= read -r -d '' candidate; do
    [[ "$candidate" == "$HELPER" || "$candidate" == "$MAIN" ]] && continue
    if file -b "$candidate" | grep -q 'Mach-O'; then
        digest="$(print -rn -- "${candidate#$BUNDLE_PATH/}" | shasum -a 256 | awk '{print substr($1,1,12)}')"
        codesign --force --sign "$SIGNING_IDENTITY" --timestamp --options runtime --identifier "com.conadv.torrentcore.webui.nested.$digest" "$candidate"
    fi
done < <(find "$BUNDLE_PATH/Contents" -type f -print0)

codesign --force --sign "$SIGNING_IDENTITY" --timestamp --options runtime --identifier com.conadv.torrentcore.webui.runtime --entitlements "$ENTITLEMENTS" "$HELPER"
codesign --force --sign "$SIGNING_IDENTITY" --timestamp --options runtime --identifier com.conadv.torrentcore.webui.launcher "$MAIN"
codesign --force --sign "$SIGNING_IDENTITY" --timestamp --options runtime --identifier com.conadv.torrentcore.webui "$BUNDLE_PATH"
print -r -- "Signed $BUNDLE_PATH"
