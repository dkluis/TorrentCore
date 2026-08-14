#!/usr/bin/env zsh

set -euo pipefail

SCRIPT_DIR="${0:A:h}"
DEPLOYMENTS_ROOT="/Volumes/CA-Desktop-HD-2/Development/Deployments"
INSTALLATION=""
CPU=""
RELEASE_NAME=""
RELEASE_DATE="$(date '+%Y.%m.%d')"
NOTES=""
PACKAGE_DIR=""
PACKAGE_ROOT=""
DMG_DIR=""
DMG_PATH=""
VOLUME_NAME=""
SIGNING_IDENTITY=""
NOTARY_PROFILE="TorrentCore-notary"
PDF_TOOL="pandoc"
PDF_ENGINE="tectonic"
SKIP_PDF=false
REQUIRE_PDF=false
CLEAN=false

fail() { print -ru2 -- "[TorrentCore release DMG] ERROR: $*"; exit 1; }

usage() {
    cat <<'EOF'
Usage: release-service-app-dmg.zsh --installation <Dick|Tom|Shared> --cpu arm --release-name <name> --notes <summary> [options]

This is the established two-step TorrentCore release driver. It first saves the complete release package under
TorrentCore-Deployments/<installation>, then creates the DMG from that saved package.

Options:
  --date <YYYY.MM.DD>          Release date. Defaults to today.
  --package-dir <path>         Installation package parent directory.
  --package-root <path>        Exact persistent package directory.
  --dmg-dir <path>             DMG output directory.
  --output-dir <path>          Compatibility alias for --dmg-dir.
  --dmg-path <path>            Exact DMG path.
  --volume-name <name>         Defaults to the package directory name.
  --signing-identity <name>    Exact Developer ID Application identity.
  --notary-profile <name>      Defaults to TorrentCore-notary.
  --pdf-tool <command>         Defaults to pandoc.
  --pdf-engine <engine>        Defaults to tectonic.
  --skip-pdf                   Generate Markdown only.
  --require-pdf                Fail if either PDF cannot be generated.
  --clean                      Replace an existing package directory and DMG.
EOF
}

while (( $# > 0 )); do
    case "$1" in
        --installation) INSTALLATION="${2:-}"; shift 2 ;;
        --cpu) CPU="${2:-}"; shift 2 ;;
        --release-name) RELEASE_NAME="${2:-}"; shift 2 ;;
        --date) RELEASE_DATE="${2:-}"; shift 2 ;;
        --notes) NOTES="${2:-}"; shift 2 ;;
        --package-dir) PACKAGE_DIR="${2:-}"; shift 2 ;;
        --package-root) PACKAGE_ROOT="${2:-}"; shift 2 ;;
        --dmg-dir|--output-dir) DMG_DIR="${2:-}"; shift 2 ;;
        --dmg-path) DMG_PATH="${2:-}"; shift 2 ;;
        --volume-name) VOLUME_NAME="${2:-}"; shift 2 ;;
        --signing-identity) SIGNING_IDENTITY="${2:-}"; shift 2 ;;
        --notary-profile) NOTARY_PROFILE="${2:-}"; shift 2 ;;
        --pdf-tool) PDF_TOOL="${2:-}"; shift 2 ;;
        --pdf-engine) PDF_ENGINE="${2:-}"; shift 2 ;;
        --skip-pdf) SKIP_PDF=true; shift ;;
        --require-pdf) REQUIRE_PDF=true; shift ;;
        --clean) CLEAN=true; shift ;;
        --help|-h) usage; exit 0 ;;
        *) fail "Unknown argument: $1" ;;
    esac
done

case "${INSTALLATION:l}" in
    dick) INSTALLATION="Dick" ;;
    tom) INSTALLATION="Tom" ;;
    shared) INSTALLATION="Shared" ;;
    *) fail "--installation must be Dick, Tom, or Shared." ;;
esac
[[ "${CPU:l}" == arm ]] || fail "--cpu must be arm; Intel packaging is not supported."
CPU="arm"
[[ "$RELEASE_NAME" =~ '^[A-Za-z][A-Za-z0-9.-]*$' ]] || fail "--release-name is required and invalid."
[[ "$RELEASE_DATE" =~ '^[0-9]{4}\.[0-9]{2}\.[0-9]{2}$' ]] || fail "Invalid release date: $RELEASE_DATE"
[[ -n "$NOTES" ]] || fail "--notes is required so the package states what changed."
[[ "$SKIP_PDF" != true || "$REQUIRE_PDF" != true ]] || fail "--skip-pdf cannot be used with --require-pdf."

if [[ "$INSTALLATION" == "Shared" ]]; then
    RELEASE_ID="torrentcore.$RELEASE_DATE.$RELEASE_NAME"
else
    RELEASE_ID="torrentcore.$RELEASE_DATE.$INSTALLATION.$RELEASE_NAME"
fi
ARTIFACT_STEM="TorrentCore-$RELEASE_ID"
if [[ -z "$PACKAGE_ROOT" ]]; then
    [[ -n "$PACKAGE_DIR" ]] || PACKAGE_DIR="$DEPLOYMENTS_ROOT/TorrentCore-Deployments/$INSTALLATION"
    mkdir -p "$PACKAGE_DIR"
    PACKAGE_ROOT="${PACKAGE_DIR:A}/$ARTIFACT_STEM"
fi
if [[ -z "$DMG_PATH" ]]; then
    [[ -n "$DMG_DIR" ]] || DMG_DIR="$DEPLOYMENTS_ROOT/DMGs"
    mkdir -p "$DMG_DIR"
    DMG_PATH="${DMG_DIR:A}/${PACKAGE_ROOT:t}.dmg"
fi
[[ -n "$VOLUME_NAME" ]] || VOLUME_NAME="${PACKAGE_ROOT:t}"

print -r -- "Creating TorrentCore release DMG..."
print -r -- "Package root: $PACKAGE_ROOT"
print -r -- "DMG path:     $DMG_PATH"
print -r -- "Volume name:  $VOLUME_NAME"

stage_args=(
    --installation "$INSTALLATION"
    --cpu "$CPU"
    --release-name "$RELEASE_NAME"
    --date "$RELEASE_DATE"
    --notes "$NOTES"
    --package-root "$PACKAGE_ROOT"
    --pdf-tool "$PDF_TOOL"
    --pdf-engine "$PDF_ENGINE"
)
[[ -z "$SIGNING_IDENTITY" ]] || stage_args+=(--signing-identity "$SIGNING_IDENTITY")
[[ "$SKIP_PDF" != true ]] || stage_args+=(--skip-pdf)
[[ "$REQUIRE_PDF" != true ]] || stage_args+=(--require-pdf)
[[ "$CLEAN" != true ]] || stage_args+=(--clean)
zsh "$SCRIPT_DIR/stage-release-package.zsh" "${stage_args[@]}"

build_args=(
    --package-root "$PACKAGE_ROOT"
    --dmg-path "$DMG_PATH"
    --volume-name "$VOLUME_NAME"
    --notary-profile "$NOTARY_PROFILE"
)
[[ -z "$SIGNING_IDENTITY" ]] || build_args+=(--signing-identity "$SIGNING_IDENTITY")
[[ "$CLEAN" != true ]] || build_args+=(--clean)
zsh "$SCRIPT_DIR/build-package-dmg.zsh" "${build_args[@]}"

print -r -- ""
print -r -- "TorrentCore release DMG complete."
print -r -- "Package root: $PACKAGE_ROOT"
print -r -- "DMG path:     $DMG_PATH"
