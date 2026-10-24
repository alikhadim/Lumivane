import AppKit
import ScreenCaptureKit
import CoreGraphics
import HueCore

public struct LiveDisplay: Sendable {
    public var geometry: DisplayGeometry
    public var directID: UInt32
    public init(geometry: DisplayGeometry, directID: UInt32) { self.geometry = geometry; self.directID = directID }
}

public struct LiveWindow: Sendable, Identifiable {
    public var id: String          // stable source id
    public var title: String
    public var appName: String
    public var bundleID: String
    public var windowID: UInt32
    public init(id: String, title: String, appName: String, bundleID: String, windowID: UInt32) {
        self.id = id; self.title = title; self.appName = appName; self.bundleID = bundleID; self.windowID = windowID
    }
}

public enum DisplayCatalog {
    /// Connected displays with name, native resolution, refresh rate and desktop frame. Call on the main thread.
    public static func current() -> [LiveDisplay] {
        NSScreen.screens.compactMap { s in
            guard let num = s.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber else { return nil }
            let id = CGDirectDisplayID(num.uint32Value)
            let b = CGDisplayBounds(id)
            let mode = CGDisplayCopyDisplayMode(id)
            let hz = (mode?.refreshRate ?? 0) > 0 ? mode!.refreshRate : Double(s.maximumFramesPerSecond)
            let g = DisplayGeometry(key: uuid(id), name: s.localizedName,
                                    pixelWidth: mode?.pixelWidth ?? Int(b.width), pixelHeight: mode?.pixelHeight ?? Int(b.height),
                                    x: b.origin.x, y: b.origin.y, width: b.width, height: b.height,
                                    refreshHz: hz, isBuiltIn: CGDisplayIsBuiltin(id) != 0)
            return LiveDisplay(geometry: g, directID: id)
        }
    }

    static func uuid(_ id: CGDirectDisplayID) -> String {
        if let u = CGDisplayCreateUUIDFromDisplayID(id)?.takeRetainedValue(), let s = CFUUIDCreateString(nil, u) { return s as String }
        return "id\(id)"
    }
}

public enum WindowCatalog {
    public static func current() async -> [LiveWindow] {
        guard let c = try? await SCShareableContent.excludingDesktopWindows(true, onScreenWindowsOnly: true) else { return [] }
        let me = Bundle.main.bundleIdentifier
        var seen = Set<String>()
        return c.windows.compactMap { w in
            guard w.isOnScreen, w.windowLayer == 0, w.frame.width > 300, w.frame.height > 200,
                  let title = w.title, !title.isEmpty, let app = w.owningApplication, app.bundleIdentifier != me else { return nil }
            let id = SourceID.window(bundle: app.bundleIdentifier, title: title)
            guard seen.insert(id).inserted else { return nil }
            return LiveWindow(id: id, title: title, appName: app.applicationName, bundleID: app.bundleIdentifier, windowID: w.windowID)
        }
    }
}

/// One-shot low-resolution screenshots. Called only while a preview is on screen, so no stream stays open.
public enum Thumbnailer {
    public static func capture(_ targets: [CaptureTarget], width: Int = 360) async -> [CaptureTarget: CGImage] {
        guard !targets.isEmpty, let c = try? await SCShareableContent.excludingDesktopWindows(true, onScreenWindowsOnly: true) else { return [:] }
        // Sequential on purpose: SCShareableContent is not Sendable, and a handful of one-shot captures is fast.
        var out: [CaptureTarget: CGImage] = [:]
        for t in targets {
            let filter: SCContentFilter, size: CGSize
            switch t {
            case .display(let id):
                guard let d = c.displays.first(where: { $0.displayID == id }) else { continue }
                filter = SCContentFilter(display: d, excludingWindows: []); size = CGSize(width: d.width, height: d.height)
            case .window(let id):
                guard let w = c.windows.first(where: { $0.windowID == id }) else { continue }
                filter = SCContentFilter(desktopIndependentWindow: w); size = w.frame.size
            }
            let cfg = SCStreamConfiguration()
            cfg.width = width; cfg.height = max(16, Int(Double(width) * size.height / max(size.width, 1)))
            cfg.showsCursor = false
            if let img = try? await SCScreenshotManager.captureImage(contentFilter: filter, configuration: cfg) { out[t] = img }
        }
        return out
    }
}
