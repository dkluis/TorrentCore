#!/usr/bin/env zsh

set -euo pipefail

PACKAGE_ROOT="${0:A:h}"
cd "$PACKAGE_ROOT"
clear

cat <<'EOF'
TorrentCore Service app DMG

This Terminal is now in the mounted DMG root.

Start with:

  ./install.zsh plan
  ./install.zsh dry-run

Runbook.md contains the complete apply, verify, history, and rollback commands.
EOF

print -r -- ""
exec zsh -l
