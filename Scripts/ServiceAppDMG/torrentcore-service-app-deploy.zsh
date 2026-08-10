#!/bin/zsh

set -euo pipefail

SCRIPT_DIR="${0:A:h}"
exec python3 "$SCRIPT_DIR/torrentcore_service_app_deploy.py" "$@"
