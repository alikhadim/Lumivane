# Lumivane for Fire TV

Hue control and light sync for the Fire TV, with the remote's D-pad and a sidebar like the Mac app.

- **Home:** start and stop sync, pick a source (Screen, Movie Ambient, Aurora, Cinema Glow, Firelight, Ocean, Candle, Color loop), a mode (Movie, Gaming, Relaxed), a color style (Accurate, Enhanced, Cinematic, Gaming, Vivid+), brightness and smoothness. A live preview shows the light colors.
- **Areas:** choose which lights follow the picture and where each sits (top, left, right, bottom and corners), create new areas, delete areas.
- **Rooms, Lights, Scenes:** direct control. Left and right dim, OK switches on or off, Back returns to the menu.
- **Demo mode** works without a bridge.

Sync streams colors to an entertainment area on the bridge (DTLS, up to 20 lights) and uses the same color engine as the Mac app. If the bridge refuses the streaming connection, the app falls back to plain Hue updates (about 8 per second across all lights, so slower and less smooth) and shows the reason on the Home page. The **Screen** source captures the display at low resolution through Android screen capture. It depends on Fire OS offering that capability and cannot see protected video (Netflix, Prime Video and similar usually give capture a black picture). The ambient sources need no capture. Sound sync is Mac only.

### Protected video

When the captured picture is pure black for 3 seconds, Screen switches to an ambient mode (Movie Ambient by default, changeable in Settings), shows "Protected video detected. Direct picture sync unavailable for this content." on Home, and keeps checking the capture. After 2 seconds of a visible picture it blends back. Paused or static pictures are not treated as protected.

What has not been verified: capture on specific Fire TV models and Fire OS versions, behavior in each streaming app, and audio capture. Treat Screen sync as unavailable for DRM-protected services. Not implemented: audio-reactive lighting, detection of the foreground app, and driving the Fire TV from a running Mac app.

Requires Fire OS 6 or newer and a Hue Bridge on the same network, with Hue lights that support entertainment streaming for sync. Version 1.0 pairings have no streaming key: forget the bridge in Settings and pair again once.

## Install over the internet

1. On the Fire TV: Settings, My Fire TV, Developer Options, **Install unknown apps**, enable it for **Downloader** (install Downloader from the Amazon Appstore first). On older Fire OS this is "Apps from Unknown Sources".
2. Open Downloader and enter the APK URL from the latest `firetv-v*` release on GitHub: `https://github.com/alikhadim/Lumivane/releases`. Copy the link of the `.apk` asset.
3. Downloader fetches the file. Choose **Install**, then **Open**.
4. Compare the SHA-256 in the release notes with the file if you want to verify it (`shasum -a 256 file.apk` on a computer).

Updates: install the newer APK over the old one. Settings and pairing are kept as long as the same release key signed both.

## Pair with your bridge

1. Choose **Find my Hue Bridge**. The app searches the local network. If nothing is found, use **Enter IP address** (the Hue app shows it under Settings, Bridges).
2. Select the bridge, then press the round link button on top of it within 30 seconds.
3. Home: create the sync area if none exists (it uses every sync-capable light, placed around the screen; change lights and positions under Areas), choose a source, press Start sync. For Screen, Android asks once to allow screen capture. Then press Home on the remote and use any app; sync keeps running until you press Stop.

## Privacy

- The app talks only to your bridge, over HTTPS. The bridge's self-signed certificate is pinned on first pairing; if it ever changes, the app refuses to connect until you forget the bridge in Settings and pair again.
- Screen capture stays on the TV: frames are reduced to a few colors in memory and never stored or sent.
- The access keys are stored on the TV, encrypted with an Android Keystore key where the device supports it.
- No accounts, analytics or ads. The optional **Look up online** button asks the Philips discovery service, which sees your public IP address; it is never used automatically.

## Build

Needs JDK 17+ and the Android SDK (platform 34).

```
cd firetv
echo "sdk.dir=$HOME/Library/Android/sdk" > local.properties
./gradlew test assembleRelease
```

The APK is `app/build/outputs/apk/release/app-release.apk`. Without `firetv/signing.properties` it is signed with the debug key. For a real release create a keystore and a `signing.properties` with `storeFile`, `storePassword`, `keyAlias`, `keyPassword`. Keep both outside the repository and back them up: losing the key means users must uninstall before installing an update.
