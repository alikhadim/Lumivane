# Benchmarks

No measurements recorded yet.

Record each scenario with `./scripts/benchmark.sh <scenario> 120 --gpu` and regenerate this file with `python3 tools/bench_report.py`.
Scenarios: `desktop`, `youtube-4k`, `netflix`, `vlc-4k-hdr`, `gaming` (and `baseline` with `--no-sync`).

## Definitions and limits
- CPU: process CPU time as a percentage of one core (100 = one full core), sampled by the app every 0.5 s.
- Memory: resident set size of the app from `ps`.
- GPU: `powermetrics` GPU hardware active residency for the whole system, not only this app. Compare with the idle row.
- Screen capture: delay between the display presenting a frame and the app receiving it (ScreenCaptureKit).
- Screen to bridge: frame presentation to the Entertainment packet leaving the Mac. Sound to bridge: audio buffer end to packet send.
- Bridge round trip: REST request to the bridge, a proxy for local network and bridge response time.
- Not measured: time from the bridge to visible light. That needs an external camera or photodiode.
- Protected video (some streaming apps) is captured as black; the pipeline cost is still measured, the light output is not representative.
