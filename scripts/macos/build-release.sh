#!/bin/bash
set -euo pipefail
source "$(dirname "$0")/common.sh"
[[ $# == 0 ]] || fail 'Usage: build-release.sh'
mkdir -p "$OUT"
DERIVED="$HOME/Library/Developer/Xcode/DerivedData/AgentMeterDistribution"
xcodebuild -project "$ROOT/macos/AgentMeter.xcodeproj" -scheme AgentMeter -configuration Release -derivedDataPath "$DERIVED" -destination 'platform=macOS' CODE_SIGNING_ALLOWED=NO clean build
APP="$DERIVED/Build/Products/Release/AgentMeter.app"
app_check "$APP"
[[ ! -e "$OUT/AgentMeter.app" ]] || fail 'dist/macos/AgentMeter.app already exists; move it aside before building another artifact.'
ditto --norsrc --noextattr "$APP" "$OUT/AgentMeter.app"
printf 'NOT SIGNED (Developer ID) / NOT NOTARIZED\nApp: %s\n' "$OUT/AgentMeter.app"
