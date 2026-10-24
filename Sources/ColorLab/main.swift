import Foundation
import HueCore

// Offline evaluation: synthetic scenes -> 3x3 zones -> each color algorithm -> light colors and metrics.
// Usage: ColorLab <output-dir>

/// The pre-refinement engine (v1), kept only as a baseline for comparison.
final class LegacyEngine {
    private var state: [Int: RGB] = [:]
    private var lumaAvg: Float = 0.35, autoGain: Float = 1
    func process(_ channels: [(id: Int, stats: ZoneStats)], dt: Float, intensity: Float = 1, smoothing: Float = 0.5, gamma: Float = 1.2, dominance: Float = 0.5) -> [Int: RGB] {
        let frame = channels.reduce(0) { $0 + $1.stats.average.luma } / Float(channels.count)
        lumaAvg += (frame - lumaAvg) * (1 - exp(-dt / 3))
        autoGain += (min(max(0.35 / max(lumaAvg, 0.05), 0.7), 1.6) - autoGain) * (1 - exp(-dt / 2))
        let tau = 0.04 + smoothing * 0.76, slew = 1.5 + (1 - smoothing) * 6.5
        var out: [Int: RGB] = [:]
        for (id, st) in channels {
            var c = st.average.mixed(st.dominant, dominance * min(1, st.dominance * 4))
            var (h, sat, v) = ColorMath.hsv(c)
            sat = min(1, sat * intensity); c = ColorMath.rgb(h: h, s: sat, v: v)
            var cur = state[id] ?? .black
            func step(_ cur: Float, _ tgt: Float) -> Float {
                let t = tgt > cur ? tau * 0.5 : tau * 1.5
                let n = cur + (tgt - cur) * (1 - exp(-dt / t)); let m = slew * dt
                return min(max(n, cur - m), cur + m)
            }
            cur = RGB(step(cur.r, c.r), step(cur.g, c.g), step(cur.b, c.b)); state[id] = cur
            let o = RGB(pow(cur.r, gamma), pow(cur.g, gamma), pow(cur.b, gamma))
            out[id] = o.scaled(min(1 * autoGain, 1)).clamped()
        }
        return out
    }
}

// MARK: scenes (256 x 144 BGRA, deterministic)

let W = 256, H = 144
struct Rng { var s: UInt64 = 0x9E3779B97F4A7C15; mutating func next() -> Float { s = s &* 6364136223846793005 &+ 1442695040888963407; return Float((s >> 40) & 0xFFFF) / 65535 } }

func scene(_ f: (Float, Float, inout Rng) -> RGB) -> [UInt8] {
    var px = [UInt8](repeating: 255, count: W * H * 4); var rng = Rng()
    for y in 0..<H { for x in 0..<W {
        let c = f(Float(x) / Float(W), Float(y) / Float(H), &rng).clamped(), i = (y * W + x) * 4
        px[i] = UInt8((c.b * 255).rounded()); px[i + 1] = UInt8((c.g * 255).rounded()); px[i + 2] = UInt8((c.r * 255).rounded())
    } }
    return px
}
func n(_ r: inout Rng, _ a: Float) -> Float { (r.next() - 0.5) * a }

