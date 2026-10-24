#if canImport(Network)
import Foundation
import Network
import Security
import HueCore

/// Hue Entertainment API: DTLS 1.2 over UDP :2100 with PSK (identity = application id, key = client key),
/// cipher suite TLS_PSK_WITH_AES_128_GCM_SHA256.
public final class EntertainmentStreamer: @unchecked Sendable {
    private let host: String, appID: String, areaID: String
    private let psk: Data
    private var conn: NWConnection?
    private let q = DispatchQueue(label: "hue.dtls", qos: .userInteractive)
    private var seq: UInt8 = 0
    private var started = false
    public var onFailure: (@Sendable (String) -> Void)?

    public init?(host: String, appID: String, clientKeyHex: String, areaID: String) {
        var bytes = Data(); var it = clientKeyHex.makeIterator()
        while let a = it.next(), let b = it.next() {
            guard let v = UInt8(String([a, b]), radix: 16) else { return nil }
            bytes.append(v)
        }
        guard bytes.count == 16 else { return nil }
        self.host = host; self.appID = appID; self.areaID = areaID; self.psk = bytes
    }

    public func start(timeout: TimeInterval = 6) async throws {
        let tls = NWProtocolTLS.Options()
        let o = tls.securityProtocolOptions
        let pskData = psk.withUnsafeBytes { DispatchData(bytes: $0) }
        let idData = Array(appID.utf8).withUnsafeBytes { DispatchData(bytes: $0) }
        sec_protocol_options_add_pre_shared_key(o, pskData as __DispatchData, idData as __DispatchData)
        sec_protocol_options_append_tls_ciphersuite(o, tls_ciphersuite_t(rawValue: 0x00A8)!)
        sec_protocol_options_set_min_tls_protocol_version(o, .DTLSv12)
        let params = NWParameters(dtls: tls, udp: NWProtocolUDP.Options())
        let c = NWConnection(host: NWEndpoint.Host(host), port: 2100, using: params)
        conn = c
        let done = Locked(false)
        try await withCheckedThrowingContinuation { (cont: CheckedContinuation<Void, Error>) in
            @Sendable func finish(_ r: Result<Void, Error>) {
                var first = false
                done.mutate { if !$0 { $0 = true; first = true } }
                if first { cont.resume(with: r) }
            }
            c.stateUpdateHandler = { [weak self] st in
                switch st {
                case .ready: self?.started = true; finish(.success(()))
                case .failed(let e):
                    finish(.failure(HueError.streamFailed(e.localizedDescription)))
                    if self?.started == true { self?.onFailure?(e.localizedDescription) }
                case .waiting(let e): if self?.started == true { self?.onFailure?(e.localizedDescription) }
                default: break
                }
            }
            c.start(queue: q)
            q.asyncAfter(deadline: .now() + timeout) {
                finish(.failure(HueError.streamFailed("The bridge did not answer. Make sure no other app is syncing.")))
                if !self.started { c.cancel() }
            }
        }
    }

    public func send(_ colors: [(channel: UInt8, rgb: RGB)]) {
        seq &+= 1
        guard started, let pkt = StreamPacket.build(areaID: areaID, sequence: seq, colors: colors) else { return }
        conn?.send(content: pkt, completion: .contentProcessed { _ in })
    }

    public func stop() {
        started = false
        conn?.cancel(); conn = nil
    }
}
#endif
