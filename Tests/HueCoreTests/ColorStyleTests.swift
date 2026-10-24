import XCTest
@testable import HueCore

final class ColorStyleTests: XCTestCase {
    private func settle(_ stats: [ZoneStats], style: ColorStyle, frames: Int = 400, adapt: Bool = true) -> [RGB] {
        var s = SyncSettings(); s.style = style; s.dynamicBrightness = adapt
        let e = ColorEngine(); var out: [Int: RGB] = [:]
        for _ in 0..<frames { out = e.process(stats.enumerated().map { ($0.offset, $0.element) }, dt: 0.02, settings: s) }
        return stats.indices.map { out[$0]! }
    }
    private func zone(_ c: RGB, dominant: RGB? = nil, dominance: Float = 0) -> ZoneStats {
        ZoneStats(average: c, dominant: dominant ?? c, dominance: dominance, brightness: c.luma, saturation: ColorMath.hsv(c).s)
    }

    func testWhiteStaysNeutralAndFull() {
        for st in [ColorStyle.accurate, .enhanced, .gaming] {
            let o = settle([zone(RGB(1, 1, 1))], style: st, adapt: false)[0]
            XCTAssertEqual(o.r, 1, accuracy: 0.01); XCTAssertEqual(o.b, 1, accuracy: 0.01)
        }
    }
    func testNearWhiteIsNotTinted() {
        let o = settle([zone(RGB(0.97, 0.97, 0.96))], style: .enhanced)[0]
        XCTAssertLessThan(ColorMath.hsv(o).s, 0.02)
    }
    func testBlackStaysBlack() {
        XCTAssertLessThan(settle([zone(.black)], style: .enhanced)[0].luma, 0.01)
        XCTAssertLessThan(settle([zone(RGB(0.01, 0.01, 0.02))], style: .enhanced)[0].luma, 0.02)
    }
    func testEnhancedIsMoreVividAndBrighterThanAccurate() {
        let muted = zone(RGB(0.45, 0.30, 0.25), dominant: RGB(0.8, 0.3, 0.1), dominance: 0.3)
        let a = ColorMath.hsv(settle([muted], style: .accurate)[0]), e = ColorMath.hsv(settle([muted], style: .enhanced)[0])
        XCTAssertGreaterThan(e.s, a.s); XCTAssertGreaterThan(e.v, a.v)
    }
    func testDominantColorWinsOverMuddyAverage() {
        let z = zone(RGB(0.35, 0.3, 0.3), dominant: RGB(1, 0.1, 0.1), dominance: 0.4)
        let o = ColorMath.hsv(settle([z], style: .gaming)[0])
        XCTAssertLessThan(min(o.h, 1 - o.h), 0.03); XCTAssertGreaterThan(o.s, 0.7)
    }
    func testHueTakesShortestArcWithoutPassingThroughGray() {
        var s = SyncSettings(); s.style = .enhanced; s.smoothing = 0.3
        let e = ColorEngine()
        let red = zone(RGB(1, 0, 0.05), dominance: 1), magenta = zone(RGB(1, 0, 0.9), dominance: 1)
        for _ in 0..<200 { _ = e.process([(0, red)], dt: 0.02, settings: s) }
        var minS: Float = 1
        for _ in 0..<100 {
            let o = e.process([(0, magenta)], dt: 0.02, settings: s)[0]!
            let h = ColorMath.hsv(o); minS = min(minS, h.s)
            XCTAssertGreaterThan(h.h, 0.8)       // goes red -> magenta the short way, never through green or blue
        }
        XCTAssertGreaterThan(minS, 0.8)
    }
    func testWarmthLowersBlue() {
        let w = settle([zone(RGB(1, 1, 1))], style: .cinematic)[0]
        XCTAssertLessThan(w.b, w.r)
    }
    func testOldProfilesDecodeAndMigrate() throws {
        let old = #"{"brightness":0.8,"intensity":1.1,"smoothing":0.6,"fps":50,"gamma":1.2,"dominance":0.5,"dynamicBrightness":true,"nightMode":false,"lowLightMode":false,"brightnessLimit":1,"channelGain":[],"audio":{"enabled":true,"input":{"system":{}},"influence":0.2,"bassSensitivity":1,"trebleSensitivity":1,"beatIntensity":1,"smoothing":0.5,"musicStrength":0.5}}"#
        let s = try JSONDecoder().decode(SyncSettings.self, from: Data(old.utf8))
        XCTAssertEqual(s.gamma, 1.0); XCTAssertEqual(s.style, .enhanced); XCTAssertEqual(s.brightness, 0.8)
        let again = try JSONDecoder().decode(SyncSettings.self, from: JSONEncoder().encode(s))
        XCTAssertEqual(again, s)
    }
    func testStyleBrightnessOrdering() {
        let scene = zone(RGB(0.3, 0.2, 0.15), dominant: RGB(0.6, 0.3, 0.1), dominance: 0.2)
        let v = ColorStyle.allCases.map { ColorMath.hsv(settle([scene], style: $0)[0]).v }
        XCTAssertLessThan(v[0], v[1])   // accurate < enhanced
    }
}

final class PrivacyTests: XCTestCase {
    func testRedactedReportHidesNetworkIdentifiers() {
        let b = BridgeInfo(id: "001788FFFE123456", ip: "192.168.7.42", name: "Living Room Bridge", firmware: "1976154040", apiVersion: "1.65.0", model: "BSB002")
        let d = Diagnostics(bridge: b, reachable: true, latencyMs: 12, lightCount: 5, entertainmentLightCount: 3, areas: [])
        let r = d.report(redacted: true)
        XCTAssertFalse(r.contains("192.168.7.42")); XCTAssertFalse(r.contains("123456")); XCTAssertTrue(r.contains("192.168.x.x"))
        XCTAssertTrue(d.report(redacted: false).contains("192.168.7.42"))
    }
}