let scenes: [(String, String, [UInt8])] = [
    ("sunset", "Sunset over water", scene { u, v, r in
        if v < 0.55 { let t = v / 0.55; return RGB(0.15 + 0.85 * t, 0.12 + 0.45 * t * t, 0.35 - 0.2 * t).mixed(RGB(1, 0.55, 0.1), pow(max(0, 1 - abs(u - 0.5) * 2.2) * t, 2)) }
        let t = (v - 0.55) / 0.45; return RGB(0.55 - 0.4 * t, 0.2 - 0.12 * t, 0.2 - 0.05 * t + n(&r, 0.04)) }),
    ("tealorange", "Teal and orange film frame", scene { u, v, r in
        let base = u < 0.5 ? RGB(0.08, 0.32, 0.36) : RGB(0.78, 0.42, 0.2)
        let shade = 1 - 0.65 * max(0, v - 0.55)
        return RGB(base.r * shade + n(&r, 0.05), base.g * shade + n(&r, 0.05), base.b * shade + n(&r, 0.05)) }),
    ("neon", "Dark scene, neon sign", scene { u, v, r in
        let sign = u > 0.62 && u < 0.9 && v > 0.25 && v < 0.55
        if sign { return u < 0.76 ? RGB(1, 0.1, 0.55) : RGB(0.1, 0.85, 1) }
        return RGB(0.03 + n(&r, 0.02), 0.03 + n(&r, 0.02), 0.05 + n(&r, 0.02)) }),
    ("forest", "Forest, muted greens", scene { u, v, r in
        let g = 0.22 + 0.3 * sin(u * 18 + v * 9) * sin(v * 11); return RGB(0.1 + 0.05 * g + n(&r, 0.04), 0.2 + g * 0.6, 0.08 + n(&r, 0.03)) }),
    ("explosion", "Explosion on black", scene { u, v, r in
        let d = hypot(u - 0.5, (v - 0.5) * 0.56) * 2
        if d < 0.12 { return RGB(1, 0.95, 0.8) }
        if d < 0.7 { let t = (d - 0.12) / 0.58; return RGB(1, 0.85 - 0.75 * t, 0.2 * (1 - t)).scaled(1 - 0.6 * t) }
        return RGB(0.02, 0.02, 0.03).scaled(1 + n(&r, 0.5)) }),
    ("document", "Document, white page and blue toolbar", scene { u, v, r in
        if v < 0.12 { return RGB(0.18, 0.4, 0.85) }
        let line = Int(v * 40) % 3 == 1 && u > 0.12 && u < 0.88
        return line ? RGB(0.12, 0.12, 0.12) : RGB(0.97, 0.97, 0.96) }),
    ("ocean", "Ocean with foam", scene { u, v, r in
        let foam = max(0, sin(u * 40 + v * 20) * sin(v * 33)) * 0.6 * (v > 0.5 ? 1 : 0)
        return v < 0.45 ? RGB(0.5, 0.75, 0.95).mixed(RGB(0.25, 0.5, 0.9), v / 0.45) : RGB(0.0, 0.3 + 0.2 * v, 0.55 + 0.15 * v).mixed(RGB(1, 1, 1), foam) }),
    ("game", "Game: sky, grass, red HUD", scene { u, v, r in
        if v < 0.5 { return RGB(0.3, 0.65, 1.0) }
        if v > 0.88 && u < 0.35 { return RGB(0.95, 0.12, 0.1) }
        return RGB(0.15 + n(&r, 0.05), 0.65 + n(&r, 0.08), 0.12) }),
    ("dusty", "Mixed zones: gray plus red accents", scene { u, v, r in
        let k = r.next()
        if k < 0.55 { return RGB(0.42, 0.42, 0.44) }
        if k < 0.8 { return u < 0.5 ? RGB(0.9, 0.3, 0.1) : RGB(0.1, 0.5, 0.9) }
        return RGB(0.05, 0.05, 0.06) }),
    ("muted", "Muted film, skin tones", scene { u, v, r in
        let bg = RGB(0.33, 0.4, 0.47); let skin = RGB(0.72, 0.55, 0.45)
        let face = hypot(u - 0.5, (v - 0.45) * 1.4) < 0.18
        let c = face ? skin : bg.mixed(RGB(0.2, 0.22, 0.26), v)
        return RGB(c.r + n(&r, 0.04), c.g + n(&r, 0.04), c.b + n(&r, 0.04)) }),
    ("city", "Night city, scattered lights", scene { u, v, r in
        let k = r.next()
        if k > 0.985 { let p: [RGB] = [RGB(1, 0.8, 0.3), RGB(1, 0.2, 0.3), RGB(0.3, 0.8, 1), RGB(0.9, 0.9, 1)]; return p[Int(r.next() * 3.99)] }
        return RGB(0.04 + n(&r, 0.02), 0.05 + n(&r, 0.02), 0.09 + n(&r, 0.03)) }),
]

// MARK: reference: the most prominent hue in a zone, from a finer 36-bin histogram

