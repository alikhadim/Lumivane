# Lumivane for Windows and Linux

The same app as the Mac version, built once in C# with Avalonia, so Windows and Linux look and behave alike. Screen sync, sound sync, color styles, profiles, multi-display routing, the light hub, Demo Mode and diagnostics are all there. Source: [`desktop/`](../desktop).

![Home](screenshots/desktop-01-home.png)

## Install

**Windows 10 or later, 64-bit.** Download `Lumivane-Windows-x64-<version>.zip` from the [Releases page](https://github.com/alikhadim/Lumivane/releases), unzip it anywhere and run `Lumivane.exe`. The file is not code-signed, so SmartScreen warns once: choose *More info*, then *Run anyway*. Check the SHA-256 on the release page first.

**Linux, x86-64, X11 session.** Download `Lumivane-Linux-x64-<version>.tar.gz`, then:

```sh
tar xzf Lumivane-Linux-x64-*.tar.gz
cd lumivane && ./install.sh
```

This installs for your user only (no root) and adds an app menu entry. It needs `libX11`, `libXext`, `libXrandr` and `libpulse` (installed on nearly every desktop; the script tells you if one is missing). PipeWire works through its PulseAudio layer.

No .NET install is needed: both downloads carry their own runtime.

## What differs from the Mac app

| | Mac | Windows | Linux |
|---|---|---|---|
| Screen capture | ScreenCaptureKit, Metal | GDI (`StretchBlt`, `PrintWindow`) | X11 (XShm, XRandR) |
| System / mic audio | ScreenCaptureKit | WASAPI loopback / capture | PulseAudio monitor / source |
| One app's audio | ScreenCaptureKit | Process loopback (Windows 10 2004+) | PulseAudio sink input |
| Bridge key storage | Keychain | DPAPI, current user | Desktop keyring (`secret-tool`), else a 0600 file |
| Menu bar item | Menu bar | Tray icon | Tray icon (StatusNotifier) |
| Launch at login | Login item | `HKCU\...\Run` | XDG autostart |
| Zone reduction | GPU | CPU | CPU |

Color analysis, styles, smoothing, routing, Entertainment streaming (DTLS 1.2 PSK) and the REST fallback are one shared implementation (`desktop/src/Lumivane.Core`), tested against a simulated bridge.

## Limits

- **Wayland.** Wayland does not let an app read the screen. Under Wayland the app says so and screen sync stays off; sound sync and light control work. Log in with an X11 session (for example "Ubuntu on Xorg" or "Plasma (X11)") for screen sync.
- **Protected video.** Video that the GPU draws on a protected path (DRM streaming in some players) is captured black on every OS. Most browser playback and local players are fine.
- **HDR.** HDR content is captured as the desktop shows it, without tone mapping.
- **Per-app audio on Windows** needs the app to be playing when you start, and Windows 10 version 2004 or later.
- **Display names.** Windows shows the monitor name Windows reports, or "Display N" when it only reports a generic one.

## Verification status

- Core logic: 66 automated tests (color, routing, profiles, streaming, discovery, demo engine).
- Linux platform layer: 30 automated tests (27 against a real X server (Xvfb) and a real PulseAudio server: displays, capture, windows, thumbnails, system, microphone and per-app audio, secrets; 3 for the Windows audio conversion). The UI was run and inspected on Linux, light and dark. Not tested: a real Hue bridge or lights, the tray icon, Wayland, PipeWire, multi-monitor hardware.
- Windows: the app and the Windows platform layer compile and publish, and the shared Core tests are platform-neutral. **The Windows-specific code (GDI capture, WASAPI, process loopback, DPAPI, tray, toasts, autostart) has not been run on Windows.** Treat the first Windows release as untested on hardware and please report problems with the diagnostics report from Settings.

## Build from source

Needs the .NET 8 SDK.

```sh
dotnet run --project desktop/src/Lumivane.App -- --demo      # try it without a bridge
scripts/build_desktop.sh                                      # tests, then win-x64 zip and linux-x64 tar.gz in desktop/dist
```

Launch options (also used for screenshots): `--demo`, `--section home|screens|sound|lights|profiles|settings`, `--tab N`, `--sync`, `--appearance light|dark`, `--data-dir <folder>`, `--metrics-log <file>`.

## Privacy

Same as the Mac app: no account, no telemetry, no analytics. Frames and audio are analysed in memory and discarded. Settings live in `%APPDATA%\Lumivane` (Windows) or `~/.config/lumivane` (Linux). See [PRIVACY.md](PRIVACY.md).
