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
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.request import urlopen


class DeployError(RuntimeError):
    pass


SERVICE_LABEL = "com.torrentcore.service"
WEBUI_LABEL = "com.torrentcore.webui"
SERVICE_BUNDLE_ID = "com.conadv.torrentcore.service"
WEBUI_BUNDLE_ID = "com.conadv.torrentcore.webui"
SERVICE_APP_NAME = "TorrentCoreService.app"
WEBUI_APP_NAME = "TorrentCoreWebUI.app"
PRODUCT = "TorrentCoreManagedApps"
TOOL_VERSION = "2"


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


def tree_hash(root: Path) -> str:
    digest = hashlib.sha256()
    for path in sorted(root.rglob("*"), key=lambda item: item.relative_to(root).as_posix()):
        relative = path.relative_to(root).as_posix()
        digest.update(relative.encode())
        digest.update(b"\0")
        if path.is_symlink():
            digest.update(b"L" + os.readlink(path).encode())
        elif path.is_file():
            digest.update(b"F")
            with path.open("rb") as handle:
                for chunk in iter(lambda: handle.read(1024 * 1024), b""):
                    digest.update(chunk)
        elif path.is_dir():
            digest.update(b"D")
        digest.update(b"\0")
    return digest.hexdigest()


def file_hash(path: Path) -> str | None:
    if not path.is_file():
        return None
    return hashlib.sha256(path.read_bytes()).hexdigest()


def current_runtime() -> str:
    machine = platform.machine().lower()
    if machine != "arm64":
        raise DeployError(f"This combined release supports only Arm64; host architecture is {machine or '<unknown>'}.")
    return "osx-arm64"


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
        relative_text = parts[1].strip().removeprefix("*")
        relative = Path(relative_text)
        if relative.is_absolute() or ".." in relative.parts:
            raise DeployError(f"Unsafe checksum path on line {line_number}: {relative_text}")
        path = package_root / relative
        if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != parts[0].lower():
            raise DeployError(f"Package checksum mismatch: {relative_text}")


class Context:
    def __init__(self, package_root: Path):
        self.package_root = package_root.expanduser().resolve()
        if not self.package_root.is_dir():
            raise DeployError(f"Mounted package root was not found: {self.package_root}")
        self.release = load_json(self.package_root / "release.json")
        if self.release.get("product") != PRODUCT:
            raise DeployError("release.json does not describe a combined TorrentCore managed-app package.")
        self.runtime = current_runtime()
        apps = self.release.get("managedApps")
        if not isinstance(apps, dict):
            raise DeployError("release.json has no managedApps metadata.")
        self.service_metadata = self._app_metadata(apps, "service", SERVICE_APP_NAME)
        self.webui_metadata = self._app_metadata(apps, "webUi", WEBUI_APP_NAME)
        self.service_payload = self.package_root / self.service_metadata["path"]
        self.webui_payload = self.package_root / self.webui_metadata["path"]
        self.service_verifier = self.package_root / "Tools/verify-macos-service-app.zsh"
        self.webui_verifier = self.package_root / "Tools/verify-macos-webui-app.zsh"
        self.user_home = Path.home().resolve()
        self.torrentcore_home = self.user_home / "TorrentCore"
        self.service_home = self.torrentcore_home / "Service"
        self.webui_home = self.torrentcore_home / "WebUI"
        self.connection_file = self.webui_home / "Config/service-connection.json"
        self.logs_home = self.torrentcore_home / "Logs"
        self.env_file = self.torrentcore_home / "Scripts/torrentcore.env"
        self.deploy_home = self.torrentcore_home / "DeploymentState"
        self.history_home = self.deploy_home / "history"
        self.backups_home = self.torrentcore_home / ".backups"
        self.installed_path = self.deploy_home / "installed.json"
        self.apps_home = self.user_home / "Applications/TorrentCore"
        self.service_app = self.apps_home / SERVICE_APP_NAME
        self.webui_app = self.apps_home / WEBUI_APP_NAME
        self.service_agent = self.user_home / "Library/LaunchAgents" / f"{SERVICE_LABEL}.plist"
        self.webui_agent = self.user_home / "Library/LaunchAgents" / f"{WEBUI_LABEL}.plist"

    def _app_metadata(self, apps: dict[str, Any], key: str, expected_name: str) -> dict[str, str]:
        value = apps.get(key)
        if not isinstance(value, dict):
            raise DeployError(f"release.json has no {key} managed app.")
        relative = Path(str(value.get("path", "")))
        if relative.is_absolute() or ".." in relative.parts or relative.name != expected_name:
            raise DeployError(f"Unsafe {key} payload path in release.json.")
        if value.get("runtime") != self.runtime:
            raise DeployError(f"The {key} payload runtime does not match this host.")
        return {"path": relative.as_posix(), "sha256": str(value.get("sha256", ""))}


