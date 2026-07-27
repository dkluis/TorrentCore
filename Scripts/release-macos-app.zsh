#!/bin/zsh

set -euo pipefail

readonly TEAM_ID="5GRR76N48V"
readonly PROJECT_RELATIVE_PATH="clients/apple/TorrentCoreApple.xcodeproj"
readonly EXPORT_OPTIONS_RELATIVE_PATH="clients/apple/ExportOptions-DeveloperID.plist"
readonly SCHEME="TorrentCoreMac"
readonly PRODUCT_NAME="TorrentCore"

VERSION="0.3.1"
BUILD_NUMBER="5"
OUTPUT_DIR="/Volumes/CA-Desktop-HD-2/Development/Deployments/DMGs"
NOTARY_PROFILE="TorrentCore-notary"
SIGNING_IDENTITY=""
CHECK_ONLY=false
OVERWRITE=false
WORK_DIR=""

print_usage() {
  cat <<'EOF'
Usage: ./Scripts/release-macos-app.zsh [options]

Build, Developer ID sign, notarize, staple, and verify the TorrentCore macOS DMG.

Options:
  --version <version>              Marketing version (default: 0.3.1)
  --build <number>                 Positive integer build number (default: 5)
  --output-dir <absolute-path>     DMG destination directory
  --notary-profile <name>          notarytool Keychain profile
                                   (default: TorrentCore-notary)
  --signing-identity <name>        Exact Developer ID Application identity
                                   (auto-selected when exactly one matches the team)
  --check                          Validate local signing/notary prerequisites only
  --overwrite                      Replace an existing same-version DMG
  -h, --help                       Show this help

The default output is:
  /Volumes/CA-Desktop-HD-2/Development/Deployments/DMGs/
  TorrentCore-macOS-App-0.3.1.dmg
EOF
}

log_info() {
  print -r -- "[TorrentCore release] $*"
}

fail() {
  print -ru2 -- "[TorrentCore release] ERROR: $*"
  exit 1
}

cleanup() {
  if [[ -n "${WORK_DIR}" &&
        "${WORK_DIR}" == /private/tmp/TorrentCore-release.* &&
        -d "${WORK_DIR}" ]]; then
    /bin/rm -rf -- "${WORK_DIR}"
  fi
}

require_command() {
  local command_name="$1"
  command -v "${command_name}" >/dev/null 2>&1 ||
    fail "Required command is unavailable: ${command_name}"
}

select_signing_identity() {
  local identity_line=""
  local identity_name=""
  local existing_identity=""
  local duplicate=false
  local -a detected_identities=()
  local identity_pattern='^ *[0-9]+\) [A-F0-9]+ "(Developer ID Application:.*\('"${TEAM_ID}"'\))"$'

  while IFS= read -r identity_line; do
    if [[ "${identity_line}" =~ ${identity_pattern} ]]; then
      identity_name="${match[1]}"
      duplicate=false
      for existing_identity in "${detected_identities[@]}"; do
        if [[ "${existing_identity}" == "${identity_name}" ]]; then
          duplicate=true
          break
        fi
      done
      if [[ "${duplicate}" == false ]]; then
        detected_identities+=("${identity_name}")
      fi
    fi
  done < <(/usr/bin/security find-identity -v -p codesigning)

  if [[ -n "${SIGNING_IDENTITY}" ]]; then
    for existing_identity in "${detected_identities[@]}"; do
      if [[ "${existing_identity}" == "${SIGNING_IDENTITY}" ]]; then
        return
      fi
    done
    fail "The requested Developer ID Application identity is not valid for Team ${TEAM_ID}: ${SIGNING_IDENTITY}"
  fi

  case "${#detected_identities[@]}" in
    0)
      fail "No valid 'Developer ID Application' identity exists for Team ${TEAM_ID}. Create it in Xcode before releasing."
      ;;
    1)
      SIGNING_IDENTITY="${detected_identities[1]}"
      ;;
    *)
      print -ru2 -- "[TorrentCore release] Multiple matching identities were found:"
      for existing_identity in "${detected_identities[@]}"; do
        print -ru2 -- "  ${existing_identity}"
      done
      fail "Run again with --signing-identity and one exact identity name."
      ;;
  esac
}

