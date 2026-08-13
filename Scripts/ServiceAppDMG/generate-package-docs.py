#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import tempfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
from zoneinfo import ZoneInfo

PDF_LATEX_HEADER = r"""
\usepackage{fvextra}
\usepackage{xurl}
\usepackage{seqsplit}
\usepackage{array}
\usepackage{ragged2e}
\sloppy
\setlength{\emergencystretch}{3em}
\fvset{breaklines=true,breakanywhere=true,fontsize=\small}
\makeatletter
\AtBeginDocument{%
  \RecustomVerbatimEnvironment{Verbatim}{Verbatim}{breaklines,breakanywhere,fontsize=\small}%
  \@ifundefined{Highlighting}{}{%
    \RecustomVerbatimEnvironment{Highlighting}{Verbatim}{breaklines,breakanywhere,fontsize=\small,commandchars=\{\}}%
  }%
}
\makeatother
"""


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Generate the established TorrentCore package Markdown, PDFs, and helper scripts."
    )
    parser.add_argument("--package-root", required=True)
    parser.add_argument("--pdf-tool", default="pandoc")
    parser.add_argument("--pdf-engine", default="tectonic")
    parser.add_argument("--skip-pdf", action="store_true")
    parser.add_argument("--require-pdf", action="store_true")
    return parser.parse_args()


