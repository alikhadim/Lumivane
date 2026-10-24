import Foundation

public enum SyncMode: String, Codable, Sendable, CaseIterable {
    /// One source drives every selected light.
    case single
    /// Several sources are analysed together; each light follows the average of them.
    case mirror
    /// Each light follows its own assigned source.
    case extend
}

/// Stable source identifiers. Displays use the display UUID so mappings survive reconnects and reboots.
public enum SourceID {
    public static let desktop = "desktop"
    public static func display(_ key: String) -> String { "display:\(key)" }
    public static func window(bundle: String, title: String) -> String { "window:\(bundle)|\(title)" }
    public static func displayKey(_ id: String) -> String? { id.hasPrefix("display:") ? String(id.dropFirst(8)) : nil }
    public static func isWindow(_ id: String) -> Bool { id.hasPrefix("window:") }
}

public struct DisplayGeometry: Codable, Sendable, Hashable, Identifiable {
    public var key: String
    public var name: String
    public var pixelWidth: Int, pixelHeight: Int
    /// Global desktop frame in points, origin top-left of the main display.
    public var x: Double, y: Double, width: Double, height: Double
    public var refreshHz: Double
    public var isBuiltIn: Bool
    public var isConnected: Bool
    public var id: String { SourceID.display(key) }
    public var isUltrawide: Bool { pixelHeight > 0 && Double(pixelWidth) / Double(pixelHeight) >= 2.0 }
    public var resolution: String { "\(pixelWidth)×\(pixelHeight)" }
    public init(key: String, name: String, pixelWidth: Int, pixelHeight: Int, x: Double, y: Double, width: Double, height: Double,
                refreshHz: Double, isBuiltIn: Bool, isConnected: Bool = true) {
        self.key = key; self.name = name; self.pixelWidth = pixelWidth; self.pixelHeight = pixelHeight
        self.x = x; self.y = y; self.width = width; self.height = height
        self.refreshHz = refreshHz; self.isBuiltIn = isBuiltIn; self.isConnected = isConnected
    }
}

public struct WindowSource: Codable, Sendable, Hashable, Identifiable {
    public var id: String
    public var title: String
    public var appName: String
    public init(id: String, title: String, appName: String) { self.id = id; self.title = title; self.appName = appName }
}

public struct LightRoute: Codable, Sendable, Hashable {
    public var channelID: Int
    /// Position on the assigned source, 0...1 (u: left to right, v: top to bottom).
    public var u: Double, v: Double
    /// Extend mode: the source this light follows. nil = not assigned.
    public var sourceID: String?
    public var enabled: Bool
    public init(channelID: Int, u: Double, v: Double, sourceID: String? = nil, enabled: Bool = true) {
        self.channelID = channelID; self.u = u; self.v = v; self.sourceID = sourceID; self.enabled = enabled
    }
}

public struct Profile: Codable, Sendable, Equatable, Identifiable {
    public var id: UUID
    public var name: String
    public var symbol: String
    public var areaID: String?
    public var mode: SyncMode
    public var primarySource: String?
    public var mirrorSources: [String]
    public var fallbackSource: String?
    public var windowSources: [WindowSource]
    public var routes: [LightRoute]
    public var settings: SyncSettings

    public init(id: UUID = UUID(), name: String, symbol: String = "sparkles.tv", areaID: String? = nil, mode: SyncMode = .single,
                primarySource: String? = nil, mirrorSources: [String] = [], fallbackSource: String? = nil,
                windowSources: [WindowSource] = [], routes: [LightRoute] = [], settings: SyncSettings = SyncSettings()) {
        self.id = id; self.name = name; self.symbol = symbol; self.areaID = areaID; self.mode = mode
        self.primarySource = primarySource; self.mirrorSources = mirrorSources; self.fallbackSource = fallbackSource
        self.windowSources = windowSources; self.routes = routes; self.settings = settings
    }