while (( $# > 0 )); do
  case "$1" in
    --version)
      (( $# >= 2 )) || fail "--version requires a value."
      VERSION="$2"
      shift 2
      ;;
    --build)
      (( $# >= 2 )) || fail "--build requires a value."
      BUILD_NUMBER="$2"
      shift 2
      ;;
    --output-dir)
      (( $# >= 2 )) || fail "--output-dir requires a value."
      OUTPUT_DIR="$2"
      shift 2
      ;;
    --notary-profile)
      (( $# >= 2 )) || fail "--notary-profile requires a value."
      NOTARY_PROFILE="$2"
      shift 2
      ;;
    --signing-identity)
      (( $# >= 2 )) || fail "--signing-identity requires a value."
      SIGNING_IDENTITY="$2"
      shift 2
      ;;
    --check)
      CHECK_ONLY=true
      shift
      ;;
    --overwrite)
      OVERWRITE=true
      shift
      ;;
    -h|--help)
      print_usage
      exit 0
      ;;
    *)
      fail "Unknown argument: $1"
      ;;
  esac
done

[[ "${VERSION}" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] ||
  fail "Version must use three numeric components, for example 0.1.0."
[[ "${BUILD_NUMBER}" == <-> ]] && (( BUILD_NUMBER > 0 )) ||
  fail "Build number must be a positive integer."
[[ "${OUTPUT_DIR}" == /* ]] ||
  fail "Output directory must be an absolute path."
[[ -n "${NOTARY_PROFILE}" ]] ||
  fail "Notary profile name cannot be empty."

readonly SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
readonly REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly PROJECT_PATH="${REPO_ROOT}/${PROJECT_RELATIVE_PATH}"
readonly EXPORT_OPTIONS_PATH="${REPO_ROOT}/${EXPORT_OPTIONS_RELATIVE_PATH}"
readonly DMG_NAME="TorrentCore-macOS-App-${VERSION}.dmg"
readonly OUTPUT_PATH="${OUTPUT_DIR}/${DMG_NAME}"

require_command xcodebuild
require_command xcrun
require_command security
require_command codesign
require_command hdiutil
require_command spctl
require_command ditto
require_command lipo
require_command plutil
require_command shasum

[[ -d "${PROJECT_PATH}" ]] || fail "Xcode project was not found: ${PROJECT_PATH}"
[[ -f "${EXPORT_OPTIONS_PATH}" ]] || fail "Export options were not found: ${EXPORT_OPTIONS_PATH}"
/usr/bin/plutil -lint "${EXPORT_OPTIONS_PATH}" >/dev/null ||
  fail "Export options plist is invalid: ${EXPORT_OPTIONS_PATH}"
/usr/bin/xcrun --find notarytool >/dev/null ||
  fail "notarytool is unavailable in the selected Xcode command-line tools."
/usr/bin/xcrun --find stapler >/dev/null ||
  fail "stapler is unavailable in the selected Xcode command-line tools."

if [[ "${CHECK_ONLY}" != true &&
      -e "${OUTPUT_PATH}" &&
      "${OVERWRITE}" != true ]]; then
  fail "Output already exists: ${OUTPUT_PATH}. Use --overwrite only when replacement is intentional."
fi

select_signing_identity
log_info "Developer ID identity: ${SIGNING_IDENTITY}"

log_info "Validating notarytool Keychain profile '${NOTARY_PROFILE}'."
if ! /usr/bin/xcrun notarytool history \
  --keychain-profile "${NOTARY_PROFILE}" \
  --output-format json >/dev/null; then
  fail "Notary profile '${NOTARY_PROFILE}' is missing or invalid. Store valid credentials before releasing."
fi

if [[ "${CHECK_ONLY}" == true ]]; then
  log_info "Release prerequisites are ready for version ${VERSION} (${BUILD_NUMBER})."
  exit 0
fi

WORK_DIR="$(/usr/bin/mktemp -d /private/tmp/TorrentCore-release.XXXXXX)"
trap cleanup EXIT

readonly ARCHIVE_PATH="${WORK_DIR}/TorrentCore.xcarchive"
readonly DERIVED_DATA_PATH="${WORK_DIR}/DerivedData"
readonly EXPORT_PATH="${WORK_DIR}/Export"
readonly STAGING_PATH="${WORK_DIR}/DMG"
readonly DMG_PATH="${WORK_DIR}/${DMG_NAME}"
readonly EXPORTED_APP="${EXPORT_PATH}/${PRODUCT_NAME}.app"
readonly EXECUTABLE_PATH="${EXPORTED_APP}/Contents/MacOS/${PRODUCT_NAME}"

log_info "Archiving ${PRODUCT_NAME} ${VERSION} (${BUILD_NUMBER})."
/usr/bin/xcodebuild archive \
  -project "${PROJECT_PATH}" \
  -scheme "${SCHEME}" \
  -configuration Release \
  -destination "generic/platform=macOS" \
  -archivePath "${ARCHIVE_PATH}" \
  -derivedDataPath "${DERIVED_DATA_PATH}" \
  -skipPackagePluginValidation \
  -allowProvisioningUpdates \
  MARKETING_VERSION="${VERSION}" \
  CURRENT_PROJECT_VERSION="${BUILD_NUMBER}" \
  DEVELOPMENT_TEAM="${TEAM_ID}"

log_info "Exporting the Developer ID-signed application."
/usr/bin/xcodebuild -exportArchive \
  -archivePath "${ARCHIVE_PATH}" \
  -exportPath "${EXPORT_PATH}" \
  -exportOptionsPlist "${EXPORT_OPTIONS_PATH}" \
  -allowProvisioningUpdates

[[ -d "${EXPORTED_APP}" ]] || fail "Export did not produce ${EXPORTED_APP}."
[[ -x "${EXECUTABLE_PATH}" ]] || fail "Exported app executable was not found."

readonly EXPORTED_VERSION="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "${EXPORTED_APP}/Contents/Info.plist")"
readonly EXPORTED_BUILD="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleVersion' "${EXPORTED_APP}/Contents/Info.plist")"
readonly EXPORTED_ARCHITECTURES="$(/usr/bin/lipo -archs "${EXECUTABLE_PATH}")"

[[ "${EXPORTED_VERSION}" == "${VERSION}" ]] ||
  fail "Exported version ${EXPORTED_VERSION} does not match requested version ${VERSION}."
[[ "${EXPORTED_BUILD}" == "${BUILD_NUMBER}" ]] ||
  fail "Exported build ${EXPORTED_BUILD} does not match requested build ${BUILD_NUMBER}."
[[ "${EXPORTED_ARCHITECTURES}" == "arm64" ]] ||
  fail "Exported executable architectures are '${EXPORTED_ARCHITECTURES}', expected only arm64."

/usr/bin/codesign --verify --deep --strict --verbose=2 "${EXPORTED_APP}"

log_info "Creating the drag-to-Applications disk image."
/bin/mkdir -p "${STAGING_PATH}"
/usr/bin/ditto "${EXPORTED_APP}" "${STAGING_PATH}/${PRODUCT_NAME}.app"
/bin/ln -s /Applications "${STAGING_PATH}/Applications"
/usr/bin/hdiutil create \
  -volname "${PRODUCT_NAME}" \
  -srcfolder "${STAGING_PATH}" \
  -format UDZO \
  -ov \
  "${DMG_PATH}"

log_info "Signing the disk image."
/usr/bin/codesign \
  --force \
  --sign "${SIGNING_IDENTITY}" \
  --timestamp \
  "${DMG_PATH}"
/usr/bin/codesign --verify --verbose=2 "${DMG_PATH}"

log_info "Submitting the disk image to Apple's notary service."
/usr/bin/xcrun notarytool submit "${DMG_PATH}" \
  --keychain-profile "${NOTARY_PROFILE}" \
  --wait

log_info "Stapling and validating the notarization ticket."
/usr/bin/xcrun stapler staple -v "${DMG_PATH}"
/usr/bin/xcrun stapler validate -v "${DMG_PATH}"
/usr/bin/hdiutil verify "${DMG_PATH}"
/usr/sbin/spctl --assess \
  --type open \
  --context context:primary-signature \
  --verbose=4 \
  "${DMG_PATH}"

/bin/mkdir -p "${OUTPUT_DIR}"
if [[ -e "${OUTPUT_PATH}" ]]; then
  /bin/rm -f -- "${OUTPUT_PATH}"
fi
/usr/bin/ditto "${DMG_PATH}" "${OUTPUT_PATH}"

log_info "Release complete: ${OUTPUT_PATH}"
/usr/bin/shasum -a 256 "${OUTPUT_PATH}"
