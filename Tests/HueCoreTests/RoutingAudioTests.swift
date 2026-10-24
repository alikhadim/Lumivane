import XCTest
@testable import HueCore

private func disp(_ key: String, _ name: String, x: Double, w: Double = 1920, h: Double = 1080, builtIn: Bool = false, connected: Bool = true) -> DisplayGeometry {
    DisplayGeometry(key: key, name: name, pixelWidth: Int(w), pixelHeight: Int(h), x: x, y: 0, width: w, height: h, refreshHz: 60, isBuiltIn: builtIn, isConnected: connected)
}

final class RoutingTests: XCTestCase {
    let lg = "display:LG", mac = "display:MAC", odyssey = "display:ODY"
    var layout: [DisplayGeometry] {
        [disp("MAC", "MacBook Pro Display", x: 0, w: 1512, h: 982, builtIn: true), disp("LG", "LG UltraFine 5K", x: 1512, w: 2560, h: 1440),
         disp("ODY", "Samsung Odyssey G9", x: 4072, w: 5120, h: 1440)]
    }
    func profile(_ mode: SyncMode) -> Profile {
        var p = Profile(name: "t", mode: mode)
        p.routes = [LightRoute(channelID: 0, u: 0.1, v: 0.5), LightRoute(channelID: 1, u: 0.9, v: 0.5), LightRoute(channelID: 2, u: 0.5, v: 0.1)]
        p.settings.audio.influence = 0.2
        return p
    }

    func testSingleUsesOnlyChosenDisplay() {
        var p = profile(.single); p.primarySource = lg
        let plan = RoutePlanner.plan(p, channelIDs: [0, 1, 2], displays: layout, windows: [])
        XCTAssertEqual(plan.physicalSources, [lg])
        XCTAssertEqual(plan.samples.count, 3)
        XCTAssertTrue(plan.substitutions.isEmpty)
    }

    func testExtendAssignsPerLightAndSkipsUnassigned() {
        var p = profile(.extend)
        p.routes[0].sourceID = mac; p.routes[1].sourceID = odyssey      // light 2 unassigned
        let plan = RoutePlanner.plan(p, channelIDs: [0, 1, 2], displays: layout, windows: [])
        XCTAssertEqual(plan.physicalSources, [mac, odyssey])             // the LG is never captured
        XCTAssertNil(plan.samples[2])
    }

    func testMirrorAveragesSources() {
        var p = profile(.mirror); p.mirrorSources = [mac, lg]
        let plan = RoutePlanner.plan(p, channelIDs: [0], displays: layout, windows: [])
        XCTAssertEqual(plan.samples[0]?.count, 2)
        XCTAssertEqual(plan.physicalSources, [mac, lg])
        let c = ZoneStats.combine([ZoneStats(average: RGB(1, 0, 0), dominant: RGB(1, 0, 0), dominance: 1, brightness: 0.2, saturation: 1),
                                   ZoneStats(average: RGB(0, 0, 1), dominant: RGB(0, 0, 1), dominance: 0, brightness: 0.1, saturation: 1)])
        XCTAssertEqual(c.average, RGB(0.5, 0, 0.5))
    }

    func testHotPlugFallsBackAndRestoresWithoutRewritingMappings() {
        var p = profile(.single); p.primarySource = lg
        let before = p
        var d = layout; d[1].isConnected = false
        let gone = RoutePlanner.plan(p, channelIDs: [0, 1, 2], displays: d, windows: [])
        XCTAssertEqual(gone.physicalSources, [mac])                      // built-in is the fallback
        XCTAssertEqual(gone.substitutions, [Substitution(requested: lg, actual: mac)])
        let back = RoutePlanner.plan(p, channelIDs: [0, 1, 2], displays: layout, windows: [])
        XCTAssertEqual(back.physicalSources, [lg]); XCTAssertTrue(back.substitutions.isEmpty)
        XCTAssertEqual(p, before)
    }

