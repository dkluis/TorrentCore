#!/usr/bin/env zsh

set -euo pipefail

PACKAGE_ROOT="${0:A:h}"
cd "$PACKAGE_ROOT"

if [[ -f README.pdf ]]; then
    open README.pdf
elif [[ -f README.md ]]; then
    open README.md
elif [[ -f README-FIRST.txt ]]; then
    open README-FIRST.txt
else
    open .
fi
