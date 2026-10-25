using System.Text.Json.Serialization;

namespace Lumivane.Core;

public readonly record struct RGB(float R, float G, float B)
{
    public static readonly RGB Black = new(0, 0, 0);
    [JsonIgnore] public float Luma => 0.2126f * R + 0.7152f * G + 0.0722f * B;
    public RGB Mixed(RGB o, float t) => new(R + (o.R - R) * t, G + (o.G - G) * t, B + (o.B - B) * t);
    public RGB Scaled(float k) => new(R * k, G * k, B * k);
    public RGB Clamped() => new(Math.Clamp(R, 0, 1), Math.Clamp(G, 0, 1), Math.Clamp(B, 0, 1));
}

public static class ColorMath
{
    public static (float H, float S, float V) Hsv(RGB c)
    {
        float mx = MathF.Max(c.R, MathF.Max(c.G, c.B)), mn = MathF.Min(c.R, MathF.Min(c.G, c.B)), d = mx - mn;
        float h = 0;
        if (d > 0)
        {
            if (mx == c.R) { h = (c.G - c.B) / d; if (h < 0) h += 6; }
            else if (mx == c.G) h = (c.B - c.R) / d + 2;
            else h = (c.R - c.G) / d + 4;
            h /= 6;
        }
        return (h, mx > 0 ? d / mx : 0, mx);
    }

    public static RGB Rgb(float h, float s, float v)
    {
        float hh = Math.Clamp(h, 0, 0.99999f) * 6;
        int i = (int)hh;
        float f = hh - i;
        float p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        return i switch
        {
            0 => new RGB(v, t, p),
            1 => new RGB(q, v, p),
            2 => new RGB(p, v, t),
            3 => new RGB(p, q, v),
            4 => new RGB(t, p, v),
            _ => new RGB(v, p, q),
        };
    }

    public static float ToLinear(float v) => v > 0.04045f ? MathF.Pow((v + 0.055f) / 1.055f, 2.4f) : v / 12.92f;

    /// <summary>sRGB (gamma-encoded, 0..1) to CIE xy using the Philips wide-gamut D65 matrix, clamped to the light's gamut.</summary>
    public static XY ToXY(RGB c, Gamut? gamut)
    {
        double r = ToLinear(c.R), g = ToLinear(c.G), b = ToLinear(c.B);
        double X = r * 0.664511 + g * 0.154324 + b * 0.162028;
        double Y = r * 0.283881 + g * 0.668433 + b * 0.047685;
        double Z = r * 0.000088 + g * 0.072310 + b * 0.986039;
        double sum = X + Y + Z;
        var p = sum > 0 ? new XY(X / sum, Y / sum) : new XY(0.3127, 0.3290);
        return gamut is null ? p : Clamp(p, gamut);
    }

    internal static XY Clamp(XY p, Gamut g)
    {
        static double Cross(XY a, XY b) => a.X * b.Y - a.Y * b.X;
        static XY Sub(XY a, XY b) => new(a.X - b.X, a.Y - b.Y);
        var v1 = Sub(g.Green, g.Red); var v2 = Sub(g.Blue, g.Red); var q = Sub(p, g.Red);
        double den = Cross(v1, v2);
        double s = Cross(q, v2) / den, t = Cross(v1, q) / den;
        if (s >= 0 && t >= 0 && s + t <= 1) return p;
        XY Closest(XY a, XY b)
        {
            var ap = Sub(p, a); var ab = Sub(b, a);
            double u = Math.Clamp((ap.X * ab.X + ap.Y * ab.Y) / (ab.X * ab.X + ab.Y * ab.Y), 0, 1);
            return new XY(a.X + ab.X * u, a.Y + ab.Y * u);
        }
        double D2(XY a) => (a.X - p.X) * (a.X - p.X) + (a.Y - p.Y) * (a.Y - p.Y);
        XY best = Closest(g.Red, g.Green);
        foreach (var c in new[] { Closest(g.Green, g.Blue), Closest(g.Blue, g.Red) }) if (D2(c) < D2(best)) best = c;
        return best;
    }
}

public readonly record struct ZoneRect(float X0, float Y0, float X1, float Y1);

