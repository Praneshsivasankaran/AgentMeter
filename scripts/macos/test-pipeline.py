#!/usr/bin/env python3
"""Local/CI distribution guard tests. Never sign or submit; requires a built app."""
import os, pathlib, plistlib, shutil, subprocess, sys, tempfile, unittest
ROOT = pathlib.Path(__file__).resolve().parents[2]
APP = str(pathlib.Path(sys.argv.pop(1)).resolve())
SCRIPTS = ROOT / 'scripts/macos'
class DistributionGuards(unittest.TestCase):
    def test_setup_is_copy_only_and_notch_has_no_forced_dark_scheme(self):
        setup = (ROOT/'macos/AgentMeter/Views/SetupView.swift').read_text()
        flow = (ROOT/'macos/AgentMeter/Presentation/SetupFlow.swift').read_text()
        for forbidden in ('Process(', 'Subprocess.', 'NSAppleScript', 'OSAScript', 'URLSession', 'readLine('):
            self.assertNotIn(forbidden, setup + flow)
        self.assertIn('model.refreshAction()', setup)
        self.assertIn('NSPasteboard.general.setString(command', setup)
        notch = (ROOT/'macos/AgentMeter/Views/NotchView.swift').read_text()
        self.assertNotIn('environment(\\.colorScheme, .dark)', notch)
        self.assertNotIn('Native. Local. No AgentMeter account.',
                         (ROOT/'macos/AgentMeter/Views/MainView.swift').read_text())
    def test_production_name_without_packaging(self):
        r = self.run_script('create-dmg.sh', APP, '--print-production-name')
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual(r.stdout.splitlines()[-1], 'AgentMeter-1.1.1-macos.dmg')
    def test_rejects_placeholder_identity_and_incoherent_versions(self):
        for key, value in [('CFBundleIdentifier', 'local.agentmeter.mac'),
                           ('CFBundleShortVersionString', '0.1.0'),
                           ('CFBundleVersion', '2'),
                           ('AgentMeterReleaseVersion', '0.1.0-beta.3')]:
            with self.subTest(key=key), tempfile.TemporaryDirectory() as directory:
                app = pathlib.Path(directory) / 'AgentMeter.app'
                shutil.copytree(APP, app)
                path = app / 'Contents/Info.plist'
                info = plistlib.loads(path.read_bytes()); info[key] = value
                path.write_bytes(plistlib.dumps(info))
                result = subprocess.run([sys.executable, str(SCRIPTS/'validate-bundle.py'), str(app)],
                                        capture_output=True, text=True)
                self.assertNotEqual(result.returncode, 0)
    def run_script(self, name, *args):
        env = os.environ.copy()
        env.pop('DEVELOPER_ID_APPLICATION', None)
        env.pop('NOTARY_PROFILE', None)
        return subprocess.run([str(SCRIPTS/name), *args], env=env, text=True, capture_output=True)
    def test_shell_syntax(self):
        for path in SCRIPTS.glob('*.sh'):
            self.assertEqual(subprocess.run(['/bin/bash', '-n', str(path)]).returncode, 0)
    def test_sign_defaults_to_plan_without_identity(self):
        r = self.run_script('sign-app.sh', APP)
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertIn('PLAN:', r.stdout)
        self.assertIn('--options runtime', r.stdout)
        self.assertIn('no signing command executed', r.stdout)
    def test_notary_defaults_to_plan_without_credentials(self):
        r = self.run_script('notarize.sh', APP)
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertIn('notarytool submit', r.stdout)
        self.assertIn('no Apple submission', r.stdout)
    def test_sign_requires_real_identity(self):
        r = self.run_script('sign-app.sh', APP, '--execute')
        self.assertNotEqual(r.returncode, 0)
        self.assertIn('Set DEVELOPER_ID_APPLICATION', r.stderr)
    def test_notary_requires_existing_profile(self):
        r = self.run_script('notarize.sh', APP, '--execute')
        self.assertNotEqual(r.returncode, 0)
        self.assertIn('Set NOTARY_PROFILE', r.stderr)
    def test_unsigned_cannot_pass_signed_verification(self):
        self.assertNotEqual(self.run_script('verify-release.sh', APP, '--signed').returncode, 0)
    def test_unknown_modes_rejected(self):
        for name in ('sign-app.sh', 'notarize.sh', 'create-dmg.sh', 'verify-release.sh'):
            self.assertNotEqual(self.run_script(name, APP, '--typo').returncode, 0)
    def test_unsigned_bundle_validation(self):
        r = self.run_script('verify-release.sh', APP, '--unsigned')
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertIn('NOT NOTARIZED', r.stdout)
unittest.main()
