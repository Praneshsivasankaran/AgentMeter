#!/usr/bin/env python3
"""Static production privacy guard; run from the developer checkout, never in the app."""
from pathlib import Path
import plistlib,re,sys,subprocess
root=Path(__file__).resolve().parents[1]
info=plistlib.loads((root/'AgentMeter/Info.plist').read_bytes())
errors=[f'Unnecessary privacy description: {k}' for k in info if k.endswith('UsageDescription')]
for p in (root/'AgentMeter').rglob('*'):
 if p.suffix not in {'.swift','.c','.h','.plist'}:continue
 s=p.read_text()
 for rule in [r'\bimport\s+(MusicKit|MediaPlayer|StoreKit|ScreenCaptureKit|ScriptingBridge)\b',r'\b(MPMediaLibrary|SKCloudServiceController|MusicAuthorization|AXIsProcessTrusted|AXUIElementCreateApplication|CGRequestScreenCaptureAccess|CGWindowListCreateImage|CGDisplayCreateImage|CGRequestPostEventAccess|CGRequestListenEventAccess|NSAppleScript|OSAScript)\b',r'NS(DocumentsFolder|DesktopFolder|DownloadsFolder|AppleMusic)UsageDescription',r'/(Users/[^/]+|~)/(Documents|Desktop|Downloads|Music)/']:
  if re.search(rule,s):errors.append(f'{p.relative_to(root)}: forbidden API/path pattern {rule}')
for p in root.rglob('*.entitlements'):
 if any(part.startswith('.') for part in p.relative_to(root).parts):continue
 if plistlib.loads(p.read_bytes()):errors.append(f'Unexpected entitlements: {p.relative_to(root)}')
project=plistlib.loads((root/'AgentMeter.xcodeproj/project.pbxproj').read_bytes())
for o in project['objects'].values():
 if o.get('isa')=='XCBuildConfiguration' and o.get('buildSettings',{}).get('ENABLE_APP_SANDBOX') not in (None,'NO'):
  errors.append('App Sandbox unexpectedly enabled')
if len(sys.argv)>1:
 app=Path(sys.argv[1]);built=plistlib.loads((app/'Contents/Info.plist').read_bytes())
 errors += [f'Built app privacy description: {k}' for k in built if k.endswith('UsageDescription')]
 binary=app/'Contents/MacOS'/built['CFBundleExecutable']
 links=subprocess.check_output(['/usr/bin/otool','-L',str(binary)],text=True)
 for framework in ['MusicKit','MediaPlayer','StoreKit']:
  if '/'+framework+'.framework/' in links:errors.append(f'Unexpected media/store framework: {framework}')
print('\n'.join(errors) if errors else 'PASS: production privacy descriptions, APIs, protected paths and entitlements audit')
sys.exit(bool(errors))
