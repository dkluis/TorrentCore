#!/bin/zsh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
export TORRENTCORE_SCRIPT_DIR="${SCRIPT_DIR}"
source "${SCRIPT_DIR}/lib/torrentcore-common.zsh"
tc_load_env_file

APP_DIR="${TORRENTCORE_BASE_DIR}/WebUI"
PID_FILE="${TORRENTCORE_RUN_DIR}/webui.pid"
LOG_FILE="${TORRENTCORE_LOG_DIR}/webui.log"

tc_start_dotnet_app \
  "TorrentCore.WebUI" \
  "${APP_DIR}" \
  "TorrentCore.Web.dll" \
  "${PID_FILE}" \
  "${LOG_FILE}" \
  "ASPNETCORE_ENVIRONMENT=${TORRENTCORE_ASPNETCORE_ENVIRONMENT}" \
  "ASPNETCORE_URLS=${TORRENTCORE_WEBUI_URLS}" \
  "TorrentCoreService__BaseUrl=${TORRENTCORE_WEBUI_SERVICE_BASE_URL}"
