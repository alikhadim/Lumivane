#!/bin/bash
# One pass of the remaining release validation on a Mac: verify, install, walkthrough, screenshots, benchmarks, report, runtime audit.
#   ./scripts/run_all.sh [--skip verify,install,walk,shots,bench,audit]     SECS=120 sets the length of each benchmark scenario
# Needs your Hue Bridge on the network. Asks for the admin password once, at install.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/brand.env
SECS="${SECS:-120}"; SKIP=","
[ "${1:-}" = "--skip" ] && SKIP=",${2:-}," || [ $# -eq 0 ] || { echo "Usage: $0 [--skip verify,install,walk,shots,bench,audit]"; exit 2; }
skip() { [[ "$SKIP" == *",$1,"* ]]; }
step() { printf '\n==== %s\n' "$*"; }
pause() { read -r -p "$1 [Return] " _; }

skip verify || { step "1/7 Verify (audit, tests, warnings)"; ./scripts/verify.sh; }

skip install || { step "2/7 Install"; if skip verify; then ./scripts/install.sh; else ./scripts/install.sh --no-build; fi; }

skip walk || {
  step "3/7 First-run walkthrough"
  echo "Use only the app window: finish onboarding, pair the bridge, pick lights and area, choose a display, start and stop sync,"
  echo "save and switch profiles, quit and reopen it. Then answer the questions below."
  pause "Press Return when you are ready to answer"
  python3 tools/e2e_walk.py
}

skip shots || {
  step "4/7 Screenshots (Demo Mode, no real names shown)"
  pause "Leave the screen alone while the windows are captured"
  ./scripts/screenshots.sh
}

if ! skip bench; then
  step "5/7 Benchmarks ($SECS s each)"
  echo "Baseline: leave the app alone and do NOT press Start. Every other scenario: the script starts sync itself, you only start the content."
  ./scripts/benchmark.sh baseline "$SECS" --gpu --no-sync --focus ""
  BROWSER="${BROWSER:-Google Chrome}"
  read -r -p "Browser for YouTube and Netflix [$BROWSER]: " b; BROWSER="${b:-$BROWSER}"
  read -r -p "Game app name as shown in the Dock (Return = leave windows alone): " GAME
  for s in desktop youtube-4k netflix vlc-4k-hdr gaming; do
    case "$s" in youtube-4k|netflix) F=(--focus "$BROWSER");; vlc-4k-hdr) F=(--focus "VLC");; gaming) F=(); [ -z "$GAME" ] || F=(--focus "$GAME");; *) F=();; esac
    echo; echo "Scenario: $s"; read -r -p "Run it now? [Return = yes, s = skip] " a
    [ "$a" = s ] || ./scripts/benchmark.sh "$s" "$SECS" --gpu ${F[@]+"${F[@]}"}
  done
  step "6/7 Report"
  python3 tools/bench_report.py
fi

skip audit || {
  step "7/7 Runtime privacy audit"
  pkill -x "$APP_NAME" 2>/dev/null || true; sleep 1
  open -n "/Applications/$APP_NAME.app" --args --sync; sleep 20
  ./scripts/runtime_audit.sh || true
  pkill -x "$APP_NAME" 2>/dev/null || true
}

step "Done"
echo "Review docs/screenshots/*.png, docs/BENCHMARKS.md and docs/E2E_CHECKLIST.md, then send me the folder or the output above."
