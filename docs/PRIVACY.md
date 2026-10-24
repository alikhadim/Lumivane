# Privacy report

All screen analysis, audio analysis and lighting decisions happen locally on the user's Mac.

## Data processed
| Data | Where | Kept? |
|---|---|---|
| Screen content | ScreenCaptureKit delivers a reduced frame (256 px wide) to memory. Zones are averaged on the GPU (Metal) or CPU into colors | Discarded after each frame. Never written to disk |
| Display and window thumbnails | Shown on the Screens page only while it is open | Memory only |
| System audio | ScreenCaptureKit audio stream, analyzed into levels, bands and beats | Discarded after analysis. Never written to disk |
| Microphone | Only if the user picks "Microphone (fallback)" | Same as system audio |
| Hue Bridge key | macOS Keychain, service = bundle identifier | Until the user pairs again or deletes it |
| Settings, profiles, bridge address, remembered display names, window and app identifiers used by profiles | UserDefaults (local) | Until removed |

## What leaves the device
| Destination | Content | When |
|---|---|---|
| The user's Hue Bridge, local network | Light colors, on/off, scene and group commands, Entertainment stream (DTLS) | While controlling or syncing lights |
| Philips discovery service (discovery.meethue.com) | A request that reveals the network's public IP address | Only if the user presses "Try online lookup" during setup. Off by default |

Nothing else. The app contains no analytics, telemetry, crash reporting, update checker, advertising identifier or account system. Network code exists only in `HueKit` (bridge client, discovery, streamer).

## What never leaves the device
Screen images, audio, window titles, app names, display names, profiles, the bridge key.

## Source audit (what was checked and changed)
| Check | Result |
|---|---|
| Absolute paths, user names, host names in source and scripts | None. One reverse-DNS identifier containing the author's name was replaced by a neutral placeholder (now `com.lumivane.app`). Changing it means one re-pair and a reset of saved profiles |
| IP addresses | The placeholder `192.168.1.20` in a text-field hint and a fake address in one unit test |
| URLs | `https://discovery.meethue.com` (optional, see above) and the bridge's own address. Documentation links in docs/ only |
| Secrets, tokens, API keys | None in the repository. The bridge key is created at pairing and stored in the Keychain |
| Logging | The app has no logging calls. Errors are shown in the interface only |
| Diagnostics text | The Settings page offers a shareable report that masks the bridge IP (`192.168.x.x`), bridge ID and area names |
| Disk cache of the optional lookup | The shared URL session could cache the lookup response (it lists the bridge address) under `~/Library/Caches`. The lookup now uses an ephemeral session: no cache, cookies or stored credentials |
| Network APIs outside `HueKit` | None (`URLSession`, `NWConnection`, `NWBrowser`, web views, socket APIs searched) |
| Files written by the app | UserDefaults and Keychain only. The one exception is the developer option `--metrics-log <file>`, which appends timing numbers (no screen, audio or device data) to a file you name. `ColorLab`, a developer tool, writes only to a folder you name |
| Telemetry, analytics, crash-reporting, user-activity APIs | None |
| Binary leak check | `scripts/build_app.sh` scans the built binary for `/Users/`, `/home/`, the build machine's home path, user name and host name, and fails the build if any appear. It also strips symbols and remaps source paths (`-file-prefix-map`). This check has not yet been run on a Mac <!-- audit:allow --> |

## Known exposures
- The local network permission and the bridge's self-signed certificate: TLS trust is limited to the selected bridge IP. A device that spoofs that IP on the same network could receive commands.
- Screen Recording permission is required for capture. macOS shows its recording indicator while capturing.
- Protected video may capture as black. Audio is unaffected.
- Ad-hoc signing means no notarization. A public release needs a Developer ID signature and notarization.

## Demo Mode
Demo Mode uses built-in virtual data and a simulated engine. It makes no network connection, captures no screen or audio, writes nothing to UserDefaults or the Keychain (the demo flag and the appearance setting excepted) and cannot overwrite the real bridge, profiles or display list.

## Runtime verification
`scripts/runtime_audit.sh` inspects a running install: saved setting keys, files under `~/Library`, crash reports, window restoration data, Keychain presence, system log lines, open network peers and the installed binary. It prints a flag for any personal identifier or non-local peer. Not yet run on a Mac.

What is stored locally, by design: bridge address and ID, profiles (including display names, and window titles and app names if the user added a window source), remembered display names, the bridge key in the Keychain. Display names and window titles are read from macOS and never leave the Mac. If a display's name contains the user's own name, it appears in the saved settings and in the Settings > diagnostics view only; the shareable report masks area names, IP and bridge ID but does not list displays.
