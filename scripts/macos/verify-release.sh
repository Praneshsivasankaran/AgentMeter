#!/bin/bash
set -euo pipefail
source "$(dirname "$0")/common.sh"
[[ $# == 2 ]] || fail 'Usage: verify-release.sh APP_OR_DMG --unsigned|--signed|--notarized'
TARGET="$1"; MODE="$2"
[[ -e "$TARGET" ]] || fail 'Artifact not found'
[[ "$MODE" == --unsigned || "$MODE" == --signed || "$MODE" == --notarized ]] || fail 'Unknown verification mode'
case "$TARGET" in
  *.app) app_check "$TARGET" ;;
  *.dmg) /usr/bin/hdiutil verify "$TARGET" ;;
  *) fail 'Expected .app or .dmg' ;;
esac
if [[ "$MODE" == --unsigned ]]; then
  printf 'NOT SIGNED (Developer ID) / NOT NOTARIZED: structural check only.\n'
else
  /usr/bin/codesign --verify --deep --strict --verbose=2 "$TARGET"
  INFO=$(/usr/bin/codesign -d --verbose=4 "$TARGET" 2>&1)
  [[ "$INFO" == *'Authority=Developer ID Application:'* ]] || fail 'Not Developer ID Application signed'
  [[ "$INFO" == *'Timestamp='* ]] || fail 'Missing secure timestamp'
  if [[ "$TARGET" == *.app ]]; then
    [[ "$INFO" == *'(runtime)'* ]] || fail 'Missing Hardened Runtime'
    ENT=$(mktemp)
    trap 'rm -f "$ENT"' EXIT
    /usr/bin/codesign -d --entitlements :- "$TARGET" > "$ENT" 2>/dev/null
    python3 - "$ENT" <<'CHECK'
import pathlib, plistlib, sys
raw = pathlib.Path(sys.argv[1]).read_bytes()
assert not raw or not plistlib.loads(raw), 'Unexpected entitlements; review before release'
CHECK
  fi
  if [[ "$MODE" == --notarized ]]; then
    xcrun stapler validate "$TARGET"
    if [[ "$TARGET" == *.app ]]; then
      /usr/sbin/spctl --assess --type execute --verbose=2 "$TARGET"
    else
      /usr/sbin/spctl --assess --type open --context context:primary-signature --verbose=2 "$TARGET"
    fi
  fi
fi
if [[ "$TARGET" == *.dmg ]]; then
  (cd "$(dirname "$TARGET")" && shasum -a 256 "$(basename "$TARGET")" > "$(basename "$TARGET").sha256")
fi
