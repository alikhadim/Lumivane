#!/bin/bash
# Read-only check of what the app stores on this Mac and where it connects. Run after using the app, ideally while sync is on.
#   scripts/runtime_audit.sh [path/to/App.app]       default: /Applications/<name>.app
cd "$(dirname "$0")/.."
source scripts/brand.env
ID="$BUNDLE_ID"; APP="${1:-/Applications/$APP_NAME.app}"; L="$HOME/Library"
USERN="$(id -un)"; HOST="$(hostname -s)"; fail=0
note() { printf '%s\n' "$*"; }
bad() { printf 'FLAG: %s\n' "$*"; fail=1; }

note "== Saved settings (UserDefaults): keys only"
defaults read "$ID" 2>/dev/null | grep -E '^    [A-Za-z]+ =' | sed 's/ =.*//' | sed 's/^ */  /' || note "  none"
hits="$(defaults read "$ID" 2>/dev/null | grep -c -F -e "$HOME" -e "$USERN" -e "$HOST" || true)"
[ "${hits:-0}" -gt 0 ] && bad "settings contain your home path, user name or host name ($hits lines)"

note "== Files in app folders (expect none except the preferences plist)"
for d in "Application Support/$ID" "Caches/$ID" "Logs/$ID" "HTTPStorages/$ID" "WebKit/$ID" "Containers/$ID" "Saved Application State/$ID.savedState" "Cookies/$ID.binarycookies"; do
  [ -e "$L/$d" ] && { note "  $L/$d"; find "$L/$d" -type f 2>/dev/null | head -20 | sed 's/^/    /'; [ "$d" != "Caches/$ID" ] || true; }
done
ls "$L/Preferences/$ID.plist" >/dev/null 2>&1 && note "  $L/Preferences/$ID.plist"
if [ -d "$L/Caches/$ID" ]; then
  n="$(find "$L/Caches/$ID" -type f | wc -l | tr -d ' ')"; [ "$n" -gt 0 ] && bad "cache directory has $n files (check for stored network responses)"
fi

note "== Crash reports and diagnostics"
ls "$L/Logs/DiagnosticReports" 2>/dev/null | grep -i "$APP_NAME" | sed 's/^/  /' || note "  none"

note "== Recent-items and window restoration"
[ -d "$L/Saved Application State/$ID.savedState" ] && { note "  saved window state present"; strings "$L/Saved Application State/$ID.savedState"/* 2>/dev/null | grep -F -e "$HOME" -e "$USERN" -e "$HOST" | head -3 | sed 's/^/    /'; }
defaults read com.apple.recentitems 2>/dev/null | grep -i -c "$APP_NAME" | sed 's/^/  recent-items mentions: /'

note "== Keychain items (existence only, no secrets read)"
security find-generic-password -s "$ID" >/dev/null 2>&1 && note "  1+ item with service $ID" || note "  none"

note "== System log lines from the app mentioning your name, host or home (last 15 min)"
n="$(log show --last 15m --style compact --predicate "process == \"$APP_NAME\"" 2>/dev/null | grep -c -F -e "$HOME" -e "$USERN" -e "$HOST" || true)"
note "  matches: ${n:-0}"; [ "${n:-0}" -gt 0 ] && bad "system log lines include personal identifiers"

note "== Network peers of the running app"
PID="$(pgrep -x "$APP_NAME" | head -1)"
if [ -n "$PID" ]; then
  peers="$(lsof -nP -a -p "$PID" -i 2>/dev/null | awk 'NR>1{print $8, $9}' | sort -u)"
  note "${peers:-  none}" | sed 's/^/  /'
  pub="$(printf '%s\n' "$peers" | grep -oE -- '->[0-9.]+' | tr -d '>-' | grep -v -E '^(10\.|192\.168\.|172\.(1[6-9]|2[0-9]|3[01])\.|127\.|169\.254\.)' || true)"
  [ -n "$pub" ] && bad "connections to non-local addresses: $pub (expected only if the online bridge lookup was used)"
else note "  app not running"; fi

note "== Installed binary"
if [ -d "$APP" ]; then
  EXE="$APP/Contents/MacOS/$APP_NAME"
  b="$(strings -a "$EXE" | grep -E "/Users/|/home/|/Volumes/|/private/var" | head -5; strings -a "$EXE" | grep -w -F -e "$USERN" -e "$HOST" | head -5)"  # audit:allow
  [ -n "$b" ] && bad "binary contains: $b" || note "  no paths, user name or host name"
else note "  $APP not found"; fi

[ "$fail" = 0 ] && note "runtime audit: no flags" || note "runtime audit: FLAGS RAISED"
exit $fail
