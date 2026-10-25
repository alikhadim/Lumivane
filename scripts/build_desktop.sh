#!/bin/bash
# Builds the Windows and Linux desktop apps from desktop/ into desktop/dist/.
#   scripts/build_desktop.sh            tests + both targets
#   scripts/build_desktop.sh --skip-tests
# Needs the .NET 8 SDK. Cross-compiles, so it runs on macOS, Linux or Windows (Git Bash).
set -euo pipefail
cd "$(dirname "$0")/../desktop"
VERSION="$(sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' src/Lumivane.App/Lumivane.App.csproj | head -1)"
DIST="$PWD/dist"; rm -rf "$DIST"; mkdir -p "$DIST"
if [ "${1:-}" != "--skip-tests" ]; then dotnet test tests/Lumivane.Core.Tests -c Release; fi

publish() { dotnet publish src/Lumivane.App -c Release -r "$1" --self-contained -p:DebugType=none -p:DebugSymbols=false -o "$2" >/dev/null; }

W="$DIST/win/Lumivane"; publish win-x64 "$W"
cp packaging/windows/README.txt "$W/README.txt"
(cd "$DIST/win" && zip -qr "$DIST/Lumivane-Windows-x64-$VERSION.zip" Lumivane)

L="$DIST/linux/lumivane"; publish linux-x64 "$L"
cp packaging/linux/install.sh packaging/linux/lumivane.desktop packaging/linux/lumivane.png "$L/"
chmod +x "$L/Lumivane" "$L/install.sh"
tar -C "$DIST/linux" -czf "$DIST/Lumivane-Linux-x64-$VERSION.tar.gz" lumivane

rm -rf "$DIST/win" "$DIST/linux"
(cd "$DIST" && (sha256sum * 2>/dev/null || shasum -a 256 *) > SHA256SUMS.txt)
ls -lh "$DIST"
