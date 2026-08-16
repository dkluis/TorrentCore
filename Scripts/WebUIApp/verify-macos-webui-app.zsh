#!/bin/zsh

set -euo pipefail

BUNDLE_PATH=""
REQUIRE_SIGNED=false
TEAM_ID="5GRR76N48V"
fail() { print -ru2 -- "[TorrentCoreWebUI verification] ERROR: $*"; exit 1; }
while (( $# > 0 )); do
    case "$1" in
        --bundle) BUNDLE_PATH="${2:-}"; shift 2 ;;
        --require-signed) REQUIRE_SIGNED=true; shift ;;
        --team-id) TEAM_ID="${2:-}"; shift 2 ;;
        *) fail "Unknown argument: $1" ;;
    esac
done

[[ -d "$BUNDLE_PATH" ]] || fail "Bundle was not found: $BUNDLE_PATH"
INFO="$BUNDLE_PATH/Contents/Info.plist"
MAIN="$BUNDLE_PATH/Contents/MacOS/TorrentCoreWebUI"
RUNTIME="$BUNDLE_PATH/Contents/Resources/Runtime"
HELPER="$RUNTIME/TorrentCore.WebUI"
DEFAULTS="$BUNDLE_PATH/Contents/Resources/Defaults"
DEPLOYMENT="$BUNDLE_PATH/Contents/Resources/Deployment"
VERSION_JSON="$BUNDLE_PATH/Contents/Resources/version.json"
plutil -lint "$INFO" >/dev/null
[[ "$(plutil -extract CFBundleIdentifier raw -o - "$INFO")" == "com.conadv.torrentcore.webui" ]] || fail "Bundle identifier is incorrect."
[[ "$(plutil -extract CFBundleExecutable raw -o - "$INFO")" == "TorrentCoreWebUI" ]] || fail "Bundle executable is incorrect."
[[ "$(plutil -extract CFBundleShortVersionString raw -o - "$INFO")" == "0.7.0" ]] || fail "Bundle version is not 0.7.0."
[[ "$(plutil -extract CFBundleVersion raw -o - "$INFO")" == "13" ]] || fail "Bundle build is not 13."
[[ "$(plutil -extract LSMinimumSystemVersion raw -o - "$INFO")" == "26.0" ]] || fail "Minimum macOS version is not 26.0."
[[ "$(plutil -extract LSBackgroundOnly raw -o - "$INFO")" == "true" ]] || fail "Bundle is not background-only."
[[ -x "$MAIN" && -x "$HELPER" ]] || fail "Bundle executables are incomplete."
[[ -f "$RUNTIME/TorrentCore.WebUI.runtimeconfig.json" ]] || fail "WebUI runtime configuration is missing."
[[ -d "$RUNTIME/wwwroot" ]] || fail "Bundled wwwroot is missing."
[[ -f "$DEFAULTS/appsettings.json" ]] || fail "Default appsettings.json is missing."
[[ ! -e "$RUNTIME/appsettings.json" ]] || fail "Mutable appsettings.json must be in Defaults, not Runtime."
[[ -x "$DEPLOYMENT/install.zsh" && -f "$DEPLOYMENT/com.torrentcore.webui.plist" ]] || fail "Deployment resources are incomplete."
[[ -f "$VERSION_JSON" ]] || fail "Version metadata is missing."
[[ "$(lipo -archs "$MAIN")" == "arm64" ]] || fail "Native launcher is not Arm64-only."
[[ "$(lipo -archs "$HELPER")" == "arm64" ]] || fail "WebUI helper is not Arm64-only."
MAIN_UUID="$(dwarfdump --uuid "$MAIN" | awk 'NR == 1 {print $2}')"
HELPER_UUID="$(dwarfdump --uuid "$HELPER" | awk 'NR == 1 {print $2}')"
[[ -n "$MAIN_UUID" && -n "$HELPER_UUID" && "$MAIN_UUID" != "$HELPER_UUID" ]] || fail "Launcher/helper UUID separation failed."
grep -q '"component": "TorrentCoreWebUI"' "$VERSION_JSON" || fail "Version component is incorrect."
grep -q '"runtime": "osx-arm64"' "$VERSION_JSON" || fail "Version runtime is incorrect."
grep -q '<string>__BUNDLE_IDENTIFIER__</string>' "$DEPLOYMENT/com.torrentcore.webui.plist" || fail "Bundle identifier placeholder is missing."
grep -q 'TORRENTCORE_WEBUI_WORKING_DIRECTORY' "$DEPLOYMENT/com.torrentcore.webui.plist" || fail "External working-directory environment is missing."
grep -q 'WORKING_DIRECTORY="\$HOME/TorrentCore/WebUI"' "$DEPLOYMENT/install.zsh" || fail "Installer does not use ~/TorrentCore/WebUI."
grep -q 'TORRENTCORE_WEBUI_URLS:=http://0.0.0.0:7053' "$DEPLOYMENT/install.zsh" || fail "Installer does not retain the WebUI LAN default."
grep -q 'TORRENTCORE_WEBUI_SERVICE_BASE_URL:=http://127.0.0.1:7033/' "$DEPLOYMENT/install.zsh" || fail "Installer does not retain the Service fallback."
if find "$BUNDLE_PATH" -path '*/Config/service-connection.json' -print -quit | grep -q .; then fail "Machine-local service connection was found in bundle."; fi
if find "$BUNDLE_PATH" -type f \( -name '*.db' -o -name '*.resume' -o -name '*.torrent' \) | grep -q .; then fail "Mutable runtime data was found inside the app bundle."; fi

if [[ "$REQUIRE_SIGNED" == true ]]; then
    codesign --verify --deep --strict --verbose=2 "$BUNDLE_PATH"
    while IFS= read -r -d '' candidate; do
        if file -b "$candidate" | grep -q 'Mach-O'; then
            codesign --verify --strict --verbose=2 "$candidate"
            details="$(codesign --display --verbose=4 "$candidate" 2>&1)"
            [[ "$details" == *"TeamIdentifier=$TEAM_ID"* && "$details" == *"runtime"* && "$details" == *"Timestamp="* ]] || fail "Signing metadata is incomplete for ${candidate#$BUNDLE_PATH/}."
        fi
    done < <(find "$BUNDLE_PATH/Contents" -type f -print0)
    helper_entitlements="$(codesign --display --entitlements - "$HELPER" 2>&1)"
    [[ "$helper_entitlements" == *"com.apple.security.cs.allow-jit"* ]] || fail "WebUI helper JIT entitlement is missing."
    [[ "$helper_entitlements" == *"com.apple.security.cs.disable-library-validation"* ]] || fail "WebUI helper library-validation entitlement is missing."
fi
print -r -- "Verified $BUNDLE_PATH ($([[ "$REQUIRE_SIGNED" == true ]] && print signed || print unsigned) Arm64 app)."
