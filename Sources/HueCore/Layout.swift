import Foundation

public enum ZoneLayout {
    /// 3x3 grid cell containing the point. Edge/corner lights get edge/corner zones, center lights the center cell.
    public static func rect(u: Double, v: Double) -> ZoneRect {
        func band(_ t: Double) -> (Float, Float) { t < 0.34 ? (0, 0.34) : (t > 0.66 ? (0.66, 1) : (0.33, 0.67)) }
        let (x0, x1) = band(u), (y0, y1) = band(v)
        return ZoneRect(x0, y0, x1, y1)
    }

    /// Hue positions: x -1 (left) ... 1 (right), z -1 (floor) ... 1 (ceiling).
    public static func defaultRoutes(for channels: [EntertainmentChannel]) -> [LightRoute] {
        channels.map { LightRoute(channelID: $0.id, u: min(max(($0.x + 1) / 2, 0), 1), v: min(max((1 - $0.z) / 2, 0), 1)) }
    }
}

public enum StreamPacket {
    /// Hue Entertainment API v2 message: 16-byte header, 36-byte area id, 7 bytes per channel (RGB, 16 bit big endian).
    public static func build(areaID: String, sequence: UInt8, colors: [(channel: UInt8, rgb: RGB)]) -> Data? {
        let id = Array(areaID.utf8)
        guard id.count == 36, colors.count <= 20 else { return nil }
        var d = Data(capacity: 52 + colors.count * 7)
        d.append(contentsOf: Array("HueStream".utf8))
        d.append(contentsOf: [0x02, 0x00, sequence, 0x00, 0x00, 0x00, 0x00])
        d.append(contentsOf: id)
        for (ch, c) in colors {
            d.append(ch)
            for v in [c.r, c.g, c.b] {
                let u = UInt16((min(max(v, 0), 1) * 65535).rounded())
                d.append(UInt8(u >> 8)); d.append(UInt8(u & 0xFF))
            }
        }
        return d
    }
}
