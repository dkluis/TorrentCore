#!/usr/bin/env zsh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
export TORRENTCORE_SCRIPT_DIR="${SCRIPT_DIR}"
source "${SCRIPT_DIR}/lib/torrentcore-common.zsh"
tc_load_env_file

LAUNCHD_DOMAIN="$(tc_launchd_domain)"

for label in "${TORRENTCORE_SERVICE_LAUNCH_LABEL}" "${TORRENTCORE_WEBUI_LAUNCH_LABEL}"; do
  echo "=== $label ==="
  if ! launchctl print "$LAUNCHD_DOMAIN/$label" 2>/dev/null | rg "state =|pid =|last exit code =|program =|path ="; then
    echo "not loaded"
  fi
done
