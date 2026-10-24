# Architecture

```
ScreenCaptureKit (256 px BGRA, minimumFrameInterval = 1/fps)
  -> MetalReducer (1 threadgroup per zone: RGB sum + 12-bin hue histogram)   [CPU fallback: ZoneAccumulator]
  -> ZoneStats per light channel (average, dominant, brightness, saturation)
  -> ColorEngine (evidence-weighted dominant pick, saturation and tone curves, auto-gain, HSV smoothing with shortest-arc hue, slew limit, white balance, night/low-light); see COLOR.md
  -> StreamPacket -> DTLS 1.2 PSK, UDP 2100 (Entertainment)     [fallback: REST PUT light, ~9 commands/s]
```

Routing (profile -> capture)
- A profile holds per-light routes (position, enabled, source) and a mode. `RoutePlanner` (pure, tested) turns profile + connected displays into a plan: which screen areas each light averages, and which sources need a capture session. It never rewrites the profile, so a disconnected display falls back (explicit fallback, else built-in, else main) and the original is restored on reconnect.
- Modes: single (one source), mirror (stats of several sources averaged per light), extend (per-light source, including `desktop`, which maps a light's position across the bounding box of all displays to the display under it).
- Display identity is the display UUID; windows are `bundle|title`.

Audio (system audio, not microphone)
- ScreenCaptureKit audio capture (`capturesAudio`) taps the system mix, or one app via an application filter, before the output device.
- `AudioAnalyzer`: 1024-point FFT (Hann) every 256 samples. Bass 20-250 Hz, mid 250-4000 Hz, treble 4-16 kHz with slow adaptive gain; spectral-flux onsets with adaptive threshold for beats, bass impulses and transients; autocorrelation tempo (60-190 BPM); heuristic speech/music score. The speech/music score is not a trained model and has only been checked on synthetic signals.
- `AudioMixer`: out = screen * (1 - e) + audioLayer * e, e = influence scaled down by speech and up by music strength (max 0.95). Bass: warm room-wide brightness pulses and bursts in the frame's own colors. Mid: slow hue drift. Treble: short sparkles. Beat: global pulse. The brightness limiter is a hard ceiling after mixing.
- A detected beat triggers an immediate extra output tick instead of waiting for the next timer slot. "Sound to bridge" in Performance measures buffer end to packet send. No latency figure has been measured yet; the < 50 ms target is unverified.

Targets
- HueCore: pure Swift 6, no Apple frameworks. Models, color math, zone reduction, ColorEngine, layout, stream packet. Unit-tested.
- HueKit: Hue v2 REST client (actor), discovery (Bonjour + cloud), pairing, DTLS streamer.
- CaptureKit: ScreenCaptureKit capture, Metal reducer (kernel compiled at runtime from source).
- Lumivane (Sources/Lumivane): SwiftUI app. MVVM: `AppModel` (observable) owns state and a `SyncEngine`; views are stateless.

Decisions
- SCK does the downscale, so the GPU/CPU only touch ~37k pixels per frame.
- SCK sends no frames for static content, so a fixed-rate timer drives output. This also keeps the Entertainment stream alive (the bridge drops it after ~10 s of silence).
- Zones: 3x3 grid. A light's screen position selects its cell (corners, edges, center).
- Strobe guard: brightness EMA (rise tau/2, fall 1.5 tau, tau 40-800 ms, scaled by style) plus a per-second slew cap; hue rate-limited and frozen in darkness.
- HueKit, CaptureKit and Lumivane compile in Swift 5 language mode (Swift 6 compiler) because the Apple callback APIs were not strict-concurrency checked. HueCore is Swift 6 mode.
- Bridge TLS: trust is accepted for the selected bridge IP only (the bridge uses a private CA).
- Credentials in Keychain (service = bundle identifier); settings in UserDefaults. Profiles saved before the color rework decode with defaults for new fields and are migrated once (gamma 1.2 becomes 1.0).
- Bridge discovery is local (Bonjour). The Philips cloud lookup runs only on request.
- Identity lives in `Brand` (Design.swift) and `scripts/brand.env`.

Unverified assumptions (check on first run)
- DTLS PSK via Network.framework with suite 0x00A8 and `.DTLSv12`.
- `entertainment_configuration` POST/PUT body shape for automatic area creation. If the bridge rejects it, create the area in the Hue app.
- Hue positions to screen mapping uses x (left/right) and z (height).

Fire TV: not started. Findings on direct audio access (Oct 2026):
- Android's AudioPlaybackCapture exists from Android 10 (API 29). It needs RECORD_AUDIO and a MediaProjection consent dialog, and apps can opt out of being captured ([Android Developers Blog](https://android-developers.googleblog.com/2019/07/capturing-audio-in-android-q.html)).
- Per [Amazon's device table](https://developer.amazon.com/docs/device-specs/identify-fire-tv-devices.html): Fire TV Stick 4K 2nd gen, 4K Max 2nd gen and 4K Plus run Fire OS 8 (Android 11, API 30). 4K Max 1st gen, Stick 3rd gen, Lite and HD run Fire OS 7 (Android 9, API 28). Stick 4K 1st gen runs Fire OS 6 (Android 7.1).
- So option 1 (companion app capturing the audio session) is feasible at most on Fire OS 8 devices; Fire OS 7 and 6 devices would fall through to option 4 (microphone). Whether Fire OS 8 exposes MediaProjection to sideloaded apps, and which streaming apps opt out, is unverified.
- `android.media.audiofx.Visualizer` on the global output mix needs RECORD_AUDIO; I could not confirm its current status in the docs and have not evaluated it on Fire OS.
- Architecture caveat: the lights are driven by the Mac. A Fire TV playing content sends neither its video nor its audio to the Mac, so a Fire TV companion would have to send audio features (not raw audio) to the Mac over the LAN, and the Mac would need that TV's picture (HDMI capture) for screen colors. Planned control channel: a small HTTP/WebSocket server in the Mac app (start/stop, profile, brightness, status) found via Bonjour, with an Android TV Compose client.
