namespace Lumivane.Core;

/// <summary>Procedural moving pictures. Each returns an sRGB color for a point (u, v in 0..1) at time t seconds.</summary>
public static class DemoScene
{
    public const int Count = 4;

    static RGB Mix(RGB a, RGB b, float t) { float k = Math.Clamp(t, 0, 1); return new RGB(a.R + (b.R - a.R) * k, a.G + (b.G - a.G) * k, a.B + (b.B - a.B) * k); }
    static float Smooth(float a, float b, float x) { float t = Math.Clamp((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); }

    public static RGB Color(int scene, float u, float v, float t)
    {
        switch (((scene % Count) + Count) % Count)
        {
            case 0:   // sunset over water
            {
                float sun = 0.5f + 0.12f * MathF.Sin(t * 0.15f);
                var sky = Mix(new RGB(0.22f, 0.10f, 0.42f), new RGB(1.0f, 0.42f, 0.10f), Smooth(0.0f, 0.55f, v));
                var sea = Mix(new RGB(0.10f, 0.20f, 0.30f), new RGB(0.03f, 0.08f, 0.14f), Smooth(0.55f, 1.0f, v));
                var c = v < 0.55f ? sky : sea;
                float glow = MathF.Exp(-MathF.Pow((u - sun) * 4.5f, 2)) * (v < 0.55f ? 0.35f : 0.7f * (1 - Smooth(0.55f, 1.0f, v)));
                c = Mix(c, new RGB(1.0f, 0.62f, 0.22f), glow);
                if (MathF.Pow(u - sun, 2) + MathF.Pow((v - 0.5f) * 0.56f, 2) < 0.0035f) c = new RGB(1.0f, 0.92f, 0.65f);
                return c;
            }
            case 1:   // neon street
            {
                int stripe = (int)(u * 6);
                float phase = MathF.Sin(t * 1.3f + stripe * 1.7f) * 0.5f + 0.5f;
                var tint = stripe % 2 == 0 ? new RGB(0.0f, 0.85f, 1.0f) : new RGB(1.0f, 0.10f, 0.65f);
                float lit = Smooth(0.25f, 0.7f, v) * (0.35f + 0.65f * phase);
                return Mix(new RGB(0.03f, 0.02f, 0.09f), tint, lit);
            }
            case 2:   // forest
            {
                float dapple = MathF.Sin(u * 9 + t * 0.3f) * MathF.Sin(v * 7 - t * 0.2f) * 0.5f + 0.5f;
                var bse = Mix(new RGB(0.60f, 0.78f, 0.30f), new RGB(0.04f, 0.22f, 0.09f), Smooth(0.0f, 0.9f, v));
                return Mix(bse, new RGB(0.85f, 0.92f, 0.50f), dapple * 0.25f * (1 - v));
            }
            default:  // ember glow
            {
                float flicker = 0.85f + 0.15f * MathF.Sin(t * 7 + u * 13) * MathF.Sin(t * 4.3f + u * 5);
                float heat = Smooth(0.15f, 1.0f, v) * flicker;
                return Mix(new RGB(0.12f, 0.02f, 0.02f), new RGB(1.0f, 0.34f, 0.05f), heat * (0.7f + 0.3f * (1 - MathF.Abs(u - 0.5f) * 2)));
            }
        }
    }

    /// <summary>Statistics of a zone, in the same shape the GPU reducer produces.</summary>
    public static ZoneStats Stats(int scene, ZoneRect rect, float t)
    {
        const int n = 4;
        RGB sum = RGB.Black; float bright = 0, sat = 0; int counted = 0;
        var binW = new float[12]; var binRGB = new RGB[12]; var binN = new int[12];
        for (int iy = 0; iy < n; iy++)
            for (int ix = 0; ix < n; ix++)
            {
                float u = rect.X0 + (rect.X1 - rect.X0) * (ix + 0.5f) / n, v = rect.Y0 + (rect.Y1 - rect.Y0) * (iy + 0.5f) / n;
                var c = Color(scene, u, v, t);
                sum = new RGB(sum.R + c.R, sum.G + c.G, sum.B + c.B);
                float mx = MathF.Max(c.R, MathF.Max(c.G, c.B)), mn = MathF.Min(c.R, MathF.Min(c.G, c.B)), d = mx - mn, s = mx > 0 ? d / mx : 0;
                bright += mx; sat += s; counted++;
                if (mx >= 0.08f && s >= 0.2f)
                {
                    float h;
                    if (mx == c.R) { h = (c.G - c.B) / d; if (h < 0) h += 6; } else if (mx == c.G) h = (c.B - c.R) / d + 2; else h = (c.R - c.G) / d + 4;
                    int bin = Math.Min(11, (int)(h / 6 * 12)); float w = s * mx;
                    binW[bin] += w; binRGB[bin] = new RGB(binRGB[bin].R + c.R * w, binRGB[bin].G + c.G * w, binRGB[bin].B + c.B * w); binN[bin]++;
                }
            }
        float k = counted;
        var avg = new RGB(sum.R / k, sum.G / k, sum.B / k);
        int best = 0; for (int i = 1; i < 12; i++) if (binW[i] > binW[best]) best = i;
        if (binW[best] <= 0) return new ZoneStats(avg, avg, 0, bright / k, sat / k);
        return new ZoneStats(avg, binRGB[best].Scaled(1 / binW[best]), binN[best] / k, bright / k, sat / k);
    }
}

/// <summary>A steady 112 BPM groove for the Sound page and the audio mixer.</summary>
public static class DemoSignal
{
    public static AudioFeatures Audio(float t)
    {
        const float period = 60f / 112;
        float phase = (t % period) / period;
        float pulse = MathF.Exp(-phase * 7);
        return new AudioFeatures
        {
            Level = 0.62f + 0.1f * MathF.Sin(t * 0.7f), VolumeDB = -18,
            Bass = MathF.Min(1, 0.45f + 0.45f * pulse), Mid = 0.42f + 0.12f * MathF.Sin(t * 1.9f), Treble = 0.30f + 0.18f * MathF.Sin(t * 3.1f + 1),
            Beat = pulse, BassImpulse = pulse > 0.8f ? pulse : 0, Transient = MathF.Max(0, MathF.Sin(t * 6.2f)) * 0.3f,
            Tempo = 112, TempoConfidence = 0.85f, Energy = 0.6f, Speech = 0.05f,
        };
    }
}
