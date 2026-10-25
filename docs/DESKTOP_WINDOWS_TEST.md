# Windows test plan for Lumivane desktop 1.0.0

For the person or agent testing on a real Windows 10/11 x64 machine. The Windows-specific code has been compiled but never run, so this is the first real test. Report facts only: pass, fail, or not tested, with what you saw.

Rules: do not enter passwords or keys anywhere. Pairing with a real Hue bridge needs the owner to press the bridge button; do that part only when the owner is present. Do not change system settings. Everything below runs from the unzipped folder.

## Install
1. Verify the SHA-256 of `Lumivane-Windows-x64-1.0.0.zip` against `SHA256SUMS.txt` (`Get-FileHash <zip>`).
2. Unzip to a normal user folder, for example `%USERPROFILE%\Lumivane`.
3. Start it from PowerShell so errors are visible: `& .\Lumivane.exe *> run.log`. If SmartScreen blocks it, choose More info, then Run anyway.
4. Note Windows version and build (`winver`), CPU, GPU, number of monitors, resolution and scale of each.

## Checks (record PASS / FAIL / NOT TESTED and a one-line observation)
**Launch**
1. Window opens, no crash within 60 s, the icon appears in the taskbar and the window title bar. Text is crisp at your display scale (100/125/150 %).
2. Onboarding shows; choose *Explore with Demo Mode*. Home, Screens, Sound, Lights, Profiles and Settings all open without errors. Switch Appearance between Light and Dark in Settings.

**Displays and capture** (Screens page, Displays tab)
3. Every monitor is listed with the right pixel size and refresh rate. Names are sensible or "Display N".
4. Each display tile shows a live thumbnail that matches what is on that monitor. Open a window with colorful content and check the thumbnail changes.
5. Add a window as a source (Screens page): its thumbnail shows that window's content even when another window covers it. Minimize it: no crash.
6. Unplug or disable a monitor (or change resolution) with the app open: the list updates within a few seconds. Skip if not possible.
7. Play a video in a browser. In Home, start syncing in Demo Mode and check the room preview colors follow the picture. Measure CPU of `Lumivane.exe` in Task Manager while syncing at the default rate. Report the percentage.
8. Play protected video (Netflix or similar in a browser). Report whether the capture is black.

**Sound** (Sound page)
9. Source *Everything my computer plays*: play music; the level meter and bands move. Pause the music: they fall to zero and stay stable (no frozen last value).
10. Change the default output device (for example plug in headphones) while sound plays: meters keep moving within about 2 s.
11. Source *Microphone*: speak; meter moves.
12. Source *One app only*: the list shows the app that is playing (for example the browser). Select it; only that app's sound moves the meter, other apps do not. Report any error text shown.

**System integration** (Settings)
13. Turn on *Open Lumivane at login*; run `reg query HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v Lumivane` and report the value. Turn it off and confirm the value is gone.
14. While syncing, close the window: the app should stay in the tray with working show/stop controls. Quit from the tray menu.
15. Look at `%APPDATA%\Lumivane`. List the files (names only). Open `secrets.json` if present and confirm values are base64 text, not a readable key.
16. If a notification is triggered anywhere (for example sync stops with an error), note whether a Windows toast appears.

**Real bridge (owner present only)**
17. Find bridges, pair (owner presses the button), pick an Entertainment area, start syncing. Report whether lights follow the screen, the measured latency in Home or Diagnostics, and any error text.

## Report back
- A table of checks 1 to 17 with PASS / FAIL / NOT TESTED and the observation.
- The contents of `run.log`.
- Settings > Diagnostics > *Copy full details* (it is redacted of keys and addresses). Check it contains no personal data before sharing.
- Screenshots of Home, Screens and Sound. Blur or crop anything personal such as window titles.
- For each failure: what you did, what happened, what you expected.
