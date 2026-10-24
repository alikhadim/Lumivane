import Foundation

public struct RGB: Sendable, Equatable, Codable {
    public var r: Float, g: Float, b: Float
    public init(_ r: Float, _ g: Float, _ b: Float) { self.r = r; self.g = g; self.b = b }
    public static let black = RGB(0, 0, 0)
    public var luma: Float { 0.2126 * r + 0.7152 * g + 0.0722 * b }
    public func mixed(_ o: RGB, _ t: Float) -> RGB { RGB(r + (o.r - r) * t, g + (o.g - g) * t, b + (o.b - b) * t) }
    public func scaled(_ k: Float) -> RGB { RGB(r * k, g * k, b * k) }
    public func clamped() -> RGB { RGB(min(max(r, 0), 1), min(max(g, 0), 1), min(max(b, 0), 1)) }
}

public enum ColorMath {
    public static func hsv(_ c: RGB) -> (h: Float, s: Float, v: Float) {
        let mx = max(c.r, c.g, c.b), mn = min(c.r, c.g, c.b), d = mx - mn
        var h: Float = 0
        if d > 0 {
            if mx == c.r { h = (c.g - c.b) / d; if h < 0 { h += 6 } }
            else if mx == c.g { h = (c.b - c.r) / d + 2 }
            else { h = (c.r - c.g) / d + 4 }
            h /= 6
        }
        return (h, mx > 0 ? d / mx : 0, mx)
    }

    public static func rgb(h: Float, s: Float, v: Float) -> RGB {
        let hh = min(max(h, 0), 0.99999) * 6
        let i = Int(hh), f = hh - Float(i)
        let p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s)
        switch i {
        case 0: return RGB(v, t, p)
        case 1: return RGB(q, v, p)
        case 2: return RGB(p, v, t)
        case 3: return RGB(p, q, v)
        case 4: return RGB(t, p, v)
        default: return RGB(v, p, q)
        }
    }

    public static func toLinear(_ v: Float) -> Float {
        v > 0.04045 ? pow((v + 0.055) / 1.055, 2.4) : v / 12.92
    }

    /// sRGB (gamma-encoded, 0...1) to CIE xy using the Philips wide-gamut D65 matrix, clamped to the light's gamut.
    public static func xy(_ c: RGB, gamut: Gamut?) -> XY {
        let r = Double(toLinear(c.r)), g = Double(toLinear(c.g)), b = Double(toLinear(c.b))
        let X = r * 0.664511 + g * 0.154324 + b * 0.162028
        let Y = r * 0.283881 + g * 0.668433 + b * 0.047685
        let Z = r * 0.000088 + g * 0.072310 + b * 0.986039
        let sum = X + Y + Z
        let p = sum > 0 ? XY(X / sum, Y / sum) : XY(0.3127, 0.3290)
        guard let gm = gamut else { return p }
        return clamp(p, to: gm)
    }

    static func clamp(_ p: XY, to g: Gamut) -> XY {
        func cross(_ a: XY, _ b: XY) -> Double { a.x * b.y - a.y * b.x }
        func sub(_ a: XY, _ b: XY) -> XY { XY(a.x - b.x, a.y - b.y) }
        let v1 = sub(g.green, g.red), v2 = sub(g.blue, g.red), q = sub(p, g.red)
        let den = cross(v1, v2)
        let s = cross(q, v2) / den, t = cross(v1, q) / den
        if s >= 0, t >= 0, s + t <= 1 { return p }
        func closest(_ a: XY, _ b: XY) -> XY {
            let ap = sub(p, a), ab = sub(b, a)
            let u = min(max((ap.x * ab.x + ap.y * ab.y) / (ab.x * ab.x + ab.y * ab.y), 0), 1)
            return XY(a.x + ab.x * u, a.y + ab.y * u)
        }
        let cands = [closest(g.red, g.green), closest(g.green, g.blue), closest(g.blue, g.red)]
        return cands.min { d2($0, p) < d2($1, p) }!
    }
    private static func d2(_ a: XY, _ b: XY) -> Double { (a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y) }
}

// MARK: - Zone reduction (CPU reference; the Metal kernel uses the same raw layout)

public struct ZoneRect: Codable, Sendable, Hashable {
    public var x0: Float, y0: Float, x1: Float, y1: Float
    public init(_ x0: Float, _ y0: Float, _ x1: Float, _ y1: Float) { self.x0 = x0; self.y0 = y0; self.x1 = x1; self.y1 = y1 }
}

