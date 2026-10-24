# Lumivane 1.0.0

Ambient lighting for macOS: Philips Hue lights follow your screen and your sound. Everything is processed on your Mac; there is no account, telemetry or cloud dependency.

## Highlights
- Hue Entertainment streaming (DTLS 1.2) with a REST fallback
- Single, mirror and extend display modes; each light can follow its own display
- Direct system or per-app audio for sound reactivity
- Four color styles (Accurate, Enhanced, Cinematic, Gaming) with a live comparison
- Profiles for movies, music, gaming and relaxed ambient
- Demo Mode: a virtual bridge, rooms, displays and lights, no hardware needed

## Install
Requires macOS 14 or later, Apple Silicon and a Hue Bridge (v2 or Pro) on the same network. Build from source with `./scripts/install.sh`, or open the attached disk image. The app is self-signed, not notarized: on first launch use right-click, Open. Then switch on Screen & System Audio Recording for Lumivane in System Settings, and allow Local Network access.

## Verified
- Zero compiler and Swift 6 strict-concurrency warnings; 37 of 37 unit tests (core logic)
- A first-run walkthrough through the interface on one Mac, one bridge and two displays
- Static privacy audit of the source tree and release binary

## Known limitations
- Tested on a single setup; Entertainment area creation is implemented but not verified on hardware
- No HDR or OLED handling or letterbox detection; protected video can capture as black
- Not notarized; no VoiceOver audit; English only
- Latency and CPU figures are not published yet

Not affiliated with, endorsed by or sponsored by Signify N.V. or Philips. "Philips" and "Hue" are trademarks of their owners.
