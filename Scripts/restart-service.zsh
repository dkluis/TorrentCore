#!/bin/zsh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

"${SCRIPT_DIR}/stop-service.zsh"
"${SCRIPT_DIR}/start-service.zsh"
