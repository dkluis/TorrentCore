#!/bin/zsh

setopt local_options no_unset pipe_fail

if [[ -z "${TORRENTCORE_SCRIPT_DIR:-}" ]]; then
  print -u2 -- "TORRENTCORE_SCRIPT_DIR must be set before sourcing torrentcore-common.zsh"
  return 1
fi

: "${TORRENTCORE_BASE_DIR:=$(cd "${TORRENTCORE_SCRIPT_DIR}/.." && pwd)}"
: "${TORRENTCORE_RUN_DIR:=${TORRENTCORE_SCRIPT_DIR}/run}"
: "${TORRENTCORE_LOG_DIR:=${TORRENTCORE_SCRIPT_DIR}/logs}"
: "${TORRENTCORE_DEPLOY_BASE:=/Volumes/HD-Boot-CA-Server/Users/dick/TorrentCore}"
: "${TORRENTCORE_DEPLOY_BASE_INTEL:=/Volumes/HD-Boot-CA-Server/Users/dick/TorrentCore}"
: "${TORRENTCORE_DEPLOY_BASE_ARM:=${HOME}/TorrentCore}"
: "${TORRENTCORE_PUBLISH_CONFIGURATION:=Release}"
: "${TORRENTCORE_PUBLISH_RUNTIME:=osx-x64}"
: "${TORRENTCORE_PUBLISH_RUNTIME_INTEL:=osx-x64}"
: "${TORRENTCORE_PUBLISH_RUNTIME_ARM:=osx-arm64}"
: "${TORRENTCORE_PUBLISH_SELF_CONTAINED:=false}"
: "${TORRENTCORE_ASPNETCORE_ENVIRONMENT:=Production}"
: "${TORRENTCORE_SERVICE_URLS:=http://127.0.0.1:7033}"
: "${TORRENTCORE_WEBUI_URLS:=http://127.0.0.1:7053}"
: "${TORRENTCORE_WEBUI_SERVICE_BASE_URL:=http://127.0.0.1:7033/}"
: "${TORRENTCORE_AVALONIA_PUBLISH_TARGET:=}"
: "${TORRENTCORE_AVALONIA_APP_TARGET:=}"
: "${TORRENTCORE_AVALONIA_APP_MIRROR_TARGET:=}"
: "${TORRENTCORE_AVALONIA_SYSTEM_APPLICATIONS_TARGET:=/Applications}"
: "${TORRENTCORE_AVALONIA_SYSTEM_APPLICATIONS_MIRROR:=auto}"
: "${TORRENTCORE_AVALONIA_BUNDLE_NAME:=TorrentCore}"
: "${TORRENTCORE_AVALONIA_DISPLAY_NAME:=TorrentCore Control Center}"
: "${TORRENTCORE_AVALONIA_BUNDLE_IDENTIFIER:=com.torrentcore.controlcenter}"
: "${TORRENTCORE_AVALONIA_EXECUTABLE_NAME:=TorrentCore.Avalonia}"
: "${TORRENTCORE_AVALONIA_ICON_PATH:=}"
: "${TORRENTCORE_AVALONIA_ICON_NAME:=TorrentCore.icns}"
: "${TORRENTCORE_ARTIFACT_SEGMENT:=intel}"
: "${TORRENTCORE_LAUNCH_AGENT_TARGET_DIR:=${HOME}/Library/LaunchAgents}"
: "${TORRENTCORE_LAUNCH_AGENT_LOG_DIR:=${HOME}/TorrentCore/Logs}"
: "${TORRENTCORE_SERVICE_LAUNCH_LABEL:=com.torrentcore.service}"
: "${TORRENTCORE_WEBUI_LAUNCH_LABEL:=com.torrentcore.webui}"

tc_timestamp() {
  date '+%Y-%m-%d %H:%M:%S'
}

tc_log() {
  local level="$1"
  shift
  print -- "[$(tc_timestamp)] [$level] $*"
}

tc_log_info() {
  tc_log "INFO" "$@"
}

tc_log_warn() {
  tc_log "WARN" "$@"
}

tc_log_error() {
  tc_log "ERROR" "$@" >&2
}

tc_load_env_file() {
  local env_file="${TORRENTCORE_ENV_FILE:-${TORRENTCORE_SCRIPT_DIR}/torrentcore.env}"
  export TORRENTCORE_ENV_FILE_RESOLVED="${env_file}"
  export TORRENTCORE_ENV_FILE_PRESENT=false

  if [[ -f "${env_file}" ]]; then
    export TORRENTCORE_ENV_FILE_PRESENT=true
    # shellcheck disable=SC1090
    source "${env_file}"
  fi
}

