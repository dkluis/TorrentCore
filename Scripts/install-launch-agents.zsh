#!/usr/bin/env zsh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
export TORRENTCORE_SCRIPT_DIR="${SCRIPT_DIR}"
source "${SCRIPT_DIR}/lib/torrentcore-common.zsh"
tc_load_env_file

SERVICE_TEMPLATE="${SCRIPT_DIR}/com.torrentcore.service.plist"
WEBUI_TEMPLATE="${SCRIPT_DIR}/com.torrentcore.webui.plist"
TARGET_DIR="${TORRENTCORE_LAUNCH_AGENT_TARGET_DIR}"
LOG_DIR="${TORRENTCORE_LAUNCH_AGENT_LOG_DIR}"
LAUNCHD_DOMAIN="$(tc_launchd_domain)"

service_program_path="$(tc_service_program_path)"
webui_program_path="$(tc_webui_program_path)"
service_working_directory="${service_program_path:h}"
webui_working_directory="${webui_program_path:h}"
service_target_plist="${TARGET_DIR}/${TORRENTCORE_SERVICE_LAUNCH_LABEL}.plist"
webui_target_plist="${TARGET_DIR}/${TORRENTCORE_WEBUI_LAUNCH_LABEL}.plist"

usage() {
  cat <<'EOF'
Usage:
  install-launch-agents.zsh [all|service|webui]
EOF
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

xml_escape() {
  local value="$1"
  value="${value//&/&amp;}"
  value="${value//</&lt;}"
  value="${value//>/&gt;}"
  print -r -- "$value"
}

render_env_block() {
  local app_kind="$1"
  local service_urls_escaped
  local webui_urls_escaped
  local webui_service_base_url_escaped
  local aspnet_env_escaped

  service_urls_escaped="$(xml_escape "${TORRENTCORE_SERVICE_URLS}")"
  webui_urls_escaped="$(xml_escape "${TORRENTCORE_WEBUI_URLS}")"
  webui_service_base_url_escaped="$(xml_escape "${TORRENTCORE_WEBUI_SERVICE_BASE_URL}")"
  aspnet_env_escaped="$(xml_escape "${TORRENTCORE_ASPNETCORE_ENVIRONMENT}")"

  if [[ "${app_kind}" == "service" ]]; then
    cat <<EOF
        <dict>
            <key>ASPNETCORE_ENVIRONMENT</key>
            <string>${aspnet_env_escaped}</string>
            <key>ASPNETCORE_URLS</key>
            <string>${service_urls_escaped}</string>
        </dict>
EOF
    return
  fi

  cat <<EOF
        <dict>
            <key>ASPNETCORE_ENVIRONMENT</key>
            <string>${aspnet_env_escaped}</string>
            <key>ASPNETCORE_URLS</key>
            <string>${webui_urls_escaped}</string>
            <key>TorrentCoreService__BaseUrl</key>
            <string>${webui_service_base_url_escaped}</string>
        </dict>
EOF
}

render_plist() {
  local template_path="$1"
  local target_plist="$2"
  local label="$3"
  local program_path="$4"
  local working_directory="$5"
  local stdout_path="$6"
  local stderr_path="$7"
  local app_kind="$8"
  local env_block_file

  env_block_file="$(mktemp)"
  render_env_block "${app_kind}" > "${env_block_file}"

  sed \
    -e "s|__LABEL__|${label}|g" \
    -e "s|__PROGRAM_PATH__|${program_path}|g" \
    -e "s|__WORKING_DIRECTORY__|${working_directory}|g" \
    -e "s|__STDOUT_PATH__|${stdout_path}|g" \
    -e "s|__STDERR_PATH__|${stderr_path}|g" \
    -e "/__ENVIRONMENT_VARIABLES__/{
r ${env_block_file}
d
}" \
    "${template_path}" > "${target_plist}"

  rm -f "${env_block_file}"
}

bootstrap_agent() {
  local target_plist="$1"
  local label="$2"

  launchctl bootout "${LAUNCHD_DOMAIN}" "${target_plist}" 2>/dev/null || true
  launchctl enable "${LAUNCHD_DOMAIN}/${label}"
  launchctl bootstrap "${LAUNCHD_DOMAIN}" "${target_plist}"
  launchctl kickstart -k "${LAUNCHD_DOMAIN}/${label}"
}

target="${1:-all}"
target="$(normalize_target "${target}")" || {
  usage
  exit 1
}

tc_require_file "${SERVICE_TEMPLATE}"
tc_require_file "${WEBUI_TEMPLATE}"
tc_require_command plutil

mkdir -p "${TARGET_DIR}" "${LOG_DIR}"

if [[ "${target}" == "all" || "${target}" == "service" ]]; then
  if [[ ! -x "${service_program_path}" ]]; then
    tc_log_error "TorrentCore.Service executable not found or not executable: ${service_program_path}"
    exit 1
  fi

  render_plist \
    "${SERVICE_TEMPLATE}" \
    "${service_target_plist}" \
    "${TORRENTCORE_SERVICE_LAUNCH_LABEL}" \
    "${service_program_path}" \
    "${service_working_directory}" \
    "${LOG_DIR}/TorrentCore.Service.launchd.out.log" \
    "${LOG_DIR}/TorrentCore.Service.launchd.err.log" \
    "service"

  plutil -lint "${service_target_plist}" >/dev/null
  bootstrap_agent "${service_target_plist}" "${TORRENTCORE_SERVICE_LAUNCH_LABEL}"
  print -- "Installed ${TORRENTCORE_SERVICE_LAUNCH_LABEL}"
  print -- "Service plist: ${service_target_plist}"
fi

if [[ "${target}" == "all" || "${target}" == "webui" ]]; then
  if [[ ! -x "${webui_program_path}" ]]; then
    tc_log_error "TorrentCore.WebUI executable not found or not executable: ${webui_program_path}"
    exit 1
  fi

  render_plist \
    "${WEBUI_TEMPLATE}" \
    "${webui_target_plist}" \
    "${TORRENTCORE_WEBUI_LAUNCH_LABEL}" \
    "${webui_program_path}" \
    "${webui_working_directory}" \
    "${LOG_DIR}/TorrentCore.WebUI.launchd.out.log" \
    "${LOG_DIR}/TorrentCore.WebUI.launchd.err.log" \
    "webui"

  plutil -lint "${webui_target_plist}" >/dev/null
  bootstrap_agent "${webui_target_plist}" "${TORRENTCORE_WEBUI_LAUNCH_LABEL}"
  print -- "Installed ${TORRENTCORE_WEBUI_LAUNCH_LABEL}"
  print -- "WebUI plist: ${webui_target_plist}"
fi

print -- "Logs: ${LOG_DIR}"
