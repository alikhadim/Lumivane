import XCTest
@testable import HueCore

final class HueCoreTests: XCTestCase {
    func testPacketLayout() throws {
        let id = "12345678-1234-1234-1234-123456789abc"
        let d = try XCTUnwrap(StreamPacket.build(areaID: id, sequence: 7, colors: [(0, RGB(1, 0, 0.5)), (3, RGB(0, 1, 0))]))
        XCTAssertEqual(d.count, 16 + 36 + 14)
        XCTAssertEqual(String(decoding: d.prefix(9), as: UTF8.self), "HueStream")
        XCTAssertEqual(Array(d[9..<16]), [2, 0, 7, 0, 0, 0, 0])
        XCTAssertEqual(String(decoding: d[16..<52], as: UTF8.self), id)
        XCTAssertEqual(Array(d[52..<59]), [0, 0xFF, 0xFF, 0, 0, 0x80, 0x00])
        XCTAssertEqual(d[59], 3)
        XCTAssertNil(StreamPacket.build(areaID: "short", sequence: 0, colors: []))
    }

    func testReduceSplitFrame() {
        let w = 20, h = 10
        var px = [UInt8](repeating: 0, count: w * h * 4)
        for y in 0..<h { for x in 0..<w {
            let i = (y * w + x) * 4
            if x < w / 2 { px[i + 2] = 255 } else { px[i] = 255 }   // BGRA: left red, right blue
            px[i + 3] = 255
        } }
        let zones = [ZoneRect(0, 0, 0.5, 1), ZoneRect(0.5, 0, 1, 1), ZoneRect(0, 0, 1, 1)]
        let raw = px.withUnsafeBytes { ZoneAccumulator.reduceBGRA($0.baseAddress!, width: w, height: h, bytesPerRow: w * 4, zones: zones) }
        let l = ZoneAccumulator.stats(raw, zone: 0), r = ZoneAccumulator.stats(raw, zone: 1), all = ZoneAccumulator.stats(raw, zone: 2)
        XCTAssertEqual(l.average, RGB(1, 0, 0)); XCTAssertEqual(r.dominant.b, 1, accuracy: 1e-4)
        XCTAssertEqual(all.average.r, 0.5, accuracy: 1e-4); XCTAssertEqual(all.average.b, 0.5, accuracy: 1e-4)
        XCTAssertEqual(l.dominance, 1, accuracy: 1e-4)
    }

    func testBlackFrameIsNotDominant() {
        let px = [UInt8](repeating: 0, count: 4 * 4 * 4)
        let raw = px.withUnsafeBytes { ZoneAccumulator.reduceBGRA($0.baseAddress!, width: 4, height: 4, bytesPerRow: 16, zones: [ZoneRect(0, 0, 1, 1)]) }
        XCTAssertEqual(ZoneAccumulator.stats(raw, zone: 0).dominance, 0)
    }

    func testSmootherRespectsSlewAndConverges() {
        let e = ColorEngine()
        var s = SyncSettings(); s.dynamicBrightness = false; s.gamma = 1; s.smoothing = 0.5
        let white = ZoneStats(average: RGB(1, 1, 1), dominant: RGB(1, 1, 1), dominance: 0, brightness: 1, saturation: 0)
        let dt: Float = 0.02
        var prev: Float = 0
        for _ in 0..<5 {
            let v = e.process([(0, white)], dt: dt, settings: s)[0]!.r
            XCTAssertLessThanOrEqual(v - prev, (1.5 + 0.5 * 6.5) * dt + 1e-5)
            prev = v
        }
        var last: Float = 0
        for _ in 0..<300 { last = e.process([(0, white)], dt: dt, settings: s)[0]!.r }
        XCTAssertEqual(last, 1, accuracy: 0.01)
    }

    func testNightModeCapsOutput() {
        let e = ColorEngine()
        var s = SyncSettings(); s.nightMode = true; s.smoothing = 0
        let white = ZoneStats(average: RGB(1, 1, 1), dominant: RGB(1, 1, 1), dominance: 0, brightness: 1, saturation: 0)
        var o = RGB.black
        for _ in 0..<200 { o = e.process([(0, white)], dt: 0.02, settings: s)[0]! }
        XCTAssertLessThanOrEqual(o.r, 0.3 + 1e-4); XCTAssertLessThan(o.b, o.r)
    }

    func testXY() {
        let red = ColorMath.xy(RGB(1, 0, 0), gamut: nil)
        XCTAssertEqual(red.x, 0.6915, accuracy: 0.01); XCTAssertEqual(red.y, 0.3083, accuracy: 0.01)
        let white = ColorMath.xy(RGB(1, 1, 1), gamut: nil)
        XCTAssertEqual(white.x, 0.3227, accuracy: 0.01)
        let c = Gamut(red: XY(0.675, 0.322), green: XY(0.409, 0.518), blue: XY(0.167, 0.04))
        let clamped = ColorMath.xy(RGB(0, 0, 1), gamut: c)
        XCTAssertGreaterThanOrEqual(clamped.y, 0.03)
    }

    func testLayout() {
        XCTAssertEqual(ZoneLayout.rect(u: 0.05, v: 0.05), ZoneRect(0, 0, 0.34, 0.34))
        XCTAssertEqual(ZoneLayout.rect(u: 0.5, v: 0.5), ZoneRect(0.33, 0.33, 0.67, 0.67))
        let p = ZoneLayout.defaultRoutes(for: [EntertainmentChannel(id: 0, x: -1, y: 0, z: 1)])
        XCTAssertEqual(p[0].u, 0); XCTAssertEqual(p[0].v, 0)
    }
}
