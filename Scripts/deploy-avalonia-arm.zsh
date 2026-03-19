#!/bin/zsh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
export TORRENTCORE_SCRIPT_DIR="${SCRIPT_DIR}"
source "${SCRIPT_DIR}/lib/torrentcore-common.zsh"
tc_load_env_file
tc_select_deploy_target arm
tc_apply_avalonia_target_defaults

REPO_ROOT="$(tc_resolve_repo_root)"
PUBLISH_DIR="${REPO_ROOT}/artifacts/publish/${TORRENTCORE_ARTIFACT_SEGMENT}/avalonia"
BUNDLE_OUTPUT_DIR="${REPO_ROOT}/artifacts/publish/${TORRENTCORE_ARTIFACT_SEGMENT}/avalonia-app"
PROJECT_PATH="src/TorrentCore.Avalonia/TorrentCore.Avalonia.csproj"

tc_log_info "Publishing TorrentCore.Avalonia for ${TORRENTCORE_PUBLISH_RUNTIME}."
tc_publish_project "${PROJECT_PATH}" "${PUBLISH_DIR}"

tc_log_info "Creating macOS app bundle."
APP_BUNDLE="$(
  tc_create_macos_app_bundle \
    "${PUBLISH_DIR}" \
    "${BUNDLE_OUTPUT_DIR}" \
    "${TORRENTCORE_AVALONIA_EXECUTABLE_NAME}" \
    "${TORRENTCORE_AVALONIA_BUNDLE_NAME}" \
    "${TORRENTCORE_AVALONIA_DISPLAY_NAME}" \
    "${TORRENTCORE_AVALONIA_BUNDLE_IDENTIFIER}" \
    "${TORRENTCORE_AVALONIA_ICON_PATH}" \
    "${TORRENTCORE_AVALONIA_ICON_NAME}"
)"

tc_log_info "Syncing Avalonia publish output to ${TORRENTCORE_AVALONIA_PUBLISH_TARGET}."
tc_sync_directory "${PUBLISH_DIR}" "${TORRENTCORE_AVALONIA_PUBLISH_TARGET}"

tc_log_info "Syncing Avalonia app bundle to ${TORRENTCORE_AVALONIA_APP_TARGET}."
tc_sync_app_bundle "${APP_BUNDLE}" "${TORRENTCORE_AVALONIA_APP_TARGET}"

if [[ -n "${TORRENTCORE_AVALONIA_APP_MIRROR_TARGET}" ]]; then
  tc_log_info "Syncing Avalonia app bundle mirror to ${TORRENTCORE_AVALONIA_APP_MIRROR_TARGET}."
  tc_sync_app_bundle "${APP_BUNDLE}" "${TORRENTCORE_AVALONIA_APP_MIRROR_TARGET}"
fi

tc_log_info "Avalonia deployment complete."
tc_log_info "Raw publish output: ${PUBLISH_DIR}"
tc_log_info "App bundle: ${APP_BUNDLE}"
