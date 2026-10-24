import Foundation
import CoreGraphics
import HueCore
import CaptureKit

/// Synthetic content for Demo Mode. Nothing here touches a device, the network, the screen or the Keychain.
enum DemoData {
    static let bridge = BridgeInfo(id: "DEMO0000000000", ip: "192.168.0.2", name: "Demo Bridge", firmware: "demo", apiVersion: "demo", model: "Demo")
    static let credentials = Credentials(appKey: "demo", clientKey: "demo")

    private static func light(_ n: Int, _ name: String, _ arch: String, ent: Bool = true, on: Bool = true, bri: Double = 80) -> HueLight {
        HueLight(id: "demo-light-\(n)", name: name, archetype: arch, isOn: on, brightness: bri, gamut: nil, entertainmentServiceID: ent ? "demo-ent-\(n)" : nil)
    }

    static let lights: [HueLight] = [
        light(1, "Bar Left", "hue_play"), light(2, "Bar Right", "hue_play"), light(3, "Floor Lamp", "floor_shade"),
        light(4, "Light Strip", "hue_lightstrip"), light(5, "Corner Lamp", "table_shade"), light(6, "Desk Lamp", "desk_lamp"),
        light(7, "Monitor Glow", "hue_lightstrip"), light(8, "Shelf Strip", "hue_lightstrip"),
        light(9, "Bedside Left", "table_shade", ent: false, on: false, bri: 40), light(10, "Bedside Right", "table_shade", ent: false, on: false, bri: 40),
    ]

    static let groups: [HueGroup] = [
        HueGroup(id: "demo-room-living", name: "Living Room", kind: .room, archetype: "living_room", lightIDs: ["demo-light-1", "demo-light-2", "demo-light-3", "demo-light-4", "demo-light-5"], groupedLightID: "demo-gl-1"),
        HueGroup(id: "demo-room-office", name: "Office", kind: .room, archetype: "office", lightIDs: ["demo-light-6", "demo-light-7", "demo-light-8"], groupedLightID: "demo-gl-2"),
        HueGroup(id: "demo-room-bed", name: "Bedroom", kind: .room, archetype: "bedroom", lightIDs: ["demo-light-9", "demo-light-10"], groupedLightID: "demo-gl-3"),
        HueGroup(id: "demo-zone-cinema", name: "Cinema Corner", kind: .zone, archetype: nil, lightIDs: ["demo-light-1", "demo-light-2", "demo-light-4", "demo-light-5"], groupedLightID: "demo-gl-4"),
    ]

    static let scenes: [HueScene] = [
        HueScene(id: "demo-scene-1", name: "Relax", groupID: "demo-room-living", groupType: "room", isActive: false),
        HueScene(id: "demo-scene-2", name: "Evening Glow", groupID: "demo-room-living", groupType: "room", isActive: true),
        HueScene(id: "demo-scene-3", name: "Concentrate", groupID: "demo-room-office", groupType: "room", isActive: false),
        HueScene(id: "demo-scene-4", name: "Energize", groupID: "demo-room-office", groupType: "room", isActive: false),
        HueScene(id: "demo-scene-5", name: "Nightlight", groupID: "demo-room-bed", groupType: "room", isActive: false),
    ]

    // x: -1 left ... 1 right, z: -1 floor ... 1 ceiling
    private static let positions: [(Double, Double)] = [(-0.6, -0.2), (0.6, -0.2), (-0.95, 0.4), (0, -0.9), (0.95, 0.6), (-0.5, -0.7), (0.5, 0.8), (0, 0.95)]

    static let areas: [EntertainmentArea] = [
        EntertainmentArea(id: "demo-area-screen", name: "Screen Sync", type: "screen", isActive: false,
                          channels: positions.enumerated().map { EntertainmentChannel(id: $0.offset, x: $0.element.0, y: 0.8, z: $0.element.1, serviceIDs: ["demo-ent-\($0.offset + 1)"]) }),
        EntertainmentArea(id: "demo-area-music", name: "Music Corner", type: "music", isActive: false,
                          channels: [0, 1, 3, 4].enumerated().map { EntertainmentChannel(id: $0.offset, x: positions[$0.element].0, y: 0.8, z: positions[$0.element].1, serviceIDs: ["demo-ent-\($0.element + 1)"]) }),
    ]

