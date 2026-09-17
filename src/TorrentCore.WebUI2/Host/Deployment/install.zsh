#!/bin/zsh

set -euo pipefail

SCRIPT_DIR="${0:A:h}"
PLIST_TEMPLATE="$SCRIPT_DIR/com.torrentcore.webui2.plist"
TARGET_DIR="$HOME/Library/LaunchAgents"
TARGET_PLIST="$TARGET_DIR/com.torrentcore.webui2.plist"
PROGRAM_NAME="TorrentCoreWebUI2"
BUNDLE_IDENTIFIER="com.conadv.torrentcore.webui2"
LABEL="com.torrentcore.webui2"
BUNDLE_PATH="${1:-}"

if [[ "$BUNDLE_PATH" != *.app || ! -d "$BUNDLE_PATH" ]]; then
    print -ru2 -- "Usage: install.zsh <TorrentCoreWebUI2.app> [ASPNETCORE_URLS]"
    exit 2
fi

BUNDLE_PATH="${BUNDLE_PATH:A}"
PROGRAM_PATH="$BUNDLE_PATH/Contents/MacOS/$PROGRAM_NAME"
RUNTIME_DIRECTORY="$BUNDLE_PATH/Contents/Resources/Runtime"
DEFAULTS_DIRECTORY="$BUNDLE_PATH/Contents/Resources/Defaults"
WORKING_DIRECTORY="$HOME/TorrentCore/WebUI2"
ENV_FILE="$HOME/TorrentCore/Scripts/torrentcore.env"
if [[ -f "$ENV_FILE" ]]; then
    source "$ENV_FILE"
fi
: "${TORRENTCORE_ASPNETCORE_ENVIRONMENT:=Production}"
: "${TORRENTCORE_WEBUI2_URLS:=http://0.0.0.0:7510}"
ASPNETCORE_ENVIRONMENT="$TORRENTCORE_ASPNETCORE_ENVIRONMENT"
ASPNETCORE_URLS="${2:-$TORRENTCORE_WEBUI2_URLS}"
LOG_DIR="$HOME/TorrentCore/Logs"
STDOUT_PATH="$LOG_DIR/TorrentCore.WebUI2.launchd.out.log"
STDERR_PATH="$LOG_DIR/TorrentCore.WebUI2.launchd.err.log"
LAUNCHD_DOMAIN="gui/$(id -u)"

[[ -f "$PLIST_TEMPLATE" ]] || { print -ru2 -- "LaunchAgent template not found: $PLIST_TEMPLATE"; exit 1; }
[[ -x "$PROGRAM_PATH" ]] || { print -ru2 -- "TorrentCoreWebUI2 launcher not found or not executable: $PROGRAM_PATH"; exit 1; }
[[ -x "$RUNTIME_DIRECTORY/TorrentCore.WebUI2" ]] || { print -ru2 -- "TorrentCore.WebUI2 runtime helper not found or not executable."; exit 1; }
[[ ! -e "$DEFAULTS_DIRECTORY/Config" && ! -e "$RUNTIME_DIRECTORY/Config" ]] || { print -ru2 -- "Machine-local WebUI2 configuration was found in the bundle."; exit 1; }

mkdir -p "$TARGET_DIR" "$LOG_DIR" "$WORKING_DIRECTORY/Config/browser-connections"
if [[ ! -f "$WORKING_DIRECTORY/appsettings.json" ]]; then
    cp -p "$DEFAULTS_DIRECTORY/appsettings.json" "$WORKING_DIRECTORY/appsettings.json"
fi
cp -p "$BUNDLE_PATH/Contents/Resources/version.json" "$WORKING_DIRECTORY/.version.json.new"
mv -f "$WORKING_DIRECTORY/.version.json.new" "$WORKING_DIRECTORY/version.json"
"$PROGRAM_PATH" --register-bundle

sed \
    -e "s|__PROGRAM_PATH__|$PROGRAM_PATH|g" \
    -e "s|__BUNDLE_IDENTIFIER__|$BUNDLE_IDENTIFIER|g" \
    -e "s|__WORKING_DIRECTORY__|$WORKING_DIRECTORY|g" \
    -e "s|__STDOUT_PATH__|$STDOUT_PATH|g" \
    -e "s|__STDERR_PATH__|$STDERR_PATH|g" \
    -e "s|__ASPNETCORE_URLS__|$ASPNETCORE_URLS|g" \
    -e "s|__ASPNETCORE_ENVIRONMENT__|$ASPNETCORE_ENVIRONMENT|g" \
    "$PLIST_TEMPLATE" > "$TARGET_PLIST"

plutil -lint "$TARGET_PLIST" >/dev/null
launchctl bootout "$LAUNCHD_DOMAIN" "$TARGET_PLIST" 2>/dev/null || true
launchctl enable "$LAUNCHD_DOMAIN/$LABEL"
launchctl bootstrap "$LAUNCHD_DOMAIN" "$TARGET_PLIST"
launchctl kickstart -k "$LAUNCHD_DOMAIN/$LABEL"

print -r -- "Installed $LABEL"
print -r -- "Bundle: $BUNDLE_PATH"
print -r -- "Program: $PROGRAM_PATH"
print -r -- "Working directory: $WORKING_DIRECTORY"
print -r -- "ASPNETCORE_URLS: $ASPNETCORE_URLS"