public struct ZoneStats: Sendable, Equatable {
    public var average: RGB
    public var dominant: RGB
    /// Share of pixels (0...1) that fall in the dominant hue bin and pass the saturation/value filter.
    public var dominance: Float
    public var brightness: Float
    public var saturation: Float
    public init(average: RGB, dominant: RGB, dominance: Float, brightness: Float, saturation: Float) {
        self.average = average; self.dominant = dominant; self.dominance = dominance; self.brightness = brightness; self.saturation = saturation
    }
    public static let empty = ZoneStats(average: .black, dominant: .black, dominance: 0, brightness: 0, saturation: 0)
}

/// Raw layout per zone (52 floats): [0..2] sum RGB, [3] pixel count,
/// [4..15] hue-bin weights (sat*val), [16..51] hue-bin weighted RGB sums.
public enum ZoneAccumulator {
    public static let stride = 52
    public static let bins = 12
    static let minValue: Float = 0.08, minSat: Float = 0.2

    public static func reduceBGRA(_ base: UnsafeRawPointer, width: Int, height: Int, bytesPerRow: Int,
                                  zones: [ZoneRect], step: Int = 1) -> [Float] {
        var out = [Float](repeating: 0, count: zones.count * stride)
        let st = max(1, step)
        for (zi, z) in zones.enumerated() {
            let x0 = min(Int(z.x0 * Float(width)), width - 1), y0 = min(Int(z.y0 * Float(height)), height - 1)
            let x1 = max(x0 + 1, min(width, Int(z.x1 * Float(width)))), y1 = max(y0 + 1, min(height, Int(z.y1 * Float(height))))
            let o = zi * stride
            var y = y0
            while y < y1 {
                let row = base.advanced(by: y * bytesPerRow).assumingMemoryBound(to: UInt8.self)
                var x = x0
                while x < x1 {
                    let p = row + x * 4
                    let c = RGB(Float(p[2]) / 255, Float(p[1]) / 255, Float(p[0]) / 255)
                    out[o] += c.r; out[o + 1] += c.g; out[o + 2] += c.b; out[o + 3] += 1
                    let (h, s, v) = ColorMath.hsv(c)
                    if v >= minValue && s >= minSat {
                        let w = s * v, bin = min(bins - 1, Int(h * Float(bins)))
                        out[o + 4 + bin] += w
                        out[o + 16 + bin * 3] += c.r * w; out[o + 17 + bin * 3] += c.g * w; out[o + 18 + bin * 3] += c.b * w
                    }
                    x += st
                }
                y += st
            }
        }
        return out
    }

    public static func stats(_ raw: [Float], zone i: Int) -> ZoneStats {
        let o = i * stride
        guard o + stride <= raw.count, raw[o + 3] > 0 else { return .empty }
        let n = raw[o + 3]
        let avg = RGB(raw[o] / n, raw[o + 1] / n, raw[o + 2] / n)
        var best = 0
        for b in 1..<bins where raw[o + 4 + b] > raw[o + 4 + best] { best = b }
        let w0 = raw[o + 4 + best]
        guard w0 > 0 else { return ZoneStats(average: avg, dominant: avg, dominance: 0, brightness: avg.luma, saturation: ColorMath.hsv(avg).s) }
        // Best hue bin plus any neighbour with at least a third of its weight, so a hue that straddles a bin edge isn't split.
        var w = w0, r = raw[o + 16 + best * 3], g = raw[o + 17 + best * 3], b = raw[o + 18 + best * 3]
        for d in [-1, 1] {
            let k = (best + d + bins) % bins
            if raw[o + 4 + k] > 0.35 * w0 { w += raw[o + 4 + k]; r += raw[o + 16 + k * 3]; g += raw[o + 17 + k * 3]; b += raw[o + 18 + k * 3] }
        }
        let dom = RGB(r / w, g / w, b / w)
        // Weights are saturation * value per pixel (at most 1), so w / n is the share of colorful energy in the zone.
        return ZoneStats(average: avg, dominant: dom, dominance: min(1, w / n), brightness: avg.luma, saturation: ColorMath.hsv(avg).s)
    }
}


// MARK: - Styles

/// How zone statistics become light colors. Each style is a parameter set for `ColorEngine`.
public enum ColorStyle: String, Codable, Sendable, CaseIterable, Identifiable {
    case accurate, enhanced, cinematic, gaming
    public var id: String { rawValue }

    public var title: String {
        switch self { case .accurate: "Accurate"; case .enhanced: "Enhanced"; case .cinematic: "Cinematic"; case .gaming: "Gaming" }
    }
    public var summary: String {
        switch self {
        case .accurate: "Colors as they appear on screen"
        case .enhanced: "Richer, more vivid color. Recommended"
        case .cinematic: "Warmer, deeper contrast, slower and more dramatic fades"
        case .gaming: "Aggressive color, fastest response"
        }
    }

