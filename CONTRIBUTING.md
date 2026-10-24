# Contributing

## Setup
macOS 14+, Xcode 16+ (Swift 6 toolchain).

```
swift test                      # HueCore tests (also run on Linux)
swift run ColorLab colorlab-out # color style board
./scripts/build_app.sh          # release build of the app
```

`HueCore` is pure Swift with no Apple frameworks. Put logic there when possible, so it stays testable on any platform.

## Rules
- All analysis stays on the device. No telemetry, analytics or third-party network calls. Network code lives in `HueKit` only.
- No absolute paths, user or host names, IP addresses, keys or tokens in code, docs, fixtures or screenshots. Use `<user-home>`, `<app-data>`, `<project-root>`.
- Add a unit test for color, routing, audio or packet changes.
- Keep commits focused; describe the behavior change, not the files touched.
- Do not use Philips, Signify or Hue trademarks in names, icons or marketing. Factual compatibility statements are fine.

## Pull requests
Run `swift test` and `./scripts/build_app.sh` before opening one. Note which macOS version and Hue hardware you tested on.

## Reporting bugs
Use the redacted report from Settings. Do not post bridge IDs, IP addresses or screenshots of your room names.
