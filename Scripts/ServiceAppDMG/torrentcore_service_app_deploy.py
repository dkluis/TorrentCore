#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
import os
import platform
import plistlib
import shutil
import subprocess
import sys
import time
import zipfile
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
from urllib.error import URLError
from urllib.request import urlopen


class DeployError(RuntimeError):
    pass


LABEL = "com.torrentcore.service"
BUNDLE_IDENTIFIER = "com.conadv.torrentcore.service"
APP_NAME = "TorrentCoreService.app"
TOOL_VERSION = "1"


def utc_now() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def load_json(path: Path) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError) as error:
        raise DeployError(f"Could not read JSON file {path}: {error}") from error
    if not isinstance(value, dict):
        raise DeployError(f"Expected a JSON object: {path}")
    return value


def write_json_atomic(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(f".{path.name}.new")
    temporary.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")
    os.replace(temporary, path)


def shell(command: list[str], *, check: bool = True, capture: bool = False) -> subprocess.CompletedProcess[str]:
    return subprocess.run(command, check=check, text=True, capture_output=capture)


def payload_hash(root: Path) -> str:
    digest = hashlib.sha256()
    for path in sorted(root.rglob("*"), key=lambda item: item.relative_to(root).as_posix()):
        relative = path.relative_to(root).as_posix()
        digest.update(relative.encode("utf-8"))
        digest.update(b"\0")
        if path.is_symlink():
            digest.update(b"L")
            digest.update(os.readlink(path).encode("utf-8"))
        elif path.is_file():
            digest.update(b"F")
            with path.open("rb") as handle:
                for chunk in iter(lambda: handle.read(1024 * 1024), b""):
                    digest.update(chunk)
        elif path.is_dir():
            digest.update(b"D")
        digest.update(b"\0")
    return digest.hexdigest()


def current_runtime() -> str:
    machine = platform.machine().lower()
    if machine == "arm64":
        return "osx-arm64"
    if machine in {"x86_64", "amd64"}:
        return "osx-x64"
    raise DeployError(f"Unsupported host architecture: {machine or '<unknown>'}")


def verify_package_checksums(package_root: Path) -> None:
    checksum_path = package_root / "checksums.txt"
    if not checksum_path.is_file():
        raise DeployError(f"Package checksum file is missing: {checksum_path}")
    for line_number, line in enumerate(checksum_path.read_text(encoding="utf-8").splitlines(), start=1):
        if not line.strip():
            continue
        parts = line.split(maxsplit=1)
        if len(parts) != 2 or len(parts[0]) != 64:
            raise DeployError(f"Invalid checksum entry on line {line_number}.")
        relative_text = parts[1].strip()
        if relative_text.startswith("*"):
            relative_text = relative_text[1:]
        relative = Path(relative_text)
        if relative.is_absolute() or ".." in relative.parts:
            raise DeployError(f"Unsafe checksum path on line {line_number}: {relative_text}")
        path = package_root / relative
        if not path.is_file():
            raise DeployError(f"Checksummed package file is missing: {relative_text}")
        observed = hashlib.sha256(path.read_bytes()).hexdigest()
        if observed != parts[0].lower():
            raise DeployError(f"Package checksum mismatch: {relative_text}")


class Context:
    def __init__(self, package_root: Path):
        self.package_root = package_root.expanduser().resolve()
        self.release_path = self.package_root / "release.json"
        if not self.package_root.is_dir():
            raise DeployError(f"Mounted package root was not found: {self.package_root}")
        self.release = load_json(self.release_path)
        if self.release.get("product") != "TorrentCoreServiceApp":
            raise DeployError("release.json does not describe a TorrentCore Service app package.")

        self.runtime = current_runtime()
        runtime_metadata = self.release.get("runtimes", {}).get(self.runtime)
        if not isinstance(runtime_metadata, dict):
            raise DeployError(f"This package has no payload for {self.runtime}.")
        self.runtime_metadata = runtime_metadata
        relative_payload = Path(str(runtime_metadata.get("path", "")))
        if relative_payload.is_absolute() or ".." in relative_payload.parts:
            raise DeployError("The release payload path is unsafe.")
        self.payload = self.package_root / relative_payload
        self.verifier = self.package_root / "Tools/verify-macos-service-app.zsh"

        self.user_home = Path.home()
        self.torrentcore_home = self.user_home / "TorrentCore"
        self.service_home = self.torrentcore_home / "Service"
        self.scripts_home = self.torrentcore_home / "Scripts"
        self.logs_home = self.torrentcore_home / "Logs"
        self.deploy_home = self.torrentcore_home / ".deploy"
        self.history_home = self.deploy_home / "history"
        self.backups_home = self.torrentcore_home / ".backups"
        self.env_file = self.scripts_home / "torrentcore.env"
        self.installed_path = self.deploy_home / "installed.json"
        self.app = self.user_home / "Applications/TorrentCore" / APP_NAME
        self.launch_agent = self.user_home / "Library/LaunchAgents" / f"{LABEL}.plist"


def verify_source(ctx: Context) -> None:
    verify_package_checksums(ctx.package_root)
    if not ctx.payload.is_dir():
        raise DeployError(f"Runtime payload was not found: {ctx.payload}")
    expected = str(ctx.runtime_metadata.get("sha256", "")).lower()
    observed = payload_hash(ctx.payload).lower()
    if not expected or observed != expected:
        raise DeployError(f"Payload checksum mismatch. Expected {expected or '<missing>'}, observed {observed}.")
    if not os.access(ctx.verifier, os.X_OK):
        raise DeployError(f"Packaged app verifier is missing or not executable: {ctx.verifier}")
    result = shell(
        [str(ctx.verifier), "--bundle", str(ctx.payload), "--require-signed"],
        check=False,
        capture=True,
    )
    if result.returncode != 0:
        detail = result.stderr.strip() or result.stdout.strip() or "no details"
        raise DeployError(f"Packaged app verification failed: {detail}")


def verify_target_structure(ctx: Context) -> None:
    if not ctx.torrentcore_home.is_dir():
        raise DeployError(f"Existing TorrentCore root was not found: {ctx.torrentcore_home}")
    if not ctx.service_home.is_dir():
        raise DeployError(f"Existing TorrentCore Service directory was not found: {ctx.service_home}")


def read_installed_version(ctx: Context) -> str:
    version_path = ctx.service_home / "version.json"
    if not version_path.is_file():
        return "not recorded"
    try:
        value = load_json(version_path)
        version = str(value.get("version", "unknown"))
        build = str(value.get("build", "unknown"))
        return f"{version} ({build})"
    except DeployError:
        return "unreadable"


def print_plan(ctx: Context, *, dry_run: bool) -> None:
    verify_source(ctx)
    verify_target_structure(ctx)
    print("TorrentCore Service App Deployment Plan")
    print(f"Release:          {ctx.release['releaseId']}")
    print(f"Installation:     {ctx.release.get('installation', 'unspecified')}")
    print(f"Runtime:          {ctx.runtime}")
    print(f"Current version:  {read_installed_version(ctx)}")
    print(f"App target:       {ctx.app}")
    print(f"Service files:    {ctx.service_home}")
    print(f"Logs:             {ctx.logs_home}")
    print(f"Environment file: {ctx.env_file}")
    print(f"Deployment state: {ctx.deploy_home}")
    print(f"Backups:          {ctx.backups_home}")
    print(f"LaunchAgent:       {ctx.launch_agent}")
    print("")
    print("Planned actions:")
    print("  1. Reverify the signed Arm64 app and package checksum.")
    print("  2. Back up the existing app, complete legacy Service directory, environment file, and Service LaunchAgent.")
    print("  3. Stop only com.torrentcore.service; do not stop or change the WebUI.")
    print("  4. Replace and register TorrentCoreService.app atomically.")
    print("  5. Preserve Service/appsettings.json and every existing legacy Service file.")
    print("  6. Update Service/version.json and install only the Service app LaunchAgent.")
    print("  7. Verify the app identity, LaunchAgent, API health, Service version, and installed record.")
    print("")
    if not ctx.env_file.is_file():
        print("Note: torrentcore.env is absent; the Service app installer will use the approved LAN default http://0.0.0.0:7033.")
    print("Dry-run only. No files, directories, backups, services, installed records, or history were changed." if dry_run else
          "Plan only. No files, directories, backups, services, installed records, or history were changed.")


def archive_directory(source: Path, archive: Path) -> bool:
    if not source.exists():
        return False
    if not source.is_dir() or source.is_symlink():
        raise DeployError(f"Backup source is not a directory: {source}")
    archive.parent.mkdir(parents=True, exist_ok=True)
    result = shell(
        ["/usr/bin/ditto", "-c", "-k", "--sequesterRsrc", "--keepParent", str(source), str(archive)],
        check=False,
        capture=True,
    )
    if result.returncode != 0 or not archive.is_file():
        detail = result.stderr.strip() or result.stdout.strip() or "archive was not created"
        raise DeployError(f"Could not archive {source}: {detail}")
    return True


def create_backup(ctx: Context) -> tuple[Path, dict[str, Any]]:
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    backup = ctx.backups_home / f"{stamp}-{ctx.release['releaseId']}"
    if backup.exists():
        raise DeployError(f"Backup path already exists: {backup}")
    backup.mkdir(parents=True)
    contents: dict[str, Any] = {
        "appExisted": ctx.app.is_dir(),
        "serviceDirectoryExisted": ctx.service_home.is_dir(),
        "launchAgentExisted": ctx.launch_agent.is_file(),
        "environmentFileExisted": ctx.env_file.is_file(),
    }
    if ctx.app.exists():
        archive_directory(ctx.app, backup / f"{APP_NAME}.zip")
    if ctx.service_home.exists():
        archive_directory(ctx.service_home, backup / "Service.zip")
    if ctx.launch_agent.is_file():
        target = backup / "LaunchAgents" / ctx.launch_agent.name
        target.parent.mkdir(parents=True)
        shutil.copy2(ctx.launch_agent, target)
    if ctx.env_file.is_file():
        target = backup / "Scripts/torrentcore.env"
        target.parent.mkdir(parents=True)
        shutil.copy2(ctx.env_file, target)
    if ctx.installed_path.is_file():
        target = backup / "DeploymentState/installed.before.json"
        target.parent.mkdir(parents=True)
        shutil.copy2(ctx.installed_path, target)
    metadata = {
        "schemaVersion": 1,
        "product": "TorrentCoreServiceApp",
        "releaseId": ctx.release["releaseId"],
        "createdAtUtc": utc_now(),
        "home": str(ctx.user_home),
        "backupRoot": str(backup),
        **contents,
    }
    write_json_atomic(backup / "backup.json", metadata)
    return backup, metadata


def launchctl_domain() -> str:
    return f"gui/{os.getuid()}"


def stop_service() -> None:
    result = shell(["launchctl", "bootout", f"{launchctl_domain()}/{LABEL}"], check=False, capture=True)
    if result.returncode != 0:
        detail = (result.stderr + result.stdout).lower()
        if "could not find service" not in detail and "service not found" not in detail:
            raise DeployError(f"Could not stop {LABEL}: {result.stderr.strip() or result.stdout.strip()}")


def replace_app(ctx: Context, history_id: str) -> Path | None:
    ctx.app.parent.mkdir(parents=True, exist_ok=True)
    staging = ctx.app.parent / f".{APP_NAME}.{history_id}.staging"
    retired = ctx.app.parent / f".{APP_NAME}.{history_id}.previous"
    if staging.exists() or retired.exists():
        raise DeployError("App replacement staging path already exists.")
    shell(["/usr/bin/ditto", str(ctx.payload), str(staging)])
    result = shell([str(ctx.verifier), "--bundle", str(staging), "--require-signed"], check=False, capture=True)
    if result.returncode != 0:
        shutil.rmtree(staging, ignore_errors=True)
        raise DeployError(f"Staged app verification failed: {result.stderr.strip() or result.stdout.strip()}")
    if ctx.app.exists():
        os.replace(ctx.app, retired)
    try:
        os.replace(staging, ctx.app)
    except Exception:
        if retired.exists() and not ctx.app.exists():
            os.replace(retired, ctx.app)
        raise
    return retired if retired.exists() else None


def configured_health_url(ctx: Context) -> str:
    url = "http://127.0.0.1:7033"
    if not ctx.env_file.is_file():
        return url
    command = (
        f"source {str(ctx.env_file)!r}; "
        "print -r -- ${TORRENTCORE_SERVICE_URLS:-http://0.0.0.0:7033}"
    )
    result = shell(["zsh", "-c", command], check=False, capture=True)
    if result.returncode != 0 or not result.stdout.strip():
        raise DeployError(f"Could not read TORRENTCORE_SERVICE_URLS from {ctx.env_file}.")
    first = result.stdout.strip().split(";")[0].strip().rstrip("/")
    return first.replace("0.0.0.0", "127.0.0.1").replace("localhost", "127.0.0.1")


def wait_for_json(url: str, timeout_seconds: int = 45) -> dict[str, Any]:
    deadline = time.monotonic() + timeout_seconds
    last_error = "no response"
    while time.monotonic() < deadline:
        try:
            with urlopen(url, timeout=3) as response:
                value = json.loads(response.read().decode("utf-8"))
                if isinstance(value, dict):
                    return value
                last_error = "response was not an object"
        except (OSError, URLError, json.JSONDecodeError) as error:
            last_error = str(error)
        time.sleep(1)
    raise DeployError(f"Service did not respond at {url}: {last_error}")


def verify_installed(ctx: Context, *, require_snapshot: bool) -> dict[str, Any]:
    result = shell([str(ctx.verifier), "--bundle", str(ctx.app), "--require-signed"], check=False, capture=True)
    if result.returncode != 0:
        raise DeployError(f"Installed app verification failed: {result.stderr.strip() or result.stdout.strip()}")
    if not ctx.launch_agent.is_file():
        raise DeployError(f"Service LaunchAgent is missing: {ctx.launch_agent}")
    with ctx.launch_agent.open("rb") as handle:
        plist = plistlib.load(handle)
    expected_program = str(ctx.app / "Contents/MacOS/TorrentCoreService")
    if plist.get("ProgramArguments") != [expected_program]:
        raise DeployError("Service LaunchAgent does not use the installed app launcher.")
    if plist.get("AssociatedBundleIdentifiers") != [BUNDLE_IDENTIFIER]:
        raise DeployError("Service LaunchAgent is not associated with the Service app bundle identifier.")
    if plist.get("WorkingDirectory") != str(ctx.service_home):
        raise DeployError("Service LaunchAgent working directory is not ~/TorrentCore/Service.")

    base_url = configured_health_url(ctx)
    health = wait_for_json(f"{base_url}/api/health")
    if health.get("status") != "ok":
        raise DeployError(f"Service health is not ok: {health.get('status')}")
    host = wait_for_json(f"{base_url}/api/host/status")
    if str(host.get("serviceVersion")) != str(ctx.release.get("version")):
        raise DeployError("Running Service version does not match the installed app release.")
    phase = str(host.get("vpnConnectionPhase", ""))
    if phase not in {"Disabled", "Ready", "Degraded"}:
        raise DeployError(f"Running Service reported an unexpected VPN state: {phase or '<missing>'}")
    if require_snapshot:
        installed = load_json(ctx.installed_path)
        if installed.get("releaseId") != ctx.release.get("releaseId"):
            raise DeployError("Installed deployment record does not match this release.")
    return {"health": health.get("status"), "vpnConnectionPhase": phase, "baseUrl": base_url}


def apply(ctx: Context, *, confirm: bool, dry_run: bool) -> None:
    if dry_run:
        print_plan(ctx, dry_run=True)
        return
    if not confirm:
        raise DeployError("A real apply requires --confirm after reviewing dry-run.")
    if os.geteuid() == 0:
        raise DeployError("Run the Service app installer as the target user, not with sudo or as root.")
    verify_source(ctx)
    verify_target_structure(ctx)
    started = utc_now()
    history_id = datetime.now().strftime("%Y%m%d-%H%M%S") + f"_{ctx.release['releaseId']}_apply"
    backup, backup_metadata = create_backup(ctx)
    history_path = ctx.history_home / f"{history_id}.json"
    history = {
        "schemaVersion": 1,
        "toolVersion": TOOL_VERSION,
        "action": "apply",
        "status": "applying",
        "historyId": history_id,
        "releaseId": ctx.release["releaseId"],
        "startedAtUtc": started,
        "home": str(ctx.user_home),
        "backupRoot": str(backup),
        "backup": backup_metadata,
    }
    write_json_atomic(history_path, history)
    try:
        stop_service()
        retired = replace_app(ctx, history_id)
        installer = ctx.app / "Contents/Resources/Deployment/install.zsh"
        result = shell([str(installer), str(ctx.app)], check=False, capture=True)
        if result.returncode != 0:
            raise DeployError(f"Service app installer failed: {result.stderr.strip() or result.stdout.strip()}")
        verification = verify_installed(ctx, require_snapshot=False)
        if retired is not None:
            shutil.rmtree(retired)
    except Exception as error:
        history["status"] = "failed"
        history["completedAtUtc"] = utc_now()
        history["error"] = str(error)
        write_json_atomic(history_path, history)
        raise DeployError(
            f"Installation failed. Rollback material is at {backup}; use history {history_path}: {error}"
        ) from error

    installed = {
        "schemaVersion": 1,
        "product": "TorrentCoreServiceApp",
        "releaseId": ctx.release["releaseId"],
        "version": ctx.release["version"],
        "build": ctx.release["build"],
        "gitSha": ctx.release["gitSha"],
        "runtime": ctx.runtime,
        "appPath": str(ctx.app),
        "serviceHome": str(ctx.service_home),
        "backupRoot": str(backup),
        "installedAtUtc": utc_now(),
    }
    write_json_atomic(ctx.installed_path, installed)
    history["status"] = "applied"
    history["completedAtUtc"] = utc_now()
    history["verification"] = verification
    write_json_atomic(history_path, history)
    print("TorrentCore Service app installation completed.")
    print(f"Installed app: {ctx.app}")
    print(f"Backup:       {backup}")
    print(f"History:      {history_path}")
    print(f"VPN state:    {verification['vpnConnectionPhase']}")


def rollback_plan(ctx: Context, history_path: Path) -> tuple[dict[str, Any], Path]:
    history = load_json(history_path)
    if history.get("action") != "apply" or history.get("status") not in {"applied", "applying", "failed"}:
        raise DeployError("Rollback history must be a Service app apply or failed-apply record.")
    if Path(str(history.get("home", ""))) != ctx.user_home:
        raise DeployError("Rollback history belongs to a different user home.")
    backup = Path(str(history.get("backupRoot", "")))
    metadata = load_json(backup / "backup.json")
    if metadata.get("releaseId") != history.get("releaseId"):
        raise DeployError("Rollback backup and apply history do not match.")
    return history, backup


def rollback(ctx: Context, history_path: Path, *, confirm: bool, dry_run: bool) -> None:
    history, backup = rollback_plan(ctx, history_path)
    print("TorrentCore Service App Rollback Plan")
    print(f"Apply history: {history_path}")
    print(f"Backup:        {backup}")
    print(f"App target:    {ctx.app}")
    print(f"LaunchAgent:   {ctx.launch_agent}")
    print("Legacy ~/TorrentCore/Service files will be restored from the compressed backup without cleanup.")
    if dry_run:
        print("Dry-run only. No files or services were changed.")
        return
    if not confirm:
        raise DeployError("A real rollback requires --confirm after reviewing rollback --dry-run.")
    stop_service()

    metadata = load_json(backup / "backup.json")
    if ctx.app.exists():
        shutil.rmtree(ctx.app)
    app_archive = backup / f"{APP_NAME}.zip"
    if metadata.get("appExisted"):
        shell(["/usr/bin/ditto", "-x", "-k", str(app_archive), str(ctx.app.parent)])

    service_archive = backup / "Service.zip"
    if metadata.get("serviceDirectoryExisted"):
        shell(["/usr/bin/ditto", "-x", "-k", str(service_archive), str(ctx.service_home.parent)])

    launch_backup = backup / "LaunchAgents" / ctx.launch_agent.name
    if metadata.get("launchAgentExisted"):
        ctx.launch_agent.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(launch_backup, ctx.launch_agent)
        shell(["launchctl", "bootstrap", launchctl_domain(), str(ctx.launch_agent)])
        shell(["launchctl", "kickstart", "-k", f"{launchctl_domain()}/{LABEL}"])
    elif ctx.launch_agent.exists():
        ctx.launch_agent.unlink()

    before = backup / "DeploymentState/installed.before.json"
    if before.is_file():
        shutil.copy2(before, ctx.installed_path)
    elif ctx.installed_path.exists():
        ctx.installed_path.unlink()

    rollback_id = datetime.now().strftime("%Y%m%d-%H%M%S") + f"_{history['releaseId']}_rollback"
    record = {
        "schemaVersion": 1,
        "toolVersion": TOOL_VERSION,
        "action": "rollback",
        "status": "rolledBack",
        "historyId": rollback_id,
        "applyHistory": str(history_path),
        "releaseId": history["releaseId"],
        "completedAtUtc": utc_now(),
    }
    record_path = ctx.history_home / f"{rollback_id}.json"
    write_json_atomic(record_path, record)
    print(f"Rollback completed. History: {record_path}")


def list_history(ctx: Context) -> None:
    if not ctx.history_home.is_dir():
        print("No TorrentCore Service app deployment history exists.")
        return
    records = sorted(ctx.history_home.glob("*.json"))
    if not records:
        print("No TorrentCore Service app deployment history exists.")
        return
    for path in records:
        try:
            value = load_json(path)
            print(f"{path.name}: {value.get('action', 'unknown')} {value.get('status', 'unknown')} {value.get('releaseId', '')}")
        except DeployError:
            print(f"{path.name}: unreadable")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="TorrentCore Service app deployment tool")
    parser.add_argument("--package-root", required=True)
    subparsers = parser.add_subparsers(dest="command", required=True)
    subparsers.add_parser("plan")
    apply_parser = subparsers.add_parser("apply")
    apply_parser.add_argument("--dry-run", action="store_true")
    apply_parser.add_argument("--confirm", action="store_true")
    subparsers.add_parser("verify")
    rollback_parser = subparsers.add_parser("rollback")
    rollback_parser.add_argument("--history", required=True)
    rollback_parser.add_argument("--dry-run", action="store_true")
    rollback_parser.add_argument("--confirm", action="store_true")
    subparsers.add_parser("history")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    try:
        ctx = Context(Path(args.package_root))
        if args.command == "plan":
            print_plan(ctx, dry_run=False)
        elif args.command == "apply":
            apply(ctx, confirm=args.confirm, dry_run=args.dry_run)
        elif args.command == "verify":
            verification = verify_installed(ctx, require_snapshot=True)
            print(f"Verify passed. API: {verification['baseUrl']}; VPN state: {verification['vpnConnectionPhase']}")
        elif args.command == "rollback":
            if args.confirm == args.dry_run:
                raise DeployError("Rollback requires exactly one of --dry-run or --confirm.")
            rollback(ctx, Path(args.history).expanduser().resolve(), confirm=args.confirm, dry_run=args.dry_run)
        elif args.command == "history":
            list_history(ctx)
        return 0
    except (DeployError, OSError, subprocess.SubprocessError, plistlib.InvalidFileException) as error:
        print(f"TorrentCore Service app deployment failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