tc_is_loopback_url() {
  local url="$1"

  [[ "${url}" == http://127.0.0.1* || "${url}" == https://127.0.0.1* || "${url}" == http://localhost* || "${url}" == https://localhost* ]]
}

tc_path_is_within_home() {
  local path="$1"
  local normalized_home="${HOME%/}"

  [[ "${path}" == "${normalized_home}" || "${path}" == "${normalized_home}/"* ]]
}

tc_log_runtime_override_status() {
  local app_name="$1"

  if [[ "${TORRENTCORE_ENV_FILE_PRESENT:-false}" == true ]]; then
    tc_log_info "${app_name} runtime overrides loaded from ${TORRENTCORE_ENV_FILE_RESOLVED}."
    return
  fi

  tc_log_warn "${app_name} is using built-in script defaults because ${TORRENTCORE_ENV_FILE_RESOLVED} was not found."
  tc_log_warn "Copy torrentcore.env.example to torrentcore.env on each target host so network bindings are explicit."
}

tc_log_service_runtime_configuration() {
  tc_log_runtime_override_status "TorrentCore.Service"
  tc_log_info "TorrentCore.Service binding URLs: ${TORRENTCORE_SERVICE_URLS}"

  if tc_is_loopback_url "${TORRENTCORE_SERVICE_URLS}"; then
    tc_log_info "TorrentCore.Service is bound to loopback only. Remote API and remote Avalonia access are disabled."
  fi
}

tc_log_webui_runtime_configuration() {
  tc_log_runtime_override_status "TorrentCore.WebUI"
  tc_log_info "TorrentCore.WebUI binding URLs: ${TORRENTCORE_WEBUI_URLS}"
  tc_log_info "TorrentCore.WebUI service base URL: ${TORRENTCORE_WEBUI_SERVICE_BASE_URL}"

  if tc_is_loopback_url "${TORRENTCORE_WEBUI_URLS}"; then
    tc_log_warn "TorrentCore.WebUI is bound to loopback only. Other machines will not be able to open the Web UI."
    tc_log_warn "For LAN browser access, set TORRENTCORE_WEBUI_URLS=http://0.0.0.0:7053 in torrentcore.env and restart the Web UI."
  fi
}

tc_launchd_domain() {
  print -- "gui/$(id -u)"
}

tc_service_program_path() {
  print -- "${TORRENTCORE_DEPLOY_BASE}/Service/TorrentCore.Service"
}

tc_webui_program_path() {
  print -- "${TORRENTCORE_DEPLOY_BASE}/WebUI/TorrentCore.WebUI"
}

tc_warn_if_target_env_file_missing() {
  local target_env_file="${TORRENTCORE_DEPLOY_BASE}/Scripts/torrentcore.env"

  if [[ -f "${target_env_file}" ]]; then
    return
  fi

  tc_log_warn "No target runtime override file exists at ${target_env_file}."
  tc_log_warn "Copy ${TORRENTCORE_DEPLOY_BASE}/Scripts/torrentcore.env.example to torrentcore.env on the target host before first runtime start."
}

tc_require_command() {
  local command_name="$1"

  if ! command -v "${command_name}" >/dev/null 2>&1; then
    tc_log_error "Required command '${command_name}' was not found."
    return 1
  fi
}

tc_require_file() {
  local file_path="$1"

  if [[ ! -f "${file_path}" ]]; then
    tc_log_error "Required file was not found: ${file_path}"
    return 1
  fi
}

tc_ensure_runtime_directories() {
  mkdir -p "${TORRENTCORE_RUN_DIR}" "${TORRENTCORE_LOG_DIR}"
}

tc_pid_matches_app() {
  local pid="$1"
  local expected_fragment="$2"
  local command_line

  if ! kill -0 "${pid}" >/dev/null 2>&1; then
    return 1
  fi

  command_line="$(ps -p "${pid}" -o command= 2>/dev/null || true)"
  [[ -n "${command_line}" && "${command_line}" == *"${expected_fragment}"* ]]
}

tc_start_dotnet_app() {
  local app_name="$1"
  local app_dir="$2"
  local dll_name="$3"
  local pid_file="$4"
  local log_file="$5"
  shift 5
  local -a env_args=("$@")
  local dll_path="${app_dir}/${dll_name}"
  local pid

  tc_require_command dotnet
  tc_require_file "${dll_path}"
  tc_ensure_runtime_directories

  if [[ -f "${pid_file}" ]]; then
    pid="$(<"${pid_file}")"
    if [[ -n "${pid}" ]] && tc_pid_matches_app "${pid}" "${dll_name}"; then
      tc_log_warn "${app_name} is already running. Pid=${pid}"
      return 0
    fi

    rm -f "${pid_file}"
  fi

  mkdir -p "$(dirname "${log_file}")"
  touch "${log_file}"

  (
    cd "${app_dir}"
    nohup env "${env_args[@]}" dotnet "${dll_name}" >>"${log_file}" 2>&1 &
    echo $! > "${pid_file}"
  )

  pid="$(<"${pid_file}")"
  sleep 1

  if ! tc_pid_matches_app "${pid}" "${dll_name}"; then
    rm -f "${pid_file}"
    tc_log_error "Failed to start ${app_name}. Check ${log_file}."
    return 1
  fi

  tc_log_info "Started ${app_name}. Pid=${pid} Log=${log_file}"
}

tc_stop_dotnet_app() {
  local app_name="$1"
  local dll_name="$2"
  local pid_file="$3"
  local pid=""
  local command_line=""
  local attempt

  if [[ ! -f "${pid_file}" ]]; then
    tc_log_warn "${app_name} is not running. No PID file at ${pid_file}."
    return 0
  fi

  pid="$(<"${pid_file}")"
  if [[ -z "${pid}" ]]; then
    rm -f "${pid_file}"
    tc_log_warn "${app_name} PID file was empty. Removed stale file."
    return 0
  fi

  if ! tc_pid_matches_app "${pid}" "${dll_name}"; then
    command_line="$(ps -p "${pid}" -o command= 2>/dev/null || true)"
    rm -f "${pid_file}"
    tc_log_warn "${app_name} PID file was stale. Removed stale file. Pid=${pid} Command='${command_line}'"
    return 0
  fi

  kill "${pid}" >/dev/null 2>&1 || true

  for attempt in {1..20}; do
    if ! kill -0 "${pid}" >/dev/null 2>&1; then
      rm -f "${pid_file}"
      tc_log_info "Stopped ${app_name}. Pid=${pid}"
      return 0
    fi

    sleep 0.5
  done

  tc_log_warn "${app_name} did not exit after TERM. Sending KILL. Pid=${pid}"
  kill -9 "${pid}" >/dev/null 2>&1 || true
  rm -f "${pid_file}"
  tc_log_info "Stopped ${app_name}. Pid=${pid}"
}

tc_resolve_repo_root() {
  local repo_root="${TORRENTCORE_REPO_ROOT:-}"

  if [[ -n "${repo_root}" ]]; then
    repo_root="$(cd "${repo_root}" && pwd)"
  else
    repo_root="$(cd "${TORRENTCORE_SCRIPT_DIR}/.." && pwd)"
  fi

  if [[ ! -f "${repo_root}/TorrentCore.sln" ]]; then
    tc_log_error "Unable to find TorrentCore.sln. Set TORRENTCORE_REPO_ROOT before running deploy scripts."
    return 1
  fi

  print -- "${repo_root}"
}

tc_publish_project() {
  local project_path="$1"
  local output_dir="$2"
  local repo_root

  repo_root="$(tc_resolve_repo_root)"
  tc_require_command dotnet

  rm -rf "${output_dir}"
  mkdir -p "${output_dir}"

  dotnet publish \
    "${repo_root}/${project_path}" \
    -c "${TORRENTCORE_PUBLISH_CONFIGURATION}" \
    -r "${TORRENTCORE_PUBLISH_RUNTIME}" \
    --self-contained "${TORRENTCORE_PUBLISH_SELF_CONTAINED}" \
    -o "${output_dir}"
}

tc_sync_directory() {
  local source_dir="$1"
  local target_dir="$2"

  tc_require_command rsync
  mkdir -p "${target_dir}"
  rsync -a --delete "${source_dir}/" "${target_dir}/"
}

tc_create_macos_app_bundle() {
  local publish_dir="$1"
  local bundle_output_dir="$2"
  local executable_name="$3"
  local bundle_name="$4"
  local display_name="$5"
  local bundle_identifier="$6"
  local icon_path="${7:-}"
  local icon_name="${8:-}"
  local app_bundle="${bundle_output_dir}/${bundle_name}.app"
  local app_contents="${app_bundle}/Contents"
  local app_macos="${app_contents}/MacOS"
  local app_resources="${app_contents}/Resources"

  tc_require_command rsync
  tc_require_file "${publish_dir}/${executable_name}"

  rm -rf "${app_bundle}"
  mkdir -p "${app_macos}" "${app_resources}"
  rsync -a --exclude "*.app" "${publish_dir}/" "${app_macos}/"
  chmod +x "${app_macos}/${executable_name}" || true

  if [[ -n "${icon_path}" && -f "${icon_path}" ]]; then
    cp "${icon_path}" "${app_resources}/${icon_name}"
  fi

  cat > "${app_contents}/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>${bundle_name}</string>
    <key>CFBundleDisplayName</key>
    <string>${display_name}</string>
    <key>CFBundleIdentifier</key>
    <string>${bundle_identifier}</string>
    <key>CFBundleVersion</key>
    <string>1.0</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0</string>
    <key>CFBundleExecutable</key>
    <string>${executable_name}</string>
EOF

  if [[ -n "${icon_path}" && -f "${icon_path}" ]]; then
    cat >> "${app_contents}/Info.plist" <<EOF
    <key>CFBundleIconFile</key>
    <string>${icon_name}</string>
EOF
  fi

  cat >> "${app_contents}/Info.plist" <<EOF
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
EOF

  print -- "${app_bundle}"
}

tc_sync_app_bundle() {
  local app_bundle="$1"
  local target_dir="$2"
  local bundle_name

  if [[ -z "${target_dir}" ]]; then
    return 0
  fi

  tc_require_command rsync
  mkdir -p "${target_dir}"
  bundle_name="$(basename "${app_bundle}")"
  rm -rf "${target_dir}/${bundle_name}"
  rsync -a "${app_bundle}" "${target_dir}/"
}

tc_apply_avalonia_target_defaults() {
  local repo_root=""
  local default_icon_path=""
  local should_enable_system_mirror=false

  if [[ -z "${TORRENTCORE_AVALONIA_PUBLISH_TARGET}" ]]; then
    export TORRENTCORE_AVALONIA_PUBLISH_TARGET="${TORRENTCORE_DEPLOY_BASE}/Avalonia"
  fi

  if [[ -z "${TORRENTCORE_AVALONIA_APP_TARGET}" ]]; then
    export TORRENTCORE_AVALONIA_APP_TARGET="${TORRENTCORE_DEPLOY_BASE}/Applications"
  fi

  if [[ -z "${TORRENTCORE_AVALONIA_ICON_PATH}" ]]; then
    repo_root="$(tc_resolve_repo_root)"
    default_icon_path="${repo_root}/src/TorrentCore.Avalonia/Assets/TorrentCore.icns"
    if [[ -f "${default_icon_path}" ]]; then
      export TORRENTCORE_AVALONIA_ICON_PATH="${default_icon_path}"
    fi
  fi

  case "${TORRENTCORE_AVALONIA_SYSTEM_APPLICATIONS_MIRROR}" in
    auto)
      if tc_path_is_within_home "${TORRENTCORE_DEPLOY_BASE}"; then
        should_enable_system_mirror=true
      fi
      ;;
    always)
      should_enable_system_mirror=true
      ;;
    never)
      should_enable_system_mirror=false
      ;;
    *)
      tc_log_error "Unknown TORRENTCORE_AVALONIA_SYSTEM_APPLICATIONS_MIRROR value '${TORRENTCORE_AVALONIA_SYSTEM_APPLICATIONS_MIRROR}'. Expected auto, always, or never."
      return 1
      ;;
  esac

  if [[ "${should_enable_system_mirror}" == true &&
        -z "${TORRENTCORE_AVALONIA_APP_MIRROR_TARGET}" &&
        "${TORRENTCORE_AVALONIA_APP_TARGET}" != "${TORRENTCORE_AVALONIA_SYSTEM_APPLICATIONS_TARGET}" ]]; then
    export TORRENTCORE_AVALONIA_APP_MIRROR_TARGET="${TORRENTCORE_AVALONIA_SYSTEM_APPLICATIONS_TARGET}"
  fi
}

