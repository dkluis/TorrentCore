#!/bin/zsh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
export TORRENTCORE_SCRIPT_DIR="${SCRIPT_DIR}"
source "${SCRIPT_DIR}/lib/torrentcore-common.zsh"
tc_load_env_file

PID_FILE="${TORRENTCORE_RUN_DIR}/webui.pid"
APP_DIR="${TORRENTCORE_BASE_DIR}/WebUI"
WEBUI_DLL="${TORRENTCORE_WEBUI_DLL:-}"
PID_VALUE=""
COMMAND_LINE=""

if [[ -z "${WEBUI_DLL}" && -f "${PID_FILE}" ]]; then
  PID_VALUE="$(<"${PID_FILE}")"
  if [[ -n "${PID_VALUE}" ]]; then
    COMMAND_LINE="$(ps -p "${PID_VALUE}" -o command= 2>/dev/null || true)"
    if [[ "${COMMAND_LINE}" == *"TorrentCore.WebUI.dll"* ]]; then
      WEBUI_DLL="TorrentCore.WebUI.dll"
    elif [[ "${COMMAND_LINE}" == *"TorrentCore.Web.dll"* ]]; then
      WEBUI_DLL="TorrentCore.Web.dll"
    fi
  fi
fi

if [[ -z "${WEBUI_DLL}" ]]; then
  if [[ -f "${APP_DIR}/TorrentCore.WebUI.dll" ]]; then
    WEBUI_DLL="TorrentCore.WebUI.dll"
  elif [[ -f "${APP_DIR}/TorrentCore.Web.dll" ]]; then
    WEBUI_DLL="TorrentCore.Web.dll"
  else
    WEBUI_DLL="TorrentCore.WebUI.dll"
  fi
fi

tc_stop_dotnet_app \
  "TorrentCore.WebUI" \
  "${WEBUI_DLL}" \
  "${PID_FILE}"
