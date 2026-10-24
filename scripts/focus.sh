#!/bin/bash
# Hides every app window except the named apps, so a benchmark sees only what matters. Reversible.
#   scripts/focus.sh "Google Chrome,VLC"     hide all other apps (empty string hides everything but Finder)
#   scripts/focus.sh --restore               bring back what was hidden
# Uses System Events; macOS asks once whether your terminal may control it (Allow).
cd "$(dirname "$0")/.."
STATE="${TMPDIR:-/tmp}/lumivane-hidden.txt"
if [ "${1:---restore}" = "--restore" ]; then
  [ -f "$STATE" ] || exit 0
  osascript - "$STATE" <<'AS' >/dev/null 2>&1
on run argv
  set names to paragraphs of (do shell script "cat " & quoted form of item 1 of argv)
  tell application "System Events"
    repeat with n in names
      if n is not "" then try
        set visible of process (n as text) to true
      end try
    end repeat
  end tell
end run
AS
  rm -f "$STATE"; exit 0
fi
osascript - "$1" <<'AS' > "$STATE"
on run argv
  set keepList to {}
  repeat with k in (paragraphs of (do shell script "echo " & quoted form of item 1 of argv & " | tr ',' '\n'"))
    if (k as text) is not "" then set end of keepList to (k as text)
  end repeat
  set out to ""
  tell application "System Events"
    repeat with p in (every process whose background only is false and visible is true)
      set n to name of p
      set keepIt to (n is "Finder")
      repeat with k in keepList
        if n contains (k as text) then set keepIt to true
      end repeat
      if not keepIt then
        try
          set visible of p to false
          set out to out & n & linefeed
        end try
      end if
    end repeat
  end tell
  return out
end run
AS
echo "hidden $(grep -c . "$STATE" 2>/dev/null || echo 0) apps"
