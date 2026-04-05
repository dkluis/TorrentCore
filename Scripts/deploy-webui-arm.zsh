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
PUBLISH_DIR="${REPO_ROOT}/artifacts/publish/${TORRENTCORE_ARTIFACT_SEGMENT}/webui"
TARGET_WEBUI_DIR="${TORRENTCORE_DEPLOY_BASE}/WebUI"
TARGET_SCRIPT_DIR="${TORRENTCORE_DEPLOY_BASE}/Scripts"

if [[ "${RESTART_AFTER_DEPLOY}" == true && -x "${TARGET_SCRIPT_DIR}/stop-webui.zsh" ]]; then
  "${TARGET_SCRIPT_DIR}/stop-webui.zsh"
fi

tc_log_info "Publishing TorrentCore.WebUI for ${TORRENTCORE_PUBLISH_RUNTIME}."
tc_publish_project "src/TorrentCore.WebUI/TorrentCore.WebUI.csproj" "${PUBLISH_DIR}"

tc_log_info "Syncing web publish output to ${TARGET_WEBUI_DIR}."
tc_sync_directory "${PUBLISH_DIR}" "${TARGET_WEBUI_DIR}"

tc_log_info "Syncing scripts to ${TARGET_SCRIPT_DIR}."
tc_sync_scripts_to_target

if [[ "${RESTART_AFTER_DEPLOY}" == true ]]; then
  "${TARGET_SCRIPT_DIR}/start-webui.zsh"
fi

tc_log_info "Arm WebUI deployment complete."
