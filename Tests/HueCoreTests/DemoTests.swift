import XCTest
@testable import HueCore

final class DemoTests: XCTestCase {
    func testScenesAreFiniteAndInRange() {
        for s in 0..<DemoScene.count { for t in stride(from: Float(0), to: 20, by: 3.7) {
            let st = DemoScene.stats(scene: s, rect: ZoneRect(0, 0, 1, 1), t: t)
            for v in [st.average.r, st.average.g, st.average.b, st.dominant.r, st.brightness, st.saturation, st.dominance] {
                XCTAssertTrue(v.isFinite); XCTAssertGreaterThanOrEqual(v, 0); XCTAssertLessThanOrEqual(v, 1.0001)
            }
        } }
    }

    func testScenesDiffer() {
        let a = DemoScene.stats(scene: 0, rect: ZoneRect(0, 0, 1, 1), t: 1).average, b = DemoScene.stats(scene: 1, rect: ZoneRect(0, 0, 1, 1), t: 1).average
        XCTAssertGreaterThan(abs(a.r - b.r) + abs(a.g - b.g) + abs(a.b - b.b), 0.1)
    }

    func testSunsetSkyIsWarmAndSeaIsCool() {
        let sky = DemoScene.stats(scene: 0, rect: ZoneRect(0, 0, 0.34, 0.34), t: 0).average
        let sea = DemoScene.stats(scene: 0, rect: ZoneRect(0, 0.66, 0.34, 1), t: 0).average
        XCTAssertGreaterThan(sky.r, sky.b); XCTAssertGreaterThan(sea.b, sea.r)
    }

    func testAudioBeatPulsesAtTempo() {
        let period: Float = 60 / 112
        XCTAssertGreaterThan(DemoSignal.audio(t: period * 3 + 0.001).beat, 0.9)
        XCTAssertLessThan(DemoSignal.audio(t: period * 3.6).beat, 0.2)
    }

    func testDemoFeedsColorEngine() {
        let e = ColorEngine(); var s = SyncSettings(); s.smoothing = 0
        let st = DemoScene.stats(scene: 1, rect: ZoneRect(0.33, 0.33, 0.67, 0.67), t: 2)
        var out = RGB.black
        for _ in 0..<60 { out = e.process([(0, st)], dt: 0.02, settings: s)[0]! }
        XCTAssertGreaterThan(max(out.r, out.g, out.b), 0.1)
    }
}
