#!/bin/bash
# Commits the Windows/Linux app and publishes its downloads as a GitHub release. Run on your Mac, from the repo.
#   scripts/publish_desktop.sh Lumivane-Windows-x64-1.0.0.zip Lumivane-Linux-x64-1.0.0.tar.gz [owner/repo]
# Needs the GitHub CLI, signed in (gh auth login), and git configured with your own name and email.
set -euo pipefail
cd "$(dirname "$0")/.."
WIN="${1:?Usage: scripts/publish_desktop.sh <windows.zip> <linux.tar.gz> [owner/repo]}"; LIN="${2:?Usage: scripts/publish_desktop.sh <windows.zip> <linux.tar.gz> [owner/repo]}"
[ -f "$WIN" ] && [ -f "$LIN" ] || { echo "Both files must exist."; exit 1; }
command -v gh >/dev/null || { echo "Install the GitHub CLI first: brew install gh"; exit 1; }
gh auth status >/dev/null 2>&1 || { echo "Sign in first: gh auth login"; exit 1; }
REPO="${3:-$(gh api user -q .login)/Lumivane}"
VERSION="$(sed -n 's|.*<Version>\(.*\)</Version>.*|\1|p' desktop/src/Lumivane.App/Lumivane.App.csproj | head -1)"
TAG="desktop-v$VERSION"
./scripts/audit.sh
echo "Commit identity: $(git config user.name) <$(git config user.email)>"
echo "Publishing $(basename "$WIN") and $(basename "$LIN") to $REPO as $TAG"
read -r -p "Continue? [y/N] " a; [ "$a" = y ] || exit 1
git add .gitignore .github README.md CHANGELOG.md desktop docs scripts
git diff --cached --quiet || git commit -q -m "Add Windows and Linux app $VERSION"
git push -q origin HEAD
TMP="$(mktemp -d)"; cp "$WIN" "$LIN" "$TMP/"
(cd "$TMP" && shasum -a 256 "$(basename "$WIN")" "$(basename "$LIN")" > SHA256SUMS.txt)
git tag -f "$TAG" >/dev/null && git push -q -f origin "$TAG"
python3 - "$TMP" "$(basename "$WIN")" "$(basename "$LIN")" <<'PY'
import sys
t, w, l = sys.argv[1:]
s = open("docs/RELEASE_NOTES_DESKTOP.md").read().replace("@WIN@", w).replace("@LINUX@", l).replace("@SUMS@", open(t + "/SHA256SUMS.txt").read().strip())
open(t + "/notes.md", "w").write(s)
PY
FILES=("$TMP/$(basename "$WIN")" "$TMP/$(basename "$LIN")" "$TMP/SHA256SUMS.txt")
if gh release view "$TAG" --repo "$REPO" >/dev/null 2>&1; then
  gh release upload "$TAG" "${FILES[@]}" --repo "$REPO" --clobber
  gh release edit "$TAG" --repo "$REPO" --notes-file "$TMP/notes.md"
else
  gh release create "$TAG" "${FILES[@]}" --repo "$REPO" --title "Lumivane for Windows and Linux $VERSION" --notes-file "$TMP/notes.md"
fi
echo "Done: https://github.com/$REPO/releases/tag/$TAG"
