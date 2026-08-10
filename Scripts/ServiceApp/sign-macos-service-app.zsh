#!/bin/zsh

set -euo pipefail

SCRIPT_DIR="${0:A:h}"
ENTITLEMENTS="$SCRIPT_DIR/TorrentCoreService.entitlements"
BUNDLE_PATH=""
SIGNING_IDENTITY=""
TEAM_ID="5GRR76N48V"

fail() {
    print -ru2 -- "[TorrentCoreService signing] ERROR: $*"
    exit 1
}

while (( $# > 0 )); do
    case "$1" in
        --bundle) BUNDLE_PATH="${2:-}"; shift 2 ;;
        --signing-identity) SIGNING_IDENTITY="${2:-}"; shift 2 ;;
        --team-id) TEAM_ID="${2:-}"; shift 2 ;;
        --help|-h)
            print -r -- "Usage: sign-macos-service-app.zsh --bundle <TorrentCoreService.app> --signing-identity <Developer ID Application identity> [--team-id 5GRR76N48V]"
            exit 0
            ;;
        *) fail "Unknown argument: $1" ;;
    esac
done

[[ -d "$BUNDLE_PATH" ]] || fail "Bundle was not found: $BUNDLE_PATH"
[[ -n "$SIGNING_IDENTITY" ]] || fail "--signing-identity is required."
[[ "$SIGNING_IDENTITY" == Developer\ ID\ Application:*"($TEAM_ID)" ]] ||
    fail "Signing identity does not belong to Team $TEAM_ID."
[[ -f "$ENTITLEMENTS" ]] || fail "Entitlements were not found."

HELPER="$BUNDLE_PATH/Contents/Resources/Runtime/TorrentCoreService"
MAIN="$BUNDLE_PATH/Contents/MacOS/TorrentCoreService"
[[ -x "$HELPER" && -x "$MAIN" ]] || fail "Bundle executables are incomplete."

is_macho() {
    file -b "$1" | grep -q 'Mach-O'
}

while IFS= read -r -d '' candidate; do
    [[ "$candidate" == "$HELPER" || "$candidate" == "$MAIN" ]] && continue
    if is_macho "$candidate"; then
        digest="$(print -rn -- "${candidate#$BUNDLE_PATH/}" | shasum -a 256 | awk '{print substr($1,1,12)}')"
        codesign --force --sign "$SIGNING_IDENTITY" --timestamp --options runtime \
            --identifier "com.conadv.torrentcore.service.nested.$digest" "$candidate"
    fi
done < <(find "$BUNDLE_PATH/Contents" -type f -print0)

codesign --force --sign "$SIGNING_IDENTITY" --timestamp --options runtime \
    --identifier com.conadv.torrentcore.service.runtime \
    --entitlements "$ENTITLEMENTS" "$HELPER"
codesign --force --sign "$SIGNING_IDENTITY" --timestamp --options runtime \
    --identifier com.conadv.torrentcore.service.launcher "$MAIN"
codesign --force --sign "$SIGNING_IDENTITY" --timestamp --options runtime \
    --identifier com.conadv.torrentcore.service "$BUNDLE_PATH"

print -r -- "Signed $BUNDLE_PATH"
