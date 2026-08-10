#!/usr/bin/env zsh

set -euo pipefail

PACKAGE_ROOT="${0:A:h}"
cd "$PACKAGE_ROOT"
open README.md
