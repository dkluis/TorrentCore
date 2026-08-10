#!/bin/zsh

set -euo pipefail

SCRIPT_DIR="${0:A:h}"
REPO_ROOT="${SCRIPT_DIR:h:h}"
TEAM_ID="5GRR76N48V"
PROJECT_PATH="$REPO_ROOT/clients/apple/TorrentCoreApple.xcodeproj"
EXPORT_OPTIONS_PATH="$REPO_ROOT/clients/apple/ExportOptions-DeveloperID.plist"
SCHEME="TorrentCoreMac"
PRODUCT_NAME="TorrentCore"

OUTPUT_BUNDLE=""
VERSION=""
BUILD_NUMBER=""
GIT_SHA=""
BUILT_AT_UTC=""
SIGNING_IDENTITY=""

fail() {
    print -ru2 -- "[TorrentCore macOS UI] ERROR: $*"
    exit 1
}

usage() {
    cat <<'EOF'
Usage: build-signed-macos-ui-app.zsh --output-bundle <TorrentCore.app> --version <version> --build-number <number> --git-sha <40-hex> --built-at-utc <ISO-8601> --signing-identity <Developer ID Application identity>
EOF
}

while (( $# > 0 )); do
    case "$1" in
        --output-bundle) OUTPUT_BUNDLE="${2:-}"; shift 2 ;;
        --version) VERSION="${2:-}"; shift 2 ;;
        --build-number) BUILD_NUMBER="${2:-}"; shift 2 ;;
        --git-sha) GIT_SHA="${2:-}"; shift 2 ;;
        --built-at-utc) BUILT_AT_UTC="${2:-}"; shift 2 ;;
        --signing-identity) SIGNING_IDENTITY="${2:-}"; shift 2 ;;
        --help|-h) usage; exit 0 ;;
        *) fail "Unknown argument: $1" ;;
    esac
done

[[ "$OUTPUT_BUNDLE" == */TorrentCore.app || "$OUTPUT_BUNDLE" == TorrentCore.app ]] ||
    fail "--output-bundle must name TorrentCore.app."
[[ "$VERSION" =~ '^[0-9]+\.[0-9]+\.[0-9]+$' ]] || fail "Invalid version: $VERSION"
[[ "$BUILD_NUMBER" =~ '^[1-9][0-9]*$' ]] || fail "Invalid build number: $BUILD_NUMBER"
[[ "$GIT_SHA" =~ '^[0-9a-f]{40}$' ]] || fail "Git SHA must be 40 lowercase hexadecimal characters."
[[ "$BUILT_AT_UTC" =~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$' ]] ||
    fail "Built-at time must use UTC ISO-8601 seconds (YYYY-MM-DDTHH:MM:SSZ)."
[[ "$SIGNING_IDENTITY" == Developer\ ID\ Application:*"($TEAM_ID)" ]] ||
    fail "Signing identity is not for Team $TEAM_ID."
[[ -d "$PROJECT_PATH" ]] || fail "Xcode project was not found: $PROJECT_PATH"
[[ -f "$EXPORT_OPTIONS_PATH" ]] || fail "Export options were not found: $EXPORT_OPTIONS_PATH"
[[ ! -e "$OUTPUT_BUNDLE" ]] || fail "Output bundle already exists: $OUTPUT_BUNDLE"

for command_name in codesign ditto lipo plutil xcodebuild; do
    command -v "$command_name" >/dev/null 2>&1 || fail "Required command is unavailable: $command_name"
done
plutil -lint "$EXPORT_OPTIONS_PATH" >/dev/null || fail "Export options plist is invalid."

WORK_DIR="$(mktemp -d /private/tmp/TorrentCoreMac-app.XXXXXX)"
trap 'rm -rf "$WORK_DIR"' EXIT
ARCHIVE_PATH="$WORK_DIR/TorrentCore.xcarchive"
DERIVED_DATA_PATH="$WORK_DIR/DerivedData"
EXPORT_PATH="$WORK_DIR/Export"
EXPORTED_APP="$EXPORT_PATH/$PRODUCT_NAME.app"
EXECUTABLE_PATH="$EXPORTED_APP/Contents/MacOS/$PRODUCT_NAME"

print -r -- "[TorrentCore macOS UI] Archiving $PRODUCT_NAME $VERSION ($BUILD_NUMBER)."
xcodebuild archive \
    -project "$PROJECT_PATH" \
    -scheme "$SCHEME" \
    -configuration Release \
    -destination "generic/platform=macOS" \
    -archivePath "$ARCHIVE_PATH" \
    -derivedDataPath "$DERIVED_DATA_PATH" \
    -skipPackagePluginValidation \
    -allowProvisioningUpdates \
    MARKETING_VERSION="$VERSION" \
    CURRENT_PROJECT_VERSION="$BUILD_NUMBER" \
    DEVELOPMENT_TEAM="$TEAM_ID"

print -r -- "[TorrentCore macOS UI] Exporting the Developer ID-signed application."
xcodebuild -exportArchive \
    -archivePath "$ARCHIVE_PATH" \
    -exportPath "$EXPORT_PATH" \
    -exportOptionsPlist "$EXPORT_OPTIONS_PATH" \
    -allowProvisioningUpdates

[[ -d "$EXPORTED_APP" ]] || fail "Export did not produce $EXPORTED_APP."
[[ -x "$EXECUTABLE_PATH" ]] || fail "Exported app executable was not found."
[[ "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$EXPORTED_APP/Contents/Info.plist")" == "$VERSION" ]] ||
    fail "Exported app version does not match $VERSION."
[[ "$(/usr/libexec/PlistBuddy -c 'Print :CFBundleVersion' "$EXPORTED_APP/Contents/Info.plist")" == "$BUILD_NUMBER" ]] ||
    fail "Exported app build does not match $BUILD_NUMBER."
[[ "$(lipo -archs "$EXECUTABLE_PATH")" == "arm64" ]] || fail "Exported app is not Arm64-only."

cat > "$EXPORTED_APP/Contents/Resources/version.json" <<EOF
{
  "component": "TorrentCoreNativeUI",
  "version": "$VERSION",
  "build": "$BUILD_NUMBER",
  "gitSha": "$GIT_SHA",
  "builtAtUtc": "$BUILT_AT_UTC",
  "runtime": "osx-arm64"
}
EOF

# Adding release metadata changes the sealed resource set. Re-sign only the outer app while preserving the exact
# identifier, designated requirement, and exported sandbox entitlements produced by Xcode.
codesign \
    --force \
    --sign "$SIGNING_IDENTITY" \
    --timestamp \
    --options runtime \
    --preserve-metadata=identifier,requirements,entitlements \
    "$EXPORTED_APP"
codesign --verify --deep --strict --verbose=2 "$EXPORTED_APP"

mkdir -p "${OUTPUT_BUNDLE:h}"
ditto --noqtn "$EXPORTED_APP" "$OUTPUT_BUNDLE"
codesign --verify --deep --strict --verbose=2 "$OUTPUT_BUNDLE"
[[ -f "$OUTPUT_BUNDLE/Contents/Resources/version.json" ]] || fail "Packaged version.json is missing."
print -r -- "Built signed TorrentCore macOS UI app: $OUTPUT_BUNDLE"
