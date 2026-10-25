# Lumivane for Windows and Linux

The Mac app's features on Windows and Linux with the same look: screen sync, sound sync, color styles, profiles, multi-display routing, the light hub, Demo Mode and diagnostics. Needs a Hue Bridge on the same network; Demo Mode works without one.

- **Windows:** `@WIN@` (Windows 10 or later, 64-bit). Unzip and run `Lumivane.exe`. Not code-signed, so SmartScreen asks once.
- **Linux:** `@LINUX@` (x86-64, X11 session). Extract, then run `./install.sh`.

Screen sync on Linux needs an X11 session: Wayland does not allow screen reading, and the app says so. Protected video is captured black, as everywhere.

The Linux build and its platform layer were tested here against a real X server and PulseAudio. The Windows-specific code has been compiled but not run on a Windows machine. No real Hue bridge was used for either. Details and limits: [docs/DESKTOP.md](https://github.com/alikhadim/Lumivane/blob/main/docs/DESKTOP.md).

SHA-256:

```
@SUMS@
```
