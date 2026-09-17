#!/bin/zsh

set -euo pipefail

BUNDLE_PATH="${1:-}"
[[ -d "$BUNDLE_PATH" ]] || { print -ru2 -- "Usage: verify-macos-webui2-static-assets.zsh <TorrentCoreWebUI2.app>"; exit 2; }
RUNTIME="$BUNDLE_PATH/Contents/Resources/Runtime"
WWWROOT="$RUNTIME/wwwroot"
[[ -f "$WWWROOT/index.html" && -f "$WWWROOT/_framework/dotnet.js" ]] || {
    print -ru2 -- "The bundled WebAssembly entry page or runtime boot script is missing."
    exit 1
}
grep -q '"mainAssemblyName": "TorrentCore.WebUI2.Client"' "$WWWROOT/_framework/dotnet.js" || {
    print -ru2 -- "The embedded WebAssembly boot configuration is missing."
    exit 1
}

WORKING_DIRECTORY="$(mktemp -d /private/tmp/TorrentCoreWebUI2-static-working.XXXXXX)"
RESPONSE="$(mktemp /private/tmp/TorrentCoreWebUI2-static-response.XXXXXX)"
PORT="$(python3 - <<'PY'
import socket
with socket.socket() as listener:
    listener.bind(("127.0.0.1", 0))
    print(listener.getsockname()[1])
PY
)"
PID=""
cleanup() {
    [[ -z "$PID" ]] || kill "$PID" 2>/dev/null || true
    rm -rf "$WORKING_DIRECTORY" "$RESPONSE"
}
trap cleanup EXIT

TORRENTCORE_WEBUI2_WORKING_DIRECTORY="$WORKING_DIRECTORY" ASPNETCORE_URLS="http://127.0.0.1:$PORT" "$BUNDLE_PATH/Contents/MacOS/TorrentCoreWebUI2" >"$WORKING_DIRECTORY/stdout.log" 2>"$WORKING_DIRECTORY/stderr.log" &
PID=$!

for route in index.html css/app.css TorrentCore.WebUI2.Client.styles.css js/browser.js js/gridLayout.js _framework/blazor.webassembly.js _framework/dotnet.js _content/MudBlazor/MudBlazor.min.css _content/MudBlazor/MudBlazor.min.js; do
    expected="$WWWROOT/$route"
    [[ -f "$expected" ]] || { print -ru2 -- "Bundled asset is missing: $route"; exit 1; }
    served=false
    for _ in {1..45}; do
        if curl --silent --fail --max-time 2 "http://127.0.0.1:$PORT/$route" --output "$RESPONSE"; then
            served=true
            break
        fi
        kill -0 "$PID" 2>/dev/null || { print -ru2 -- "Bundled WebUI2 exited before serving $route."; exit 1; }
        sleep 1
    done
    [[ "$served" == true ]] || { print -ru2 -- "Bundled WebUI2 did not serve $route."; exit 1; }
    cmp -s "$expected" "$RESPONSE" || { print -ru2 -- "Bundled response differed from $route."; exit 1; }
done

print -r -- "Verified bundled WebAssembly entry, embedded boot configuration, JavaScript, and CSS from an empty external working directory."
