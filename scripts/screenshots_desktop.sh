#!/bin/bash
# Captures the Linux desktop app in Demo Mode, one image per section, into docs/screenshots/desktop-*.png.
# Runs the real window inside a virtual X server, so no real bridge, lights or displays appear.
# Needs: .NET 8 SDK, Xvfb, ImageMagick (import, convert).
set -euo pipefail
cd "$(dirname "$0")/../desktop"
OUT="../docs/screenshots"; mkdir -p "$OUT"
dotnet build src/Lumivane.App -c Release >/dev/null
DLL="$PWD/src/Lumivane.App/bin/Release/net8.0/Lumivane.dll"
shot() { # file, args...
  local file="$1"; shift
  local d=$((200 + RANDOM % 700)) tmp; tmp="$(mktemp -d)"
  Xvfb ":$d" -screen 0 1100x760x24 >/dev/null 2>&1 & local xp=$!
  sleep 1
  DISPLAY=":$d" dotnet "$DLL" --demo --data-dir "$tmp" "$@" >/dev/null 2>&1 & local ap=$!
  sleep 9
  DISPLAY=":$d" import -window root png:- | convert png:- -crop 960x692+10+10 +repage -strip "$OUT/$file"
  kill $ap $xp 2>/dev/null || true; wait 2>/dev/null || true; rm -rf "$tmp"
}
shot desktop-01-home.png     --section home     --appearance dark
shot desktop-02-screens.png  --section screens  --appearance dark
shot desktop-03-sound.png    --section sound    --appearance dark
shot desktop-04-lights.png   --section lights   --appearance dark
shot desktop-05-profiles.png --section profiles --appearance dark
shot desktop-06-settings.png --section settings --appearance dark
shot desktop-07-light-theme.png --section home --appearance light
ls -1 "$OUT"/desktop-*.png
