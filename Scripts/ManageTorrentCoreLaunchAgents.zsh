#!/usr/bin/env zsh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
export TORRENTCORE_SCRIPT_DIR="${SCRIPT_DIR}"
source "${SCRIPT_DIR}/lib/torrentcore-common.zsh"
tc_load_env_file

LOG_DIR="${TORRENTCORE_LAUNCH_AGENT_LOG_DIR}"
CONSOLE_LOG="${LOG_DIR}/LaunchAgents.console.log"
ERROR_LOG="${LOG_DIR}/LaunchAgents.errors.log"
TARGET_DIR="${TORRENTCORE_LAUNCH_AGENT_TARGET_DIR}"
LAUNCHD_DOMAIN="$(tc_launchd_domain)"

typeset -A LABELS
LABELS=(
  service "${TORRENTCORE_SERVICE_LAUNCH_LABEL}"
  webui "${TORRENTCORE_WEBUI_LAUNCH_LABEL}"
)

typeset -A DISPLAY_NAMES
DISPLAY_NAMES=(
  service "TorrentCore.Service"
  webui "TorrentCore.WebUI"
)

typeset -A PLISTS
PLISTS=(
  service "${TARGET_DIR}/${TORRENTCORE_SERVICE_LAUNCH_LABEL}.plist"
  webui "${TARGET_DIR}/${TORRENTCORE_WEBUI_LAUNCH_LABEL}.plist"
)

usage() {
  cat <<'EOF'
Usage:
  ManageTorrentCoreLaunchAgents.zsh <start|stop|restart> <all|service|webui>
EOF
}

log_line() {
  local message="$1"
  local line
  line="$(date '+%Y-%m-%d %H:%M:%S') ${message}"
  echo "$line" | tee -a "$CONSOLE_LOG" >/dev/null
}

run_launchctl() {
  "$@" >>"$CONSOLE_LOG" 2>>"$ERROR_LOG"
}

normalize_target() {
  local target="${1:l}"

  case "$target" in
    all)
      echo "all"
      ;;
    service|torrentcore.service)
      echo "service"
      ;;
    webui|torrentcore.webui|web)
      echo "webui"
      ;;
    *)
      return 1
      ;;
  esac
}

is_loaded() {
  local key="$1"
  launchctl print "$LAUNCHD_DOMAIN/${LABELS[$key]}" >/dev/null 2>&1
}

current_state() {
  local key="$1"
  launchctl print "$LAUNCHD_DOMAIN/${LABELS[$key]}" 2>/dev/null |
    awk -F'= ' '/^\tstate = / {print $2; exit}'
}

current_pid() {
  local key="$1"
  launchctl print "$LAUNCHD_DOMAIN/${LABELS[$key]}" 2>/dev/null |
    awk -F'= ' '/^\tpid = / {print $2; exit}'
}

last_exit_code() {
  local key="$1"
  launchctl print "$LAUNCHD_DOMAIN/${LABELS[$key]}" 2>/dev/null |
    awk -F'= ' '/^\tlast exit code = / {print $2; exit}'
}

wait_for_running() {
  local key="$1"
  local display_name="${DISPLAY_NAMES[$key]}"
  local state=""
  local pid=""
  local exit_code=""
  local attempt

  for attempt in {1..80}; do
    state="$(current_state "$key")"
    pid="$(current_pid "$key")"
    if [[ "$state" == "running" && -n "$pid" ]]; then
      return 0
    fi

    sleep 0.5
  done

  exit_code="$(last_exit_code "$key")"
  log_line "${display_name} did not reach running state. Last state: ${state:-unavailable}; pid: ${pid:-none}; last exit: ${exit_code:-unavailable}"
  return 1
}

start_service() {
  local key="$1"
  local label="${LABELS[$key]}"
  local plist="${PLISTS[$key]}"
  local display_name="${DISPLAY_NAMES[$key]}"

  if [[ ! -f "$plist" ]]; then
    log_line "Start failed for ${display_name}: plist not found at ${plist}"
    return 1
  fi

  log_line "Starting ${display_name}"
  run_launchctl launchctl enable "$LAUNCHD_DOMAIN/$label" || true

  if ! is_loaded "$key"; then
    run_launchctl launchctl bootstrap "$LAUNCHD_DOMAIN" "$plist"
  fi

  run_launchctl launchctl kickstart -k "$LAUNCHD_DOMAIN/$label"
  wait_for_running "$key"
  log_line "${display_name} start complete"
}

stop_service() {
  local key="$1"
  local label="${LABELS[$key]}"
  local plist="${PLISTS[$key]}"
  local display_name="${DISPLAY_NAMES[$key]}"

  log_line "Stopping ${display_name}"
  run_launchctl launchctl bootout "$LAUNCHD_DOMAIN" "$plist" || true
  run_launchctl launchctl disable "$LAUNCHD_DOMAIN/$label" || true
  log_line "${display_name} stop complete"
}

restart_service() {
  local key="$1"
  local label="${LABELS[$key]}"
  local plist="${PLISTS[$key]}"
  local display_name="${DISPLAY_NAMES[$key]}"

  if [[ ! -f "$plist" ]]; then
    log_line "Restart failed for ${display_name}: plist not found at ${plist}"
    return 1
  fi

  log_line "Restarting ${display_name}"
  run_launchctl launchctl enable "$LAUNCHD_DOMAIN/$label" || true

  if ! is_loaded "$key"; then
    run_launchctl launchctl bootstrap "$LAUNCHD_DOMAIN" "$plist"
  fi

  run_launchctl launchctl kickstart -k "$LAUNCHD_DOMAIN/$label"
  wait_for_running "$key"
  log_line "${display_name} restart complete"
}

if [[ $# -ne 2 ]]; then
  usage
  exit 1
fi

mkdir -p "$LOG_DIR"

action="${1:l}"
target="$(normalize_target "$2")" || {
  usage
  exit 1
}

typeset -a service_keys
if [[ "$target" == "all" ]]; then
  case "$action" in
    stop)
      service_keys=(webui service)
      ;;
    start|restart)
      service_keys=(service webui)
      ;;
    *)
      service_keys=(service webui)
      ;;
  esac
else
  service_keys=("$target")
fi

for key in "${service_keys[@]}"; do
  case "$action" in
    start)
      start_service "$key"
      ;;
    stop)
      stop_service "$key"
      ;;
    restart)
      restart_service "$key"
      ;;
    *)
      usage
      exit 1
      ;;
  esac
done
