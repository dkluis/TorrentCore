#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import os
import shutil
import stat
import subprocess
import sys
import zipfile
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Sequence


class ArchiveError(RuntimeError):
    pass


@dataclass(frozen=True)
class Entry:
    kind: str
    mode: int
    sha256: str = ""
    link_target: str = ""


def file_sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def snapshot_tree(root: Path) -> dict[str, Entry]:
    result: dict[str, Entry] = {}
    for path in [root, *sorted(root.rglob("*"), key=lambda item: item.as_posix())]:
        relative = "." if path == root else path.relative_to(root).as_posix()
        details = path.lstat()
        mode = stat.S_IMODE(details.st_mode)
        if stat.S_ISDIR(details.st_mode):
            result[relative] = Entry("directory", mode)
        elif stat.S_ISREG(details.st_mode):
            result[relative] = Entry("file", mode, sha256=file_sha256(path))
        elif stat.S_ISLNK(details.st_mode):
            result[relative] = Entry("symlink", mode, link_target=os.readlink(path))
        else:
            raise ArchiveError(f"Unsupported filesystem entry in release package: {path}")
    return result


def validate_paths(package_value: str, dmg_value: str | None, *, replace_existing: bool = False,
                   archive_staging_only: bool = False) -> tuple[Path, Path | None, Path]:
    requested_package = Path(package_value).expanduser().absolute()
    try:
        package_lstat = requested_package.lstat()
    except FileNotFoundError as exc:
        raise ArchiveError(f"Package root was not found: {requested_package}") from exc
    if stat.S_ISLNK(package_lstat.st_mode) or not stat.S_ISDIR(package_lstat.st_mode):
        raise ArchiveError(f"Package root must be a real directory, not a symlink: {requested_package}")
    package = requested_package.resolve(strict=True)

    home = Path.home().resolve()
    protected = [Path("/Applications"), home / "Applications", home / "TVMaze", home / "TorrentCore"]
    if any(package == item or package.is_relative_to(item) for item in protected):
        raise ArchiveError(f"Refusing to archive an active installed path: {package}")
    broad = {Path("/"), Path("/Applications"), Path("/Users"), Path("/Volumes"), Path("/private"),
             Path("/tmp"), home}
    if package in broad or package.parent == package:
        raise ArchiveError(f"Refusing to archive an overly broad root: {package}")
    if not (package / "release.json").is_file() or (package / "release.json").is_symlink():
        raise ArchiveError(f"Package root does not contain a regular release.json: {package}")

    if archive_staging_only:
        if dmg_value is not None:
            raise ArchiveError("Historical staging-only mode cannot also specify a DMG.")
        dmg = None
    else:
        if dmg_value is None:
            raise ArchiveError("A completed DMG is required unless --archive-staging-only is explicit.")
        requested_dmg = Path(dmg_value).expanduser().absolute()
        try:
            dmg_lstat = requested_dmg.lstat()
        except FileNotFoundError as exc:
            raise ArchiveError(f"Completed DMG was not found: {requested_dmg}") from exc
        if stat.S_ISLNK(dmg_lstat.st_mode) or not stat.S_ISREG(dmg_lstat.st_mode) or dmg_lstat.st_size == 0:
            raise ArchiveError(f"Completed DMG must be a non-empty regular file, not a symlink: {requested_dmg}")
        dmg = requested_dmg.resolve(strict=True)
        if dmg == package or dmg.is_relative_to(package):
            raise ArchiveError("Completed DMG must be outside the expanded package root.")

    archive = package.with_name(package.name + ".zip")
    if (archive.exists() or archive.is_symlink()) and not replace_existing:
        raise ArchiveError(f"Archive already exists and will not be overwritten: {archive}")
    if replace_existing and (archive.is_symlink() or archive.exists() and not archive.is_file()):
        raise ArchiveError(f"Existing archive must be a regular file to replace: {archive}")
    return package, dmg, archive