def verify_app_payload(payload: Path, metadata: dict[str, str], verifier: Path, label: str) -> None:
    if not payload.is_dir():
        raise DeployError(f"{label} payload was not found: {payload}")
    observed = tree_hash(payload).lower()
    if not metadata["sha256"] or observed != metadata["sha256"].lower():
        raise DeployError(f"{label} payload checksum mismatch.")
    if not os.access(verifier, os.X_OK):
        raise DeployError(f"Packaged {label} verifier is missing or not executable: {verifier}")
    result = shell([str(verifier), "--bundle", str(payload), "--require-signed"], check=False, capture=True)
    if result.returncode != 0:
        raise DeployError(f"Packaged {label} verification failed: {result.stderr.strip() or result.stdout.strip()}")


def verify_source(ctx: Context) -> None:
    verify_package_checksums(ctx.package_root)
    verify_app_payload(ctx.service_payload, ctx.service_metadata, ctx.service_verifier, "Service app")
    verify_app_payload(ctx.webui_payload, ctx.webui_metadata, ctx.webui_verifier, "WebUI app")
    if any(ctx.package_root.rglob("service-connection.json")):
        raise DeployError("The package contains machine-local Config/service-connection.json.")


def verify_target_structure(ctx: Context) -> None:
    if not ctx.torrentcore_home.is_dir() or not ctx.service_home.is_dir():
        raise DeployError("Existing ~/TorrentCore and ~/TorrentCore/Service directories are required for this upgrade.")


def read_version(path: Path) -> str:
    if not path.is_file():
        return "not recorded"
    try:
        value = load_json(path)
        return f"{value.get('version', 'unknown')} ({value.get('build', 'unknown')})"
    except DeployError:
        return "unreadable"


def print_plan(ctx: Context, *, dry_run: bool) -> None:
    verify_source(ctx)
    verify_target_structure(ctx)
    print("TorrentCore Combined Managed-App Deployment Plan")
    print(f"Release:             {ctx.release['releaseId']}")
    print(f"Installation:        {ctx.release.get('installation', 'unspecified')}")
    print(f"Runtime:             {ctx.runtime}")
    print(f"Service version:     {read_version(ctx.service_home / 'version.json')}")
    print(f"WebUI version:       {read_version(ctx.webui_home / 'version.json')}")
    print(f"Service app target:  {ctx.service_app}")
    print(f"WebUI app target:    {ctx.webui_app}")
    print(f"Service files:       {ctx.service_home}")
    print(f"WebUI files:         {ctx.webui_home}")
    print(f"Connection override: {ctx.connection_file} ({'present and preserved' if ctx.connection_file.is_file() else 'absent; fallback retained'})")
    print(f"Environment file:    {ctx.env_file}")
    print(f"Backups:             {ctx.backups_home}")
    print(f"Service LaunchAgent: {ctx.service_agent}")
    print(f"WebUI LaunchAgent:   {ctx.webui_agent}")
    print("\nPlanned actions:")
    print("  1. Reverify both signed Arm64 app bundles and all package checksums.")
    print("  2. Back up both apps, both complete working directories, both LaunchAgents, and deployment state.")
    print("  3. Stop both LaunchAgents only after both payloads pass preflight.")
    print("  4. Stage, verify, and atomically replace TorrentCoreService.app and TorrentCoreWebUI.app.")
    print("  5. Preserve all external state, including WebUI/Config/service-connection.json byte-for-byte.")
    print("  6. Install both LaunchAgents and verify Service API health/version plus WebUI reachability.")
    print("Dry-run only. Nothing was changed." if dry_run else "Plan only. Nothing was changed.")