    func testExplicitFallbackAndMissingWindow() {
        var p = profile(.single); p.primarySource = SourceID.window(bundle: "com.apple.Safari", title: "Movie"); p.fallbackSource = odyssey
        let plan = RoutePlanner.plan(p, channelIDs: [0], displays: layout, windows: [])
        XCTAssertEqual(plan.physicalSources, [odyssey])
        let ok = RoutePlanner.plan(p, channelIDs: [0], displays: layout, windows: [p.primarySource!])
        XCTAssertEqual(ok.physicalSources, [p.primarySource!])
    }

    func testDesktopSpanCapturesOnlyDisplaysUnderLights() {
        var p = profile(.extend)
        p.routes = [LightRoute(channelID: 0, u: 0.01, v: 0.5, sourceID: SourceID.desktop), LightRoute(channelID: 1, u: 0.35, v: 0.5, sourceID: SourceID.desktop)]
        let plan = RoutePlanner.plan(p, channelIDs: [0, 1], displays: layout, windows: [])
        XCTAssertEqual(plan.samples[0]?.first?.source, mac)
        XCTAssertEqual(plan.samples[1]?.first?.source, lg)               // 35% across 9192 pt lands in the LG (1512...4072)
        XCTAssertEqual(plan.physicalSources, [mac, lg])                  // the Odyssey is not captured
    }

    func testAudioOnlyNeedsNoCapture() {
        var p = profile(.single); p.primarySource = lg; p.settings.audio.influence = 1
        XCTAssertTrue(RoutePlanner.plan(p, channelIDs: [0, 1], displays: layout, windows: []).physicalSources.isEmpty)
    }

    func testProfileRoundTrip() throws {
        var p = Profile.presets()[0]; p.mode = .extend; p.routes = [LightRoute(channelID: 3, u: 0.2, v: 0.8, sourceID: lg)]
        p.settings.audio.input = .app("com.apple.Music"); p.settings.channelGain[3] = 0.5
        let back = try JSONDecoder().decode(Profile.self, from: JSONEncoder().encode(p))
        XCTAssertEqual(back, p)
    }
}

// MARK: - Audio

private func render(_ seconds: Float, sr: Float = 48000, _ f: (Float) -> Float) -> [Float] { (0..<Int(seconds * sr)).map { f(Float($0) / sr) } }
private func pump(_ a: AudioAnalyzer, _ x: [Float]) { var i = 0; while i < x.count { a.feed(Array(x[i..<min(i + 480, x.count)])); i += 480 } }

final class AudioTests: XCTestCase {
    func tone(_ hz: Float, _ amp: Float = 0.5) -> [Float] { render(4) { amp * sin(2 * .pi * hz * $0) } }

    func testBands() {
        let b = AudioAnalyzer(); pump(b, tone(80)); XCTAssertGreaterThan(b.features.bass, 0.8); XCTAssertLessThan(b.features.treble, 0.1)
        let t = AudioAnalyzer(); pump(t, tone(8000)); XCTAssertGreaterThan(t.features.treble, 0.8); XCTAssertLessThan(t.features.bass, 0.1)
        XCTAssertEqual(b.features.volumeDB, Float(20 * log10(0.5 / 2.0.squareRoot())), accuracy: 1.0)
    }

    func testSilenceIsQuiet() {
        let a = AudioAnalyzer(); pump(a, [Float](repeating: 0, count: 48000))
        XCTAssertEqual(a.features.bass, 0); XCTAssertEqual(a.features.beat, 0); XCTAssertEqual(a.features.tempo, 0)
    }

    func kickTrack(bpm: Float, seconds: Float) -> [Float] {
        let period = 60 / bpm
        return render(seconds) { t in
            let ph = t.truncatingRemainder(dividingBy: period)
            return 0.8 * exp(-ph / 0.07) * sin(2 * .pi * 60 * ph) + 0.05 * sin(2 * .pi * 440 * t)
        }
    }

    func testBeatsAndTempo() {
        let a = AudioAnalyzer(); let x = kickTrack(bpm: 120, seconds: 12)
        var beats = 0, last: Float = 0
        var i = 0
        while i < x.count {
            a.feed(Array(x[i..<min(i + 480, x.count)])); i += 480
            if a.features.beat > 0.5 && last <= 0.5 { beats += 1 }
            last = a.features.beat
        }
        XCTAssertGreaterThanOrEqual(beats, 18); XCTAssertLessThanOrEqual(beats, 26)   // 24 kicks in 12 s
        XCTAssertEqual(a.features.tempo, 120, accuracy: 4)
        XCTAssertGreaterThan(a.features.tempoConfidence, 0.3)
    }