def load_json(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8-sig") as handle:
        value = json.load(handle)
    if not isinstance(value, dict):
        raise ValueError(f"Expected JSON object: {path}")
    return value


def local_time(value: str | None = None) -> str:
    zone = ZoneInfo("America/New_York")
    if value:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    else:
        parsed = datetime.now(timezone.utc)
    return parsed.astimezone(zone).replace(microsecond=0).isoformat()


def fenced_shell(command: str) -> str:
    return f"```zsh\n{command}\n```"


def helper_script(command: str, *extra: str) -> str:
    arguments = " ".join(("./install.zsh", command, *extra))
    return f'''#!/usr/bin/env zsh
set -euo pipefail

PACKAGE_ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$PACKAGE_ROOT"
exec {arguments}
'''


def render_readme(release: dict[str, Any]) -> str:
    managed = release["managedApps"]
    protected = "\n".join(f"- `{item}`" for item in release.get("protectedFiles", []))
    return f"""# TorrentCore Package Summary

Generated: `{local_time()}`

## Release

- Product: `TorrentCore`
- Installation: `{release['installation']}`
- Machine: `{release['machine']}`
- Runtime: `{release['runtime']}`
- Release ID: `{release['releaseId']}`
- Component version: `{release['componentVersion']}`
- Git SHA: `{release['gitSha']}`
- Built at: `{local_time(str(release['builtAtUtc']))}`

## Changes

{release['notes']}

## Package Scope

This package always deploys the complete managed TorrentCore runtime for one installation host:

- `TorrentCoreService.app`
- `TorrentCoreWebUI.app`
- both LaunchAgents and their deployment helpers

The package also includes `TorrentCore.app` for the established manual drag to `/Applications`. The managed installer
does not install, replace, back up, or control the native UI.

Target home:

- `{release['targetHome']}`

## Managed Apps Included

- Service: `{managed['service']['path']}` (`{managed['service']['bundleIdentifier']}`, version `{managed['service']['version']}`, build `{managed['service']['build']}`)
- WebUI: `{managed['webUi']['path']}` (`{managed['webUi']['bundleIdentifier']}`, version `{managed['webUi']['version']}`, build `{managed['webUi']['build']}`)

## Protected Host-Local Files

These files are preserved and are not packaged as live runtime state:

{protected}

## Important Rules

- Service and WebUI are always planned, backed up, installed, and verified together.
- Existing `Service` and `WebUI` working directories are preserved in full.
- `Scripts/torrentcore.env` remains host-local and is never overwritten by this package.
- `WebUI/Config/service-connection.json` is excluded from the package and preserved byte-for-byte when present.
- VPN `Disabled`, `Ready`, and `Degraded` are valid after installation when Service API health succeeds and WebUI is reachable.

## Payload Layout

- `payload/{release['runtime']}/TorrentCoreService.app`
- `payload/{release['runtime']}/TorrentCoreWebUI.app`
- `TorrentCore.app`

Open `Runbook.md` or `Runbook.pdf` for the manual deployment procedure for this package.
"""


def render_runbook(release: dict[str, Any]) -> str:
    return f"""# TorrentCore Runbook

Generated: `{local_time()}`

## Target

- Installation: `{release['installation']}`
- Machine: `{release['machine']}`
- Runtime: `{release['runtime']}`
- Target home: `{release['targetHome']}`
- Release ID: `{release['releaseId']}`

## Changes

{release['notes']}

## Preconditions

- Mount this DMG on `{release['machine']}`.
- Open Terminal in the mounted DMG root.
- Confirm the existing `{release['targetHome']}/Service` working directory is present.
- Review `{release['targetHome']}/Scripts/torrentcore.env` when it exists; the package preserves it.
- Review and run the helper scripts from this package root.

Recommended order:

- `./plan.zsh`
- `./dry-run.zsh`
- `./backup.zsh`
- `./apply.zsh`
- `./verify.zsh`

## Plan

Run:

{fenced_shell('./plan.zsh')}

## Dry-run

Run:

{fenced_shell('./dry-run.zsh')}

## Backup

Run:

{fenced_shell('./backup.zsh')}

## Apply

Run only during an explicitly approved deployment window:

{fenced_shell('./apply.zsh')}

## Verify

Run:

{fenced_shell('./verify.zsh')}

## Install TorrentCore Native UI

After `./verify.zsh` succeeds, quit the existing TorrentCore UI. Drag `TorrentCore.app` onto the `Applications` link
in the mounted DMG and choose **Replace** if prompted. Launch the UI and confirm its Dashboard connects to the verified
Service.

## Notes

- The package helper scripts invoke `./install.zsh` with the mounted package root automatically.
- The Service health endpoint is `GET /api/health`.
- `torrentcore.env` and the complete Service/WebUI working directories are preserved.
- `WebUI/Config/service-connection.json` never comes from the release machine.
"""


def render_pdf(markdown_path: Path, pdf_path: Path, tool: str, engine: str) -> None:
    if shutil.which(tool) is None:
        raise FileNotFoundError(f"PDF tool not found: {tool}")
    with tempfile.NamedTemporaryFile("w", suffix=".tex", delete=False, encoding="utf-8") as handle:
        handle.write(PDF_LATEX_HEADER)
        header_path = Path(handle.name)
    try:
        subprocess.run([
            tool, str(markdown_path), "--from", "gfm", "--pdf-engine", engine,
            "--include-in-header", str(header_path), "--output", str(pdf_path),
        ], check=True)
    finally:
        header_path.unlink(missing_ok=True)


def main() -> int:
    args = parse_args()
    package_root = Path(args.package_root).expanduser().resolve()
    release = load_json(package_root / "release.json")
    outputs = {
        "README.md": render_readme(release),
        "Runbook.md": render_runbook(release),
        "plan.zsh": helper_script("plan"),
        "dry-run.zsh": helper_script("dry-run"),
        "backup.zsh": helper_script("backup"),
        "apply.zsh": helper_script("apply", "--confirm"),
        "verify.zsh": helper_script("verify"),
    }
    for name, content in outputs.items():
        path = package_root / name
        path.write_text(content, encoding="utf-8")
        if path.suffix == ".zsh":
            path.chmod(0o755)
        print(f"Wrote {path}")
    if args.skip_pdf:
        if args.require_pdf:
            raise RuntimeError("PDF generation was skipped but --require-pdf was supplied.")
        return 0
    failures: list[str] = []
    for name in ("README.md", "Runbook.md"):
        markdown = package_root / name
        pdf = markdown.with_suffix(".pdf")
        try:
            render_pdf(markdown, pdf, args.pdf_tool, args.pdf_engine)
            print(f"Wrote {pdf}")
        except Exception as error:  # noqa: BLE001
            failures.append(f"{pdf.name}: {error}")
    if failures and args.require_pdf:
        raise RuntimeError("; ".join(failures))
    for failure in failures:
        print(f"PDF generation warning: {failure}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
