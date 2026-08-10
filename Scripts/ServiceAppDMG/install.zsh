#!/bin/zsh

set -euo pipefail

PACKAGE_ROOT="${0:A:h}"
DEPLOY_TOOL="$PACKAGE_ROOT/Tools/torrentcore-service-app-deploy.zsh"

usage() {
    cat <<'EOF'
Usage:
  ./install.zsh plan
  ./install.zsh dry-run
  ./install.zsh apply --confirm
  ./install.zsh verify
  ./install.zsh history
  ./install.zsh rollback --dry-run --history <apply-history.json>
  ./install.zsh rollback --confirm --history <apply-history.json>

The mounted DMG supplies its own package path. No machine manifest is required.
EOF
}

[[ -x "$DEPLOY_TOOL" ]] || {
    print -ru2 -- "Embedded deployment tool is missing: $DEPLOY_TOOL"
    exit 1
}

if (( $# == 0 )); then
    usage
    exit 0
fi

COMMAND="$1"
shift
for argument in "$@"; do
    case "$argument" in
        --package-root|--package-root=*)
            print -ru2 -- "The mounted DMG package path is supplied automatically."
            exit 2
            ;;
    esac
done

case "$COMMAND" in
    plan|apply|verify|history|rollback)
        exec "$DEPLOY_TOOL" --package-root "$PACKAGE_ROOT" "$COMMAND" "$@"
        ;;
    dry-run)
        exec "$DEPLOY_TOOL" --package-root "$PACKAGE_ROOT" apply --dry-run "$@"
        ;;
    --help|-h|help)
        usage
        ;;
    *)
        print -ru2 -- "Unknown command: $COMMAND"
        usage >&2
        exit 2
        ;;
esac
