#!/bin/bash
# Scans the working tree for personal or machine-specific data before a release. Exit 1 on any hit.
set -uo pipefail
cd "$(dirname "$0")/.."
SELF="scripts/audit.sh"
fail=0
scan() { # label, extended regex
  # Lines containing "audit:allow" are exempt (the build-time leak check and its documentation name the patterns).
  local out; out="$(grep -rIn -E "$2" . --exclude-dir=.git --exclude-dir=.build --exclude-dir=build --exclude="$(basename "$SELF")" 2>/dev/null | grep -v "audit:allow" | grep -v -E "${3:-^\$}" || true)"
  [ -n "$out" ] && { echo "FOUND $1:"; echo "$out" | head -20; fail=1; }
}
scan "absolute paths" '/Users/|/home/[a-z]|/Volumes/|/private/var|C:\\Users'
scan "email addresses" '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}'
scan "private keys / tokens" 'BEGIN (RSA |EC |OPENSSH |)PRIVATE KEY|gh[pousr]_[A-Za-z0-9]{30,}|AKIA[0-9A-Z]{16}|xox[baprs]-'
scan "public IPv4 addresses" '\b((2[0-4][0-9]|25[0-5]|1?[0-9]?[0-9])\.){3}(2[0-4][0-9]|25[0-5]|1?[0-9]?[0-9])\b' '192\.168\.|0\.0\.0\.0|127\.0\.0\.1'
for n in "$(id -un)" "$(hostname -s 2>/dev/null)"; do
  [ ${#n} -ge 3 ] && [ "$n" != root ] && scan "local name '$n'" "\\b$n\\b"
done
# Binary assets: no text, EXIF or time chunks in PNGs, and no local paths in any image bytes
m="$(python3 - <<'PY'
import os, re, struct
bad = {b"tEXt", b"iTXt", b"zTXt", b"tIME"}   # eXIf only counts if it holds text (the encoder writes pixel dimensions)
for root, dirs, files in os.walk("."):
    dirs[:] = [d for d in dirs if d not in (".build", "build", ".git")]
    for f in files:
        if not f.endswith((".png", ".icns")): continue
        p = os.path.join(root, f); b = open(p, "rb").read()
        if re.search(rb"/Users/|/home/", b): print(f"{p}: contains a local path")
        if b[:8] == b"\x89PNG\r\n\x1a\n":
            i = 8
            while i + 8 <= len(b):
                n, t = struct.unpack(">I4s", b[i:i + 8])
                if t in bad or (t == b"eXIf" and re.search(rb"[ -~]{6,}", b[i + 8:i + 8 + n])): print(f"{p}: {t.decode()} chunk")
                i += 12 + n
PY
)"
[ -n "$m" ] && { echo "FOUND metadata: $m"; fail=1; }
# Debug output in app sources
d="$(grep -rIn -E '\bprint\(|NSLog\(|os_log|Logger\(|debugPrint\(' Sources/Lumivane Sources/HueKit Sources/CaptureKit Sources/HueCore || true)"
[ -n "$d" ] && { echo "FOUND debug output:"; echo "$d" | head; fail=1; }
[ "$fail" = 0 ] && echo "audit: clean" || echo "audit: FAILED"
exit $fail
