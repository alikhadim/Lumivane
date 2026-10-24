#!/bin/bash
# Installs the app system-wide, replacing any previous version.
#   scripts/install.sh              build, remove old versions, install to /Applications, open the permission pane
#   scripts/install.sh --no-build   install the existing build/<app>.app
#   scripts/install.sh --purge      also delete old settings, saved bridge key and privacy grants (you pair again)
#   scripts/install.sh --uninstall  remove the app (add --purge to remove its data too)
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/brand.env
source scripts/signing.sh
[ "$(uname)" = "Darwin" ] || { echo "macOS only."; exit 1; }

BUILD=1; PURGE=0; UNINSTALL=0
for a in "$@"; do case "$a" in
  --no-build) BUILD=0;; --purge) PURGE=1;; --uninstall) UNINSTALL=1;;
  *) echo "Unknown option: $a"; exit 2;; esac; done

PB=/usr/libexec/PlistBuddy
DEST="/Applications/$APP_NAME.app"
plist() { "$PB" -c "Print :$1" "$2/Contents/Info.plist" 2>/dev/null || true; }

# Only remove bundles that are ours (current or listed pre-release bundle ID). Other apps, including the official Philips Hue Sync app, are never touched.
is_ours() {
  local id; id="$(plist CFBundleIdentifier "$1")"
  [ "$id" = "$BUNDLE_ID" ] && return 0
  local p; for p in ${PREVIOUS_BUNDLE_IDS:-}; do [ "$id" = "$p" ] && return 0; done
  return 1
}

echo "Enter your administrator password once. Nothing else will ask."
sudo -v
( while true; do sudo -n true; sleep 50; kill -0 "$$" 2>/dev/null || exit; done ) 2>/dev/null &
KEEPALIVE=$!
trap 'kill $KEEPALIVE 2>/dev/null || true' EXIT

# 1. Quit running copies
osascript -e "tell application id \"$BUNDLE_ID\" to quit" >/dev/null 2>&1 || true
pkill -x "$APP_NAME" 2>/dev/null || true
sleep 1

# 2. Remove previous versions (and note their bundle IDs for cleanup)
IDS=("$BUNDLE_ID")
for dir in /Applications "$HOME/Applications"; do
  for name in "$APP_NAME.app"; do
    app="$dir/$name"
    [ -d "$app" ] || continue
    if is_ours "$app"; then
      old="$(plist CFBundleIdentifier "$app")"; [ -n "$old" ] && IDS+=("$old")
      req="$(/usr/bin/codesign -d -r- "$app" 2>&1 || true)"
      DR_BEFORE="$(printf '%s\n' "$req" | grep '^designated' || true)"
      echo "Removing $app"; sudo rm -rf "$app"
    else
      echo "Skipping $app (not this app)."
    fi
  done
done

purge() {
  for id in $(printf '%s\n' "${IDS[@]}" | sort -u); do
    echo "Clearing data for $id"
    defaults delete "$id" >/dev/null 2>&1 || true
    rm -rf "$HOME/Library/Preferences/$id.plist" "$HOME/Library/Caches/$id" "$HOME/Library/Saved Application State/$id.savedState" "$HOME/Library/HTTPStorages/$id"
    while security delete-generic-password -s "$id" >/dev/null 2>&1; do :; done
    tccutil reset All "$id" >/dev/null 2>&1 || true
  done
}
[ "$PURGE" = "1" ] && purge
if [ "$UNINSTALL" = "1" ]; then echo "Uninstalled."; exit 0; fi

# 3. Build with a stable local signing identity, so privacy grants survive the next install
ensure_signing_identity || true
if [ "$BUILD" = "1" ] || [ ! -d "build/$APP_NAME.app" ]; then bash scripts/build_app.sh; fi
SRC="build/$APP_NAME.app"
[ -d "$SRC" ] || { echo "Missing $SRC"; exit 1; }

# 4. A grant belongs to one signing identity. If the identity changed, clear the stale grant once so macOS asks cleanly
DR_AFTER="$(/usr/bin/codesign -d -r- "$SRC" 2>&1 | grep '^designated' || true)"
if [ -n "${DR_BEFORE:-}" ] && [ "$DR_BEFORE" != "$DR_AFTER" ] && [ "$PURGE" = "0" ]; then
  tccutil reset ScreenCapture "$BUNDLE_ID" >/dev/null 2>&1 || true
  tccutil reset ListenEvent "$BUNDLE_ID" >/dev/null 2>&1 || true
fi

# 5. Install
sudo ditto "$SRC" "$DEST"
sudo chown -R root:wheel "$DEST"
sudo xattr -dr com.apple.quarantine "$DEST" 2>/dev/null || true
echo "Installed $DEST"

# 6. Launch and open the one permission macOS will not let any installer grant for you
open "$DEST"
open "x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture" || true
cat <<MSG

One step left: in the window that just opened, switch on $APP_NAME under Screen & System Audio Recording, then reopen $APP_NAME.
Also allow Local Network access if macOS asks.
macOS does not let installers, sudo or scripts grant these permissions. With the stable signing identity the grant survives future installs.
MSG