    /// Adds default routes for channels that have none. Existing routes (including stale ones) are kept.
    public mutating func ensureRoutes(for channels: [EntertainmentChannel]) {
        for r in ZoneLayout.defaultRoutes(for: channels) where !routes.contains(where: { $0.channelID == r.channelID }) { routes.append(r) }
    }

    public static func presets() -> [Profile] {
        var movie = SyncSettings(); movie.style = .cinematic; movie.smoothing = 0.7; movie.dominance = 0.6
        movie.audio.influence = 0.15; movie.audio.musicStrength = 0.4; movie.audio.smoothing = 0.6
        var music = SyncSettings(); music.style = .enhanced; music.smoothing = 0.3; music.dominance = 0.3
        music.audio.influence = 0.85; music.audio.musicStrength = 0.8; music.audio.beatIntensity = 1.2; music.audio.smoothing = 0.35
        var gaming = SyncSettings(); gaming.style = .gaming; gaming.smoothing = 0.15; gaming.fps = 60; gaming.dynamicBrightness = false
        gaming.audio.influence = 0.3; gaming.audio.beatIntensity = 1; gaming.audio.smoothing = 0.4
        var ambient = SyncSettings(); ambient.style = .enhanced; ambient.smoothing = 0.9; ambient.brightness = 0.4; ambient.lowLightMode = true; ambient.intensity = 0.9; ambient.dominance = 0.7
        ambient.audio.influence = 0.2; ambient.audio.bassSensitivity = 0.7; ambient.audio.beatIntensity = 0.5; ambient.audio.smoothing = 0.85; ambient.audio.musicStrength = 0.3
        return [Profile(name: "Movie Mode", symbol: "film", settings: movie),
                Profile(name: "Music Mode", symbol: "music.note", settings: music),
                Profile(name: "Gaming Mode", symbol: "gamecontroller", settings: gaming),
                Profile(name: "Relaxed Ambient", symbol: "leaf", settings: ambient)]
    }
}

// MARK: - Route planning

public struct SamplePoint: Hashable, Sendable {
    /// Physical capture source: `display:<key>` or `window:...`.
    public var source: String
    public var rect: ZoneRect
    public init(source: String, rect: ZoneRect) { self.source = source; self.rect = rect }
}

public struct Substitution: Hashable, Sendable {
    public var requested: String
    public var actual: String
    public init(requested: String, actual: String) { self.requested = requested; self.actual = actual }
}

public struct RoutePlan: Equatable, Sendable {
    /// Per driven light channel: the screen areas it averages. Lights absent here are left untouched.
    public var samples: [Int: [SamplePoint]] = [:]
    public var substitutions: [Substitution] = []
    /// Requested sources that are gone and had no replacement.
    public var unavailable: [String] = []
    /// Only these sources need a capture session.
    public var physicalSources: Set<String> { Set(samples.values.flatMap { $0.map(\.source) }) }
    public init() {}
}

