#!/bin/zsh

set -euo pipefail

SCRIPT_DIR="${0:A:h}"
LAUNCHER_SOURCE="$SCRIPT_DIR/torrentcore-webui-launcher.c"
PUBLISH_ROOT=""
OUTPUT_BUNDLE=""
VERSION="0.8.0"
BUILD_NUMBER="15"
GIT_SHA=""
BUILT_AT_UTC=""

fail() { print -ru2 -- "[TorrentCoreWebUI app] ERROR: $*"; exit 1; }

while (( $# > 0 )); do
    case "$1" in
        --publish-root) PUBLISH_ROOT="${2:-}"; shift 2 ;;
        --output-bundle) OUTPUT_BUNDLE="${2:-}"; shift 2 ;;
        --version) VERSION="${2:-}"; shift 2 ;;
        --build-number) BUILD_NUMBER="${2:-}"; shift 2 ;;
        --git-sha) GIT_SHA="${2:-}"; shift 2 ;;
        --built-at-utc) BUILT_AT_UTC="${2:-}"; shift 2 ;;
        *) fail "Unknown argument: $1" ;;
    esac
done

[[ -d "$PUBLISH_ROOT" ]] || fail "Publish root was not found: $PUBLISH_ROOT"
[[ -x "$PUBLISH_ROOT/TorrentCore.WebUI" ]] || fail "Published TorrentCore.WebUI apphost was not found."
[[ -d "$PUBLISH_ROOT/TorrentCoreWebUI.Deployment" ]] || fail "Published deployment resources were not found."
[[ -f "$PUBLISH_ROOT/appsettings.json" ]] || fail "Published appsettings.json was not found."
[[ "$OUTPUT_BUNDLE" == */TorrentCoreWebUI.app || "$OUTPUT_BUNDLE" == TorrentCoreWebUI.app ]] || fail "Output must name TorrentCoreWebUI.app."
[[ "$OUTPUT_BUNDLE" != "/TorrentCoreWebUI.app" && "$OUTPUT_BUNDLE" != "$HOME/TorrentCoreWebUI.app" && "$OUTPUT_BUNDLE" != "$PUBLISH_ROOT" ]] || fail "Unsafe output bundle path: $OUTPUT_BUNDLE"
[[ "$VERSION" =~ '^[0-9]+\.[0-9]+\.[0-9]+$' ]] || fail "Invalid version: $VERSION"
[[ "$BUILD_NUMBER" =~ '^[1-9][0-9]*$' ]] || fail "Invalid build number: $BUILD_NUMBER"
[[ "$GIT_SHA" =~ '^[0-9a-f]{40}$' ]] || fail "Git SHA must be 40 lowercase hexadecimal characters."
[[ "$BUILT_AT_UTC" =~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$' ]] || fail "Invalid built-at time."
[[ ! -e "$PUBLISH_ROOT/Config/service-connection.json" ]] || fail "Machine-local service connection exists in publish output."

for command_name in xcrun rsync plutil lipo; do command -v "$command_name" >/dev/null || fail "Required command is unavailable: $command_name"; done
[[ ! -e "$OUTPUT_BUNDLE" ]] || rm -rf "$OUTPUT_BUNDLE"
CONTENTS="$OUTPUT_BUNDLE/Contents"
MACOS_DIR="$CONTENTS/MacOS"
RESOURCES_DIR="$CONTENTS/Resources"
RUNTIME_DIR="$RESOURCES_DIR/Runtime"
DEFAULTS_DIR="$RESOURCES_DIR/Defaults"
DEPLOYMENT_DIR="$RESOURCES_DIR/Deployment"
mkdir -p "$MACOS_DIR" "$RUNTIME_DIR" "$DEFAULTS_DIR" "$DEPLOYMENT_DIR"

rsync -a --exclude 'appsettings*.json' --exclude 'TorrentCoreWebUI.Deployment' --exclude 'Config/service-connection.json' "$PUBLISH_ROOT/" "$RUNTIME_DIR/"
for settings_file in "$PUBLISH_ROOT"/appsettings*.json(N); do cp -p "$settings_file" "$DEFAULTS_DIR/${settings_file:t}"; done
rsync -a --exclude 'service-connection.json' "$PUBLISH_ROOT/TorrentCoreWebUI.Deployment/" "$DEPLOYMENT_DIR/"
chmod +x "$RUNTIME_DIR/TorrentCore.WebUI" "$DEPLOYMENT_DIR/install.zsh"

cat > "$CONTENTS/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "https://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleDevelopmentRegion</key><string>en</string>
<key>CFBundleDisplayName</key><string>TorrentCoreWebUI</string>
<key>CFBundleExecutable</key><string>TorrentCoreWebUI</string>
<key>CFBundleIdentifier</key><string>com.conadv.torrentcore.webui</string>
<key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
<key>CFBundleName</key><string>TorrentCoreWebUI</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>$VERSION</string>
<key>CFBundleVersion</key><string>$BUILD_NUMBER</string>
<key>LSBackgroundOnly</key><true/>
<key>LSMinimumSystemVersion</key><string>26.0</string>
<key>NSLocalNetworkUsageDescription</key><string>TorrentCoreWebUI connects to TorrentCoreService and accepts browser connections on your local network.</string>
</dict></plist>
EOF

cat > "$RESOURCES_DIR/version.json" <<EOF
{
  "component": "TorrentCoreWebUI",
  "version": "$VERSION",
  "build": "$BUILD_NUMBER",
  "gitSha": "$GIT_SHA",
  "builtAtUtc": "$BUILT_AT_UTC",
  "runtime": "osx-arm64"
}
EOF

xcrun --sdk macosx clang -arch arm64 -mmacosx-version-min=26.0 -Os -fno-ident "$LAUNCHER_SOURCE" -framework CoreFoundation -framework CoreServices -o "$MACOS_DIR/TorrentCoreWebUI"
chmod +x "$MACOS_DIR/TorrentCoreWebUI"
plutil -lint "$CONTENTS/Info.plist" >/dev/null
[[ "$(lipo -archs "$MACOS_DIR/TorrentCoreWebUI")" == "arm64" ]] || fail "Launcher is not Arm64-only."
if find "$OUTPUT_BUNDLE" -path '*/Config/service-connection.json' -print -quit | grep -q .; then fail "Machine-local service connection was staged in app bundle."; fi
print -r -- "Packaged $OUTPUT_BUNDLE"