public readonly record struct ZoneStats(RGB Average, RGB Dominant, float Dominance, float Brightness, float Saturation)
{
    public static readonly ZoneStats Empty = new(RGB.Black, RGB.Black, 0, 0, 0);

    /// <summary>Mirror mode: several sources analysed together.</summary>
    public static ZoneStats Combine(IReadOnlyList<ZoneStats> s)
    {
        if (s.Count <= 1) return s.Count == 1 ? s[0] : Empty;
        float n = s.Count;
        RGB Mean(Func<ZoneStats, RGB> f)
        {
            RGB a = RGB.Black;
            foreach (var z in s) { var c = f(z); a = new RGB(a.R + c.R, a.G + c.G, a.B + c.B); }
            return a.Scaled(1 / n);
        }
        // Dominant colors are weighted by how colorful their zone is, so a gray zone doesn't wash out a vivid one.
        var w = s.Select(z => z.Dominance + 0.02f).ToArray();
        float wt = w.Sum();
        RGB dom = RGB.Black;
        for (int i = 0; i < s.Count; i++) dom = new RGB(dom.R + s[i].Dominant.R * w[i], dom.G + s[i].Dominant.G * w[i], dom.B + s[i].Dominant.B * w[i]);
        return new ZoneStats(Mean(z => z.Average), dom.Scaled(1 / wt), s.Sum(z => z.Dominance) / n, s.Sum(z => z.Brightness) / n, s.Sum(z => z.Saturation) / n);
    }
}

/// <summary>
/// Raw layout per zone (52 floats): [0..2] sum RGB, [3] pixel count, [4..15] hue-bin weights (sat*val),
/// [16..51] hue-bin weighted RGB sums. Same layout as the macOS GPU kernel.
/// </summary>
public static class ZoneAccumulator
{
    public const int Stride = 52;
    public const int Bins = 12;
    const float MinValue = 0.08f, MinSat = 0.2f;

    public static float[] ReduceBgra(ReadOnlySpan<byte> data, int width, int height, int bytesPerRow, IReadOnlyList<ZoneRect> zones, int step = 1)
    {
        var o = new float[zones.Count * Stride];
        int st = Math.Max(1, step);
        for (int zi = 0; zi < zones.Count; zi++)
        {
            var z = zones[zi];
            int x0 = Math.Min((int)(z.X0 * width), width - 1), y0 = Math.Min((int)(z.Y0 * height), height - 1);
            int x1 = Math.Max(x0 + 1, Math.Min(width, (int)(z.X1 * width))), y1 = Math.Max(y0 + 1, Math.Min(height, (int)(z.Y1 * height)));
            int b = zi * Stride;
            for (int y = y0; y < y1; y += st)
            {
                int row = y * bytesPerRow;
                for (int x = x0; x < x1; x += st)
                {
                    int p = row + x * 4;
                    var c = new RGB(data[p + 2] / 255f, data[p + 1] / 255f, data[p] / 255f);
                    o[b] += c.R; o[b + 1] += c.G; o[b + 2] += c.B; o[b + 3] += 1;
                    var (h, s, v) = ColorMath.Hsv(c);
                    if (v >= MinValue && s >= MinSat)
                    {
                        float w = s * v;
                        int bin = Math.Min(Bins - 1, (int)(h * Bins));
                        o[b + 4 + bin] += w;
                        o[b + 16 + bin * 3] += c.R * w; o[b + 17 + bin * 3] += c.G * w; o[b + 18 + bin * 3] += c.B * w;
                    }
                }
            }
        }
        return o;
    }