    func speechLike(_ seconds: Float) -> [Float] {
        render(seconds) { t in
            let word = t.truncatingRemainder(dividingBy: 1.6)
            if word > 1.15 { return 0 }                                       // pause between phrases
            let syl = max(0, sin(2 * .pi * 4 * t))                            // ~4 Hz syllables
            var s: Float = 0; for k in 1...20 { s += sin(2 * .pi * 120 * Float(k) * t) / Float(k) }
            return 0.15 * syl * s
        }
    }
    func musicLike(_ seconds: Float) -> [Float] {
        var seed: UInt32 = 1
        return render(seconds) { t in
            let ph = t.truncatingRemainder(dividingBy: 0.5), hat = t.truncatingRemainder(dividingBy: 0.25)
            seed = seed &* 1664525 &+ 1013904223
            let noise = Float(seed >> 8) / Float(1 << 24) - 0.5
            let chord: Float = [220, 277, 330, 440].reduce(0) { $0 + 0.08 * sin(2 * .pi * $1 * t) }
            return chord + 0.7 * exp(-ph / 0.08) * sin(2 * .pi * 60 * ph) + 0.3 * exp(-hat / 0.02) * noise
        }
    }

    func testSpeechVersusMusic() {
        let s = AudioAnalyzer(); pump(s, speechLike(14))
        let m = AudioAnalyzer(); pump(m, musicLike(14))
        XCTAssertGreaterThan(s.features.speech, 0.55, "speech-like")
        XCTAssertLessThan(m.features.speech, 0.45, "music-like")
    }

    func testMixerBypassAndCeiling() {
        let mixer = AudioMixer()
        var s = SyncSettings(); s.audio.influence = 0
        let screen: [Int: RGB] = [0: RGB(0.3, 0.2, 0.1)]
        var f = AudioFeatures(); f.bass = 1; f.beat = 1
        XCTAssertEqual(mixer.mix(screen: screen, order: [0], features: f, dt: 0.02, settings: s), screen)
        s.audio.influence = 0.9; s.brightnessLimit = 0.5
        for _ in 0..<50 { let o = mixer.mix(screen: screen, order: [0], features: f, dt: 0.02, settings: s)[0]!; XCTAssertLessThanOrEqual(max(o.r, o.g, o.b), 0.5001) }
    }

    func testBassPulsesBrightenAndSpeechCalmsIt() {
        var s = SyncSettings(); s.audio.influence = 0.3; s.audio.smoothing = 0
        let screen: [Int: RGB] = [0: RGB(0.3, 0.2, 0.1), 1: RGB(0.1, 0.1, 0.3)]
        var f = AudioFeatures(); f.bass = 1; f.beat = 1; f.bassImpulse = 1
        let music = AudioMixer().mix(screen: screen, order: [0, 1], features: f, dt: 0.02, settings: s)
        f.speech = 1
        let talk = AudioMixer().mix(screen: screen, order: [0, 1], features: f, dt: 0.02, settings: s)
        func lum(_ d: [Int: RGB]) -> Float { d.values.map(\.luma).reduce(0, +) }
        XCTAssertGreaterThan(lum(music), lum(screen))
        XCTAssertGreaterThan(lum(music), lum(talk))
        XCTAssertGreaterThanOrEqual(lum(talk), lum(screen) - 0.001)
        XCTAssertGreaterThan(music[1]!.luma, screen[1]!.luma)             // room-wide: the dim light pulses too
    }

    func testAnalyzerSpeed() {
        let a = AudioAnalyzer(); let x = musicLike(10)
        let t0 = Date(); pump(a, x)
        let ms = Date().timeIntervalSince(t0) * 1000
        print("analysis of 10 s audio took \(ms) ms")
        XCTAssertLessThan(ms, 2500)
    }
}
