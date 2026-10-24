import Foundation

/// Procedural moving pictures. Each returns an sRGB color for a point (u, v in 0...1) at time t seconds.
public enum DemoScene {
    public static let count = 4

    private static func mix(_ a: RGB, _ b: RGB, _ t: Float) -> RGB {
        let k = min(max(t, 0), 1)
        return RGB(a.r + (b.r - a.r) * k, a.g + (b.g - a.g) * k, a.b + (b.b - a.b) * k)
    }
    private static func smooth(_ a: Float, _ b: Float, _ x: Float) -> Float { let t = min(max((x - a) / (b - a), 0), 1); return t * t * (3 - 2 * t) }

    public static func color(_ scene: Int, u: Float, v: Float, t: Float) -> RGB {
        switch scene % count {
        case 0:   // sunset over water
            let sun = Float(0.5) + 0.12 * sin(t * 0.15)
            let sky = mix(RGB(0.22, 0.10, 0.42), RGB(1.0, 0.42, 0.10), smooth(0.0, 0.55, v))
            let sea = mix(RGB(0.10, 0.20, 0.30), RGB(0.03, 0.08, 0.14), smooth(0.55, 1.0, v))
            var c = v < 0.55 ? sky : sea
            let glow = exp(-pow((u - sun) * 4.5, 2)) * (v < 0.55 ? 0.35 : 0.7 * (1 - smooth(0.55, 1.0, v)))
            c = mix(c, RGB(1.0, 0.62, 0.22), glow)
            if pow(u - sun, 2) + pow((v - 0.5) * 0.56, 2) < 0.0035 { c = RGB(1.0, 0.92, 0.65) }
            return c
        case 1:   // neon street
            let stripe = Int(u * 6), phase = sin(t * 1.3 + Float(stripe) * 1.7) * 0.5 + 0.5
            let tint = stripe % 2 == 0 ? RGB(0.0, 0.85, 1.0) : RGB(1.0, 0.10, 0.65)
            let lit = smooth(0.25, 0.7, v) * (0.35 + 0.65 * phase)
            return mix(RGB(0.03, 0.02, 0.09), tint, lit)
        case 2:   // forest
            let dapple = sin(u * 9 + t * 0.3) * sin(v * 7 - t * 0.2) * 0.5 + 0.5
            let base = mix(RGB(0.60, 0.78, 0.30), RGB(0.04, 0.22, 0.09), smooth(0.0, 0.9, v))
            return mix(base, RGB(0.85, 0.92, 0.50), dapple * 0.25 * (1 - v))
        default:  // ember glow
            let flicker = 0.85 + 0.15 * sin(t * 7 + u * 13) * sin(t * 4.3 + u * 5)
            let heat = smooth(0.15, 1.0, v) * flicker
            return mix(RGB(0.12, 0.02, 0.02), RGB(1.0, 0.34, 0.05), heat * (0.7 + 0.3 * (1 - abs(u - 0.5) * 2)))
        }
    }

    /// Statistics of a zone, in the same shape the GPU reducer produces.
    public static func stats(scene: Int, rect: ZoneRect, t: Float) -> ZoneStats {
        let n = 4
        var sum = RGB.black, bright: Float = 0, sat: Float = 0
        var binW = [Float](repeating: 0, count: 12), binRGB = [RGB](repeating: .black, count: 12), binN = [Int](repeating: 0, count: 12)
        var counted = 0
        for iy in 0..<n { for ix in 0..<n {
            let u = rect.x0 + (rect.x1 - rect.x0) * (Float(ix) + 0.5) / Float(n), v = rect.y0 + (rect.y1 - rect.y0) * (Float(iy) + 0.5) / Float(n)
            let c = color(scene, u: u, v: v, t: t)
            sum = RGB(sum.r + c.r, sum.g + c.g, sum.b + c.b)
            let mx = max(c.r, c.g, c.b), mn = min(c.r, c.g, c.b), d = mx - mn, s = mx > 0 ? d / mx : 0
            bright += mx; sat += s; counted += 1
            if mx >= 0.08 && s >= 0.2 {
                var h: Float
                if mx == c.r { h = (c.g - c.b) / d; if h < 0 { h += 6 } } else if mx == c.g { h = (c.b - c.r) / d + 2 } else { h = (c.r - c.g) / d + 4 }
                let bin = min(11, Int(h / 6 * 12)), w = s * mx
                binW[bin] += w; binRGB[bin] = RGB(binRGB[bin].r + c.r * w, binRGB[bin].g + c.g * w, binRGB[bin].b + c.b * w); binN[bin] += 1
            }
        } }
        let k = Float(counted)
        let avg = RGB(sum.r / k, sum.g / k, sum.b / k)
        guard let best = binW.indices.max(by: { binW[$0] < binW[$1] }), binW[best] > 0 else {
            return ZoneStats(average: avg, dominant: avg, dominance: 0, brightness: bright / k, saturation: sat / k)
        }
        let dom = binRGB[best].scaled(1 / binW[best])
        return ZoneStats(average: avg, dominant: dom, dominance: Float(binN[best]) / k, brightness: bright / k, saturation: sat / k)
    }
}

/// A steady 112 BPM groove for the Sound page and the audio mixer.
public enum DemoSignal {
    public static func audio(t: Float) -> AudioFeatures {
        var f = AudioFeatures()
        let period: Float = 60 / 112, phase = t.truncatingRemainder(dividingBy: period) / period
        let pulse = exp(-phase * 7)
        f.level = 0.62 + 0.1 * sin(t * 0.7); f.volumeDB = -18
        f.bass = min(1, 0.45 + 0.45 * pulse); f.mid = 0.42 + 0.12 * sin(t * 1.9); f.treble = 0.30 + 0.18 * sin(t * 3.1 + 1)
        f.beat = pulse; f.bassImpulse = pulse > 0.8 ? pulse : 0; f.transient = max(0, sin(t * 6.2)) * 0.3
        f.tempo = 112; f.tempoConfidence = 0.85; f.energy = 0.6; f.speech = 0.05
        return f
    }
}