def verify_zip(archive: Path, package_name: str, expected: dict[str, Entry]) -> None:
    seen: dict[str, Entry] = {}
    prefix = package_name.rstrip("/") + "/"
    try:
        with zipfile.ZipFile(archive) as zipped:
            for info in zipped.infolist():
                name = info.filename
                digest = hashlib.sha256()
                member_data = bytearray()
                # Streaming every member verifies its CRC, including ditto's AppleDouble metadata,
                # without holding application binaries in memory.
                with zipped.open(info) as member:
                    for chunk in iter(lambda: member.read(1024 * 1024), b""):
                        digest.update(chunk)
                        if stat.S_ISLNK((info.external_attr >> 16) & 0xFFFF):
                            member_data.extend(chunk)
                if name.startswith("__MACOSX/"):
                    continue
                if name == package_name + "/":
                    relative = "."
                elif name.startswith(prefix):
                    relative = name[len(prefix):].rstrip("/")
                else:
                    raise ArchiveError(f"ZIP contains an entry outside {package_name}: {name}")
                if not relative:
                    continue
                full_mode = (info.external_attr >> 16) & 0xFFFF
                permissions = stat.S_IMODE(full_mode)
                if stat.S_ISLNK(full_mode):
                    entry = Entry("symlink", permissions, link_target=member_data.decode("utf-8"))
                elif info.is_dir() or stat.S_ISDIR(full_mode):
                    entry = Entry("directory", permissions)
                elif stat.S_ISREG(full_mode) or full_mode == 0:
                    entry = Entry("file", permissions, sha256=digest.hexdigest())
                else:
                    raise ArchiveError(f"ZIP contains an unsupported entry type: {name}")
                if relative in seen:
                    raise ArchiveError(f"ZIP contains a duplicate source entry: {relative}")
                seen[relative] = entry
    except (OSError, UnicodeError, zipfile.BadZipFile) as exc:
        raise ArchiveError(f"Could not verify ZIP archive {archive}: {exc}") from exc

    if seen != expected:
        missing = sorted(set(expected) - set(seen))
        extra = sorted(set(seen) - set(expected))
        changed = sorted(key for key in set(expected) & set(seen) if expected[key] != seen[key])
        raise ArchiveError(
            f"ZIP does not exactly match the source package; missing={missing}, extra={extra}, changed={changed}"
        )


def archive_package(
    package: Path,
    dmg: Path | None,
    archive: Path,
    *,
    apply: bool,
    replace_existing: bool = False,
    runner: Callable[..., subprocess.CompletedProcess[str]] = subprocess.run,
) -> None:
    expected = snapshot_tree(package)
    if not apply:
        context = (f"after verified DMG {dmg}" if dmg is not None
                   else "as operator-approved inactive historical staging without a matching DMG")
        print(f"DRY RUN: would archive {package} to {archive} {context}")
        print(f"DRY RUN: would verify {len(expected)} filesystem entries and remove the expanded package")
        return

    partial = archive.with_name(f".{archive.name}.partial.{os.getpid()}")
    removal = package.with_name(f".{package.name}.archive-removal.{os.getpid()}")
    if partial.exists() or removal.exists():
        raise ArchiveError("Temporary archive or removal path already exists; refusing to replace it.")
    renamed = False
    try:
        runner(
            ["/usr/bin/ditto", "-c", "-k", "--sequesterRsrc", "--keepParent", str(package), str(partial)],
            check=True,
            text=True,
            capture_output=True,
        )
        if not partial.is_file() or partial.is_symlink():
            raise ArchiveError("ditto did not create the expected regular ZIP archive.")
        verify_zip(partial, package.name, expected)
        if snapshot_tree(package) != expected:
            raise ArchiveError("Package source changed while its archive was being created or verified.")

        # Move the verified source name out of discovery before finalizing. A final snapshot at the
        # renamed path closes the ordinary path-change window while leaving restoration possible.
        package.rename(removal)
        renamed = True
        if snapshot_tree(removal) != expected:
            removal.rename(package)
            renamed = False
            raise ArchiveError("Package source changed before expanded staging could be removed.")

        # A hard link finalizes without an overwrite race; unlinking the temporary name leaves one archive inode.
        if replace_existing:
            os.replace(partial, archive)
        else:
            os.link(partial, archive)
            partial.unlink()
        shutil.rmtree(removal)
        renamed = False
    except subprocess.CalledProcessError as exc:
        detail = (exc.stderr or exc.stdout or str(exc)).strip()
        raise ArchiveError(f"ditto could not create the release archive: {detail}") from exc
    except OSError as exc:
        raise ArchiveError(f"Could not finalize the release archive or remove expanded staging: {exc}") from exc
    finally:
        if partial.exists():
            partial.unlink()
        if renamed and removal.exists() and not package.exists():
            removal.rename(package)

    print(f"Archived release package: {archive}")
    print(f"Removed expanded staging: {package}")


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Archive a verified macOS release package and remove its expanded staging directory."
    )
    parser.add_argument("--package-root", required=True)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--dmg-path", help="Existing completed DMG associated with this package.")
    source.add_argument("--archive-staging-only", action="store_true",
                        help="Archive operator-approved inactive historical staging without a matching DMG.")
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--apply", action="store_true", help="Create, verify, and finalize the archive, then remove staging.")
    mode.add_argument("--dry-run", action="store_true", help="Validate and report only (the default).")
    parser.add_argument("--replace-existing", action="store_true",
                        help="Replace an existing sibling ZIP only after the new archive passes verification.")
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    try:
        args = parse_args(argv)
        package, dmg, archive = validate_paths(
            args.package_root, args.dmg_path, replace_existing=args.replace_existing,
            archive_staging_only=args.archive_staging_only)
        archive_package(package, dmg, archive, apply=args.apply, replace_existing=args.replace_existing)
        return 0
    except (ArchiveError, OSError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