def archive_directory(source: Path, archive: Path) -> bool:
    if not source.exists():
        return False
    if not source.is_dir() or source.is_symlink():
        raise DeployError(f"Backup source is not a directory: {source}")
    archive.parent.mkdir(parents=True, exist_ok=True)
    result = shell(["/usr/bin/ditto", "-c", "-k", "--sequesterRsrc", "--keepParent", str(source), str(archive)], check=False, capture=True)
    if result.returncode != 0 or not archive.is_file():
        raise DeployError(f"Could not archive {source}: {result.stderr.strip() or result.stdout.strip()}")
    return True


def create_backup(ctx: Context) -> tuple[Path, dict[str, Any]]:
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    backup = ctx.backups_home / f"{stamp}-{ctx.release['releaseId']}"
    if backup.exists():
        raise DeployError(f"Backup path already exists: {backup}")
    backup.mkdir(parents=True)
    entries = {
        "serviceAppExisted": ctx.service_app.is_dir(),
        "webUiAppExisted": ctx.webui_app.is_dir(),
        "serviceDirectoryExisted": ctx.service_home.is_dir(),
        "webUiDirectoryExisted": ctx.webui_home.is_dir(),
        "serviceLaunchAgentExisted": ctx.service_agent.is_file(),
        "webUiLaunchAgentExisted": ctx.webui_agent.is_file(),
        "connectionSha256": file_hash(ctx.connection_file),
    }
    for source, name in ((ctx.service_app, f"{SERVICE_APP_NAME}.zip"), (ctx.webui_app, f"{WEBUI_APP_NAME}.zip"), (ctx.service_home, "Service.zip"), (ctx.webui_home, "WebUI.zip")):
        if source.exists():
            archive_directory(source, backup / name)
    for source in (ctx.service_agent, ctx.webui_agent, ctx.env_file, ctx.installed_path):
        if source.is_file():
            relative = (Path("LaunchAgents") / source.name if source in (ctx.service_agent, ctx.webui_agent)
                        else Path("Scripts/torrentcore.env") if source == ctx.env_file
                        else Path("DeploymentState/installed.before.json"))
            target = backup / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, target)
    metadata = {"schemaVersion": 2, "product": PRODUCT, "releaseId": ctx.release["releaseId"], "createdAtUtc": utc_now(), "home": str(ctx.user_home), "backupRoot": str(backup), **entries}
    write_json_atomic(backup / "backup.json", metadata)
    return backup, metadata


def launchctl_domain() -> str:
    return f"gui/{os.getuid()}"


def stop_agent(label: str) -> None:
    result = shell(["launchctl", "bootout", f"{launchctl_domain()}/{label}"], check=False, capture=True)
    if result.returncode != 0:
        detail = (result.stderr + result.stdout).lower()
        if not any(text in detail for text in ("could not find service", "service not found", "no such process")):
            raise DeployError(f"Could not stop {label}: {result.stderr.strip() or result.stdout.strip()}")


