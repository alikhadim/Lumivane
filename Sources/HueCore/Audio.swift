import Foundation

public enum AudioInput: Codable, Sendable, Equatable, Hashable {
    /// Everything the Mac plays, tapped before the output device (headphones, speakers, HDMI, AirPlay).
    case system
    /// One application, by bundle identifier.
    case app(String)
    /// Fallback only: room sound through the microphone.
    case microphone
}

public struct AudioSettings: Codable, Sendable, Equatable {
    public var enabled = true
    public var input: AudioInput = .system
    /// Share of the light that comes from audio, 0 (screen only) ... 1 (audio only). Movies: 0.1-0.3.
    public var influence: Float = 0.2
    public var bassSensitivity: Float = 1
    public var trebleSensitivity: Float = 1
    public var beatIntensity: Float = 1
    public var smoothing: Float = 0.5
    public var musicStrength: Float = 0.5
    public init() {}
}

public struct AudioFeatures: Sendable, Equatable {
    public var level: Float = 0          // 0...1 from -60...0 dBFS
    public var volumeDB: Float = -120
    public var bass: Float = 0           // 20-250 Hz, 0...1 (adaptive gain)
    public var mid: Float = 0            // 250-4000 Hz
    public var treble: Float = 0         // 4-16 kHz
    public var beat: Float = 0           // pulse at each detected beat, decays in ~120 ms
    public var bassImpulse: Float = 0    // pulse on strong bass onsets
    public var transient: Float = 0      // pulse on broadband / treble onsets
    public var tempo: Float = 0          // BPM, 0 = unknown
    public var tempoConfidence: Float = 0
    public var energy: Float = 0         // slow loudness average, ~3 s
    public var speech: Float = 0         // heuristic speech probability, 0 = music
    public static let silent = AudioFeatures()
    public init() {}
}

// MARK: - FFT

struct FFT {
    let n: Int
    private let cosT: [Float], sinT: [Float], rev: [Int]
    init(size n: Int) {
        self.n = n
        cosT = (0..<n / 2).map { Float(cos(2 * Double.pi * Double($0) / Double(n))) }
        sinT = (0..<n / 2).map { Float(-sin(2 * Double.pi * Double($0) / Double(n))) }
        var bits = 0; while (1 << bits) < n { bits += 1 }
        rev = (0..<n).map { i in var r = 0; var x = i; for _ in 0..<bits { r = (r << 1) | (x & 1); x >>= 1 }; return r }
    }
    func transform(_ re: inout [Float], _ im: inout [Float]) {
        for i in 0..<n { let j = rev[i]; if j > i { re.swapAt(i, j); im.swapAt(i, j) } }
        var len = 2
        while len <= n {
            let half = len / 2, step = n / len
            var i = 0
            while i < n {
                for k in 0..<half {
                    let wr = cosT[k * step], wi = sinT[k * step]
                    let a = i + k, b = a + half
                    let tr = re[b] * wr - im[b] * wi, ti = re[b] * wi + im[b] * wr
                    re[b] = re[a] - tr; im[b] = im[a] - ti
                    re[a] += tr; im[a] += ti
                }
                i += len
            }
            len <<= 1
        }
    }
}

// MARK: - Analyzer

/// Streaming analysis of mono PCM. 1024-point FFT (Hann) every 256 samples (5.3 ms at 48 kHz).
/// Not thread safe: feed from one queue.
public final class AudioAnalyzer {
    public let sampleRate: Float
    static let n = 1024, hop = 256
    private let fft = FFT(size: AudioAnalyzer.n)
    private let window: [Float]
    private let winNorm: Float
    private var ring = [Float](repeating: 0, count: AudioAnalyzer.n)
    private var written = 0, sinceHop = 0
    private var re = [Float](repeating: 0, count: AudioAnalyzer.n), im = [Float](repeating: 0, count: AudioAnalyzer.n)
    private var prev = [Float](repeating: 0, count: AudioAnalyzer.n / 2 + 1)
    private let hopDur: Float
    private let bassBins: Range<Int>, midBins: Range<Int>, trebBins: Range<Int>, subBins: Range<Int>
    private var peak: (b: Float, m: Float, t: Float) = (1e-3, 1e-3, 1e-3)
    private let peakDecay: Float

    // onset / beat
    private var onsetHist = [Float](repeating: 0, count: 188), onsetIdx = 0
    private var sum: Float = 0, sumSq: Float = 0
    private var sinceBeat: Float = 10
    private var env = [Float](repeating: 0, count: 1120), envIdx = 0, envFilled = 0
    private var framesSinceTempo = 0
    private var trebFluxAvg: Float = 0, bassFluxAvg: Float = 0

