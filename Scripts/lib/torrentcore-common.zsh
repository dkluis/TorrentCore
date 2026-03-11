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
: "${TORRENTCORE_ARTIFACT_SEGMENT:=intel}"

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

  if [[ -f "${env_file}" ]]; then
    # shellcheck disable=SC1090
    source "${env_file}"
  fi
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
