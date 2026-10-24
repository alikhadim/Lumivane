# Release report: Lumivane 1.0.0 candidate

**Verdict: published as 1.0.0 with the limitations below.** Verified on one Mac (Apple M4 Pro, macOS 27) and one bridge. Benchmarks are not published; registry trademark search and domain are open.

## Release checklist
| Item | State | Notes |
|---|---|---|
| Zero compiler warnings | Pass | `verify.sh` on the Mac ends with `warnings: 0` |
| Zero Swift 6 concurrency warnings | Pass | `verify.sh` builds with `-strict-concurrency=complete` and fails on any finding |
| Zero analyzer warnings | Not applicable | The Clang static analyzer covers C-family code. The package has no C or Objective-C sources |
| Tests actually execute | Pass | The "0 tests" line comes from the Swift Testing library, which the package does not use. `verify.sh` requires the executed XCTest count to equal the 37 tests found in `Tests/` |
| All tests pass | Pass | 37 of 37 on Linux and on the Mac (core only) |
| Build succeeds from clean clone | Pass | `verify.sh` cleans and rebuilds |
| Demo Mode verified | Pass | Used to capture the screenshots; core unit-tested |
| Privacy audit passes | Static: pass. Runtime: not run | `scripts/audit.sh` passes; `scripts/runtime_audit.sh` has not been run |
| Security audit passes | Static review only | See below |
| Branding finalized | Name selected | Lumivane, see `docs/BRANDING.md`. Registry search still required |
| Bundle identifier finalized | Set: `com.lumivane.app` | Domain not owned yet |
| Screenshots generated | Pass | `docs/screenshots/`, Demo Mode, no PNG text chunks |
| README finalized | Pass | |
| App icon finalized | L mark selected | Checked at 16 to 256 px |
| Benchmark report completed | **No** | Not published. Tooling is in the repository; first runs were not clean enough to publish |
| End-to-end walkthrough completed | Pass | 16 of 16 on one setup, `docs/E2E_CHECKLIST.md` |

## Compile and test status
- HueCore and ColorLab build and run on Linux (Swift 6.0.3). All targets build on macOS with zero warnings.
- Tests: 37 (color pipeline and styles, routing, audio analysis, packet layout, settings migration, diagnostics redaction, demo scenes and signal). None cover the interface, capture or streaming.

## Security audit (static)
| Check | Result |
|---|---|
| Secrets, tokens, private keys, certificates in tree | None (`scripts/audit.sh`, tested against planted hits) |
| Bridge key | Created at pairing, kept in the Keychain, never logged, not stored in Demo Mode |
| TLS to the bridge | Self-signed bridge certificate trusted only for the selected bridge IP. A spoofed IP on the same LAN could receive commands |
| Entertainment stream | DTLS 1.2 PSK, `TLS_PSK_WITH_AES_128_GCM_SHA256` |
| Installer | One `sudo` prompt; removes only bundles with this app's identifier (and optional listed earlier identifiers), never other apps |
| Distribution | Self-signed. Not Developer ID signed, not notarized, hardened runtime off |
| Third-party code | None |

## Privacy audit
See `docs/PRIVACY.md`. By source search: no network API outside `HueKit`; no telemetry, analytics or crash-reporting code; room, light and display names, screen frames and audio are never sent. The optional online lookup uses an ephemeral session. Runtime confirmation of saved state, logs, caches and connections is pending (`scripts/runtime_audit.sh`).

## Known limitations
1. DTLS streaming and area creation verified on one setup only.
2. Latency and CPU figures not published (tooling ready).
3. Color styles tuned on synthetic scenes.
4. No HDR or OLED handling, no letterbox detection; protected video captures black.
5. Screen Recording and Local Network permissions cannot be granted by a script.
6. No VoiceOver audit; English only.
7. Visual polish items (dock icon, splash, empty and loading states, light-assignment experience) only partly done.
8. The disk-image step prints a `hdiutil` deprecation notice from macOS; it is not a compiler warning.

## Dependencies
Swift packages: none. Apple frameworks: SwiftUI, AppKit, Foundation, ScreenCaptureKit, CoreMedia, CoreVideo, CoreGraphics, AVFoundation, Metal, Network, Security, ServiceManagement, UserNotifications. Build: Xcode 16+, macOS 14+. Optional: Python 3 with Pillow (`tools/`, `branding/`).

## Open before wider promotion
1. Registry trademark search for Lumivane (USPTO, EUIPO, DPMA; classes 9, 11, 42) and a decision on the domain.
2. Runtime privacy audit (`scripts/runtime_audit.sh`).
3. Clean benchmark runs for the idle, desktop, video and gaming scenarios, with sound playing for the audio latency.