    static var diagnostics: Diagnostics {
        Diagnostics(bridge: bridge, reachable: true, latencyMs: nil, lightCount: lights.count,
                    entertainmentLightCount: lights.filter(\.supportsEntertainment).count, areas: areas)
    }

    // MARK: displays and windows (virtual)

    static let liveDisplays: [LiveDisplay] = [
        LiveDisplay(geometry: DisplayGeometry(key: "demo-display-1", name: "Built-in Display", pixelWidth: 3024, pixelHeight: 1964, x: 0, y: 0, width: 1512, height: 982, refreshHz: 120, isBuiltIn: true), directID: 9001),
        LiveDisplay(geometry: DisplayGeometry(key: "demo-display-2", name: "Desk Monitor", pixelWidth: 3840, pixelHeight: 2160, x: 1512, y: -98, width: 1920, height: 1080, refreshHz: 60, isBuiltIn: false), directID: 9002),
        LiveDisplay(geometry: DisplayGeometry(key: "demo-display-3", name: "Wall TV", pixelWidth: 3840, pixelHeight: 2160, x: 3432, y: -98, width: 1920, height: 1080, refreshHz: 60, isBuiltIn: false), directID: 9003),
    ]

    static let windows: [LiveWindow] = [
        LiveWindow(id: SourceID.window(bundle: "demo.video", title: "Mountain Documentary"), title: "Mountain Documentary", appName: "Video Player", bundleID: "demo.video", windowID: 9101),
        LiveWindow(id: SourceID.window(bundle: "demo.game", title: "Night Drive"), title: "Night Drive", appName: "Game", bundleID: "demo.game", windowID: 9102),
    ]

    static let audioApps: [(bundleID: String, name: String)] = [("demo.music", "Music Player"), ("demo.video", "Video Player")]

    /// Scene shown on each virtual source. Stable across launches.
    static func scene(forSource id: String) -> Int {
        if let i = liveDisplays.firstIndex(where: { $0.geometry.id == id }) { return i }
        if let i = windows.firstIndex(where: { $0.id == id }) { return 3 - i * 2 }   // video -> ember, game -> neon
        return id.utf8.reduce(0) { ($0 + Int($1)) % 4 }
    }

    static func thumbnails(t: Float) -> [CaptureTarget: CGImage] {
        var out: [CaptureTarget: CGImage] = [:]
        for d in liveDisplays { out[.display(d.directID)] = DemoScene.image(scene: scene(forSource: d.geometry.id), t: t, aspect: d.geometry.height / d.geometry.width) }
        for w in windows { out[.window(w.windowID)] = DemoScene.image(scene: scene(forSource: w.id), t: t, aspect: 9.0 / 16.0) }
        return out
    }

    /// Movie profile with all three displays in use, so Extend mode looks populated.
    static func profiles() -> [Profile] {
        var all = Profile.presets()
        let area = areas[0]
        let desk = liveDisplays[1].geometry.id, tv = liveDisplays[2].geometry.id
        for i in all.indices {
            all[i].areaID = area.id; all[i].ensureRoutes(for: area.channels)
            all[i].primarySource = desk
        }
        all[0].mode = .extend
        for i in all[0].routes.indices {
            let c = all[0].routes[i].channelID
            all[0].routes[i].sourceID = [0, 1, 5, 6].contains(c) ? desk : tv
        }
        return all
    }
}

extension DemoScene {
    static func image(scene: Int, t: Float, aspect: Double, width w: Int = 160) -> CGImage? {
        let h = max(16, Int(Double(w) * aspect))
        var px = [UInt8](repeating: 255, count: w * h * 4)
        for y in 0..<h { for x in 0..<w {
            let c = color(scene, u: (Float(x) + 0.5) / Float(w), v: (Float(y) + 0.5) / Float(h), t: t), i = (y * w + x) * 4
            px[i] = UInt8(min(max(c.r, 0), 1) * 255); px[i + 1] = UInt8(min(max(c.g, 0), 1) * 255); px[i + 2] = UInt8(min(max(c.b, 0), 1) * 255)
        } }
        return px.withUnsafeMutableBytes { buf in
            CGContext(data: buf.baseAddress, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w * 4, space: CGColorSpace(name: CGColorSpace.sRGB)!,
                      bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue)?.makeImage()
        }
    }
}