    // speech / music
    private var slow = [Float](repeating: 0, count: 144), slowIdx = 0, slowFilled = 0, slowAcc: Float = 0, slowCount = 0
    private var speechScore: Float = 0.3

    public private(set) var features = AudioFeatures()

    public init(sampleRate: Float = 48000) {
        self.sampleRate = sampleRate
        let n = AudioAnalyzer.n
        window = (0..<n).map { Float(0.5 - 0.5 * cos(2 * Double.pi * Double($0) / Double(n - 1))) }
        winNorm = window.reduce(0, +) / 2
        hopDur = Float(AudioAnalyzer.hop) / sampleRate
        func bin(_ hz: Float) -> Int { min(n / 2, max(1, Int((hz * Float(n) / sampleRate).rounded()))) }
        bassBins = bin(20)..<bin(250) ; midBins = bin(250)..<bin(4000)
        trebBins = bin(4000)..<min(n / 2, bin(16000)); subBins = bin(30)..<bin(90)
        peakDecay = exp(-hopDur / 8)
    }

    /// Returns true if at least one analysis frame ran.
    @discardableResult
    public func feed(_ samples: [Float]) -> Bool {
        var ran = false
        for s in samples {
            ring[written % AudioAnalyzer.n] = s
            written += 1; sinceHop += 1
            if written >= AudioAnalyzer.n && sinceHop >= AudioAnalyzer.hop { sinceHop = 0; analyzeFrame(); ran = true }
        }
        return ran
    }

    private func analyzeFrame() {
        let n = AudioAnalyzer.n
        var power: Float = 0
        for i in 0..<n {
            let x = ring[(written - n + i) % n]
            re[i] = x * window[i]; im[i] = 0
            power += x * x
        }
        fft.transform(&re, &im)
        let rms = (power / Float(n)).squareRoot()
        let db = 20 * log10(rms + 1e-9)

        var mag = [Float](repeating: 0, count: n / 2 + 1)
        for k in 0...(n / 2) { mag[k] = (re[k] * re[k] + im[k] * im[k]).squareRoot() / winNorm }
        func band(_ r: Range<Int>) -> Float {
            guard !r.isEmpty else { return 0 }
            var e: Float = 0; for k in r { e += mag[k] * mag[k] }
            return (e / Float(r.count)).squareRoot()
        }
        func flux(_ r: Range<Int>) -> Float {
            var f: Float = 0
            for k in r { let d = log1p(100 * mag[k]) - log1p(100 * prev[k]); if d > 0 { f += d } }
            return r.isEmpty ? 0 : f / Float(r.count)
        }
        let b = band(bassBins), m = band(midBins), t = band(trebBins)
        let bf = flux(bassBins), mf = flux(midBins), tf = flux(trebBins)
        prev = mag

        peak.b = max(peak.b * peakDecay, b); peak.m = max(peak.m * peakDecay, m); peak.t = max(peak.t * peakDecay, t)
        var f = features
        let gate: Float = db < -70 ? 0 : 1
        f.volumeDB = db
        f.level = min(max((db + 60) / 60, 0), 1)
        f.bass = min(1, b / max(peak.b, 1e-3)) * gate
        f.mid = min(1, m / max(peak.m, 1e-3)) * gate
        f.treble = min(1, t / max(peak.t, 1e-3)) * gate

        // onset strength and adaptive-threshold beat picking
        let onset = (0.7 * bf + 0.3 * mf) * gate
        let old = onsetHist[onsetIdx]
        sum += onset - old; sumSq += onset * onset - old * old
        onsetHist[onsetIdx] = onset; onsetIdx = (onsetIdx + 1) % onsetHist.count
        let cnt = Float(onsetHist.count)
        let mean = sum / cnt, sd = max(0, sumSq / cnt - mean * mean).squareRoot()
        let thr = mean + 1.5 * sd + 0.02
        sinceBeat += hopDur
        let decay = exp(-hopDur / 0.12)
        f.beat *= decay; f.bassImpulse *= decay; f.transient *= exp(-hopDur / 0.08)
        if onset > thr && sinceBeat > 0.25 && gate > 0 {
            f.beat = max(f.beat, min(1, 0.5 + (onset - thr) / (thr + 1e-3)))
            sinceBeat = 0
        }
        // strong bass onset relative to its own recent average
        bassFluxAvg += (bf - bassFluxAvg) * (1 - exp(-hopDur / 1.0))
        if bf > 3 * bassFluxAvg + 0.03 && gate > 0 { f.bassImpulse = max(f.bassImpulse, min(1, (bf - bassFluxAvg) / (bf + 1e-3) + 0.3)) }
        trebFluxAvg += (tf - trebFluxAvg) * (1 - exp(-hopDur / 1.0))
        if tf > 2.5 * trebFluxAvg + 0.02 && gate > 0 { f.transient = max(f.transient, min(1, (tf - trebFluxAvg) / (tf + 1e-3) + 0.3)) }

        // tempo: autocorrelation of the onset envelope
        env[envIdx] = onset; envIdx = (envIdx + 1) % env.count; envFilled = min(env.count, envFilled + 1)
        framesSinceTempo += 1
        if framesSinceTempo >= Int(0.5 / hopDur) && envFilled == env.count { framesSinceTempo = 0; estimateTempo(&f) }

        // loudness envelope for energy and speech/music heuristics
        f.energy += (f.level - f.energy) * (1 - exp(-hopDur / 3))
        slowAcc += f.level * f.level; slowCount += 1
        if slowCount >= 4 {
            slow[slowIdx] = (slowAcc / Float(slowCount)).squareRoot(); slowIdx = (slowIdx + 1) % slow.count
            slowFilled = min(slow.count, slowFilled + 1); slowAcc = 0; slowCount = 0
            if slowFilled == slow.count && slowIdx % 8 == 0 { updateSpeech(&f, sub: band(subBins) / max(b, 1e-6)) }
        }
        features = f
    }