def stage_app(payload: Path, target: Path, verifier: Path, history_id: str) -> Path:
    target.parent.mkdir(parents=True, exist_ok=True)
    staging = target.parent / f".{target.name}.{history_id}.staging"
    if staging.exists():
        raise DeployError(f"App staging path already exists: {staging}")
    shell(["/usr/bin/ditto", str(payload), str(staging)])
    result = shell([str(verifier), "--bundle", str(staging), "--require-signed"], check=False, capture=True)
    if result.returncode != 0:
        shutil.rmtree(staging, ignore_errors=True)
        raise DeployError(f"Staged app verification failed: {result.stderr.strip() or result.stdout.strip()}")
    return staging


def replace_staged_apps(ctx: Context, service_staging: Path, webui_staging: Path, history_id: str) -> list[Path]:
    pairs = ((ctx.service_app, service_staging), (ctx.webui_app, webui_staging))
    retired: list[Path] = []
    try:
        for target, staging in pairs:
            previous = target.parent / f".{target.name}.{history_id}.previous"
            if previous.exists():
                raise DeployError(f"App retirement path already exists: {previous}")
            if target.exists():
                os.replace(target, previous)
                retired.append(previous)
            os.replace(staging, target)
    except Exception:
        for target, _ in reversed(pairs):
            previous = target.parent / f".{target.name}.{history_id}.previous"
            if previous.exists():
                if target.exists():
                    shutil.rmtree(target)
                os.replace(previous, target)
        raise
    return retired


def configured_url(ctx: Context, variable: str, default: str) -> str:
    if not ctx.env_file.is_file():
        value = default
    else:
        command = f"source {str(ctx.env_file)!r}; print -r -- ${{{variable}:-{default}}}"
        result = shell(["zsh", "-c", command], check=False, capture=True)
        if result.returncode != 0 or not result.stdout.strip():
            raise DeployError(f"Could not read {variable} from {ctx.env_file}.")
        value = result.stdout.strip().split(";")[0].strip()
    return value.rstrip("/").replace("0.0.0.0", "127.0.0.1").replace("localhost", "127.0.0.1")


def wait_for_json(url: str, timeout_seconds: int = 45) -> dict[str, Any]:
    deadline = time.monotonic() + timeout_seconds
    last_error = "no response"
    while time.monotonic() < deadline:
        try:
            with urlopen(url, timeout=3) as response:
                value = json.loads(response.read().decode())
                if isinstance(value, dict):
                    return value
        except (OSError, URLError, json.JSONDecodeError) as error:
            last_error = str(error)
        time.sleep(1)
    raise DeployError(f"Service did not respond at {url}: {last_error}")


def wait_for_webui(url: str, timeout_seconds: int = 45) -> None:
    deadline = time.monotonic() + timeout_seconds
    last_error = "no response"
    while time.monotonic() < deadline:
        try:
            with urlopen(url + "/", timeout=3) as response:
                body = response.read()
                if response.status == 200 and body:
                    return
        except (OSError, HTTPError, URLError) as error:
            last_error = str(error)
        time.sleep(1)
    raise DeployError(f"WebUI did not respond at {url}/: {last_error}")


def verify_agent(path: Path, program: Path, bundle_id: str, working: Path, label: str) -> None:
    if not path.is_file():
        raise DeployError(f"{label} LaunchAgent is missing: {path}")
    with path.open("rb") as handle:
        value = plistlib.load(handle)
    if value.get("ProgramArguments") != [str(program)]:
        raise DeployError(f"{label} LaunchAgent does not use the installed app launcher.")
    if value.get("AssociatedBundleIdentifiers") != [bundle_id] or value.get("WorkingDirectory") != str(working):
        raise DeployError(f"{label} LaunchAgent bundle association or working directory is incorrect.")


