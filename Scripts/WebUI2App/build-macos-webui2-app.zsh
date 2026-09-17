#!/bin/zsh

set -euo pipefail
SCRIPT_DIR="${0:A:h}"
REPO_ROOT="${SCRIPT_DIR:h:h}"
OUTPUT_BUNDLE=""
SIGNING_IDENTITY=""
while (( $# > 0 )); do
    case "$1" in
        --output-bundle) OUTPUT_BUNDLE="${2:-}"; shift 2 ;;
        --signing-identity) SIGNING_IDENTITY="${2:-}"; shift 2 ;;
        *) print -ru2 -- "Unknown argument: $1"; exit 2 ;;
    esac
done
[[ -n "$OUTPUT_BUNDLE" ]] || { print -ru2 -- "--output-bundle is required."; exit 2; }
WORK_DIR="$(mktemp -d /private/tmp/TorrentCoreWebUI2-app.XXXXXX)"
trap 'rm -rf "$WORK_DIR"' EXIT
PUBLISH_ROOT="$WORK_DIR/publish"
GIT_SHA="$(git -C "$REPO_ROOT" rev-parse HEAD)"
BUILT_AT_UTC="$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
dotnet publish "$REPO_ROOT/src/TorrentCore.WebUI2/Host/TorrentCore.WebUI2.Host.csproj" --configuration Release --runtime osx-arm64 --self-contained false --disable-build-servers --maxcpucount:1 --output "$PUBLISH_ROOT"
[[ ! -e "$PUBLISH_ROOT/Config" ]] || { print -ru2 -- "Machine-local WebUI2 configuration leaked into publish output."; exit 1; }
"$SCRIPT_DIR/package-macos-webui2-app.zsh" --publish-root "$PUBLISH_ROOT" --output-bundle "$OUTPUT_BUNDLE" --version 0.8.0 --build-number 15 --git-sha "$GIT_SHA" --built-at-utc "$BUILT_AT_UTC"
if [[ -n "$SIGNING_IDENTITY" ]]; then
    "$SCRIPT_DIR/sign-macos-webui2-app.zsh" --bundle "$OUTPUT_BUNDLE" --signing-identity "$SIGNING_IDENTITY"
    "$SCRIPT_DIR/verify-macos-webui2-app.zsh" --bundle "$OUTPUT_BUNDLE" --require-signed
else
    "$SCRIPT_DIR/verify-macos-webui2-app.zsh" --bundle "$OUTPUT_BUNDLE"
fi
