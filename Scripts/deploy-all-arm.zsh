#!/bin/zsh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
export TORRENTCORE_SCRIPT_DIR="${SCRIPT_DIR}"
source "${SCRIPT_DIR}/lib/torrentcore-common.zsh"
tc_load_env_file
tc_select_deploy_target arm

RESTART_AFTER_DEPLOY=false

while (( $# > 0 )); do
  case "$1" in
    --restart)
      RESTART_AFTER_DEPLOY=true
      ;;
    *)
      tc_log_error "Unknown argument: $1"
      exit 1
      ;;
  esac
  shift
done

REPO_ROOT="$(tc_resolve_repo_root)"
PUBLISH_ROOT="${REPO_ROOT}/artifacts/publish/${TORRENTCORE_ARTIFACT_SEGMENT}"
SERVICE_PUBLISH_DIR="${PUBLISH_ROOT}/service"
WEBUI_PUBLISH_DIR="${PUBLISH_ROOT}/webui"
TARGET_SCRIPT_DIR="${TORRENTCORE_DEPLOY_BASE}/Scripts"

tc_log_info "Publishing TorrentCore.Service for ${TORRENTCORE_PUBLISH_RUNTIME}."
tc_publish_project "src/TorrentCore.ServiceHost/TorrentCore.Service.csproj" "${SERVICE_PUBLISH_DIR}"

tc_log_info "Publishing TorrentCore.WebUI for ${TORRENTCORE_PUBLISH_RUNTIME}."
tc_publish_project "src/TorrentCore.WebUI/TorrentCore.WebUI.csproj" "${WEBUI_PUBLISH_DIR}"

tc_log_info "Syncing service publish output."
tc_sync_directory "${SERVICE_PUBLISH_DIR}" "${TORRENTCORE_DEPLOY_BASE}/Service"

tc_log_info "Syncing web publish output."
tc_sync_directory "${WEBUI_PUBLISH_DIR}" "${TORRENTCORE_DEPLOY_BASE}/WebUI"

tc_log_info "Syncing scripts."
tc_sync_scripts_to_target

if [[ "${RESTART_AFTER_DEPLOY}" == true ]]; then
  "${TARGET_SCRIPT_DIR}/install-launch-agents.zsh" all
  "${TARGET_SCRIPT_DIR}/ManageTorrentCoreLaunchAgents.zsh" restart all
fi

tc_log_info "Combined Arm deployment complete."
