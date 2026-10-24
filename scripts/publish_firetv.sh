#!/bin/bash
# Commits the Fire TV app and publishes its APK as a GitHub release. Run on your Mac, from the repo.
#   scripts/publish_firetv.sh path/to/Lumivane-FireTV-1.0.0.apk [owner/repo]
# Needs the GitHub CLI, signed in (gh auth login), and git configured with your own name and email.
set -euo pipefail
cd "$(dirname "$0")/.."
APK="${1:?Usage: scripts/publish_firetv.sh path/to/Lumivane-FireTV-<version>.apk [owner/repo]}"
[ -f "$APK" ] || { echo "No such file: $APK"; exit 1; }
command -v gh >/dev/null || { echo "Install the GitHub CLI first: brew install gh"; exit 1; }
gh auth status >/dev/null 2>&1 || { echo "Sign in first: gh auth login"; exit 1; }
REPO="${2:-$(gh api user -q .login)/Lumivane}"
VERSION="$(sed -n 's/.*versionName = "\(.*\)".*/\1/p' firetv/app/build.gradle.kts | head -1)"
TAG="firetv-v$VERSION"
NAME="Lumivane-FireTV-$VERSION.apk"
./scripts/audit.sh
echo "Commit identity: $(git config user.name) <$(git config user.email)>"
echo "Publishing $NAME to $REPO as $TAG"
read -r -p "Continue? [y/N] " a; [ "$a" = y ] || exit 1
git add .gitignore README.md firetv docs scripts
git diff --cached --quiet || git commit -q -m "Add Fire TV app $VERSION"
git push -q origin HEAD
TMP="$(mktemp -d)"; cp "$APK" "$TMP/$NAME"
SUM="$(shasum -a 256 "$TMP/$NAME" | cut -d' ' -f1)"
git tag -f "$TAG" >/dev/null && git push -q -f origin "$TAG"
sed "s/@SHA256@/$SUM/; s/@APK@/$NAME/" docs/RELEASE_NOTES_FIRETV.md > "$TMP/notes.md"
if gh release view "$TAG" --repo "$REPO" >/dev/null 2>&1; then
  gh release upload "$TAG" "$TMP/$NAME" --repo "$REPO" --clobber
  gh release edit "$TAG" --repo "$REPO" --notes-file "$TMP/notes.md"
else
  gh release create "$TAG" "$TMP/$NAME" --repo "$REPO" --title "Lumivane for Fire TV $VERSION" --notes-file "$TMP/notes.md"
fi
echo "Done: https://github.com/$REPO/releases/tag/$TAG"