    struct Params {
        var pick: Float        // 0...1 weight of the dominant color over the zone average
        var satPow: Float      // saturation exponent, < 1 lifts muted colors
        var satGain: Float     // saturation multiplier after the curve
        var valuePow: Float    // brightness exponent, < 1 lifts dark scenes
        var contrast: Float    // S-curve strength on brightness
        var warmth: Float      // white balance shift, -1 cool ... 1 warm
        var tauScale: Float    // smoothing time scale
        var slewScale: Float   // max brightness change per second scale
        var autoGain: Float    // strength of scene brightness adaptation, 0 = off
    }

    var params: Params {
        switch self {
        case .accurate:  Params(pick: 0.15, satPow: 1.0,  satGain: 1.0,  valuePow: 1.0,  contrast: 0,    warmth: 0,    tauScale: 1.0,  slewScale: 1, autoGain: 0.25)
        case .enhanced:  Params(pick: 0.65, satPow: 0.72, satGain: 1.12, valuePow: 0.78, contrast: 0.1,  warmth: 0,    tauScale: 0.8,  slewScale: 1, autoGain: 0.6)
        case .cinematic: Params(pick: 0.5,  satPow: 0.85, satGain: 1.05, valuePow: 0.9,  contrast: 0.35, warmth: 0.22, tauScale: 1.5,  slewScale: 1, autoGain: 0.6)
        case .gaming:    Params(pick: 0.85, satPow: 0.6,  satGain: 1.2,  valuePow: 0.7,  contrast: 0.15, warmth: 0,    tauScale: 0.35, slewScale: 2, autoGain: 0.3)
        }
    }
}

// MARK: - Settings and engine

public struct SyncSettings: Codable, Sendable, Equatable {
    public var brightness: Float = 1
    /// Extra saturation multiplier on top of the style.
    public var intensity: Float = 1
    public var smoothing: Float = 0.5
    public var fps: Int = 50
    /// Brightness curve exponent multiplier. 1 uses the style as designed.
    public var gamma: Float = 1.0
    /// Scales how strongly the dominant color is preferred over the zone average (0.5 = style default).
    public var dominance: Float = 0.5
    public var dynamicBrightness = true
    public var nightMode = false
    public var lowLightMode = false
    public var brightnessLimit: Float = 1
    public var channelGain: [Int: Float] = [:]
    public var audio = AudioSettings()
    public var style: ColorStyle = .enhanced
    /// Added to the style's white balance, -1 cooler ... 1 warmer.
    public var warmth: Float = 0
    var colorVersion = 2
    public init() {}

    private enum CodingKeys: String, CodingKey {
        case brightness, intensity, smoothing, fps, gamma, dominance, dynamicBrightness, nightMode, lowLightMode
        case brightnessLimit, channelGain, audio, style, warmth, colorVersion
    }

    /// Tolerant decoding: profiles saved by earlier versions lack the newer keys.
    public init(from d: Decoder) throws {
        let c = try d.container(keyedBy: CodingKeys.self)
        func v<T: Decodable>(_ k: CodingKeys, _ def: T) -> T { (try? c.decodeIfPresent(T.self, forKey: k)) ?? def }
        brightness = v(.brightness, 1); intensity = v(.intensity, 1); smoothing = v(.smoothing, 0.5); fps = v(.fps, 50)
        gamma = v(.gamma, 1.0); dominance = v(.dominance, 0.5); dynamicBrightness = v(.dynamicBrightness, true)
        nightMode = v(.nightMode, false); lowLightMode = v(.lowLightMode, false); brightnessLimit = v(.brightnessLimit, 1)
        channelGain = v(.channelGain, [:]); audio = v(.audio, AudioSettings())
        style = v(.style, .enhanced); warmth = v(.warmth, 0); colorVersion = v(.colorVersion, 1)
        if colorVersion < 2 {   // v1 darkened mid-tones with gamma 1.2 and had no style
            if abs(gamma - 1.2) < 0.001 { gamma = 1.0 }
            colorVersion = 2
        }
    }
}

/// Turns per-channel zone stats into light colors.
///
/// 1. Pick: blend the zone average toward its dominant hue (more for vivid zones, none for neutral ones).
/// 2. Shape: lift saturation with a curve, lift dark tones, adapt to scene brightness, keep true black black.
/// 3. Smooth in HSV: hue follows the shortest arc, so red to cyan doesn't pass through gray; brightness is
///    rate-limited (strobe guard) and rises faster than it falls.
/// 4. Output: white balance, night / low-light, brightness ceiling.
public final class ColorEngine {
    private struct Chan { var h: Float, s: Float, v: Float }
    private var state: [Int: Chan] = [:]
    private var vAvg: Float = 0.4
    private var autoGain: Float = 1
    public init() {}
    public func reset() { state = [:]; vAvg = 0.4; autoGain = 1 }

