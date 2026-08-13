#!/bin/zsh

set -euo pipefail
BUNDLE_PATH="${1:-}"
[[ -d "$BUNDLE_PATH" ]] || { print -ru2 -- "Usage: verify-macos-webui-static-assets.zsh <TorrentCoreWebUI.app>"; exit 2; }
RUNTIME="$BUNDLE_PATH/Contents/Resources/Runtime"
MANIFEST="$RUNTIME/TorrentCore.WebUI.staticwebassets.endpoints.json"
EXPECTED="$RUNTIME/wwwroot/app.css"
[[ -f "$MANIFEST" && -f "$EXPECTED" ]] || { print -ru2 -- "Static asset manifest or app.css is missing."; exit 1; }
VALUES=("${(@f)$(python3 - "$MANIFEST" <<'PY'
import json, sys
value=json.load(open(sys.argv[1], encoding="utf-8-sig"))
matches=[]
for endpoint in value.get("Endpoints", []):
    route=str(endpoint.get("Route", ""))
    if route.startswith("app.") and route != "app.css" and route.endswith(".css") and not endpoint.get("Selectors"):
        length=next((p.get("Value") for p in endpoint.get("ResponseHeaders", []) if p.get("Name")=="Content-Length"), "")
        matches.append((route, str(length)))
if len(matches) != 1:
    raise SystemExit(f"expected one fingerprinted app.css endpoint, found {len(matches)}")
print(matches[0][0]); print(matches[0][1])
PY
)}")
ROUTE="${VALUES[1]:-}"
EXPECTED_LENGTH="${VALUES[2]:-}"
[[ -n "$ROUTE" && "$EXPECTED_LENGTH" == <-> ]] || { print -ru2 -- "Static asset manifest values are invalid."; exit 1; }
WORKING_DIRECTORY="$(mktemp -d /private/tmp/TorrentCoreWebUI-static-working.XXXXXX)"
RESPONSE="$(mktemp /private/tmp/TorrentCoreWebUI-static-response.XXXXXX)"
PORT="$(python3 - <<'PY'
import socket
s=socket.socket(); s.bind(("127.0.0.1",0)); print(s.getsockname()[1]); s.close()
PY
)"
PID=""
cleanup() { [[ -z "$PID" ]] || kill "$PID" 2>/dev/null || true; rm -rf "$WORKING_DIRECTORY" "$RESPONSE"; }
trap cleanup EXIT
TORRENTCORE_WEBUI_WORKING_DIRECTORY="$WORKING_DIRECTORY" ASPNETCORE_URLS="http://127.0.0.1:$PORT" TorrentCoreService__BaseUrl="http://127.0.0.1:1/" "$BUNDLE_PATH/Contents/MacOS/TorrentCoreWebUI" >"$WORKING_DIRECTORY/stdout.log" 2>"$WORKING_DIRECTORY/stderr.log" &
PID=$!
for _ in {1..45}; do
    if curl --silent --show-error --fail "http://127.0.0.1:$PORT/$ROUTE" --output "$RESPONSE"; then break; fi
    kill -0 "$PID" 2>/dev/null || { print -ru2 -- "Bundled WebUI exited before serving assets."; exit 1; }
    sleep 1
done
[[ -s "$RESPONSE" ]] || { print -ru2 -- "Bundled static response was empty."; exit 1; }
[[ "$(wc -c < "$RESPONSE" | tr -d ' ')" == "$EXPECTED_LENGTH" ]] || { print -ru2 -- "Bundled static response length differed from manifest."; exit 1; }
cmp "$EXPECTED" "$RESPONSE" || { print -ru2 -- "Bundled static response differed from app.css."; exit 1; }
print -r -- "Verified bundled WebUI static route /$ROUTE from an empty external working directory."
