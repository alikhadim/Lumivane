#!/bin/bash
# Audit, clean build, tests and warning gates from the current checkout. Run from any location. Fails on any problem,
# including zero tests executed or any compiler warning in project sources.
set -euo pipefail
cd "$(dirname "$0")/.."
LOG="$(mktemp)"; trap 'rm -f "$LOG"' EXIT

echo "== Audit"; ./scripts/audit.sh

echo "== Clean"; rm -rf .build build

echo "== Test"
swift test 2>&1 | tee "$LOG" | grep -E "error:|failed|Executed [0-9]+ tests?, with" | tail -5 || true
RAN="$(grep -E "Executed [0-9]+ tests?, with" "$LOG" | tail -1 | sed -E 's/.*Executed ([0-9]+) tests?, with ([0-9]+) failures?.*/\1 \2/' || true)"
N="${RAN% *}"; F="${RAN#* }"
EXPECTED="$(grep -rhE "func test[A-Za-z0-9_]*\(" Tests | wc -l | tr -d ' ')"
[ -n "$RAN" ] && [ "${N:-0}" -gt 0 ] || { echo "FAIL: no XCTest tests executed"; exit 1; }
[ "$N" = "$EXPECTED" ] || { echo "FAIL: $N tests executed, $EXPECTED found in Tests/"; exit 1; }
[ "$F" = 0 ] || { echo "FAIL: $F test failures"; exit 1; }
echo "tests: $N/$N passed"
# (The separate 'Test run with 0 tests' line comes from the Swift Testing library, which this package does not use.)

echo "== Build (release) with strict concurrency checking"
./scripts/build_app.sh 2>&1 | tee "$LOG" | grep -v "^\[" | tail -8
swift build -c release --arch arm64 -debug-info-format none -Xswiftc -strict-concurrency=complete 2>&1 | tee -a "$LOG" >/dev/null || { echo "FAIL: strict build failed"; tail -30 "$LOG"; exit 1; }

echo "== Warnings"
W="$(grep -E "(/Sources/|/Tests/).*warning:|^warning:" "$LOG" | sort -u || true)"
if [ -n "$W" ]; then echo "$W" | head -40; echo "FAIL: $(echo "$W" | wc -l | tr -d ' ') warnings"; exit 1; fi
echo "warnings: 0"
echo "== Done"
