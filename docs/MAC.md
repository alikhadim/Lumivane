# Lumivane for macOS

The native Mac app: SwiftUI, ScreenCaptureKit and Metal. Overview and downloads for all platforms are in the [README](../README.md).

## Features

- Real-time Philips Hue synchronization over the Entertainment API (DTLS 1.2), with a REST fallback
- Multi-display support: single, mirror or extend; each light can follow its own display; automatic fallback when a display disconnects
- Entertainment-area support: uses an existing area, per-light position and source; area creation is implemented but not yet verified on hardware
- Direct system audio: lights react to what your Mac plays, system-wide or from one app, before the speakers
- Color enhancement modes: Accurate, Enhanced (default), Cinematic, Gaming, with a live side-by-side comparison
- Profiles: Movie, Music, Gaming and Relaxed Ambient included; your own store displays, lights, style, brightness and sound
- Demo Mode: virtual bridge, rooms, zones, areas, displays and lights for trying the interface and producing screenshots without any device
- Privacy-first architecture: no account, no telemetry, no analytics, no cloud dependency
- Native Apple Silicon: SwiftUI, ScreenCaptureKit, Metal

## Windows and Linux

The same app with the same interface for Windows 10+ and Linux (X11): download it from the [Releases page](https://github.com/alikhadim/Lumivane/releases) (tag `desktop-v…`). Install steps, differences from the Mac app and what has been tested: [docs/DESKTOP.md](docs/DESKTOP.md). Source in [`desktop/`](desktop).

## Screenshots

All interface images are captured in Demo Mode: no real room, light, display or network names.

| | |
|---|---|
| <img src="docs/screenshots/01-home.png" alt="Home: live light preview, color styles"> | <img src="docs/screenshots/02-screens.png" alt="Screens: display modes and light assignment"> |
| <img src="docs/screenshots/03-sound.png" alt="Sound: audio source and live levels"> | <img src="docs/screenshots/04-lights.png" alt="Lights: per-light control"> |
| <img src="docs/screenshots/05-profiles.png" alt="Profiles"> | <img src="docs/screenshots/06-settings.png" alt="Settings: Demo Mode and privacy"> |

Color styles, evaluated offline on synthetic scenes (`swift run ColorLab`, metrics in [docs/COLOR.md](docs/COLOR.md)):

<img src="docs/colorlab-board.png" width="520" alt="Color style comparison across synthetic scenes">

To regenerate the interface images: build the app, then `./scripts/screenshots.sh`. Launch options: `--demo`, `--section home|screens|sound|lights|profiles|settings`, `--sync`, `--metrics-log <file>` (see [docs/BENCHMARKS.md](docs/BENCHMARKS.md)).

## Installation

Requires macOS 14 or later and a Hue Bridge (v2 or Pro) on the same network.

1. Build and install: `./scripts/install.sh` (see Building). It asks for the administrator password once, removes earlier copies of Lumivane, installs to `/Applications` and opens the Screen Recording settings.
2. Switch on Lumivane under Privacy & Security > Screen & System Audio Recording. macOS does not allow a script to grant this. The app reopens itself.
3. Allow Local Network access when macOS asks.

Options: `--no-build`, `--purge` (also clears settings, the saved bridge key and privacy grants), `--uninstall`.

The build is not notarized. If you install by copying the app manually, right-click > Open on first launch.

## Building

Requirements: macOS 14+, Xcode 16+ (Swift 6 toolchain), Apple Silicon recommended.

```
swift test                       # core tests
swift run ColorLab colorlab-out  # color style board
./scripts/build_app.sh           # build/Lumivane.app and a .dmg
./scripts/verify.sh              # audit, clean build, tests
```

Name, bundle identifier and version live in `scripts/brand.env`. The bundle identifier (`com.lumivane.app`) should match a domain you control and must not change after release: it keys the Keychain item, privacy grants and saved settings.

`HueCore` has no Apple dependencies and its tests also run on Linux.

## Quick Start

1. Connect bridge: the setup wizard finds the bridge on your network. Press the bridge button when asked. Pairing is local; no Hue account is needed.
2. Choose lights: pick the lights or Entertainment area to use.
3. Select display: choose one display, mirror several, or extend (each light follows its own display).
4. Start sync: press Start on Home. Pick a color style and, under Sound, the audio influence.

No bridge at hand? Choose "Explore with Demo Mode" on the first screen, or turn it on in Settings. Demo Mode uses virtual devices, sends nothing anywhere and leaves your real setup untouched.

## Privacy

- No cloud dependency. Bridge discovery uses your local network. An optional online lookup (Philips discovery service) runs only if you press its button.
- No telemetry, no analytics, no crash reporting, no update checks.
- Screen data never leaves the device. Frames are reduced to colors in memory and discarded.
- Audio data never leaves the device. It is analyzed in memory and discarded.
- The bridge key is stored in the macOS Keychain.

Details and the audit table: [docs/PRIVACY.md](docs/PRIVACY.md).

## Architecture

```
ScreenCaptureKit -> Metal zone reduction -> ColorEngine -> Entertainment stream (DTLS) -> bridge
system audio    -> FFT / beat analysis  -> AudioMixer ---^
```

| Module | Role |
|---|---|
| `HueCore` | Pure Swift: models, color math, color styles, zone reduction, layout, audio analysis, packet format |
| `HueKit` | Hue v2 client, discovery, pairing, DTLS streamer. The only module that touches the network |
| `CaptureKit` | ScreenCaptureKit capture, Metal reducer |
| `Lumivane` | SwiftUI app |
| `ColorLab` | Offline color evaluation tool |

More: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), [docs/COLOR.md](docs/COLOR.md).

## Status and known limitations

Tested on a single setup so far. Not yet done: published latency and CPU figures, HDR and OLED handling, letterbox detection, VoiceOver audit, notarization. Protected video can capture as black. See [docs/RELEASE_REPORT.md](docs/RELEASE_REPORT.md).

## Roadmap

- Scene packs (fireplace, aurora, thunderstorm, ocean, sunset)
- HDR capture and letterbox detection
- Developer ID signing and notarization
- Published latency and CPU benchmarks (tooling is in `scripts/benchmark.sh` and `tools/bench_report.py`)
- Accessibility audit and localization