    /// Hard ceiling for any output component: brightness limiter, night and low-light caps.
    public static func ceiling(_ s: SyncSettings) -> Float {
        var cap = min(s.brightnessLimit, 1)
        if s.lowLightMode { cap = min(cap, 0.45) }
        if s.nightMode { cap = min(cap, 0.3) }
        return cap
    }

    private static func smooth(_ a: Float, _ b: Float, _ x: Float) -> Float {
        let t = min(max((x - a) / (b - a), 0), 1); return t * t * (3 - 2 * t)
    }

    public func process(_ channels: [(id: Int, stats: ZoneStats)], dt: Float, settings s: SyncSettings) -> [Int: RGB] {
        let dt = min(max(dt, 0.001), 0.25)
        let p = s.style.params

        // 1 + 2: target color per channel
        var targets: [(id: Int, h: Float, s: Float, v: Float)] = []
        for (id, st) in channels {
            let evidence = Self.smooth(0.03, 0.2, st.dominance)
            let k = min(1, p.pick * (0.5 + s.dominance) * evidence)
            let (h, sat, v) = ColorMath.hsv(st.average.mixed(st.dominant, k))
            // The saturation lift fades out for near-neutral colors, so white and gray stay neutral instead of picking up a tint.
            let lifted = min(1, pow(sat, p.satPow) * p.satGain * s.intensity)
            targets.append((id, h, sat + (lifted - sat) * Self.smooth(0.03, 0.15, sat), v))
        }
        if s.dynamicBrightness, p.autoGain > 0, !targets.isEmpty {
            let f = targets.reduce(0) { $0 + $1.v } / Float(targets.count)
            vAvg += (f - vAvg) * (1 - exp(-dt / 3))
            let tg = min(max(pow(0.5 / max(vAvg, 0.08), p.autoGain), 0.85), 1.8)
            autoGain += (tg - autoGain) * (1 - exp(-dt / 2))
        } else { autoGain = 1 }

        // 3 + 4
        let tau = (0.04 + s.smoothing * 0.76) * p.tauScale
        let slew = (1.5 + (1 - s.smoothing) * 6.5) * p.slewScale
        let hueRate = (1.5 + (1 - s.smoothing) * 4) * p.slewScale
        let cap = ColorEngine.ceiling(s)
        let wb = min(max(p.warmth + s.warmth, -1), 1)
        var out: [Int: RGB] = [:]
        for t in targets {
            var x = min(t.v * autoGain, 1)
            x += p.contrast * (x * x * (3 - 2 * x) - x)
            let vt = pow(x, p.valuePow * s.gamma) * Self.smooth(0.02, 0.07, x)

            var c = state[t.id] ?? Chan(h: t.h, s: t.s, v: 0)
            let tv = vt > c.v ? tau * 0.5 : tau * 1.5
            var nv = c.v + (vt - c.v) * (1 - exp(-dt / max(tv, 0.001)))
            nv = min(max(nv, c.v - slew * dt), c.v + slew * dt)
            let ns = c.s + (t.s - c.s) * (1 - exp(-dt / max(tau * 0.7, 0.001)))
            if c.s < 0.06 || c.v < 0.02 { c.h = t.h }       // no visible hue yet: jump instead of sweeping through colors
            else if vt > 0.02, t.s > 0.05 {
                var d = t.h - c.h; if d > 0.5 { d -= 1 } else if d < -0.5 { d += 1 }
                let conf = min(1, t.s * 3)
                var step = d * (1 - exp(-dt / max(tau * 0.6 / conf, 0.001)))
                step = min(max(step, -hueRate * dt), hueRate * dt)
                c.h += step; c.h -= floor(c.h)
            }
            c.s = ns; c.v = nv
            state[t.id] = c

            var o = ColorMath.rgb(h: c.h, s: c.s, v: c.v)
            if wb > 0 { o.g *= 1 - 0.10 * wb; o.b *= 1 - 0.40 * wb } else if wb < 0 { o.r *= 1 + 0.25 * wb; o.g *= 1 + 0.05 * wb }
            if s.lowLightMode { o = RGB(pow(o.r, 0.8), pow(o.g, 0.8), pow(o.b, 0.8)) }
            if s.nightMode { o.g *= 0.9; o.b *= 0.7 }
            out[t.id] = o.scaled(min(s.brightness * (s.channelGain[t.id] ?? 1), cap)).clamped()
        }
        return out
    }
}
