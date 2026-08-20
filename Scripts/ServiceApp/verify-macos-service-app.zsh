#!/bin/zsh

set -euo pipefail

BUNDLE_PATH=""
REQUIRE_SIGNED=false
TEAM_ID="5GRR76N48V"

fail() {
    print -ru2 -- "[TorrentCoreService verification] ERROR: $*"
    exit 1
}

while (( $# > 0 )); do
    case "$1" in
        --bundle) BUNDLE_PATH="${2:-}"; shift 2 ;;
        --require-signed) REQUIRE_SIGNED=true; shift ;;
        --team-id) TEAM_ID="${2:-}"; shift 2 ;;
        --help|-h)
            print -r -- "Usage: verify-macos-service-app.zsh --bundle <TorrentCoreService.app> [--require-signed] [--team-id 5GRR76N48V]"
            exit 0
            ;;
        *) fail "Unknown argument: $1" ;;
    esac
done

[[ -d "$BUNDLE_PATH" ]] || fail "Bundle was not found: $BUNDLE_PATH"
INFO="$BUNDLE_PATH/Contents/Info.plist"
MAIN="$BUNDLE_PATH/Contents/MacOS/TorrentCoreService"
RUNTIME="$BUNDLE_PATH/Contents/Resources/Runtime"
HELPER="$RUNTIME/TorrentCoreService"
DEPLOYMENT="$BUNDLE_PATH/Contents/Resources/Deployment"
VERSION_JSON="$BUNDLE_PATH/Contents/Resources/version.json"

[[ -f "$INFO" ]] || fail "Info.plist is missing."
plutil -lint "$INFO" >/dev/null
[[ "$(plutil -extract CFBundleIdentifier raw -o - "$INFO")" == "com.conadv.torrentcore.service" ]] || fail "Bundle identifier is incorrect."
[[ "$(plutil -extract CFBundleExecutable raw -o - "$INFO")" == "TorrentCoreService" ]] || fail "Bundle executable is incorrect."
[[ "$(plutil -extract CFBundleShortVersionString raw -o - "$INFO")" == "0.8.0" ]] || fail "Bundle version is not 0.8.0."
[[ "$(plutil -extract CFBundleVersion raw -o - "$INFO")" == "15" ]] || fail "Bundle build is not 15."
[[ "$(plutil -extract LSMinimumSystemVersion raw -o - "$INFO")" == "26.0" ]] || fail "Minimum macOS version is not 26.0."
[[ "$(plutil -extract LSBackgroundOnly raw -o - "$INFO")" == "true" ]] || fail "Bundle is not background-only."
[[ "$(plutil -extract NSLocalNetworkUsageDescription raw -o - "$INFO")" == "TorrentCoreService accepts API connections from trusted devices on your local network." ]] || fail "Local Network text is incorrect."

[[ -x "$MAIN" ]] || fail "Native launcher is missing."
[[ -x "$HELPER" ]] || fail "Framework-dependent Service helper is missing."
[[ -f "$RUNTIME/TorrentCoreService.runtimeconfig.json" ]] || fail "Service runtime configuration is missing."
[[ -f "$RUNTIME/appsettings.json" ]] || fail "Service appsettings.json is missing from Runtime."
[[ -x "$DEPLOYMENT/install.zsh" ]] || fail "Embedded installer is missing."
[[ -f "$DEPLOYMENT/com.torrentcore.service.plist" ]] || fail "Embedded LaunchAgent template is missing."
[[ -f "$VERSION_JSON" ]] || fail "Version metadata is missing."
[[ -f "$BUNDLE_PATH/Contents/Resources/TorrentCoreService.icns" ]] || fail "App icon is missing."
[[ "$(lipo -archs "$MAIN")" == "arm64" ]] || fail "Native launcher is not Arm64-only."
[[ "$(lipo -archs "$HELPER")" == "arm64" ]] || fail "Service helper is not Arm64-only."
MAIN_UUID="$(dwarfdump --uuid "$MAIN" | awk 'NR == 1 {print $2}')"
HELPER_UUID="$(dwarfdump --uuid "$HELPER" | awk 'NR == 1 {print $2}')"
[[ -n "$MAIN_UUID" ]] || fail "Native launcher has no Mach-O UUID."
[[ -n "$HELPER_UUID" ]] || fail "Service helper has no Mach-O UUID."
[[ "$MAIN_UUID" != "$HELPER_UUID" ]] || fail "Native launcher and Service helper have the same Mach-O UUID."
grep -q '"component": "TorrentCoreService"' "$VERSION_JSON" || fail "Version component is incorrect."
grep -q '"runtime": "osx-arm64"' "$VERSION_JSON" || fail "Version runtime is incorrect."
grep -q '<string>__ASPNETCORE_URLS__</string>' "$DEPLOYMENT/com.torrentcore.service.plist" || fail "LaunchAgent URL placeholder is missing."
grep -q '<string>__BUNDLE_IDENTIFIER__</string>' "$DEPLOYMENT/com.torrentcore.service.plist" || fail "Associated bundle identifier placeholder is missing."
grep -q 'http://0.0.0.0:7033' "$DEPLOYMENT/install.zsh" || fail "Installer does not default to the Service LAN URL."
grep -q 'TORRENTCORE_WORKING_DIRECTORY' "$DEPLOYMENT/com.torrentcore.service.plist" || fail "External working-directory environment is missing."
grep -q 'WORKING_DIRECTORY=\"\$HOME/TorrentCore/Service\"' "$DEPLOYMENT/install.zsh" || fail "Installer does not use ~/TorrentCore/Service."
grep -q 'LOG_DIR=\"\$HOME/TorrentCore/Logs\"' "$DEPLOYMENT/install.zsh" || fail "Installer does not use ~/TorrentCore/Logs."
grep -q 'Scripts/torrentcore.env' "$DEPLOYMENT/install.zsh" || fail "Installer does not preserve the established environment-file path."

if find "$BUNDLE_PATH" -type f \( -name '*.db' -o -name '*.resume' -o -name '*.torrent' \) | grep -q .; then
    fail "Mutable runtime data was found inside the app bundle."
fi

if [[ "$REQUIRE_SIGNED" == true ]]; then
    codesign --verify --deep --strict --verbose=2 "$BUNDLE_PATH"
    while IFS= read -r -d '' candidate; do
        if file -b "$candidate" | grep -q 'Mach-O'; then
            codesign --verify --strict --verbose=2 "$candidate"
            signing_details="$(codesign --display --verbose=4 "$candidate" 2>&1)"
            [[ "$signing_details" == *"TeamIdentifier=$TEAM_ID"* ]] || fail "Team ID is incorrect for ${candidate#$BUNDLE_PATH/}."
            [[ "$signing_details" == *"runtime"* ]] || fail "Hardened Runtime is missing from ${candidate#$BUNDLE_PATH/}."
            [[ "$signing_details" == *"Timestamp="* ]] || fail "Secure timestamp is missing from ${candidate#$BUNDLE_PATH/}."
        fi
    done < <(find "$BUNDLE_PATH/Contents" -type f -print0)

    helper_entitlements="$(codesign --display --entitlements - "$HELPER" 2>&1)"
    [[ "$helper_entitlements" == *"com.apple.security.cs.allow-jit"* ]] || fail "Service helper JIT entitlement is missing."
    [[ "$helper_entitlements" == *"com.apple.security.cs.disable-library-validation"* ]] || fail "Service helper library-validation entitlement is missing."
fi

print -r -- "Verified $BUNDLE_PATH ($([[ "$REQUIRE_SIGNED" == true ]] && print signed || print unsigned) Arm64 app)."
