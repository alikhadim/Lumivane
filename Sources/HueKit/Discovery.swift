import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif
#if canImport(Network)
import Network
#endif
import HueCore

final class Locked<T>: @unchecked Sendable {
    private let lock = NSLock()
    private var v: T
    init(_ v: T) { self.v = v }
    var value: T { lock.lock(); defer { lock.unlock() }; return v }
    func mutate(_ f: (inout T) -> Void) { lock.lock(); f(&v); lock.unlock() }
}

public enum BridgeDiscovery {
    private struct CloudEntry: Decodable { let id: String; let internalipaddress: String }

    /// https://discovery.meethue.com
    public static func cloud() async -> [BridgeInfo] {
        guard let url = URL(string: "https://discovery.meethue.com") else { return [] }
        var req = URLRequest(url: url); req.timeoutInterval = 5
        // Ephemeral: no disk cache, cookies or credential storage, so the response (which lists the bridge address) is never written to disk.
        let session = URLSession(configuration: .ephemeral)
        defer { session.finishTasksAndInvalidate() }
        guard let (d, _) = try? await session.data(for: req),
              let e = try? JSONDecoder().decode([CloudEntry].self, from: d) else { return [] }
        return e.map { BridgeInfo(id: $0.id.uppercased(), ip: $0.internalipaddress) }
    }

    #if canImport(Network)
    /// mDNS/Bonjour, service type _hue._tcp.
    public static func bonjour(timeout: TimeInterval = 4) async -> [BridgeInfo] {
        await withCheckedContinuation { (cont: CheckedContinuation<[BridgeInfo], Never>) in
            let q = DispatchQueue(label: "hue.bonjour")
            let browser = NWBrowser(for: .bonjourWithTXTRecord(type: "_hue._tcp", domain: nil), using: .tcp)
            let found = Locked<[String: BridgeInfo]>([:])
            let conns = Locked<[NWConnection]>([])
            browser.browseResultsChangedHandler = { results, _ in
                for r in results {
                    let bid: String
                    if case .bonjour(let txt) = r.metadata { bid = txt["bridgeid"] ?? "" } else { bid = "" }
                    // Resolve to an IPv4 address: Network.framework otherwise often picks the bridge's IPv6 link-local address.
                    let params = NWParameters.tcp
                    (params.defaultProtocolStack.internetProtocol as? NWProtocolIP.Options)?.version = .v4
                    let c = NWConnection(to: r.endpoint, using: params)
                    conns.mutate { $0.append(c) }
                    c.stateUpdateHandler = { st in
                        guard case .ready = st, case let .hostPort(host, _)? = c.currentPath?.remoteEndpoint else { return }
                        if case .ipv4(let a) = host {
                            let ip = "\(a)".components(separatedBy: "%")[0]
                            found.mutate { $0[ip] = BridgeInfo(id: bid.uppercased(), ip: ip) }
                        }
                        c.cancel()
                    }
                    c.start(queue: q)
                }
            }
            browser.start(queue: q)
            q.asyncAfter(deadline: .now() + timeout) {
                browser.cancel()
                conns.value.forEach { $0.cancel() }
                cont.resume(returning: Array(found.value.values))
            }
        }
    }
    #endif

    /// Local discovery (Bonjour). The Philips cloud lookup only runs when `allowCloud` is true, because it
    /// sends this network's public IP address to Philips. Every candidate is verified against the bridge itself.
    public static func discover(allowCloud: Bool = false) async -> [BridgeInfo] {
        #if canImport(Network)
        async let b = bonjour()
        #endif
        async let c: [BridgeInfo] = allowCloud ? cloud() : []
        #if canImport(Network)
        let candidates = await b + c
        #else
        let candidates = await c
        #endif
        var seen = Set<String>()
        let unique = candidates.filter { seen.insert($0.ip).inserted }
        return await withTaskGroup(of: BridgeInfo?.self) { g in
            for x in unique { g.addTask { await HueClient.probe(ip: x.ip)?.info } }
            var out: [BridgeInfo] = []
            for await r in g { if let r { out.append(r) } }
            return out.sorted { $0.ip < $1.ip }
        }
    }
}
