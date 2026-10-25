namespace Lumivane.Core;

public static class ZoneLayout
{
    /// <summary>3x3 grid cell containing the point. Edge/corner lights get edge/corner zones, center lights the center cell.</summary>
    public static ZoneRect Rect(double u, double v)
    {
        static (float, float) Band(double t) => t < 0.34 ? (0f, 0.34f) : (t > 0.66 ? (0.66f, 1f) : (0.33f, 0.67f));
        var (x0, x1) = Band(u); var (y0, y1) = Band(v);
        return new ZoneRect(x0, y0, x1, y1);
    }

    /// <summary>Hue positions: x -1 (left) to 1 (right), z -1 (floor) to 1 (ceiling).</summary>
    public static List<LightRoute> DefaultRoutes(IEnumerable<EntertainmentChannel> channels) =>
        channels.Select(c => new LightRoute { ChannelId = c.Id, U = Math.Clamp((c.X + 1) / 2, 0, 1), V = Math.Clamp((1 - c.Z) / 2, 0, 1) }).ToList();
}

public static class StreamPacket
{
    /// <summary>Hue Entertainment API v2 message: 16-byte header, 36-byte area id, 7 bytes per channel (RGB, 16 bit big endian).</summary>
    public static byte[]? Build(string areaId, byte sequence, IReadOnlyList<(byte Channel, RGB Rgb)> colors)
    {
        var id = System.Text.Encoding.UTF8.GetBytes(areaId);
        if (id.Length != 36 || colors.Count > 20) return null;
        var d = new List<byte>(52 + colors.Count * 7);
        d.AddRange(System.Text.Encoding.ASCII.GetBytes("HueStream"));
        d.AddRange(new byte[] { 0x02, 0x00, sequence, 0x00, 0x00, 0x00, 0x00 });
        d.AddRange(id);
        foreach (var (ch, c) in colors)
        {
            d.Add(ch);
            foreach (var v in new[] { c.R, c.G, c.B })
            {
                ushort u = (ushort)MathF.Round(Math.Clamp(v, 0, 1) * 65535, MidpointRounding.ToEven);
                d.Add((byte)(u >> 8)); d.Add((byte)(u & 0xFF));
            }
        }
        return d.ToArray();
    }
}
