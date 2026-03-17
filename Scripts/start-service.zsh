#!/bin/zsh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
export TORRENTCORE_SCRIPT_DIR="${SCRIPT_DIR}"
source "${SCRIPT_DIR}/lib/torrentcore-common.zsh"
tc_load_env_file
tc_log_service_runtime_configuration

APP_DIR="${TORRENTCORE_BASE_DIR}/Service"
PID_FILE="${TORRENTCORE_RUN_DIR}/service.pid"
LOG_FILE="${TORRENTCORE_LOG_DIR}/service.log"

tc_start_dotnet_app \
  "TorrentCore.Service" \
  "${APP_DIR}" \
  "TorrentCore.Service.dll" \
  "${PID_FILE}" \
  "${LOG_FILE}" \
  "ASPNETCORE_ENVIRONMENT=${TORRENTCORE_ASPNETCORE_ENVIRONMENT}" \
  "ASPNETCORE_URLS=${TORRENTCORE_SERVICE_URLS}"
