#!/bin/zsh

set -euo pipefail

SCRIPT_DIR="${0:A:h}"
REPO_ROOT="${SCRIPT_DIR:h:h}"
LAUNCHER_SOURCE="$SCRIPT_DIR/torrentcore-service-launcher.c"
ICON_SOURCE="$REPO_ROOT/clients/apple/Apps/TorrentCoreMac/Assets.xcassets/AppIcon.appiconset"

PUBLISH_ROOT=""
OUTPUT_BUNDLE=""
VERSION="0.8.0"
BUILD_NUMBER="14"
GIT_SHA=""
BUILT_AT_UTC=""

fail() {
    print -ru2 -- "[TorrentCoreService app] ERROR: $*"
    exit 1
}

usage() {
    print -r -- "Usage: package-macos-service-app.zsh --publish-root <dir> --output-bundle <TorrentCoreService.app> --git-sha <40-hex> --built-at-utc <ISO-8601> [--version 0.8.0] [--build-number 14]"
}

while (( $# > 0 )); do
    case "$1" in
        --publish-root) PUBLISH_ROOT="${2:-}"; shift 2 ;;
        --output-bundle) OUTPUT_BUNDLE="${2:-}"; shift 2 ;;
        --version) VERSION="${2:-}"; shift 2 ;;
        --build-number) BUILD_NUMBER="${2:-}"; shift 2 ;;
        --git-sha) GIT_SHA="${2:-}"; shift 2 ;;
        --built-at-utc) BUILT_AT_UTC="${2:-}"; shift 2 ;;
        --help|-h) usage; exit 0 ;;
        *) fail "Unknown argument: $1" ;;
    esac
done

[[ -d "$PUBLISH_ROOT" ]] || fail "Publish root was not found: $PUBLISH_ROOT"
[[ -x "$PUBLISH_ROOT/TorrentCoreService" ]] || fail "Published TorrentCoreService apphost was not found."
[[ -d "$PUBLISH_ROOT/TorrentCoreService.Deployment" ]] || fail "Published deployment resources were not found."
[[ -f "$PUBLISH_ROOT/appsettings.json" ]] || fail "Published appsettings.json was not found."
[[ "$OUTPUT_BUNDLE" == */TorrentCoreService.app || "$OUTPUT_BUNDLE" == TorrentCoreService.app ]] ||
    fail "Output must name TorrentCoreService.app."
[[ "$OUTPUT_BUNDLE" != "/TorrentCoreService.app" && "$OUTPUT_BUNDLE" != "$HOME/TorrentCoreService.app" ]] ||
    fail "Unsafe output bundle path: $OUTPUT_BUNDLE"
[[ "$VERSION" =~ '^[0-9]+\.[0-9]+\.[0-9]+$' ]] || fail "Invalid version: $VERSION"
[[ "$BUILD_NUMBER" =~ '^[1-9][0-9]*$' ]] || fail "Invalid build number: $BUILD_NUMBER"
[[ "$GIT_SHA" =~ '^[0-9a-f]{40}$' ]] || fail "Git SHA must be 40 lowercase hexadecimal characters."
[[ "$BUILT_AT_UTC" =~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$' ]] ||
    fail "Built-at time must use UTC ISO-8601 seconds (YYYY-MM-DDTHH:MM:SSZ)."
[[ -f "$LAUNCHER_SOURCE" ]] || fail "Launcher source was not found."
[[ -f "$ICON_SOURCE/Contents.json" ]] || fail "TorrentCore macOS icon source was not found."

for command_name in xcrun rsync iconutil plutil file lipo; do
    command -v "$command_name" >/dev/null 2>&1 || fail "Required command is unavailable: $command_name"
done

if [[ -e "$OUTPUT_BUNDLE" ]]; then
    rm -rf "$OUTPUT_BUNDLE"
fi

CONTENTS="$OUTPUT_BUNDLE/Contents"
MACOS_DIR="$CONTENTS/MacOS"
RESOURCES_DIR="$CONTENTS/Resources"
RUNTIME_DIR="$RESOURCES_DIR/Runtime"
DEPLOYMENT_DIR="$RESOURCES_DIR/Deployment"
mkdir -p "$MACOS_DIR" "$RUNTIME_DIR" "$DEPLOYMENT_DIR"

rsync -a --exclude 'TorrentCoreService.Deployment' "$PUBLISH_ROOT/" "$RUNTIME_DIR/"
rsync -a "$PUBLISH_ROOT/TorrentCoreService.Deployment/" "$DEPLOYMENT_DIR/"
chmod +x "$RUNTIME_DIR/TorrentCoreService" "$DEPLOYMENT_DIR/install.zsh"

cat > "$CONTENTS/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "https://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key><string>en</string>
    <key>CFBundleDisplayName</key><string>TorrentCoreService</string>
    <key>CFBundleExecutable</key><string>TorrentCoreService</string>
    <key>CFBundleIconFile</key><string>TorrentCoreService</string>
    <key>CFBundleIdentifier</key><string>com.conadv.torrentcore.service</string>
    <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
    <key>CFBundleName</key><string>TorrentCoreService</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleShortVersionString</key><string>$VERSION</string>
    <key>CFBundleVersion</key><string>$BUILD_NUMBER</string>
    <key>LSBackgroundOnly</key><true/>
    <key>LSMinimumSystemVersion</key><string>26.0</string>
    <key>NSLocalNetworkUsageDescription</key><string>TorrentCoreService accepts API connections from trusted devices on your local network.</string>
</dict>
</plist>
EOF

cat > "$RESOURCES_DIR/version.json" <<EOF
{
  "component": "TorrentCoreService",
  "version": "$VERSION",
  "build": "$BUILD_NUMBER",
  "gitSha": "$GIT_SHA",
  "builtAtUtc": "$BUILT_AT_UTC",
  "runtime": "osx-arm64"
}
EOF

ICONSET_DIR="$RESOURCES_DIR/TorrentCoreService.iconset"
mkdir -p "$ICONSET_DIR"
for icon_name in icon_16x16.png icon_16x16@2x.png icon_32x32.png icon_32x32@2x.png \
    icon_128x128.png icon_128x128@2x.png icon_256x256.png icon_256x256@2x.png \
    icon_512x512.png icon_512x512@2x.png; do
    cp -p "$ICON_SOURCE/$icon_name" "$ICONSET_DIR/$icon_name"
done
iconutil -c icns "$ICONSET_DIR" -o "$RESOURCES_DIR/TorrentCoreService.icns"
rm -rf "$ICONSET_DIR"

xcrun --sdk macosx clang \
    -arch arm64 \
    -mmacosx-version-min=26.0 \
    -Os \
    -fno-ident \
    "$LAUNCHER_SOURCE" \
    -framework CoreFoundation \
    -framework CoreServices \
    -o "$MACOS_DIR/TorrentCoreService"
chmod +x "$MACOS_DIR/TorrentCoreService"

plutil -lint "$CONTENTS/Info.plist" >/dev/null
[[ "$(lipo -archs "$MACOS_DIR/TorrentCoreService")" == "arm64" ]] || fail "Launcher is not Arm64-only."
print -r -- "Packaged $OUTPUT_BUNDLE"
