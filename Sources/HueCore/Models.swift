import Foundation

public struct BridgeInfo: Codable, Sendable, Hashable, Identifiable {
    public var id: String
    public var ip: String
    public var name: String?
    public var firmware: String?
    public var apiVersion: String?
    public var model: String?
    public init(id: String = "", ip: String, name: String? = nil, firmware: String? = nil,
                apiVersion: String? = nil, model: String? = nil) {
        self.id = id.isEmpty ? ip : id
        self.ip = ip; self.name = name; self.firmware = firmware
        self.apiVersion = apiVersion; self.model = model
    }
}

public struct Credentials: Codable, Sendable, Hashable {
    public var appKey: String
    public var clientKey: String
    public init(appKey: String, clientKey: String) { self.appKey = appKey; self.clientKey = clientKey }
}

public struct XY: Codable, Sendable, Hashable {
    public var x: Double, y: Double
    public init(_ x: Double, _ y: Double) { self.x = x; self.y = y }
}

public struct Gamut: Codable, Sendable, Hashable {
    public var red: XY, green: XY, blue: XY
    public init(red: XY, green: XY, blue: XY) { self.red = red; self.green = green; self.blue = blue }
}

public struct HueLight: Sendable, Identifiable, Hashable {
    public var id: String
    public var name: String
    public var archetype: String?
    public var isOn: Bool
    /// 0...100
    public var brightness: Double
    public var gamut: Gamut?
    /// Entertainment service id of the owning device; nil if the light cannot stream.
    public var entertainmentServiceID: String?
    public var supportsEntertainment: Bool { entertainmentServiceID != nil }
    public init(id: String, name: String, archetype: String? = nil, isOn: Bool = false, brightness: Double = 100,
                gamut: Gamut? = nil, entertainmentServiceID: String? = nil) {
        self.id = id; self.name = name; self.archetype = archetype; self.isOn = isOn; self.brightness = brightness
        self.gamut = gamut; self.entertainmentServiceID = entertainmentServiceID
    }
}

public struct EntertainmentChannel: Sendable, Hashable, Identifiable {
    public var id: Int
    public var x: Double, y: Double, z: Double
    public var serviceIDs: [String]
    public init(id: Int, x: Double, y: Double, z: Double, serviceIDs: [String] = []) {
        self.id = id; self.x = x; self.y = y; self.z = z; self.serviceIDs = serviceIDs
    }
}

public struct EntertainmentArea: Sendable, Hashable, Identifiable {
    public var id: String
    public var name: String
    public var type: String
    public var isActive: Bool
    public var channels: [EntertainmentChannel]
    public init(id: String, name: String, type: String, isActive: Bool, channels: [EntertainmentChannel]) {
        self.id = id; self.name = name; self.type = type; self.isActive = isActive; self.channels = channels
    }
}

public struct Diagnostics: Sendable {
    public var bridge: BridgeInfo
    public var reachable: Bool
    public var latencyMs: Double?
    public var lightCount: Int
    public var entertainmentLightCount: Int
    public var areas: [EntertainmentArea]
    public init(bridge: BridgeInfo, reachable: Bool, latencyMs: Double?, lightCount: Int,
                entertainmentLightCount: Int, areas: [EntertainmentArea]) {
        self.bridge = bridge; self.reachable = reachable; self.latencyMs = latencyMs
        self.lightCount = lightCount; self.entertainmentLightCount = entertainmentLightCount; self.areas = areas
    }
    public var text: String { report(redacted: false) }

    /// Plain-text summary for support requests. With `redacted`, network and device identifiers are masked
    /// and area names are replaced by counts, so the text can be shared without revealing the home network.
    public func report(redacted: Bool) -> String {
        let ip = redacted ? Diagnostics.maskIP(bridge.ip) : bridge.ip
        let id = redacted ? Diagnostics.maskID(bridge.id) : bridge.id
        let areaText = areas.isEmpty ? "none" : areas.enumerated().map { (i, a) in
            "\(redacted ? "Area \(i + 1)" : a.name) (\(a.channels.count) ch)" }.joined(separator: ", ")
        return """
        Bridge IP:                 \(ip)
        Bridge ID:                 \(id)
        Model:                     \(bridge.model ?? "unknown")
        Firmware (swversion):      \(bridge.firmware ?? "unknown")
        API version:               \(bridge.apiVersion ?? "unknown")
        Reachable:                 \(reachable ? "yes" : "no")
        Response latency:          \(latencyMs.map { String(format: "%.1f ms", $0) } ?? "n/a")
        Connected lights:          \(lightCount)
        Entertainment-capable:     \(entertainmentLightCount)
        Entertainment areas:       \(areaText)
        """
    }

    static func maskIP(_ ip: String) -> String {
        let p = ip.split(separator: ".")
        return p.count == 4 ? "\(p[0]).\(p[1]).x.x" : "x.x.x.x"
    }
    static func maskID(_ id: String) -> String { id.count > 4 ? String(id.prefix(4)) + String(repeating: "x", count: id.count - 4) : id }
}
