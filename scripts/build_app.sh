#!/bin/bash
# Builds build/<APP_NAME>.app (ad-hoc signed) and build/<APP_NAME>.dmg. Run on macOS 14+ with Xcode 16+ / Swift 6.
# Fails if the finished binary contains absolute paths from this machine.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/brand.env
# No debug info and stripped symbols keep build paths out of the binary (checked below). Path-remapping flags are not used: they break the compiler's module cache lookups.
FLAGS=(-c release --arch arm64 -debug-info-format none)
swift build "${FLAGS[@]}"
BIN="$(swift build "${FLAGS[@]}" --show-bin-path)/Lumivane"
APP="build/$APP_NAME.app"
rm -rf "$APP"; mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/$APP_NAME"
strip -x "$APP/Contents/MacOS/$APP_NAME"
sed -e "s/__APP_NAME__/$APP_NAME/g" -e "s/__BUNDLE_ID__/$BUNDLE_ID/g" -e "s/__VERSION__/$VERSION/g" -e "s/__BUILD__/$BUILD/g" \
    Resources/Info.plist.template > "$APP/Contents/Info.plist"
cp Resources/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"

# Privacy gate: no home directory, user name or machine path may ship inside the app. ALLOW_PATHS=1 skips the failure.
STR="$(mktemp)"; strings -a "$APP/Contents/MacOS/$APP_NAME" > "$STR"
LEAKS="$({ grep -E "/Users/|/home/" "$STR"; grep -wF -e "$HOME" -e "$(id -un)" -e "$(hostname -s)" "$STR"; } | sort -u || true)"  # audit:allow
rm -f "$STR"
if [ -n "$LEAKS" ]; then
  echo "Local information found in the binary:"; echo "$LEAKS" | head -20
  [ "${ALLOW_PATHS:-0}" = "1" ] || exit 1
fi

source scripts/signing.sh
sign_app "$APP" "$BUNDLE_ID"
codesign --verify --verbose "$APP"
hdiutil create -volname "$APP_NAME" -srcfolder "$APP" -ov -format UDZO "build/$APP_NAME.dmg" >/dev/null
echo "Built $APP and build/$APP_NAME.dmg"
