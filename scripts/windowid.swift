// Window helper for screenshots.sh.  windowid <pid>: first on-screen window of the process.  --any <pid>: also off-screen ones.  --dump <pid>: list.
import CoreGraphics
import Foundation
var args = Array(CommandLine.arguments.dropFirst())
let mode = args.first?.hasPrefix("--") == true ? args.removeFirst() : ""
let pid = Int(args.first ?? "") ?? -1
let opts: CGWindowListOption = mode.isEmpty ? [.optionOnScreenOnly, .excludeDesktopElements] : [.optionAll]
let list = (CGWindowListCopyWindowInfo(opts, kCGNullWindowID) as? [[String: Any]]) ?? []
for w in list where (w[kCGWindowOwnerPID as String] as? Int) == pid {
    let n = w[kCGWindowNumber as String] as? Int ?? 0, layer = w[kCGWindowLayer as String] as? Int ?? -1
    let b = w[kCGWindowBounds as String] as? [String: Any] ?? [:]
    let big = ((b["Width"] as? Double) ?? 0) > 300
    if mode == "--dump" { print("window \(n) layer \(layer) onscreen \(w[kCGWindowIsOnscreen as String] ?? "?") bounds \(b)"); continue }
    if layer == 0 && (mode.isEmpty || big) { print(n); break }
}