def verify_installed(ctx: Context, *, require_snapshot: bool, check_connection: bool = False, expected_connection_hash: str | None = None) -> dict[str, Any]:
    for verifier, app, label in ((ctx.service_verifier, ctx.service_app, "Service"), (ctx.webui_verifier, ctx.webui_app, "WebUI")):
        result = shell([str(verifier), "--bundle", str(app), "--require-signed"], check=False, capture=True)
        if result.returncode != 0:
            raise DeployError(f"Installed {label} app verification failed: {result.stderr.strip() or result.stdout.strip()}")
    verify_agent(ctx.service_agent, ctx.service_app / "Contents/MacOS/TorrentCoreService", SERVICE_BUNDLE_ID, ctx.service_home, "Service")
    verify_agent(ctx.webui_agent, ctx.webui_app / "Contents/MacOS/TorrentCoreWebUI", WEBUI_BUNDLE_ID, ctx.webui_home, "WebUI")
    if check_connection and expected_connection_hash != file_hash(ctx.connection_file):
        raise DeployError("WebUI Config/service-connection.json changed during deployment.")
    service_url = configured_url(ctx, "TORRENTCORE_SERVICE_URLS", "http://0.0.0.0:7033")
    health = wait_for_json(service_url + "/api/health")
    if health.get("status") != "ok":
        raise DeployError(f"Service health is not ok: {health.get('status')}")
    host = wait_for_json(service_url + "/api/host/status")
    if str(host.get("serviceVersion")) != str(ctx.release.get("version")):
        raise DeployError("Running Service version does not match the combined release.")
    phase = str(host.get("vpnConnectionPhase", ""))
    if phase not in {"Disabled", "Ready", "Degraded"}:
        raise DeployError(f"Running Service reported an unexpected VPN state: {phase or '<missing>'}")
    webui_url = configured_url(ctx, "TORRENTCORE_WEBUI_URLS", "http://0.0.0.0:7053")
    wait_for_webui(webui_url)
    if require_snapshot and load_json(ctx.installed_path).get("releaseId") != ctx.release.get("releaseId"):
        raise DeployError("Installed deployment record does not match this release.")
    return {"health": "ok", "vpnConnectionPhase": phase, "serviceBaseUrl": service_url, "webUiBaseUrl": webui_url}


def apply(ctx: Context, *, confirm: bool, dry_run: bool) -> None:
    if dry_run:
        print_plan(ctx, dry_run=True)
        return
    if not confirm:
        raise DeployError("A real apply requires --confirm after reviewing dry-run.")
    if os.geteuid() == 0:
        raise DeployError("Run the installer as the target user, not with sudo or as root.")
    verify_source(ctx)
    verify_target_structure(ctx)
    connection_before = file_hash(ctx.connection_file)
    history_id = datetime.now().strftime("%Y%m%d-%H%M%S") + f"_{ctx.release['releaseId']}_apply"
    backup, backup_metadata = create_backup(ctx)
    history_path = ctx.history_home / f"{history_id}.json"
    history = {"schemaVersion": 2, "toolVersion": TOOL_VERSION, "action": "apply", "status": "applying", "historyId": history_id, "releaseId": ctx.release["releaseId"], "startedAtUtc": utc_now(), "home": str(ctx.user_home), "backupRoot": str(backup), "backup": backup_metadata}
    write_json_atomic(history_path, history)
    service_staging: Path | None = None
    webui_staging: Path | None = None
    retired: list[Path] = []
    try:
        service_staging = stage_app(ctx.service_payload, ctx.service_app, ctx.service_verifier, history_id)
        webui_staging = stage_app(ctx.webui_payload, ctx.webui_app, ctx.webui_verifier, history_id)
        stop_agent(WEBUI_LABEL)
        stop_agent(SERVICE_LABEL)
        retired = replace_staged_apps(ctx, service_staging, webui_staging, history_id)
        service_staging = webui_staging = None
        for app in (ctx.service_app, ctx.webui_app):
            installer = app / "Contents/Resources/Deployment/install.zsh"
            result = shell([str(installer), str(app)], check=False, capture=True)
            if result.returncode != 0:
                raise DeployError(f"{app.name} installer failed: {result.stderr.strip() or result.stdout.strip()}")
        verification = verify_installed(ctx, require_snapshot=False, check_connection=True, expected_connection_hash=connection_before)
        for path in retired:
            shutil.rmtree(path)
    except Exception as error:
        for staging in (service_staging, webui_staging):
            if staging is not None:
                shutil.rmtree(staging, ignore_errors=True)
        history.update(status="failed", completedAtUtc=utc_now(), error=str(error))
        write_json_atomic(history_path, history)
        raise DeployError(f"Installation failed. Manual recovery material is at {backup}; history: {history_path}: {error}") from error

    installed = {"schemaVersion": 2, "product": PRODUCT, "releaseId": ctx.release["releaseId"], "version": ctx.release["version"], "build": ctx.release["build"], "gitSha": ctx.release["gitSha"], "runtime": ctx.runtime, "serviceAppPath": str(ctx.service_app), "webUiAppPath": str(ctx.webui_app), "serviceHome": str(ctx.service_home), "webUiHome": str(ctx.webui_home), "webUiConnectionSha256": connection_before, "backupRoot": str(backup), "installedAtUtc": utc_now()}
    write_json_atomic(ctx.installed_path, installed)
    history.update(status="applied", completedAtUtc=utc_now(), verification=verification)
    write_json_atomic(history_path, history)
    print("TorrentCore Service and WebUI installation completed.")
    print(f"Service app: {ctx.service_app}\nWebUI app:   {ctx.webui_app}\nBackup:     {backup}\nHistory:    {history_path}")


