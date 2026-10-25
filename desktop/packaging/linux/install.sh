#!/bin/sh
# Installs Lumivane for the current user: no root needed. Remove with: ~/.local/opt/lumivane/uninstall.sh
set -e
here="$(cd "$(dirname "$0")" && pwd)"
dest="${XDG_DATA_HOME:-$HOME/.local/share}/../opt/lumivane"
dest="$(mkdir -p "$dest" && cd "$dest" && pwd)"
bin="$HOME/.local/bin"; apps="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
mkdir -p "$bin" "$apps"
cp -a "$here"/. "$dest"/
chmod +x "$dest/Lumivane"
ln -sf "$dest/Lumivane" "$bin/lumivane"
sed "s|@EXEC@|$dest/Lumivane|; s|@ICON@|$dest/lumivane.png|" "$dest/lumivane.desktop" > "$apps/lumivane.desktop"
cat > "$dest/uninstall.sh" <<U
#!/bin/sh
rm -f "$bin/lumivane" "$apps/lumivane.desktop" "\${XDG_CONFIG_HOME:-\$HOME/.config}/autostart/lumivane.desktop"
rm -rf "$dest"
echo "Lumivane removed. Settings stay in \${XDG_CONFIG_HOME:-\$HOME/.config}/lumivane."
U
chmod +x "$dest/uninstall.sh"
echo "Installed to $dest. Start it from your app menu or run: lumivane"
missing=""
for l in libX11.so.6 libXext.so.6 libXrandr.so.2 libpulse.so.0; do ldconfig -p 2>/dev/null | grep -q "$l" || missing="$missing $l"; done
[ -z "$missing" ] || echo "Missing system libraries:$missing (Debian/Ubuntu: sudo apt install libx11-6 libxext6 libxrandr2 libpulse0)"
[ "${XDG_SESSION_TYPE:-}" != wayland ] || echo "Note: this is a Wayland session. Screen sync needs an X11 session; sound sync and light control work either way."