    private func estimateTempo(_ f: inout AudioFeatures) {
        let rate = 1 / hopDur
        let len = env.count
        var x = [Float](repeating: 0, count: len)
        for i in 0..<len { x[i] = env[(envIdx + i) % len] }
        let mean = x.reduce(0, +) / Float(len)
        for i in 0..<len { x[i] -= mean }
        var zero: Float = 0; for v in x { zero += v * v }
        guard zero > 1e-6 else { f.tempo = 0; f.tempoConfidence = 0; return }
        let minLag = Int(rate * 60 / 190), maxLag = Int(rate * 60 / 60)
        var best = 0, bestScore: Float = 0, bestRaw: Float = 0
        for lag in minLag...maxLag {
            var ac: Float = 0
            for i in lag..<len { ac += x[i] * x[i - lag] }
            ac /= zero
            let bpm = rate * 60 / Float(lag)
            let prior = exp(-0.5 * pow(log2(bpm / 120) / 0.6, 2))
            if ac * prior > bestScore { bestScore = ac * prior; best = lag; bestRaw = ac }
        }
        if best > 0 && bestRaw > 0.05 { f.tempo = rate * 60 / Float(best); f.tempoConfidence = min(1, bestRaw) }
        else { f.tempo = 0; f.tempoConfidence = 0 }
    }

    /// Heuristic, not a trained model: speech has syllable-rate (3-6 Hz) loudness modulation, frequent pauses and deep valleys;
    /// music keeps sustained energy and modulates at beat rate. Accuracy on real material is unvalidated.
    private func updateSpeech(_ f: inout AudioFeatures, sub: Float) {
        let len = slow.count
        var e = [Float](repeating: 0, count: len)
        for i in 0..<len { e[i] = slow[(slowIdx + i) % len] }
        let mean = e.reduce(0, +) / Float(len)
        guard mean > 0.02 else { speechScore *= 0.9; f.speech = speechScore; return }
        let pause = Float(e.filter { $0 < 0.3 * mean }.count) / Float(len)
        let sorted = e.sorted(); let p10 = sorted[len / 10]
        let depth = 1 - p10 / mean
        let rate = 1 / (hopDur * 4)
        func power(_ hz: Float) -> Float {
            var cr: Float = 0, ci: Float = 0
            for i in 0..<len { let a = 2 * Float.pi * hz * Float(i) / rate; let v = e[i] - mean; cr += v * cos(a); ci += v * sin(a) }
            return cr * cr + ci * ci
        }
        let hi = [3, 3.5, 4, 4.5, 5, 5.5, 6].map(power).reduce(0, +)
        let lo = [0.5, 1, 1.5, 2, 2.5].map(power).reduce(0, +)
        let mod = hi / (hi + lo + 1e-9)
        var score = 0.35 * min(1, pause * 3) + 0.3 * mod + 0.2 * min(1, depth) + 0.15 * (1 - min(1, sub * 4))
        score = min(max(score, 0), 1)
        let target = 1 / (1 + exp(-10 * (score - 0.5)))
        speechScore += (target - speechScore) * 0.5
        f.speech = speechScore
    }
}