struct Ref { var hue: Float; var share: Float }
func reference(_ px: [UInt8], _ z: ZoneRect) -> Ref {
    var bins = [Float](repeating: 0, count: 36); var total: Float = 0
    let x0 = Int(z.x0 * Float(W)), x1 = Int(z.x1 * Float(W)), y0 = Int(z.y0 * Float(H)), y1 = Int(z.y1 * Float(H))
    for y in y0..<y1 { for x in x0..<x1 {
        let i = (y * W + x) * 4; let c = RGB(Float(px[i + 2]) / 255, Float(px[i + 1]) / 255, Float(px[i]) / 255)
        let (h, s, v) = ColorMath.hsv(c); total += 1
        if s > 0.25 && v > 0.15 { bins[min(35, Int(h * 36))] += s * v }
    } }
    var best = 0; for b in 1..<36 where bins[b] > bins[best] { best = b }
    return Ref(hue: (Float(best) + 0.5) / 36, share: bins[best] / total)
}
func hueErr(_ a: Float, _ b: Float) -> Float { var d = abs(a - b); if d > 0.5 { d = 1 - d }; return d * 360 }

// MARK: run

let outDir = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "colorlab-out"
try? FileManager.default.createDirectory(atPath: outDir + "/scenes", withIntermediateDirectories: true)
let zones = (0..<9).map { ZoneRect(Float($0 % 3) / 3, Float($0 / 3) / 3, Float($0 % 3 + 1) / 3, Float($0 / 3 + 1) / 3) }
let algos = ["legacy"] + ColorStyle.allCases.map(\.rawValue)

var results: [[String: Any]] = []
for (key, title, px) in scenes {
    var ppm = Data("P6\n\(W) \(H)\n255\n".utf8)
    for i in 0..<(W * H) { ppm.append(contentsOf: [px[i * 4 + 2], px[i * 4 + 1], px[i * 4]]) }
    try ppm.write(to: URL(fileURLWithPath: "\(outDir)/scenes/\(key).ppm"))
    let raw = px.withUnsafeBytes { ZoneAccumulator.reduceBGRA($0.baseAddress!, width: W, height: H, bytesPerRow: W * 4, zones: zones) }
    let stats = (0..<9).map { ZoneAccumulator.stats(raw, zone: $0) }
    let refs = zones.map { reference(px, $0) }
    let chans = stats.enumerated().map { (id: $0.offset, stats: $0.element) }
    var perAlgo: [String: Any] = [:]
    for a in algos {
        var out: [Int: RGB] = [:]
        if a == "legacy" { let e = LegacyEngine(); for _ in 0..<400 { out = e.process(chans, dt: 0.02) } }
        else {
            var s = SyncSettings(); s.style = ColorStyle(rawValue: a)!
            let e = ColorEngine(); for _ in 0..<400 { out = e.process(chans, dt: 0.02, settings: s) }
        }
        var sat: Float = 0, val: Float = 0, err: Float = 0, grays = 0, lit = 0, chromatic = 0, hueN = 0
        for i in 0..<9 {
            let c = out[i]!; let (h, s, v) = ColorMath.hsv(c)
            val += v
            if v > 0.1 { sat += s; lit += 1 }
            if refs[i].share > 0.1 {
                chromatic += 1
                if s < 0.25 || v < 0.1 { grays += 1 } else { err += hueErr(h, refs[i].hue); hueN += 1 }
            }
        }
        perAlgo[a] = ["colors": (0..<9).map { [out[$0]!.r, out[$0]!.g, out[$0]!.b] },
                      "meanSat": lit > 0 ? sat / Float(lit) : 0, "meanV": val / 9,
                      "hueErrDeg": hueN > 0 ? err / Float(hueN) : -1, "grayOfChromatic": chromatic > 0 ? Float(grays) / Float(chromatic) : -1,
                      "chromaticZones": chromatic]
    }
    results.append(["key": key, "title": title, "algos": perAlgo])
}
let json = try JSONSerialization.data(withJSONObject: ["algos": algos, "scenes": results], options: [.prettyPrinted, .sortedKeys])
try json.write(to: URL(fileURLWithPath: outDir + "/results.json"))
print("wrote \(outDir)/results.json")