    public static ZoneStats Stats(float[] raw, int zone)
    {
        int o = zone * Stride;
        if (o + Stride > raw.Length || raw[o + 3] <= 0) return ZoneStats.Empty;
        float n = raw[o + 3];
        var avg = new RGB(raw[o] / n, raw[o + 1] / n, raw[o + 2] / n);
        int best = 0;
        for (int b = 1; b < Bins; b++) if (raw[o + 4 + b] > raw[o + 4 + best]) best = b;
        float w0 = raw[o + 4 + best];
        if (w0 <= 0) return new ZoneStats(avg, avg, 0, avg.Luma, ColorMath.Hsv(avg).S);
        // Best hue bin plus any neighbour with at least a third of its weight, so a hue that straddles a bin edge isn't split.
        float w = w0, r = raw[o + 16 + best * 3], g = raw[o + 17 + best * 3], bl = raw[o + 18 + best * 3];
        foreach (int d in new[] { -1, 1 })
        {
            int k = (best + d + Bins) % Bins;
            if (raw[o + 4 + k] > 0.35f * w0) { w += raw[o + 4 + k]; r += raw[o + 16 + k * 3]; g += raw[o + 17 + k * 3]; bl += raw[o + 18 + k * 3]; }
        }
        var dom = new RGB(r / w, g / w, bl / w);
        // Weights are saturation * value per pixel (at most 1), so w / n is the share of colorful energy in the zone.
        return new ZoneStats(avg, dom, MathF.Min(1, w / n), avg.Luma, ColorMath.Hsv(avg).S);
    }
}

public enum ColorStyle { Accurate, Enhanced, Cinematic, Gaming }

public static class ColorStyleInfo
{
    public static string Title(this ColorStyle s) => s switch
    {
        ColorStyle.Accurate => "Accurate", ColorStyle.Enhanced => "Enhanced", ColorStyle.Cinematic => "Cinematic", _ => "Gaming",
    };

    public static string Summary(this ColorStyle s) => s switch
    {
        ColorStyle.Accurate => "Colors as they appear on screen",
        ColorStyle.Enhanced => "Richer, more vivid color. Recommended",
        ColorStyle.Cinematic => "Warmer, deeper contrast, slower and more dramatic fades",
        _ => "Aggressive color, fastest response",
    };

    public sealed record Params(float Pick, float SatPow, float SatGain, float ValuePow, float Contrast, float Warmth, float TauScale, float SlewScale, float AutoGain);

    public static Params GetParams(this ColorStyle s) => s switch
    {
        ColorStyle.Accurate => new(0.15f, 1.0f, 1.0f, 1.0f, 0f, 0f, 1.0f, 1, 0.25f),
        ColorStyle.Enhanced => new(0.65f, 0.72f, 1.12f, 0.78f, 0.1f, 0f, 0.8f, 1, 0.6f),
        ColorStyle.Cinematic => new(0.5f, 0.85f, 1.05f, 0.9f, 0.35f, 0.22f, 1.5f, 1, 0.6f),
        _ => new(0.85f, 0.6f, 1.2f, 0.7f, 0.15f, 0f, 0.35f, 2, 0.3f),
    };
}

/// <summary>
/// Turns per-channel zone stats into light colors.
/// 1. Pick: blend the zone average toward its dominant hue (more for vivid zones, none for neutral ones).
/// 2. Shape: lift saturation with a curve, lift dark tones, adapt to scene brightness, keep true black black.
/// 3. Smooth in HSV: hue follows the shortest arc, brightness is rate-limited (strobe guard) and rises faster than it falls.
/// 4. Output: white balance, night / low-light, brightness ceiling.
/// </summary>
public sealed class ColorEngine
{
    sealed class Chan { public float H, S, V; }
    readonly Dictionary<int, Chan> state = new();
    float vAvg = 0.4f, autoGain = 1;

    public void Reset() { state.Clear(); vAvg = 0.4f; autoGain = 1; }

    /// <summary>Hard ceiling for any output component: brightness limiter, night and low-light caps.</summary>
    public static float Ceiling(SyncSettings s)
    {
        float cap = MathF.Min(s.BrightnessLimit, 1);
        if (s.LowLightMode) cap = MathF.Min(cap, 0.45f);
        if (s.NightMode) cap = MathF.Min(cap, 0.3f);
        return cap;
    }