def backup(ctx: Context) -> None:
    if os.geteuid() == 0:
        raise DeployError("Run the backup as the target user, not with sudo or as root.")
    verify_source(ctx)
    verify_target_structure(ctx)
    backup_path, metadata = create_backup(ctx)
    history_id = datetime.now().strftime("%Y%m%d-%H%M%S") + f"_{ctx.release['releaseId']}_backup"
    history_path = ctx.history_home / f"{history_id}.json"
    write_json_atomic(history_path, {
        "schemaVersion": 2,
        "toolVersion": TOOL_VERSION,
        "action": "backup",
        "status": "backedUp",
        "historyId": history_id,
        "releaseId": ctx.release["releaseId"],
        "completedAtUtc": utc_now(),
        "home": str(ctx.user_home),
        "backupRoot": str(backup_path),
        "backup": metadata,
    })
    print("TorrentCore Service and WebUI backup completed.")
    print(f"Backup:  {backup_path}")
    print(f"History: {history_path}")


def rollback_plan(ctx: Context, history_path: Path) -> tuple[dict[str, Any], Path, dict[str, Any]]:
    history = load_json(history_path)
    if history.get("action") != "apply" or history.get("status") not in {"applied", "applying", "failed"}:
        raise DeployError("Recovery history must be a combined apply or failed-apply record.")
    if Path(str(history.get("home", ""))) != ctx.user_home:
        raise DeployError("Recovery history belongs to a different user home.")
    backup = Path(str(history.get("backupRoot", "")))
    metadata = load_json(backup / "backup.json")
    if metadata.get("releaseId") != history.get("releaseId") or metadata.get("product") != PRODUCT:
        raise DeployError("Recovery backup and apply history do not match.")
    return history, backup, metadata


