# Changelog

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versioning: [SemVer](https://semver.org/).

## [Desktop 1.0.0] - 2026-10-25

Windows and Linux app, tag `desktop-v1.0.0`. The Mac app stays at 1.0.0.

### Added
- C# / Avalonia app with the Mac app's interface and features: screen and sound sync, color styles, profiles, multi-display routing, light hub, Demo Mode, diagnostics, tray control.
- Shared core: Entertainment streaming (DTLS 1.2 PSK), REST fallback, route planner, color engine, audio analysis; 66 tests.
- Windows backend: GDI screen and window capture, WASAPI system and microphone audio, per-app audio through process loopback, DPAPI secret storage, autostart, toasts.
- Linux backend: X11 (XShm, XRandR) capture, PulseAudio system, microphone and per-app audio, keyring or 0600 file secrets, XDG autostart; 30 tests, most against Xvfb and PulseAudio.
- `scripts/build_desktop.sh`, `scripts/publish_desktop.sh`, `scripts/screenshots_desktop.sh`, `docs/DESKTOP.md`.

### Known limitations
- Screen sync on Linux needs an X11 session (no Wayland capture).
- The Windows-specific code is verified by one manual run on a single machine, not by automated tests.

## [1.0.0] - 2026-10-25

First public release.

### Added
- Screen sync: ScreenCaptureKit capture, GPU zone reduction (Metal, CPU fallback), Hue Entertainment streaming over DTLS 1.2 PSK, REST fallback.
- Multi-display routing: single, mirror and extend modes; per-light display assignment; display hot-plug fallback.
- Sound sync: system or per-app audio via ScreenCaptureKit, FFT bands, beat and tempo detection, adjustable mixer.
- Color styles: Accurate, Enhanced (default), Cinematic, Gaming, with a live side-by-side comparison. Details in `docs/COLOR.md`.
- Profiles: Movie, Music, Gaming, Relaxed Ambient; user profiles store displays, lights, style, brightness and sound.
- Lights hub: switches, dimming, color, zones, scenes.
- Local bridge discovery (Bonjour) and local pairing. Cloud lookup is optional and off by default.
- Demo Mode: virtual bridge, rooms, zones, Entertainment areas, three displays and ten lights, with a simulated sync engine. Toggle in onboarding and Settings; `--demo`, `--section`, `--sync` launch options.
- Redacted diagnostics report.
- Benchmark tooling: `--metrics-log` launch option, `scripts/benchmark.sh`, `tools/bench_report.py`.
- `scripts/screenshots.sh`, `scripts/benchmark.sh`, `scripts/runtime_audit.sh`, `scripts/audit.sh`, `scripts/verify.sh` (fails on zero executed tests, any compiler warning, or strict-concurrency warnings).
- `scripts/install.sh`: one admin prompt, removes earlier copies of the app, stable local signing identity.
- `scripts/build_app.sh`: release build with a leak check on the binary.
- `ColorLab`: offline color-style evaluation tool.

### Privacy
- The optional online bridge lookup uses an ephemeral URL session, so its response is never cached on disk.
- No telemetry, analytics, crash reporting, update checks or accounts. Screen and audio data stay in memory on the device.

### Known limitations
See `docs/RELEASE_REPORT.md`.
