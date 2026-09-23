#!/usr/bin/env python3
"""Reject unexpected bundle structure before distribution. No credentials are read."""
import pathlib, plistlib, re, subprocess, sys
app = pathlib.Path(sys.argv[1]).resolve()
p = plistlib.loads((app / 'Contents/Info.plist').read_bytes())
assert p['CFBundleIdentifier'] == 'io.github.praneshsivasankaran.agentmeter', 'Unexpected bundle identifier'
assert p['CFBundleExecutable'] == 'AgentMeter', 'Unexpected executable'
for key in ('CFBundleShortVersionString', 'CFBundleVersion', 'AgentMeterReleaseVersion'):
    assert re.fullmatch(r'[0-9][A-Za-z0-9.\-]*', p[key]), 'Invalid version'
assert p['CFBundleShortVersionString'] == p['AgentMeterReleaseVersion'] == '1.1.1', 'Production version mismatch'
assert p['CFBundleVersion'] == '1', 'Production build mismatch'
assert p['LSMinimumSystemVersion'] == '14.0', 'Review changed deployment target'
allowed = {'Contents', 'Contents/MacOS', 'Contents/Resources'}
for path in app.rglob('*'):
    assert not path.is_symlink(), 'Unexpected bundle symlink'
    rel = path.relative_to(app).as_posix()
    if path.is_dir():
        assert rel in allowed or rel.startswith('Contents/_CodeSignature'), 'Review new nested code/resources before signing'
    else:
        assert rel in {'Contents/Info.plist', 'Contents/PkgInfo', 'Contents/MacOS/AgentMeter'} or rel.startswith(('Contents/Resources/', 'Contents/_CodeSignature/')), 'Unexpected bundle file'
        assert path.suffix not in {'.p8', '.p12', '.key', '.log'}, 'Forbidden artifact'
        if rel.startswith('Contents/Resources/'):
            assert 'Mach-O' not in subprocess.check_output(['/usr/bin/file', '-b', str(path)], text=True), 'Unreviewed nested executable'
arches = set(subprocess.check_output(['/usr/bin/lipo', '-archs', str(app/'Contents/MacOS/AgentMeter')], text=True).split())
assert arches == {'arm64', 'x86_64'}, f'Unexpected architectures: {arches}'
print('Bundle:', p['AgentMeterReleaseVersion'], 'build', p['CFBundleVersion'], 'arm64 + x86_64; Intel runtime not physically validated')
