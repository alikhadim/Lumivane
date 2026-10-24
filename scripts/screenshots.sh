#!/bin/bash
# Captures one screenshot per section from the built app in Demo Mode. No real bridge, lights or displays are shown.
# Runs a throwaway copy under its own bundle ID, so the installed app, its permissions and its settings are never touched.
# Needs: build/<app>.app (run scripts/build_app.sh first). The first run may ask to allow Screen Recording for your terminal.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/brand.env
SRC="build/$APP_NAME.app"; OUT="docs/screenshots"
[ -d "$SRC" ] || { echo "Build first: ./scripts/build_app.sh"; exit 1; }
mkdir -p "$OUT"
TMP="$(mktemp -d)"; APP="$TMP/$APP_NAME.app"
trap 'pkill -f "$TMP/" 2>/dev/null || true; rm -rf "$TMP"' EXIT
ditto "$SRC" "$APP"
/usr/libexec/PlistBuddy -c "Set :CFBundleIdentifier $BUNDLE_ID.screenshots" "$APP/Contents/Info.plist"
codesign --force --deep --sign - --identifier "$BUNDLE_ID.screenshots" "$APP" >/dev/null 2>&1
WID_BIN="$TMP/windowid"; swiftc -O scripts/windowid.swift -o "$WID_BIN"
shot() { # section, file, extra args
  pkill -f "$TMP/" 2>/dev/null || true; sleep 1
  open -n "$APP" --args --demo --section "$1" ${3:-}
  local pid="" id="" i
  for i in $(seq 1 30); do   # up to 30 s for launch and first draw; after 15 s accept an off-screen window too
    pid="$(pgrep -f "$APP/Contents/MacOS" | head -1 || true)"
    if [ -n "$pid" ]; then
      id="$("$WID_BIN" "$pid" 2>/dev/null || true)"
      [ -z "$id" ] && [ "$i" -gt 15 ] && id="$("$WID_BIN" --any "$pid" 2>/dev/null || true)"
    fi
    [ -n "$id" ] && break; sleep 1
  done
  if [ -z "$id" ]; then
    echo "No window found for $1 (process: ${pid:-none})"
    [ -n "$pid" ] && "$WID_BIN" --dump "$pid" | head -5
    return 1
  fi
  open "$APP"; sleep 3   # bring it to the front and let the demo scene settle
  screencapture -o -x -l"$id" "$OUT/$2.png"; echo "wrote $OUT/$2.png"
}
shot home 01-home "--sync"
shot screens 02-screens "--sync"
shot sound 03-sound "--sync"
shot lights 04-lights
shot profiles 05-profiles
shot settings 06-settings
swiftc -O scripts/stripmeta.swift -o "$TMP/stripmeta" && "$TMP/stripmeta" "$OUT"/*.png
echo "Review each image before publishing."