public enum RoutePlanner {
    /// Pure function of the (preserved) profile and what is connected right now.
    /// Mappings are never rewritten: when a display returns, the same profile resolves back to it.
    public static func plan(_ p: Profile, channelIDs: [Int], displays: [DisplayGeometry], windows: Set<String>) -> RoutePlan {
        var plan = RoutePlan()
        let live = displays.filter(\.isConnected)
        if p.settings.audio.enabled && p.settings.audio.influence >= 0.99 { return plan }   // audio only, no capture needed

        func available(_ id: String) -> Bool {
            if id == SourceID.desktop { return !live.isEmpty }
            if let k = SourceID.displayKey(id) { return live.contains { $0.key == k } }
            return windows.contains(id)
        }
        func fallback() -> String? {
            if let f = p.fallbackSource, available(f) { return f }
            if let b = live.first(where: \.isBuiltIn) { return SourceID.display(b.key) }
            if let m = live.first(where: { $0.x == 0 && $0.y == 0 }) ?? live.first { return SourceID.display(m.key) }
            return nil
        }
        var cache: [String: String?] = [:]
        func resolve(_ requested: String?) -> String? {
            guard let r = requested else { return fallback() }
            if let c = cache[r] { return c }
            var out: String?
            if available(r) { out = r }
            else if let alt = fallback() { out = alt; plan.substitutions.append(Substitution(requested: r, actual: alt)) }
            else { plan.unavailable.append(r) }
            cache[r] = out
            return out
        }
        func point(_ src: String, _ u: Double, _ v: Double) -> SamplePoint? {
            if src == SourceID.desktop {
                guard let (key, lu, lv) = desktopPoint(u: u, v: v, displays: live) else { return nil }
                return SamplePoint(source: SourceID.display(key), rect: ZoneLayout.rect(u: lu, v: lv))
            }
            return SamplePoint(source: src, rect: ZoneLayout.rect(u: u, v: v))
        }

        let routes = Dictionary(p.routes.map { ($0.channelID, $0) }, uniquingKeysWith: { a, _ in a })
        for ch in channelIDs {
            let r = routes[ch] ?? LightRoute(channelID: ch, u: 0.5, v: 0.5)
            guard r.enabled else { continue }
            var srcs: [String] = []
            switch p.mode {
            case .single:
                if let s = resolve(p.primarySource) { srcs = [s] }
            case .mirror:
                let requested = p.mirrorSources.isEmpty ? [p.primarySource] : p.mirrorSources.map { Optional($0) }
                for q in requested { if let s = resolve(q), !srcs.contains(s) { srcs.append(s) } }
            case .extend:
                guard let sid = r.sourceID else { continue }
                if let s = resolve(sid) { srcs = [s] }
            }
            let pts = srcs.compactMap { point($0, r.u, r.v) }
            if !pts.isEmpty { plan.samples[ch] = pts }
        }
        return plan
    }

    /// Maps a point on the whole-desktop bounding box to the display under it (nearest display across gaps).
    public static func desktopPoint(u: Double, v: Double, displays: [DisplayGeometry]) -> (key: String, u: Double, v: Double)? {
        guard !displays.isEmpty else { return nil }
        let minX = displays.map(\.x).min()!, maxX = displays.map { $0.x + $0.width }.max()!
        let minY = displays.map(\.y).min()!, maxY = displays.map { $0.y + $0.height }.max()!
        let gx = minX + u * (maxX - minX), gy = minY + v * (maxY - minY)
        var best: (d: DisplayGeometry, dist: Double)?
        for d in displays {
            let cx = min(max(gx, d.x), d.x + d.width), cy = min(max(gy, d.y), d.y + d.height)
            let dist = ((gx - cx) * (gx - cx) + (gy - cy) * (gy - cy)).squareRoot()
            if best == nil || dist < best!.dist { best = (d, dist) }
        }
        let d = best!.d
        return (d.key, min(max((gx - d.x) / d.width, 0), 1), min(max((gy - d.y) / d.height, 0), 1))
    }
}

extension ZoneStats {
    /// Mirror mode: several sources analysed together.
    public static func combine(_ s: [ZoneStats]) -> ZoneStats {
        guard s.count > 1 else { return s.first ?? .empty }
        let n = Float(s.count)
        func mean(_ f: (ZoneStats) -> RGB) -> RGB { s.reduce(RGB.black) { RGB($0.r + f($1).r, $0.g + f($1).g, $0.b + f($1).b) }.scaled(1 / n) }
        // Dominant colors are weighted by how colorful their zone is, so a gray zone doesn't wash out a vivid one.
        let w = s.map { $0.dominance + 0.02 }, wt = w.reduce(0, +)
        var dom = RGB.black
        for (z, k) in zip(s, w) { dom = RGB(dom.r + z.dominant.r * k, dom.g + z.dominant.g * k, dom.b + z.dominant.b * k) }
        return ZoneStats(average: mean(\.average), dominant: dom.scaled(1 / wt), dominance: s.map(\.dominance).reduce(0, +) / n,
                         brightness: s.map(\.brightness).reduce(0, +) / n, saturation: s.map(\.saturation).reduce(0, +) / n)
    }
}
