import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif
import HueCore

public enum HueError: Error, LocalizedError, Sendable {
    case linkButtonNotPressed, noCredentials, unreachable(String), api(String), badResponse(Int), streamFailed(String)
    public var errorDescription: String? {
        switch self {
        case .linkButtonNotPressed: "Press the round button on top of your Hue Bridge, then try again."
        case .noCredentials: "This Mac is not paired with a bridge yet."
        case .unreachable(let m): "Can't reach your Hue Bridge. Check that it is powered and on the same network. (\(m))"
        case .api(let m): "The bridge said: \(m)"
        case .badResponse(let c): "Unexpected answer from the bridge (HTTP \(c))."
        case .streamFailed(let m): "Couldn't start the light stream. \(m)"
        }
    }
}

/// The bridge presents a certificate signed by a private Signify CA (or self-signed on old firmware).
/// Trust is limited to the bridge IP the user selected.
final class BridgeTrust: NSObject, URLSessionDelegate, @unchecked Sendable {
    let host: String
    init(host: String) { self.host = host }
    func urlSession(_ session: URLSession, didReceive challenge: URLAuthenticationChallenge,
                    completionHandler: @escaping (URLSession.AuthChallengeDisposition, URLCredential?) -> Void) {
        #if canImport(Darwin)
        if challenge.protectionSpace.authenticationMethod == NSURLAuthenticationMethodServerTrust,
           challenge.protectionSpace.host == host, let t = challenge.protectionSpace.serverTrust {
            completionHandler(.useCredential, URLCredential(trust: t)); return
        }
        #endif
        completionHandler(.performDefaultHandling, nil)
    }
}

private struct Ref: Decodable { let rid: String; let rtype: String }
private struct Envelope<T: Decodable>: Decodable { let data: [T]?; let errors: [APIErr]? }
private struct APIErr: Decodable { let description: String }
private struct NameMeta: Decodable { let name: String?; let archetype: String? }
private struct LightDTO: Decodable {
    let id: String; let owner: Ref?; let metadata: NameMeta?
    let on: On?; let dimming: Dim?; let color: Col?
    struct On: Decodable { let on: Bool }
    struct Dim: Decodable { let brightness: Double }
    struct Col: Decodable { let gamut: G?
        struct G: Decodable { let red: P; let green: P; let blue: P }
        struct P: Decodable { let x: Double; let y: Double } }
}
private struct DeviceDTO: Decodable { let id: String; let services: [Ref]? }
private struct GroupDTO: Decodable { let id: String; let metadata: NameMeta?; let children: [Ref]?; let services: [Ref]? }
private struct SceneDTO: Decodable {
    let id: String; let metadata: NameMeta?; let group: Ref?; let status: St?
    struct St: Decodable { let active: String? }
}
private struct AreaDTO: Decodable {
    let id: String; let metadata: NameMeta?; let configuration_type: String?; let status: String?; let channels: [Ch]?
    struct Ch: Decodable { let channel_id: Int; let position: Pos; let members: [Mem]? }
    struct Pos: Decodable { let x: Double; let y: Double; let z: Double }
    struct Mem: Decodable { let service: Ref }
}
private struct PairItem: Decodable {
    let success: S?; let error: E?
    struct S: Decodable { let username: String; let clientkey: String? }
    struct E: Decodable { let type: Int; let description: String }
}
private struct ConfigDTO: Decodable { let name: String?; let swversion: String?; let apiversion: String?; let bridgeid: String?; let modelid: String? }

