#!/bin/bash
# Records one benchmark scenario with the installed app: live pipeline metrics from the app (--metrics-log), memory from ps,
# optional system-wide GPU utilization from the GPU driver (ioreg, no admin rights). Output goes to benchmarks/<scenario>/. Then run tools/bench_report.py.
#   scripts/benchmark.sh <scenario> [seconds=120] [--gpu] [--no-sync]
# Scenarios used in the report: desktop, youtube-4k, netflix, vlc-4k-hdr, gaming (plus baseline with --no-sync).
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/brand.env
SCEN="${1:?usage: benchmark.sh <scenario> [seconds] [--gpu] [--no-sync]}"; shift
SECS=120; GPU=0; SYNC="--sync"; FOCUS=""; HAVE_FOCUS=0
while [ $# -gt 0 ]; do case "$1" in
  --gpu) GPU=1;; --no-sync) SYNC="";;
  --focus) FOCUS="${2-}"; HAVE_FOCUS=1; shift;;   # hide all other apps while recording; "" hides everything but Finder
  ''|*[!0-9]*) echo "Unknown option $1"; exit 2;; *) SECS="$1";;
esac; shift; done
trap './scripts/focus.sh --restore >/dev/null 2>&1 || true' EXIT
APP="${BENCH_APP:-/Applications/$APP_NAME.app}"; [ -d "$APP" ] || APP="build/$APP_NAME.app"; [ -d "$APP" ] || { echo "Install or build the app first."; exit 1; }
OUT="benchmarks/$SCEN"; rm -rf "$OUT"; mkdir -p "$OUT"
quit_app() {
  osascript -e "tell application id \"$BUNDLE_ID\" to quit" >/dev/null 2>&1 || true
  for _ in $(seq 1 10); do pgrep -x "$APP_NAME" >/dev/null || return 0; sleep 1; done
  pkill -x "$APP_NAME" 2>/dev/null || true; sleep 1
}
live_rows() { [ -f "$OUT/metrics.csv" ] && awk -F, 'NR>1 && $8>0' "$OUT/metrics.csv" | wc -l | tr -d ' ' || echo 0; }
any_rows() { [ -f "$OUT/metrics.csv" ] && awk 'END{print NR>1 ? NR-1 : 0}' "$OUT/metrics.csv" || echo 0; }   # the sync engine is running if it logged anything
quit_app
[ -n "$SYNC" ] && { echo "Waiting 15 s so the bridge releases the previous stream..."; sleep 15; }
open -n "$APP" --args $SYNC --metrics-log "$PWD/$OUT/metrics.csv"
for _ in $(seq 1 15); do pgrep -x "$APP_NAME" >/dev/null && break; sleep 1; done
PID="$(pgrep -x "$APP_NAME" | head -1 || true)"; [ -n "$PID" ] || { echo "App did not start."; exit 1; }
if [ -n "$SYNC" ]; then
  for _ in $(seq 1 45); do [ "$(live_rows)" -gt 0 ] && break; sleep 1; done
  [ "$(live_rows)" -gt 0 ] || { echo "Sync did not start within 45 s. Read the banner in the app window, fix it, then rerun: scripts/benchmark.sh $SCEN"; exit 1; }
else
  sleep 8
  [ "$(any_rows)" -eq 0 ] || { echo "Sync is running. A baseline needs the app idle: quit it, then rerun without pressing Start."; quit_app; exit 1; }
fi
if [ -n "$SYNC" ]; then echo "App running (pid $PID), sync on. Start the scenario now: $SCEN."; else echo "App running (pid $PID), sync off. Do NOT press Start syncing; leave the app alone."; fi
if [ "${AUTO:-0}" = 1 ]; then sleep "${AUTO_SETTLE:-12}"   # unattended: the content is already playing
else read -r -p "Press Return when it is playing, then keep it in the foreground for ${SECS}s. " _; fi
if [ -n "$SYNC" ]; then [ "$(live_rows)" -gt 0 ] || { echo "No light output recorded. Is sync running?"; exit 1; }
else [ "$(any_rows)" -eq 0 ] || { echo "Sync was started during the baseline. Rerun and leave the app idle."; quit_app; exit 1; }; fi
[ "$HAVE_FOCUS" = 1 ] && { echo "Hiding other apps for ${SECS}s (restored automatically)."; ./scripts/focus.sh "$FOCUS" || true; }
echo "t_s,rss_mb,cpu_s,gpu_pct" > "$OUT/rss.csv"
T0="$(date +%s)"
for ((i = 0; i < SECS; i++)); do
  kb="$(ps -o rss= -p "$PID" 2>/dev/null | tr -d ' ' || echo 0)"
  cs="$(ps -o cputime= -p "$PID" 2>/dev/null | tr -d ' ' | awk -F: '{s=0; for(i=1;i<=NF;i++) s=s*60+$i; printf "%.2f", s}')"
  gp=""; [ "$GPU" = 1 ] && gp="$(ioreg -r -d 1 -w 0 -c AGXAccelerator 2>/dev/null | grep -o '"Device Utilization %"=[0-9]*' | head -1 | cut -d= -f2 || true)"
  echo "$(( $(date +%s) - T0 )),$(awk -v k="${kb:-0}" 'BEGIN{printf "%.1f", k/1024}'),${cs:-0},$gp" >> "$OUT/rss.csv"; sleep 1
done
[ "$HAVE_FOCUS" = 1 ] && ./scripts/focus.sh --restore >/dev/null 2>&1 || true
[ -n "$SYNC" ] || [ "$(any_rows)" -eq 0 ] || { echo "Sync started by itself during the baseline; this recording is invalid and was not saved."; rm -rf "$OUT"; quit_app; exit 1; }
{ echo "scenario=$SCEN"; echo "seconds=$SECS"; echo "sync=${SYNC:+on}"; echo "date=$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  echo "chip=$(sysctl -n machdep.cpu.brand_string)"; echo "memory_gb=$(( $(sysctl -n hw.memsize) / 1073741824 ))"
  echo "macos=$(sw_vers -productVersion)"; echo "app_version=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$APP/Contents/Info.plist")"
  echo "mode=$([ -f "$OUT/metrics.csv" ] && tail -1 "$OUT/metrics.csv" | awk -F'"' '{print $2}')"; echo "t0_epoch=$T0"; } > "$OUT/info.txt"
quit_app
echo "Saved $OUT. Run: python3 tools/bench_report.py"
