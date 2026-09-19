#!/bin/bash
set -euo pipefail
source "$(dirname "$0")/common.sh"
[[ $# == 2 ]] || fail 'Usage: create-dmg.sh APP --unsigned|--signed'
APP="$(cd "$1" && pwd)"
MODE="$2"
[[ "$MODE" == --unsigned || "$MODE" == --signed ]] || fail 'Choose --unsigned or --signed explicitly'
app_check "$APP"
if [[ "$MODE" == --signed ]]; then
  identity_check
  "$ROOT/scripts/macos/verify-release.sh" "$APP" --notarized
fi
VERSION=$(/usr/libexec/PlistBuddy -c 'Print :AgentMeterReleaseVersion' "$APP/Contents/Info.plist")
mkdir -p "$OUT"
SUFFIX=''; [[ "$MODE" != --unsigned ]] || SUFFIX='-unsigned'
DMG="$OUT/AgentMeter-$VERSION-macos$SUFFIX.dmg"
[[ ! -e "$DMG" ]] || fail 'Artifact already exists; will not overwrite.'
STAGE=$(mktemp -d "$OUT/.stage.XXXXXX")
trap 'rm -rf "$STAGE"' EXIT
ditto --norsrc --noextattr "$APP" "$STAGE/AgentMeter.app"
ln -s /Applications "$STAGE/Applications"
/usr/bin/hdiutil create -quiet -volname AgentMeter -srcfolder "$STAGE" -format UDZO "$DMG"
/usr/bin/hdiutil verify "$DMG"
if [[ "$MODE" == --signed ]]; then
  /usr/bin/codesign --timestamp --sign "$DEVELOPER_ID_APPLICATION" "$DMG"
  printf 'Signed DMG; still requires notarization/stapling.\n'
else
  printf 'NOT SIGNED / NOT NOTARIZED: local packaging test only.\n'
fi
(cd "$OUT" && shasum -a 256 "$(basename "$DMG")" > "$(basename "$DMG").sha256")
printf '%s\n' "$DMG"
