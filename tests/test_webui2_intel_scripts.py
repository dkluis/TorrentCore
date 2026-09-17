"""Exercise only file synchronization and plist rendering in disposable directories."""
import os
from pathlib import Path
import plistlib
import re
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class IntelWebUI2ScriptTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source = self.root / 'publish'
        self.target = self.root / 'installed'
        self.source.mkdir()
        self.target.mkdir()

    def sync(self, check=True):
        return subprocess.run(['zsh', '-c',
            'set -euo pipefail; export TORRENTCORE_SCRIPT_DIR="$1"; '
            'source "$1/lib/torrentcore-common.zsh"; tc_sync_webui2_directory "$2" "$3"',
            'test', str(ROOT / 'Scripts'), str(self.source), str(self.target)],
            check=check, capture_output=True, text=True)

    def test_update_preserves_profiles_and_settings_while_replacing_runtime(self):
        profiles = self.target / 'Config/browser-connections'
        profiles.mkdir(parents=True)
        saved = b'{"profiles":[{"name":"Test"}],"activeProfileId":"selected"}\n'
        (profiles / 'browser.json').write_bytes(saved)
        (self.target / 'appsettings.json').write_bytes(b'{"custom":"settings"}\n')
        (self.target / 'appsettings.Test.json').write_bytes(b'{"custom":"test"}\n')
        (self.target / 'obsolete.dll').write_text('old runtime')
        (self.source / 'appsettings.json').write_text('{"default":true}')
        (self.source / 'TorrentCore.WebUI2').write_text('new runtime')
        self.sync()
        self.assertEqual(saved, (profiles / 'browser.json').read_bytes())
        self.assertEqual(b'{"custom":"settings"}\n', (self.target / 'appsettings.json').read_bytes())
        self.assertEqual(b'{"custom":"test"}\n', (self.target / 'appsettings.Test.json').read_bytes())
        self.assertEqual('new runtime', (self.target / 'TorrentCore.WebUI2').read_text())
        self.assertFalse((self.target / 'obsolete.dll').exists())

    def test_first_copy_supplies_defaults_and_rejects_source_profile_contamination(self):
        (self.source / 'appsettings.json').write_text('{"default":true}')
        self.sync()
        self.assertEqual('{"default":true}', (self.target / 'appsettings.json').read_text())
        (self.source / 'Config').mkdir()
        (self.target / 'retained.dll').write_text('unchanged')
        result = self.sync(check=False)
        self.assertNotEqual(0, result.returncode)
        self.assertTrue((self.target / 'retained.dll').exists())
        self.assertFalse((self.target / 'Config').exists())

    def test_flat_launchagent_uses_webui2_path_port_and_no_service_override(self):
        script = (ROOT / 'Scripts/install-launch-agents.zsh').read_text()
        functions = []
        for name in ['xml_escape', 'render_env_block', 'render_plist']:
            match = re.search(r'(?ms)^' + name + r'\(\) \{\n.*?^\}$', script)
            self.assertIsNotNone(match, name)
            functions.append(match.group(0))
        output = self.root / 'com.torrentcore.webui2.plist'
        program = self.target / 'TorrentCore.WebUI2'
        env = dict(os.environ, TORRENTCORE_SERVICE_URLS='http://127.0.0.1:7033',
            TORRENTCORE_WEBUI_URLS='http://0.0.0.0:7053', TORRENTCORE_WEBUI2_URLS='http://0.0.0.0:7510',
            TORRENTCORE_WEBUI_SERVICE_BASE_URL='http://127.0.0.1:7033/', TORRENTCORE_ASPNETCORE_ENVIRONMENT='Test')
        command = 'set -euo pipefail\n' + '\n'.join(functions)
        command += '\nrender_plist "$1" "$2" com.torrentcore.webui2 "$3" "$4" "$5" "$6" webui2\n'
        subprocess.run(['zsh', '-c', command, 'test', str(ROOT / 'Scripts/com.torrentcore.webui.plist'),
            str(output), str(program), str(self.target), str(self.root / 'out.log'), str(self.root / 'err.log')],
            env=env, check=True, capture_output=True, text=True)
        plist = plistlib.loads(output.read_bytes())
        self.assertEqual('com.torrentcore.webui2', plist['Label'])
        self.assertEqual([str(program)], plist['ProgramArguments'])
        self.assertEqual(str(self.target), plist['WorkingDirectory'])
        self.assertEqual({'ASPNETCORE_ENVIRONMENT': 'Test', 'ASPNETCORE_URLS': 'http://0.0.0.0:7510'}, plist['EnvironmentVariables'])


if __name__ == '__main__':
    unittest.main()
