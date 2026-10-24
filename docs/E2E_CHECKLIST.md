# End-to-end walkthrough

Goal: a first-time user completes everything through the interface, with no terminal after installation.

Status: **performed by the maintainer on 2026-10-24** on one setup (one bridge, two displays), all 16 steps through the interface. The friction points below come from code review and earlier hands-on runs; no new ones were recorded in this pass.

| # | Step | Expected | Verified |
|---|---|---|---|
| 1 | Launch | Onboarding appears, no terminal output needed | Yes 2026-10-24 |
| 2 | Bridge discovery | Bridge found via Bonjour; Local Network prompt appears once | Yes 2026-10-24 |
| 3 | Pairing | Press bridge button; pairing completes within 30 s | Yes 2026-10-24 |
| 4 | Light discovery | All lights listed with Entertainment capability | Yes 2026-10-24 |
| 5 | Room and zone discovery | Rooms and zones listed (Lights page) | Yes 2026-10-24 |
| 6 | Entertainment area | Existing area selected, or a new one created | Yes 2026-10-24 |
| 7 | Screen permission | Switch on once; app reopens itself | Yes 2026-10-24 |
| 8 | Display selection | Single, mirror, extend; thumbnails live | Yes 2026-10-24 |
| 9 | Light-to-display assignment | Drag a light or room onto a display | Yes 2026-10-24 |
| 10 | Audio source | System audio or one app; meters move | Yes 2026-10-24 |
| 11 | Start sync | Lights follow the screen within a second | Yes 2026-10-24 |
| 12 | Profile create and save | Autosaves on change; new profile from current | Yes 2026-10-24 |
| 13 | Profile restore | Switching profiles restores displays, lights, style | Yes 2026-10-24 |
| 14 | Quit and relaunch | Bridge, profile and assignments persist; no re-pairing | Yes 2026-10-24 |
| 15 | Stop sync | Lights return to normal control | Yes 2026-10-24 |
| 16 | Demo Mode on and off | Real setup is untouched after leaving Demo Mode | Yes 2026-10-24 |

## Known friction points
1. **Screen & System Audio Recording** cannot be granted by an installer or script. The user switches it on once in System Settings. The app watches for the change and reopens itself.
2. **Stale grant after re-signing.** A rebuild with a different signature leaves the old toggle on but ineffective. `scripts/install.sh` resets the entry when the signature changes; manual installs need the toggle removed and added again.
3. **Local Network prompt.** If denied, discovery finds nothing. The interface explains where to change it, but the user must leave the app.
4. **Bridge button.** Pairing needs physical access to the bridge and a 30 second window.
5. **Unsigned build.** Not notarized: first launch via right-click > Open, unless installed with `install.sh` (which removes the quarantine flag).
6. **Entertainment area.** Lights must belong to one area. Automatic area creation uses an API body that is not yet verified on hardware; the fallback is creating an area in the Hue app.
7. **Admin password.** One prompt at install time (`install.sh`).
8. **Protected video** (some streaming apps) captures as black; audio sync still works.
9. **Bundle identifier changes** reset saved settings and require one re-pair.