    static float Smooth(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    public Dictionary<int, RGB> Process(IReadOnlyList<(int Id, ZoneStats Stats)> channels, float dt, SyncSettings s)
    {
        dt = Math.Clamp(dt, 0.001f, 0.25f);
        var p = s.Style.GetParams();

        var targets = new List<(int id, float h, float s, float v)>();
        foreach (var (id, st) in channels)
        {
            float evidence = Smooth(0.03f, 0.2f, st.Dominance);
            float k = MathF.Min(1, p.Pick * (0.5f + s.Dominance) * evidence);
            var (h, sat, v) = ColorMath.Hsv(st.Average.Mixed(st.Dominant, k));
            // The saturation lift fades out for near-neutral colors, so white and gray stay neutral instead of picking up a tint.
            float lifted = MathF.Min(1, MathF.Pow(sat, p.SatPow) * p.SatGain * s.Intensity);
            targets.Add((id, h, sat + (lifted - sat) * Smooth(0.03f, 0.15f, sat), v));
        }
        if (s.DynamicBrightness && p.AutoGain > 0 && targets.Count > 0)
        {
            float f = targets.Sum(t => t.v) / targets.Count;
            vAvg += (f - vAvg) * (1 - MathF.Exp(-dt / 3));
            float tg = Math.Clamp(MathF.Pow(0.5f / MathF.Max(vAvg, 0.08f), p.AutoGain), 0.85f, 1.8f);
            autoGain += (tg - autoGain) * (1 - MathF.Exp(-dt / 2));
        }
        else autoGain = 1;

        float tau = (0.04f + s.Smoothing * 0.76f) * p.TauScale;
        float slew = (1.5f + (1 - s.Smoothing) * 6.5f) * p.SlewScale;
        float hueRate = (1.5f + (1 - s.Smoothing) * 4) * p.SlewScale;
        float cap = Ceiling(s);
        float wb = Math.Clamp(p.Warmth + s.Warmth, -1, 1);
        var output = new Dictionary<int, RGB>();
        foreach (var t in targets)
        {
            float x = MathF.Min(t.v * autoGain, 1);
            x += p.Contrast * (x * x * (3 - 2 * x) - x);
            float vt = MathF.Pow(x, p.ValuePow * s.Gamma) * Smooth(0.02f, 0.07f, x);

            if (!state.TryGetValue(t.id, out var c)) c = new Chan { H = t.h, S = t.s, V = 0 };
            float tv = vt > c.V ? tau * 0.5f : tau * 1.5f;
            float nv = c.V + (vt - c.V) * (1 - MathF.Exp(-dt / MathF.Max(tv, 0.001f)));
            nv = Math.Clamp(nv, c.V - slew * dt, c.V + slew * dt);
            float ns = c.S + (t.s - c.S) * (1 - MathF.Exp(-dt / MathF.Max(tau * 0.7f, 0.001f)));
            if (c.S < 0.06f || c.V < 0.02f) c.H = t.h;   // no visible hue yet: jump instead of sweeping through colors
            else if (vt > 0.02f && t.s > 0.05f)
            {
                float d = t.h - c.H;
                if (d > 0.5f) d -= 1; else if (d < -0.5f) d += 1;
                float conf = MathF.Min(1, t.s * 3);
                float step = d * (1 - MathF.Exp(-dt / MathF.Max(tau * 0.6f / conf, 0.001f)));
                step = Math.Clamp(step, -hueRate * dt, hueRate * dt);
                c.H += step; c.H -= MathF.Floor(c.H);
            }
            c.S = ns; c.V = nv;
            state[t.id] = c;

            var o = ColorMath.Rgb(c.H, c.S, c.V);
            if (wb > 0) o = o with { G = o.G * (1 - 0.10f * wb), B = o.B * (1 - 0.40f * wb) };
            else if (wb < 0) o = o with { R = o.R * (1 + 0.25f * wb), G = o.G * (1 + 0.05f * wb) };
            if (s.LowLightMode) o = new RGB(MathF.Pow(o.R, 0.8f), MathF.Pow(o.G, 0.8f), MathF.Pow(o.B, 0.8f));
            if (s.NightMode) o = o with { G = o.G * 0.9f, B = o.B * 0.7f };
            float gain = s.ChannelGain.TryGetValue(t.id, out var gv) ? gv : 1;
            output[t.id] = o.Scaled(MathF.Min(s.Brightness * gain, cap)).Clamped();
        }
        return output;
    }
}
