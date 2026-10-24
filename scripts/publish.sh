#!/bin/bash
# Publishes this repository to GitHub and creates the v1.0.0 release. Run on your Mac.
#   scripts/publish.sh [owner/repo]        default: <your GitHub login>/Lumivane
# Needs the GitHub CLI, signed in: brew install gh && gh auth login
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/brand.env
command -v gh >/dev/null || { echo "Install the GitHub CLI first: brew install gh"; exit 1; }
gh auth status >/dev/null 2>&1 || { echo "Sign in first: gh auth login"; exit 1; }
REPO="${1:-$(gh api user -q .login)/$APP_NAME}"
./scripts/audit.sh
[ -z "$(git status --porcelain)" ] || { echo "Uncommitted changes:"; git status --short; exit 1; }
git log -1 --format='commit author: %an <%ae>'
echo "Publishing PUBLIC repository $REPO"
read -r -p "Continue? [y/N] " a; [ "$a" = y ] || exit 1
gh repo create "$REPO" --public --source=. --remote=origin --push \
  --description "Ambient lighting for macOS: Philips Hue lights follow your screen and sound, processed on your Mac."
gh repo edit "$REPO" --add-topic macos --add-topic philips-hue --add-topic swift --add-topic ambient-lighting --add-topic screencapturekit >/dev/null 2>&1 || true
ASSET=(); [ -f "build/$APP_NAME.dmg" ] && ASSET=("build/$APP_NAME.dmg")
git tag -f "v$VERSION" >/dev/null && git push -q origin "v$VERSION"
gh release create "v$VERSION" ${ASSET[@]+"${ASSET[@]}"} --repo "$REPO" --title "$APP_NAME $VERSION" --notes-file "docs/RELEASE_NOTES_$VERSION.md"
echo "Done: https://github.com/$REPO"
