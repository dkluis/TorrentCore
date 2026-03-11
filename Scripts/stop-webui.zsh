#!/bin/zsh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
export TORRENTCORE_SCRIPT_DIR="${SCRIPT_DIR}"
source "${SCRIPT_DIR}/lib/torrentcore-common.zsh"
tc_load_env_file

PID_FILE="${TORRENTCORE_RUN_DIR}/webui.pid"

tc_stop_dotnet_app \
  "TorrentCore.WebUI" \
  "TorrentCore.Web.dll" \
  "${PID_FILE}"