// MARK: - Mixer

/// Blends the screen-derived colors with audio dynamics:
///   out = screen * (1 - e) + audioLayer * e, e = influence scaled by speech/music state.
/// Bass: room-wide warm brightness pulses and bursts. Mid: slow hue drift. Treble: short sparkles. Beat: global pulse.
public final class AudioMixer {
    private var bass: Float = 0, mid: Float = 0, treble: Float = 0
    private var hue: Float = 0
    private var sparkle: [Int: Float] = [:]
    private var cursor = 0, lastTransient: Float = 0
    public init() {}
    public func reset() { bass = 0; mid = 0; treble = 0; sparkle = [:]; lastTransient = 0 }

    public static func ceiling(_ s: SyncSettings) -> Float { ColorEngine.ceiling(s) }

    public func mix(screen: [Int: RGB], order: [Int], features f: AudioFeatures, dt: Float, settings s: SyncSettings) -> [Int: RGB] {
        let a = s.audio
        guard a.enabled, a.influence > 0, !order.isEmpty else { return screen }
        let dt = min(max(dt, 0.001), 0.25)
        let speech = min(max(f.speech, 0), 1)
        let responsiveness = (1 - speech) * (0.6 + 0.8 * a.musicStrength) + speech * 0.25
        let e = min(max(a.influence * responsiveness, 0), 0.95)

        let rel = 0.05 + a.smoothing * 0.45
        func follow(_ x: inout Float, _ target: Float) { x = target > x ? target : x + (target - x) * (1 - exp(-dt / rel)) }
        follow(&bass, f.bass); follow(&mid, f.mid); follow(&treble, f.treble)
        let bassBoost = min(1.5, bass * a.bassSensitivity)
        let trebleBoost = min(1.5, treble * a.trebleSensitivity)
        let beatPulse = f.beat * a.beatIntensity
        let impulse = min(1, f.bassImpulse * a.bassSensitivity)
        hue = (hue + dt * (0.02 + 0.25 * mid * a.musicStrength * (1 - speech))).truncatingRemainder(dividingBy: 1)

        if f.transient > lastTransient + 0.2 && trebleBoost > 0.25 {
            let ch = order[cursor % order.count]; cursor += 7
            sparkle[ch] = min(1, 0.5 + 0.5 * trebleBoost)
        }
        lastTransient = f.transient

        let room = order.reduce(RGB.black) { let c = screen[$1] ?? .black; return RGB($0.r + c.r, $0.g + c.g, $0.b + c.b) }.scaled(1 / Float(order.count))
        let blend = smoothstep(0.3, 0.9, e)
        let drive = min(1, 0.55 * bassBoost + 0.35 * mid + 0.5 * beatPulse)
        let ceiling = AudioMixer.ceiling(s)
        var out = screen
        for (i, ch) in order.enumerated() {
            let sc = screen[ch] ?? .black
            let palette = ColorMath.rgb(h: (hue + 0.13 * Float(i)).truncatingRemainder(dividingBy: 1), s: 0.85, v: 0.15 + 0.85 * drive)
            var layer = sc.mixed(palette, blend)
            layer = layer.mixed(RGB(1, 0.45, 0.12), 0.25 * min(1, bassBoost))
            layer = layer.scaled(1 + 1.1 * bassBoost + 0.8 * beatPulse)
            var c = sc.mixed(layer, e)
            c = RGB(c.r + room.r * 0.7 * impulse * e, c.g + room.g * 0.7 * impulse * e, c.b + room.b * 0.7 * impulse * e)
            let sp = (sparkle[ch] ?? 0) * exp(-dt / 0.09)
            sparkle[ch] = sp
            if sp > 0.01 {
                let accent = RGB(1, 1, 1).mixed(sc, 0.4).scaled(sp * min(1, 0.3 + e))
                c = RGB(c.r + accent.r, c.g + accent.g, c.b + accent.b)
            }
            let m = max(c.r, c.g, c.b)
            if m > ceiling { c = c.scaled(ceiling / m) }
            out[ch] = c.clamped()
        }
        return out
    }

    private func smoothstep(_ a: Float, _ b: Float, _ x: Float) -> Float { let t = min(max((x - a) / (b - a), 0), 1); return t * t * (3 - 2 * t) }
}