def rollback(ctx: Context, history_path: Path, *, confirm: bool, dry_run: bool) -> None:
    history, backup, metadata = rollback_plan(ctx, history_path)
    print("TorrentCore Combined Manual-Recovery Plan")
    print(f"Apply history: {history_path}\nBackup:        {backup}\nService app:   {ctx.service_app}\nWebUI app:     {ctx.webui_app}")
    print("Both complete external working directories and both LaunchAgents will be restored from the same backup without cleanup.")
    if dry_run:
        print("Dry-run only. Nothing was changed.")
        return
    if not confirm:
        raise DeployError("A real recovery requires --confirm after reviewing rollback --dry-run.")
    stop_agent(WEBUI_LABEL)
    stop_agent(SERVICE_LABEL)
    for app, key, archive_name in ((ctx.service_app, "serviceAppExisted", f"{SERVICE_APP_NAME}.zip"), (ctx.webui_app, "webUiAppExisted", f"{WEBUI_APP_NAME}.zip")):
        if app.exists():
            shutil.rmtree(app)
        if metadata.get(key):
            shell(["/usr/bin/ditto", "-x", "-k", str(backup / archive_name), str(app.parent)])
    for home, key, archive_name in ((ctx.service_home, "serviceDirectoryExisted", "Service.zip"), (ctx.webui_home, "webUiDirectoryExisted", "WebUI.zip")):
        if metadata.get(key):
            shell(["/usr/bin/ditto", "-x", "-k", str(backup / archive_name), str(home.parent)])
    if metadata.get("connectionSha256") != file_hash(ctx.connection_file):
        raise DeployError("WebUI service connection did not restore byte-for-byte.")
    for agent, key, label in ((ctx.service_agent, "serviceLaunchAgentExisted", SERVICE_LABEL), (ctx.webui_agent, "webUiLaunchAgentExisted", WEBUI_LABEL)):
        saved = backup / "LaunchAgents" / agent.name
        if metadata.get(key):
            agent.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(saved, agent)
            shell(["launchctl", "bootstrap", launchctl_domain(), str(agent)])
            shell(["launchctl", "kickstart", "-k", f"{launchctl_domain()}/{label}"])
        elif agent.exists():
            agent.unlink()
    before = backup / "DeploymentState/installed.before.json"
    if before.is_file():
        shutil.copy2(before, ctx.installed_path)
    elif ctx.installed_path.exists():
        ctx.installed_path.unlink()
    rollback_id = datetime.now().strftime("%Y%m%d-%H%M%S") + f"_{history['releaseId']}_rollback"
    record_path = ctx.history_home / f"{rollback_id}.json"
    write_json_atomic(record_path, {"schemaVersion": 2, "toolVersion": TOOL_VERSION, "action": "rollback", "status": "restored", "historyId": rollback_id, "applyHistory": str(history_path), "releaseId": history["releaseId"], "completedAtUtc": utc_now()})
    print(f"Manual recovery completed. History: {record_path}")


def list_history(ctx: Context) -> None:
    records = sorted(ctx.history_home.glob("*.json")) if ctx.history_home.is_dir() else []
    if not records:
        print("No TorrentCore combined deployment history exists.")
        return
    for path in records:
        try:
            value = load_json(path)
            print(f"{path.name}: {value.get('action', 'unknown')} {value.get('status', 'unknown')} {value.get('releaseId', '')}")
        except DeployError:
            print(f"{path.name}: unreadable")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="TorrentCore combined Service/WebUI deployment tool")
    parser.add_argument("--package-root", required=True)
    subparsers = parser.add_subparsers(dest="command", required=True)
    subparsers.add_parser("plan")
    subparsers.add_parser("backup")
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
        elif args.command == "backup":
            backup(ctx)
        elif args.command == "apply":
            apply(ctx, confirm=args.confirm, dry_run=args.dry_run)
        elif args.command == "verify":
            result = verify_installed(ctx, require_snapshot=True)
            print(f"Verify passed. Service: {result['serviceBaseUrl']}; WebUI: {result['webUiBaseUrl']}; VPN: {result['vpnConnectionPhase']}")
        elif args.command == "rollback":
            if args.confirm == args.dry_run:
                raise DeployError("Rollback requires exactly one of --dry-run or --confirm.")
            rollback(ctx, Path(args.history).expanduser().resolve(), confirm=args.confirm, dry_run=args.dry_run)
        elif args.command == "history":
            list_history(ctx)
        return 0
    except (DeployError, OSError, subprocess.SubprocessError, plistlib.InvalidFileException) as error:
        print(f"TorrentCore combined deployment failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