public actor HueClient {
    public let bridge: BridgeInfo
    private var credentials: Credentials?
    private let session: URLSession

    public init(bridge: BridgeInfo, credentials: Credentials? = nil) {
        self.bridge = bridge
        self.credentials = credentials
        let cfg = URLSessionConfiguration.ephemeral
        cfg.timeoutIntervalForRequest = 5
        cfg.httpMaximumConnectionsPerHost = 4
        session = URLSession(configuration: cfg, delegate: BridgeTrust(host: bridge.ip), delegateQueue: nil)
    }

    public func setCredentials(_ c: Credentials?) { credentials = c }

    // MARK: transport

    private func raw(_ method: String, _ path: String, body: Data? = nil, authed: Bool = true) async throws -> (Data, HTTPURLResponse) {
        guard let url = URL(string: "https://\(bridge.ip)\(path)") else { throw HueError.unreachable("bad address") }
        var req = URLRequest(url: url)
        req.httpMethod = method
        if authed {
            guard let c = credentials else { throw HueError.noCredentials }
            req.setValue(c.appKey, forHTTPHeaderField: "hue-application-key")
        }
        if let body { req.httpBody = body; req.setValue("application/json", forHTTPHeaderField: "Content-Type") }
        do {
            let (d, r) = try await session.data(for: req)
            guard let h = r as? HTTPURLResponse else { throw HueError.badResponse(0) }
            return (d, h)
        } catch let e as HueError { throw e } catch { throw HueError.unreachable(error.localizedDescription) }
    }

    private func list<T: Decodable>(_ path: String, _ type: T.Type) async throws -> [T] {
        let (d, h) = try await raw("GET", "/clip/v2/resource/\(path)")
        guard (200..<300).contains(h.statusCode) else { throw HueError.badResponse(h.statusCode) }
        let env = try JSONDecoder().decode(Envelope<T>.self, from: d)
        if let e = env.errors?.first { throw HueError.api(e.description) }
        return env.data ?? []
    }

    @discardableResult
    private func write(_ method: String, _ path: String, _ json: [String: Any]) async throws -> Data {
        let body = try JSONSerialization.data(withJSONObject: json)
        let (d, h) = try await raw(method, "/clip/v2/resource/\(path)", body: body)
        if let env = try? JSONDecoder().decode(Envelope<Ref>.self, from: d), let e = env.errors?.first { throw HueError.api(e.description) }
        guard (200..<300).contains(h.statusCode) else { throw HueError.badResponse(h.statusCode) }
        return d
    }

    // MARK: bridge

    /// Unauthenticated bridge info; nil if the address isn't a Hue Bridge.
    public static func probe(ip: String) async -> (info: BridgeInfo, latencyMs: Double)? {
        let c = HueClient(bridge: BridgeInfo(ip: ip))
        return try? await c.config()
    }

    public func config() async throws -> (info: BridgeInfo, latencyMs: Double) {
        let t0 = DispatchTime.now().uptimeNanoseconds
        let (d, h) = try await raw("GET", "/api/config", authed: false)
        let ms = Double(DispatchTime.now().uptimeNanoseconds - t0) / 1e6
        guard h.statusCode == 200, let c = try? JSONDecoder().decode(ConfigDTO.self, from: d), c.bridgeid != nil else {
            throw HueError.badResponse(h.statusCode)
        }
        return (BridgeInfo(id: c.bridgeid ?? "", ip: bridge.ip, name: c.name, firmware: c.swversion,
                           apiVersion: c.apiversion, model: c.modelid), ms)
    }

    /// Polls until the link button is pressed or the timeout passes.
    public func pair(timeout: TimeInterval = 40) async throws -> Credentials {
        let body = try JSONSerialization.data(withJSONObject: ["devicetype": "lumivane#mac", "generateclientkey": true])
        let deadline = Date().addingTimeInterval(timeout)
        while true {
            try Task.checkCancellation()
            let (d, _) = try await raw("POST", "/api", body: body, authed: false)
            if let items = try? JSONDecoder().decode([PairItem].self, from: d), let it = items.first {
                if let s = it.success, let ck = s.clientkey {
                    let c = Credentials(appKey: s.username, clientKey: ck)
                    credentials = c
                    return c
                }
                if let e = it.error, e.type != 101 { throw HueError.api(e.description) }
            }
            if Date() > deadline { throw HueError.linkButtonNotPressed }
            try await Task.sleep(nanoseconds: 1_500_000_000)
        }
    }

    /// Needed as the DTLS PSK identity.
    public func applicationID() async throws -> String {
        let (_, h) = try await raw("GET", "/auth/v1")
        guard let id = h.value(forHTTPHeaderField: "hue-application-id") else { throw HueError.badResponse(h.statusCode) }
        return id
    }

    // MARK: read

    public func lights() async throws -> [HueLight] {
        async let l = list("light", LightDTO.self)
        async let d = list("device", DeviceDTO.self)
        let (lights, devices) = try await (l, d)
        var entByDevice: [String: String] = [:]
        for dev in devices { if let e = dev.services?.first(where: { $0.rtype == "entertainment" }) { entByDevice[dev.id] = e.rid } }
        return lights.map { x in
            HueLight(id: x.id, name: x.metadata?.name ?? "Light", archetype: x.metadata?.archetype,
                     isOn: x.on?.on ?? false, brightness: x.dimming?.brightness ?? 100,
                     gamut: x.color?.gamut.map { Gamut(red: XY($0.red.x, $0.red.y), green: XY($0.green.x, $0.green.y), blue: XY($0.blue.x, $0.blue.y)) },
                     entertainmentServiceID: x.owner.flatMap { entByDevice[$0.rid] })
        }
    }

    public func groups() async throws -> [HueGroup] {
        async let r = list("room", GroupDTO.self)
        async let z = list("zone", GroupDTO.self)
        async let d = list("device", DeviceDTO.self)
        let (rooms, zones, devices) = try await (r, z, d)
        var lightByDevice: [String: String] = [:]
        for dev in devices { if let l = dev.services?.first(where: { $0.rtype == "light" }) { lightByDevice[dev.id] = l.rid } }
        func map(_ g: GroupDTO, _ k: HueGroup.Kind) -> HueGroup {
            let ids: [String] = (g.children ?? []).compactMap { c in
                c.rtype == "light" ? c.rid : (c.rtype == "device" ? lightByDevice[c.rid] : nil)
            }
            return HueGroup(id: g.id, name: g.metadata?.name ?? (k == .room ? "Room" : "Zone"), kind: k,
                            archetype: g.metadata?.archetype, lightIDs: ids,
                            groupedLightID: g.services?.first { $0.rtype == "grouped_light" }?.rid)
        }
        return rooms.map { map($0, .room) } + zones.map { map($0, .zone) }
    }

    public func scenes() async throws -> [HueScene] {
        try await list("scene", SceneDTO.self).map {
            HueScene(id: $0.id, name: $0.metadata?.name ?? "Scene", groupID: $0.group?.rid ?? "",
                     groupType: $0.group?.rtype ?? "", isActive: ($0.status?.active ?? "inactive") != "inactive")
        }
    }

    public func areas() async throws -> [EntertainmentArea] {
        try await list("entertainment_configuration", AreaDTO.self).map { a in
            EntertainmentArea(id: a.id, name: a.metadata?.name ?? "Entertainment area", type: a.configuration_type ?? "other",
                              isActive: a.status == "active",
                              channels: (a.channels ?? []).map {
                                  EntertainmentChannel(id: $0.channel_id, x: $0.position.x, y: $0.position.y, z: $0.position.z,
                                                       serviceIDs: ($0.members ?? []).map { $0.service.rid })
                              })
        }
    }

    public func diagnostics() async -> Diagnostics {
        guard let (info, ms) = try? await config() else {
            return Diagnostics(bridge: bridge, reachable: false, latencyMs: nil, lightCount: 0, entertainmentLightCount: 0, areas: [])
        }
        let lights = (try? await lights()) ?? []
        let areas = (try? await areas()) ?? []
        return Diagnostics(bridge: info, reachable: true, latencyMs: ms, lightCount: lights.count,
                           entertainmentLightCount: lights.filter(\.supportsEntertainment).count, areas: areas)
    }

    // MARK: control

    public func setLight(_ id: String, on: Bool? = nil, brightness: Double? = nil, rgb: RGB? = nil,
                         gamut: Gamut? = nil, durationMs: Int? = nil) async throws {
        var j: [String: Any] = [:]
        if let on { j["on"] = ["on": on] }
        if let brightness { j["dimming"] = ["brightness": min(max(brightness, 1), 100)] }
        if let rgb { let p = ColorMath.xy(rgb, gamut: gamut); j["color"] = ["xy": ["x": p.x, "y": p.y]] }
        if let durationMs { j["dynamics"] = ["duration": durationMs] }
        try await write("PUT", "light/\(id)", j)
    }

    public func setGroup(_ groupedLightID: String, on: Bool? = nil, brightness: Double? = nil) async throws {
        var j: [String: Any] = [:]
        if let on { j["on"] = ["on": on] }
        if let brightness { j["dimming"] = ["brightness": min(max(brightness, 1), 100)] }
        try await write("PUT", "grouped_light/\(groupedLightID)", j)
    }

    public func recall(scene id: String) async throws {
        try await write("PUT", "scene/\(id)", ["recall": ["action": "active"]])
    }

    // MARK: zones (Hue groups of lights)

    public func createZone(name: String, lightIDs: [String]) async throws {
        try await write("POST", "zone", ["metadata": ["name": name, "archetype": "other"],
                                         "children": lightIDs.map { ["rid": $0, "rtype": "light"] }])
    }
    public func updateZone(_ id: String, name: String? = nil, lightIDs: [String]? = nil) async throws {
        var j: [String: Any] = [:]
        if let name { j["metadata"] = ["name": name] }
        if let lightIDs { j["children"] = lightIDs.map { ["rid": $0, "rtype": "light"] } }
        try await write("PUT", "zone/\(id)", j)
    }
    public func deleteZone(_ id: String) async throws { _ = try await raw("DELETE", "/clip/v2/resource/zone/\(id)") }

    // MARK: entertainment areas

    public func setStreaming(areaID: String, active: Bool) async throws {
        try await write("PUT", "entertainment_configuration/\(areaID)", ["action": active ? "start" : "stop"])
    }

    private func locations(for lights: [HueLight]) -> [[String: Any]] {
        let n = max(lights.count, 1)
        return lights.compactMap { l -> [String: Any]? in
            guard let svc = l.entertainmentServiceID, let i = lights.firstIndex(of: l) else { return nil }
            let x = n == 1 ? 0 : -0.9 + 1.8 * Double(i) / Double(n - 1)
            return ["service": ["rid": svc, "rtype": "entertainment"], "equalization_factor": 1,
                    "positions": [["x": x, "y": 0.8, "z": 0.0]]]
        }
    }

    /// Creates a screen area from the given entertainment-capable lights, spread left to right in front of the screen.
    public func createArea(name: String, lights: [HueLight]) async throws {
        try await write("POST", "entertainment_configuration",
                        ["metadata": ["name": name], "configuration_type": "screen", "stream_proxy": ["mode": "auto"],
                         "light_services": lights.map { ["rid": $0.id, "rtype": "light"] },
                         "locations": ["service_locations": locations(for: lights)]])
    }

    /// Replaces the lights of an existing area; positions are reset to an even left-to-right spread.
    public func updateArea(_ id: String, lights: [HueLight]) async throws {
        try await write("PUT", "entertainment_configuration/\(id)",
                        ["light_services": lights.map { ["rid": $0.id, "rtype": "light"] },
                         "locations": ["service_locations": locations(for: lights)]])
    }
}
