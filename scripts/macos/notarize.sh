#!/bin/bash
set -euo pipefail
source "$(dirname "$0")/common.sh"
[[ $# -ge 1 && $# -le 2 ]] || fail 'Usage: notarize.sh APP_OR_DMG [--execute] (default: plan only)'
TARGET="$1"
[[ -e "$TARGET" ]] || fail 'Artifact not found'
MODE="${2:---dry-run}"
[[ "$MODE" == --execute || "$MODE" == --dry-run ]] || fail 'Unknown mode'
case "$TARGET" in *.app|*.dmg) ;; *) fail 'Expected .app or .dmg';; esac
if [[ "$MODE" == --dry-run ]]; then
  [[ "$TARGET" != *.app ]] || plan ditto -c -k --keepParent "$TARGET" '<temporary-app.zip>'
  plan xcrun notarytool submit '<archive>' --keychain-profile '<existing-profile>' --wait --output-format json
  plan xcrun notarytool log '<submission-id>' --keychain-profile '<existing-profile>' '<private-log.json>'
  plan xcrun stapler staple "$TARGET"
  plan xcrun stapler validate "$TARGET"
  printf 'NOT SIGNED / NOT NOTARIZED: no Apple submission or credential access performed.\n'
  exit 0
fi
[[ -n "${NOTARY_PROFILE:-}" ]] || fail 'Set NOTARY_PROFILE to an existing Keychain profile after enrollment activates.'
"$ROOT/scripts/macos/verify-release.sh" "$TARGET" --signed
mkdir -p "$OUT"
RUN=$(mktemp -d "$OUT/notary.XXXXXX")
ARCHIVE="$TARGET"
if [[ "$TARGET" == *.app ]]; then
  ARCHIVE="$RUN/app.zip"
  ditto -c -k --norsrc --noextattr --keepParent "$TARGET" "$ARCHIVE"
fi
# Logs stay local in ignored dist; do not echo raw service responses or credentials.
SUBMIT_STATUS=0
xcrun notarytool submit "$ARCHIVE" --keychain-profile "$NOTARY_PROFILE" --wait --output-format json > "$RUN/result.json" || SUBMIT_STATUS=$?
ID=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["id"])' "$RUN/result.json")
xcrun notarytool log "$ID" --keychain-profile "$NOTARY_PROFILE" "$RUN/log.json"
python3 - "$RUN/result.json" <<'CHECK'
import json, sys
assert json.load(open(sys.argv[1]))['status'] == 'Accepted', 'Notarization not accepted; inspect local result/log'
CHECK
[[ $SUBMIT_STATUS == 0 ]] || fail 'Submission command failed; inspect the local result/log.'
printf 'Accepted. Inspect the local notary log for warnings before publishing.\n'
xcrun stapler staple "$TARGET"
"$ROOT/scripts/macos/verify-release.sh" "$TARGET" --notarized