tc_sync_scripts_to_target() {
  setopt local_options null_glob
  local repo_root

  repo_root="$(tc_resolve_repo_root)"
  tc_require_command rsync
  mkdir -p "${TORRENTCORE_DEPLOY_BASE}/Scripts"
  rm -f "${TORRENTCORE_DEPLOY_BASE}/Scripts"/deploy-*.zsh
  rsync \
    -a \
    --delete \
    --exclude 'logs/' \
    --exclude 'run/' \
    --exclude 'torrentcore.env' \
    --exclude 'deploy-*.zsh' \
    "${repo_root}/Scripts/" \
    "${TORRENTCORE_DEPLOY_BASE}/Scripts/"

  tc_warn_if_target_env_file_missing
}

tc_select_deploy_target() {
  local target="$1"

  case "${target}" in
    intel)
      export TORRENTCORE_DEPLOY_BASE="${TORRENTCORE_DEPLOY_BASE_INTEL}"
      export TORRENTCORE_PUBLISH_RUNTIME="${TORRENTCORE_PUBLISH_RUNTIME_INTEL}"
      export TORRENTCORE_ARTIFACT_SEGMENT="intel"
      ;;
    arm)
      export TORRENTCORE_DEPLOY_BASE="${TORRENTCORE_DEPLOY_BASE_ARM}"
      export TORRENTCORE_PUBLISH_RUNTIME="${TORRENTCORE_PUBLISH_RUNTIME_ARM}"
      export TORRENTCORE_ARTIFACT_SEGMENT="arm"
      ;;
    *)
      tc_log_error "Unknown deploy target '${target}'."
      return 1
      ;;
  esac
}
