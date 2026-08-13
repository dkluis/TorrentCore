#!/usr/bin/env zsh

set -euo pipefail

PACKAGE_ROOT="${0:A:h}"
cd "$PACKAGE_ROOT"
clear

cat <<'EOF'
TorrentCore deployment DMG

This Terminal is now in the mounted DMG root.

Suggested sequence:

  ./plan.zsh
  ./dry-run.zsh
  ./backup.zsh
  ./apply.zsh
  ./verify.zsh

Runbook.pdf / Runbook.md contains the same commands.
EOF

print -r -- ""
exec zsh -l
