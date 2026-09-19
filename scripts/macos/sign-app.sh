#!/bin/bash
set -euo pipefail
source "$(dirname "$0")/common.sh"
[[ $# -ge 1 && $# -le 2 ]] || fail 'Usage: sign-app.sh APP [--execute] (default: plan only)'
APP="$(cd "$1" && pwd)"
MODE="${2:---dry-run}"
[[ "$MODE" == --execute || "$MODE" == --dry-run ]] || fail 'Unknown mode'
app_check "$APP"
# The current bundle has no nested code. Validator refuses newly added helpers/frameworks
# until their inside-out signing order and entitlements have been deliberately reviewed.
if [[ "$MODE" == --dry-run ]]; then
  plan /usr/bin/codesign --force --timestamp --options runtime --sign '<Developer ID Application identity>' "$APP"
  printf 'NOT SIGNED / NOT NOTARIZED: no signing command executed.\n'
  exit 0
fi
identity_check
/usr/bin/codesign --force --timestamp --options runtime --sign "$DEVELOPER_ID_APPLICATION" "$APP"
"$ROOT/scripts/macos/verify-release.sh" "$APP" --signed
