"""Focused, disposable tests for the optional third managed app and helper files."""

import importlib.util
import json
import os
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


SCRIPT = Path(__file__).resolve().parents[1] / "Scripts/ServiceAppDMG/torrentcore_service_app_deploy.py"
SPEC = importlib.util.spec_from_file_location("torrentcore_service_app_deploy", SCRIPT)
assert SPEC and SPEC.loader
deploy = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(deploy)


class WebUI2DeploymentTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.package = self.root / "package"
        self.package.mkdir()
        self.home = self.root / "home"
        self.home.mkdir()
        self.arch = patch.object(deploy, "current_runtime", return_value="osx-arm64")
        self.arch.start()
        self.addCleanup(self.arch.stop)
        self.user_home = patch.object(deploy.Path, "home", return_value=self.home)
        self.user_home.start()
        self.addCleanup(self.user_home.stop)

    def context(self, include_webui2=True):
        apps = {
            "service": {"path": "payload/osx-arm64/TorrentCoreService.app", "runtime": "osx-arm64", "sha256": "x"},
            "webUi": {"path": "payload/osx-arm64/TorrentCoreWebUI.app", "runtime": "osx-arm64", "sha256": "y"},
        }
        if include_webui2:
            apps["webUi2"] = {"path": "payload/osx-arm64/TorrentCoreWebUI2.app", "runtime": "osx-arm64", "sha256": "z"}
        (self.package / "release.json").write_text(json.dumps({"product": deploy.PRODUCT, "releaseId": "test", "managedApps": apps}))
        return deploy.Context(self.package)

    def stage_helpers(self, ctx):
        for relative in deploy.RUNTIME_SCRIPT_FILES:
            path = ctx.runtime_scripts / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(f"content for {relative}")
            path.chmod(0o755 if path.suffix == ".zsh" else 0o644)

    def test_old_two_app_package_does_not_select_webui2(self):
        ctx = self.context(include_webui2=False)
        self.assertIsNone(ctx.webui2_metadata)
        self.assertIsNone(ctx.webui2_payload)

    def test_runtime_scripts_are_allowlisted_and_exclude_environment(self):
        ctx = self.context()
        self.stage_helpers(ctx)
        deploy.verify_runtime_scripts(ctx)
        (ctx.runtime_scripts / "torrentcore.env").write_text("SECRET=machine-local")
        with self.assertRaisesRegex(deploy.DeployError, "supported helper set"):
            deploy.verify_runtime_scripts(ctx)

    def test_install_helpers_preserves_machine_environment_and_nested_paths(self):
        ctx = self.context()
        self.stage_helpers(ctx)
        ctx.env_file.parent.mkdir(parents=True)
        ctx.env_file.write_bytes(b"custom environment\n")
        deploy.install_runtime_helpers(ctx, "test")
        deploy.verify_runtime_helpers_installed(ctx)
        self.assertEqual(ctx.env_file.read_bytes(), b"custom environment\n")
        self.assertEqual((ctx.torrentcore_home / "Scripts/lib/torrentcore-common.zsh").read_text(),
                         "content for lib/torrentcore-common.zsh")

    def test_empty_new_profile_directory_has_no_profile_content_hash(self):
        ctx = self.context()
        self.assertIsNone(deploy.profile_hash(ctx.webui2_profiles))
        ctx.webui2_profiles.mkdir(parents=True)
        self.assertIsNone(deploy.profile_hash(ctx.webui2_profiles))
        (ctx.webui2_profiles / "browser.json").write_text('{"profiles":[]}')
        self.assertIsNotNone(deploy.profile_hash(ctx.webui2_profiles))

    def test_backup_records_relative_helper_names_and_saves_prior_files(self):
        ctx = self.context()
        prior = ctx.torrentcore_home / "Scripts/lib/torrentcore-common.zsh"
        prior.parent.mkdir(parents=True)
        prior.write_text("prior helper")
        ctx.env_file.write_text("HOST_SETTING=custom\n")
        with patch.object(deploy, "archive_directory", side_effect=lambda source, archive: archive.touch() or True):
            backup, metadata = deploy.create_backup(ctx)
        self.assertEqual(set(metadata["runtimeScriptExisted"]), set(deploy.RUNTIME_SCRIPT_FILES))
        self.assertTrue(metadata["runtimeScriptExisted"]["lib/torrentcore-common.zsh"])
        self.assertFalse(metadata["runtimeScriptExisted"]["agentstatus.zsh"])
        self.assertEqual((backup / "RuntimeScripts/lib/torrentcore-common.zsh").read_text(), "prior helper")
        self.assertEqual((backup / "Scripts/torrentcore.env").read_text(), "HOST_SETTING=custom\n")

    def test_rollback_rejects_other_release_before_mutation(self):
        ctx = self.context()
        history = self.root / "old-history.json"
        history.write_text(json.dumps({"action": "apply", "status": "applied", "releaseId": "old"}))
        with self.assertRaisesRegex(deploy.DeployError, "same mounted release"):
            deploy.rollback_plan(ctx, history)

    def test_rollback_preflight_rejects_missing_prior_helper(self):
        ctx = self.context()
        backup = self.root / "backup"
        backup.mkdir()
        metadata = {
            "product": deploy.PRODUCT, "releaseId": "test", "webUi2AppExisted": False,
            "webUi2DirectoryExisted": False, "webUi2LaunchAgentExisted": False,
            "webUi2SettingsSha256": None, "webUi2ProfilesSha256": None,
            "environmentSha256": None,
            "runtimeScriptExisted": {name: name == "agentstatus.zsh" for name in deploy.RUNTIME_SCRIPT_FILES},
        }
        (backup / "backup.json").write_text(json.dumps(metadata))
        history = self.root / "history.json"
        history.write_text(json.dumps({"action": "apply", "status": "applied", "releaseId": "test",
                                       "home": str(ctx.user_home), "backupRoot": str(backup)}))
        with self.assertRaisesRegex(deploy.DeployError, "lacks runtime helper"):
            deploy.rollback_plan(ctx, history)

    def test_replace_staged_apps_adds_third_target_and_retains_previous(self):
        ctx = self.context()
        ctx.apps_home.mkdir(parents=True)
        staged = []
        for app in (ctx.service_app, ctx.webui_app, ctx.webui2_app):
            app.mkdir()
            (app / "old").write_text("old")
            new = ctx.apps_home / f".{app.name}.staging"
            new.mkdir()
            (new / "new").write_text("new")
            staged.append(new)
        retired = deploy.replace_staged_apps(ctx, staged[0], staged[1], "test", staged[2])
        self.assertEqual(len(retired), 3)
        for app, old in zip((ctx.service_app, ctx.webui_app, ctx.webui2_app), retired):
            self.assertTrue((app / "new").is_file())
            self.assertTrue((old / "old").is_file())

    def test_third_app_replace_failure_restores_all_previous_apps(self):
        ctx = self.context()
        ctx.apps_home.mkdir(parents=True)
        staged = []
        for app in (ctx.service_app, ctx.webui_app, ctx.webui2_app):
            app.mkdir()
            (app / "old").write_text("old")
            staging = ctx.apps_home / f".{app.name}.staging"
            staging.mkdir()
            (staging / "new").write_text("new")
            staged.append(staging)

        actual_replace = os.replace

        def fail_third_stage(source, target):
            if source == staged[2] and target == ctx.webui2_app:
                raise OSError("injected third-app replacement failure")
            return actual_replace(source, target)

        with patch.object(deploy.os, "replace", side_effect=fail_third_stage):
            with self.assertRaisesRegex(OSError, "injected third-app replacement failure"):
                deploy.replace_staged_apps(ctx, staged[0], staged[1], "test", staged[2])

        for app in (ctx.service_app, ctx.webui_app, ctx.webui2_app):
            self.assertTrue((app / "old").is_file())
            self.assertFalse((app / "new").exists())
            self.assertFalse((app.parent / f".{app.name}.test.previous").exists())


if __name__ == "__main__":
    unittest.main()
