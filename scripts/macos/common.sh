#!/bin/bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="$ROOT/dist/macos"
fail() { printf '%s\n' "$*" >&2; exit 1; }
app_check() {
  [[ -d "$1/Contents/MacOS" ]] || fail 'Expected an application bundle.'
  python3 "$ROOT/scripts/macos/validate-bundle.py" "$1"
  python3 "$ROOT/macos/Scripts/privacy-check.py" "$1"
}
identity_check() {
  [[ "${DEVELOPER_ID_APPLICATION:-}" == 'Developer ID Application: '* ]] || fail 'Set DEVELOPER_ID_APPLICATION to a real Developer ID Application identity after enrollment activates.'
}
plan() { printf 'PLAN:'; printf ' %q' "$@"; printf '\n'; }
