using Lumivane.Core;
using Xunit;

namespace Lumivane.Core.Tests;

public class ColorStyleTests
{
    static RGB[] Settle(ZoneStats[] stats, ColorStyle style, int frames = 400, bool adapt = true)
    {
        var s = new SyncSettings { Style = style, DynamicBrightness = adapt };
        var e = new ColorEngine(); Dictionary<int, RGB> o = new();
        for (int i = 0; i < frames; i++) o = e.Process(stats.Select((z, k) => (k, z)).ToList(), 0.02f, s);
        return stats.Select((_, k) => o[k]).ToArray();
    }
    static ZoneStats Zone(RGB c, RGB? dominant = null, float dominance = 0) => new(c, dominant ?? c, dominance, c.Luma, ColorMath.Hsv(c).S);

    [Fact] public void WhiteStaysNeutralAndFull()
    {
        foreach (var st in new[] { ColorStyle.Accurate, ColorStyle.Enhanced, ColorStyle.Gaming })
        {
            var o = Settle(new[] { Zone(new RGB(1, 1, 1)) }, st, adapt: false)[0];
            Assert.InRange(o.R, 0.99f, 1.01f); Assert.InRange(o.B, 0.99f, 1.01f);
        }
    }
    [Fact] public void NearWhiteIsNotTinted() => Assert.True(ColorMath.Hsv(Settle(new[] { Zone(new RGB(0.97f, 0.97f, 0.96f)) }, ColorStyle.Enhanced)[0]).S < 0.02f);
    [Fact] public void BlackStaysBlack()
    {
        Assert.True(Settle(new[] { Zone(RGB.Black) }, ColorStyle.Enhanced)[0].Luma < 0.01f);
        Assert.True(Settle(new[] { Zone(new RGB(0.01f, 0.01f, 0.02f)) }, ColorStyle.Enhanced)[0].Luma < 0.02f);
    }
    [Fact] public void EnhancedIsMoreVividAndBrighterThanAccurate()
    {
        var muted = Zone(new RGB(0.45f, 0.30f, 0.25f), new RGB(0.8f, 0.3f, 0.1f), 0.3f);
        var a = ColorMath.Hsv(Settle(new[] { muted }, ColorStyle.Accurate)[0]); var e = ColorMath.Hsv(Settle(new[] { muted }, ColorStyle.Enhanced)[0]);
        Assert.True(e.S > a.S); Assert.True(e.V > a.V);
    }
    [Fact] public void DominantColorWinsOverMuddyAverage()
    {
        var z = Zone(new RGB(0.35f, 0.3f, 0.3f), new RGB(1, 0.1f, 0.1f), 0.4f);
        var o = ColorMath.Hsv(Settle(new[] { z }, ColorStyle.Gaming)[0]);
        Assert.True(Math.Min(o.H, 1 - o.H) < 0.03f); Assert.True(o.S > 0.7f);
    }
    [Fact] public void HueTakesShortestArcWithoutPassingThroughGray()
    {
        var s = new SyncSettings { Style = ColorStyle.Enhanced, Smoothing = 0.3f };
        var e = new ColorEngine();
        var red = Zone(new RGB(1, 0, 0.05f), dominance: 1); var magenta = Zone(new RGB(1, 0, 0.9f), dominance: 1);
        for (int i = 0; i < 200; i++) e.Process(new[] { (0, red) }, 0.02f, s);
        float minS = 1;
        for (int i = 0; i < 100; i++)
        {
            var h = ColorMath.Hsv(e.Process(new[] { (0, magenta) }, 0.02f, s)[0]);
            minS = Math.Min(minS, h.S);
            Assert.True(h.H > 0.8f);
        }
        Assert.True(minS > 0.8f);
    }
    [Fact] public void WarmthLowersBlue() { var w = Settle(new[] { Zone(new RGB(1, 1, 1)) }, ColorStyle.Cinematic)[0]; Assert.True(w.B < w.R); }
    [Fact] public void OldProfilesDecodeAndMigrate()
    {
        const string old = """{"brightness":0.8,"intensity":1.1,"smoothing":0.6,"fps":50,"gamma":1.2,"dominance":0.5,"dynamicBrightness":true,"nightMode":false,"lowLightMode":false,"brightnessLimit":1,"channelGain":[],"audio":{"enabled":true,"input":{"system":{}},"influence":0.2,"bassSensitivity":1,"trebleSensitivity":1,"beatIntensity":1,"smoothing":0.5,"musicStrength":0.5}}""";
        var s = Json.Read<SyncSettings>(old)!;
        Assert.Equal(1.0f, s.Gamma); Assert.Equal(ColorStyle.Enhanced, s.Style); Assert.Equal(0.8f, s.Brightness);
        Assert.Equal(AudioInput.System, s.Audio.Input);
        var again = Json.Read<SyncSettings>(Json.Write(s))!;
        Assert.Equal(Json.Write(s), Json.Write(again));
        Assert.Equal(2, again.ColorVersion);
        Assert.Equal(2, new SyncSettings().ColorVersion);
    }
    [Fact] public void StyleBrightnessOrdering()
    {
        var scene = Zone(new RGB(0.3f, 0.2f, 0.15f), new RGB(0.6f, 0.3f, 0.1f), 0.2f);
        var v = Enum.GetValues<ColorStyle>().Select(st => ColorMath.Hsv(Settle(new[] { scene }, st)[0]).V).ToArray();
        Assert.True(v[0] < v[1]);
    }
    [Fact] public void RedactedReportHidesNetworkIdentifiers()
    {
        var b = new BridgeInfo("001788FFFE123456", "192.168.7.42", "Living Room Bridge", "1976154040", "1.65.0", "BSB002");
        var d = new Diagnostics(b, true, 12, 5, 3, Array.Empty<EntertainmentArea>());
        var r = d.Report(true);
        Assert.DoesNotContain("192.168.7.42", r); Assert.DoesNotContain("123456", r); Assert.Contains("192.168.x.x", r);
        Assert.Contains("192.168.7.42", d.Report(false));
    }
}

public class DemoTests
{
    [Fact] public void ScenesAreFiniteAndInRange()
    {
        for (int s = 0; s < DemoScene.Count; s++)
            for (float t = 0; t < 20; t += 3.7f)
            {
                var st = DemoScene.Stats(s, new ZoneRect(0, 0, 1, 1), t);
                foreach (var v in new[] { st.Average.R, st.Average.G, st.Average.B, st.Dominant.R, st.Brightness, st.Saturation, st.Dominance })
                { Assert.True(float.IsFinite(v)); Assert.InRange(v, 0f, 1.0001f); }
            }
    }
    [Fact] public void ScenesDiffer()
    {
        var a = DemoScene.Stats(0, new ZoneRect(0, 0, 1, 1), 1).Average; var b = DemoScene.Stats(1, new ZoneRect(0, 0, 1, 1), 1).Average;
        Assert.True(Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) > 0.1f);
    }
    [Fact] public void SunsetSkyIsWarmAndSeaIsCool()
    {
        var sky = DemoScene.Stats(0, new ZoneRect(0, 0, 0.34f, 0.34f), 0).Average;
        var sea = DemoScene.Stats(0, new ZoneRect(0, 0.66f, 0.34f, 1), 0).Average;
        Assert.True(sky.R > sky.B); Assert.True(sea.B > sea.R);
    }
    [Fact] public void AudioBeatPulsesAtTempo()
    {
        const float period = 60f / 112;
        Assert.True(DemoSignal.Audio(period * 3 + 0.001f).Beat > 0.9f);
        Assert.True(DemoSignal.Audio(period * 3.6f).Beat < 0.2f);
    }
    [Fact] public void DemoFeedsColorEngine()
    {
        var e = new ColorEngine(); var s = new SyncSettings { Smoothing = 0 };
        var st = DemoScene.Stats(1, new ZoneRect(0.33f, 0.33f, 0.67f, 0.67f), 2);
        RGB o = RGB.Black;
        for (int i = 0; i < 60; i++) o = e.Process(new[] { (0, st) }, 0.02f, s)[0];
        Assert.True(Math.Max(o.R, Math.Max(o.G, o.B)) > 0.1f);
    }
}

public class CoreTests
{
    [Fact] public void PacketLayout()
    {
        const string id = "12345678-1234-1234-1234-123456789abc";
        var d = StreamPacket.Build(id, 7, new (byte, RGB)[] { (0, new RGB(1, 0, 0.5f)), (3, new RGB(0, 1, 0)) })!;
        Assert.Equal(16 + 36 + 14, d.Length);
        Assert.Equal("HueStream", System.Text.Encoding.ASCII.GetString(d, 0, 9));
        Assert.Equal(new byte[] { 2, 0, 7, 0, 0, 0, 0 }, d[9..16]);
        Assert.Equal(id, System.Text.Encoding.ASCII.GetString(d, 16, 36));
        Assert.Equal(new byte[] { 0, 0xFF, 0xFF, 0, 0, 0x80, 0x00 }, d[52..59]);
        Assert.Equal(3, d[59]);
        Assert.Null(StreamPacket.Build("short", 0, Array.Empty<(byte, RGB)>()));
    }

    [Fact] public void ReduceSplitFrame()
    {
        int w = 20, h = 10;
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int i = (y * w + x) * 4;
            if (x < w / 2) px[i + 2] = 255; else px[i] = 255;   // BGRA: left red, right blue
            px[i + 3] = 255;
        }
        var zones = new[] { new ZoneRect(0, 0, 0.5f, 1), new ZoneRect(0.5f, 0, 1, 1), new ZoneRect(0, 0, 1, 1) };
        var raw = ZoneAccumulator.ReduceBgra(px, w, h, w * 4, zones);
        var l = ZoneAccumulator.Stats(raw, 0); var r = ZoneAccumulator.Stats(raw, 1); var all = ZoneAccumulator.Stats(raw, 2);
        Assert.Equal(new RGB(1, 0, 0), l.Average); Assert.InRange(r.Dominant.B, 0.9999f, 1.0001f);
        Assert.InRange(all.Average.R, 0.4999f, 0.5001f); Assert.InRange(all.Average.B, 0.4999f, 0.5001f);
        Assert.InRange(l.Dominance, 0.9999f, 1.0001f);
    }

    [Fact] public void BlackFrameIsNotDominant()
    {
        var raw = ZoneAccumulator.ReduceBgra(new byte[64], 4, 4, 16, new[] { new ZoneRect(0, 0, 1, 1) });
        Assert.Equal(0, ZoneAccumulator.Stats(raw, 0).Dominance);
    }

    [Fact] public void SmootherRespectsSlewAndConverges()
    {
        var e = new ColorEngine();
        var s = new SyncSettings { DynamicBrightness = false, Gamma = 1, Smoothing = 0.5f };
        var white = new ZoneStats(new RGB(1, 1, 1), new RGB(1, 1, 1), 0, 1, 0);
        const float dt = 0.02f;
        float prev = 0;
        for (int i = 0; i < 5; i++)
        {
            float v = e.Process(new[] { (0, white) }, dt, s)[0].R;
            Assert.True(v - prev <= (1.5f + 0.5f * 6.5f) * dt + 1e-5f);
            prev = v;
        }
        float last = 0;
        for (int i = 0; i < 300; i++) last = e.Process(new[] { (0, white) }, dt, s)[0].R;
        Assert.InRange(last, 0.99f, 1.01f);
    }

    [Fact] public void NightModeCapsOutput()
    {
        var e = new ColorEngine(); var s = new SyncSettings { NightMode = true, Smoothing = 0 };
        var white = new ZoneStats(new RGB(1, 1, 1), new RGB(1, 1, 1), 0, 1, 0);
        RGB o = RGB.Black;
        for (int i = 0; i < 200; i++) o = e.Process(new[] { (0, white) }, 0.02f, s)[0];
        Assert.True(o.R <= 0.3f + 1e-4f); Assert.True(o.B < o.R);
    }

    [Fact] public void Xy()
    {
        var red = ColorMath.ToXY(new RGB(1, 0, 0), null);
        Assert.InRange(red.X, 0.6815, 0.7015); Assert.InRange(red.Y, 0.2983, 0.3183);
        var white = ColorMath.ToXY(new RGB(1, 1, 1), null);
        Assert.InRange(white.X, 0.3127, 0.3327);
        var c = new Gamut(new XY(0.675, 0.322), new XY(0.409, 0.518), new XY(0.167, 0.04));
        Assert.True(ColorMath.ToXY(new RGB(0, 0, 1), c).Y >= 0.03);
    }

    [Fact] public void Layout()
    {
        Assert.Equal(new ZoneRect(0, 0, 0.34f, 0.34f), ZoneLayout.Rect(0.05, 0.05));
        Assert.Equal(new ZoneRect(0.33f, 0.33f, 0.67f, 0.67f), ZoneLayout.Rect(0.5, 0.5));
        var p = ZoneLayout.DefaultRoutes(new[] { new EntertainmentChannel(0, -1, 0, 1) });
        Assert.Equal(0, p[0].U); Assert.Equal(0, p[0].V);
    }
}

public class RoutingTests
{
    const string Lg = "display:LG", Mac = "display:MAC", Ody = "display:ODY";
    static DisplayGeometry Disp(string key, string name, double x, double w = 1920, double h = 1080, bool builtIn = false, bool connected = true) =>
        new() { Key = key, Name = name, PixelWidth = (int)w, PixelHeight = (int)h, X = x, Y = 0, Width = w, Height = h, RefreshHz = 60, IsBuiltIn = builtIn, IsConnected = connected };
    static List<DisplayGeometry> Layout() => new()
    {
        Disp("MAC", "MacBook Pro Display", 0, 1512, 982, true), Disp("LG", "LG UltraFine 5K", 1512, 2560, 1440), Disp("ODY", "Samsung Odyssey G9", 4072, 5120, 1440),
    };
    static Profile Prof(SyncMode mode)
    {
        var p = new Profile { Name = "t", Mode = mode };
        p.Routes = new() { new() { ChannelId = 0, U = 0.1, V = 0.5 }, new() { ChannelId = 1, U = 0.9, V = 0.5 }, new() { ChannelId = 2, U = 0.5, V = 0.1 } };
        p.Settings.Audio.Influence = 0.2f;
        return p;
    }
    static readonly HashSet<string> NoWindows = new();

    [Fact] public void SingleUsesOnlyChosenDisplay()
    {
        var p = Prof(SyncMode.Single); p.PrimarySource = Lg;
        var plan = RoutePlanner.Plan(p, new[] { 0, 1, 2 }, Layout(), NoWindows);
        Assert.Equal(new[] { Lg }, plan.PhysicalSources);
        Assert.Equal(3, plan.Samples.Count); Assert.Empty(plan.Substitutions);
    }
    [Fact] public void ExtendAssignsPerLightAndSkipsUnassigned()
    {
        var p = Prof(SyncMode.Extend); p.Routes[0].SourceId = Mac; p.Routes[1].SourceId = Ody;
        var plan = RoutePlanner.Plan(p, new[] { 0, 1, 2 }, Layout(), NoWindows);
        Assert.True(plan.PhysicalSources.SetEquals(new[] { Mac, Ody }));
        Assert.False(plan.Samples.ContainsKey(2));
    }
    [Fact] public void MirrorAveragesSources()
    {
        var p = Prof(SyncMode.Mirror); p.MirrorSources = new() { Mac, Lg };
        var plan = RoutePlanner.Plan(p, new[] { 0 }, Layout(), NoWindows);
        Assert.Equal(2, plan.Samples[0].Count);
        Assert.True(plan.PhysicalSources.SetEquals(new[] { Mac, Lg }));
        var c = ZoneStats.Combine(new[] { new ZoneStats(new RGB(1, 0, 0), new RGB(1, 0, 0), 1, 0.2f, 1), new ZoneStats(new RGB(0, 0, 1), new RGB(0, 0, 1), 0, 0.1f, 1) });
        Assert.Equal(new RGB(0.5f, 0, 0.5f), c.Average);
    }
    [Fact] public void HotPlugFallsBackAndRestoresWithoutRewritingMappings()
    {
        var p = Prof(SyncMode.Single); p.PrimarySource = Lg;
        var before = p.Clone();
        var d = Layout(); d[1].IsConnected = false;
        var gone = RoutePlanner.Plan(p, new[] { 0, 1, 2 }, d, NoWindows);
        Assert.True(gone.PhysicalSources.SetEquals(new[] { Mac }));
        Assert.Equal(new[] { new Substitution(Lg, Mac) }, gone.Substitutions);
        var back = RoutePlanner.Plan(p, new[] { 0, 1, 2 }, Layout(), NoWindows);
        Assert.True(back.PhysicalSources.SetEquals(new[] { Lg })); Assert.Empty(back.Substitutions);
        Assert.True(p.SameAs(before));
    }
    [Fact] public void ExplicitFallbackAndMissingWindow()
    {
        var p = Prof(SyncMode.Single); p.PrimarySource = SourceID.Window("com.apple.Safari", "Movie"); p.FallbackSource = Ody;
        var plan = RoutePlanner.Plan(p, new[] { 0 }, Layout(), NoWindows);
        Assert.True(plan.PhysicalSources.SetEquals(new[] { Ody }));
        var ok = RoutePlanner.Plan(p, new[] { 0 }, Layout(), new HashSet<string> { p.PrimarySource });
        Assert.True(ok.PhysicalSources.SetEquals(new[] { p.PrimarySource }));
    }
    [Fact] public void DesktopSpanCapturesOnlyDisplaysUnderLights()
    {
        var p = Prof(SyncMode.Extend);
        p.Routes = new() { new() { ChannelId = 0, U = 0.01, V = 0.5, SourceId = SourceID.Desktop }, new() { ChannelId = 1, U = 0.35, V = 0.5, SourceId = SourceID.Desktop } };
        var plan = RoutePlanner.Plan(p, new[] { 0, 1 }, Layout(), NoWindows);
        Assert.Equal(Mac, plan.Samples[0][0].Source);
        Assert.Equal(Lg, plan.Samples[1][0].Source);               // 35% across 9192 pt lands in the LG (1512..4072)
        Assert.True(plan.PhysicalSources.SetEquals(new[] { Mac, Lg }));
    }
    [Fact] public void AudioOnlyNeedsNoCapture()
    {
        var p = Prof(SyncMode.Single); p.PrimarySource = Lg; p.Settings.Audio.Influence = 1;
        Assert.Empty(RoutePlanner.Plan(p, new[] { 0, 1 }, Layout(), NoWindows).PhysicalSources);
    }
    [Fact] public void ProfileRoundTrip()
    {
        var p = Profile.Presets()[0]; p.Mode = SyncMode.Extend; p.Routes = new() { new() { ChannelId = 3, U = 0.2, V = 0.8, SourceId = Lg } };
        p.Settings.Audio.Input = AudioInput.ForApp("com.apple.Music"); p.Settings.ChannelGain[3] = 0.5f;
        var back = Json.Read<Profile>(Json.Write(p))!;
        Assert.True(back.SameAs(p));
        Assert.Equal("com.apple.Music", back.Settings.Audio.Input.App); Assert.Equal(0.5f, back.Settings.ChannelGain[3]);
    }
}

public class AudioTests
{
    static float[] Render(float seconds, Func<float, float> f, float sr = 48000) { var a = new float[(int)(seconds * sr)]; for (int i = 0; i < a.Length; i++) a[i] = f(i / sr); return a; }
    static void Pump(AudioAnalyzer a, float[] x) { for (int i = 0; i < x.Length; i += 480) a.Feed(x.AsSpan(i, Math.Min(480, x.Length - i))); }
    static float[] Tone(float hz, float amp = 0.5f) => Render(4, t => amp * MathF.Sin(2 * MathF.PI * hz * t));

    [Fact] public void Bands()
    {
        var b = new AudioAnalyzer(); Pump(b, Tone(80)); Assert.True(b.Features.Bass > 0.8f); Assert.True(b.Features.Treble < 0.1f);
        var t = new AudioAnalyzer(); Pump(t, Tone(8000)); Assert.True(t.Features.Treble > 0.8f); Assert.True(t.Features.Bass < 0.1f);
        Assert.InRange(b.Features.VolumeDB, 20 * MathF.Log10(0.5f / MathF.Sqrt(2)) - 1, 20 * MathF.Log10(0.5f / MathF.Sqrt(2)) + 1);
    }
    [Fact] public void SilenceIsQuiet()
    {
        var a = new AudioAnalyzer(); Pump(a, new float[48000]);
        Assert.Equal(0, a.Features.Bass); Assert.Equal(0, a.Features.Beat); Assert.Equal(0, a.Features.Tempo);
    }
    static float[] KickTrack(float bpm, float seconds)
    {
        float period = 60 / bpm;
        return Render(seconds, t => { float ph = t % period; return 0.8f * MathF.Exp(-ph / 0.07f) * MathF.Sin(2 * MathF.PI * 60 * ph) + 0.05f * MathF.Sin(2 * MathF.PI * 440 * t); });
    }
    [Fact] public void BeatsAndTempo()
    {
        var a = new AudioAnalyzer(); var x = KickTrack(120, 12);
        int beats = 0; float last = 0;
        for (int i = 0; i < x.Length; i += 480)
        {
            a.Feed(x.AsSpan(i, Math.Min(480, x.Length - i)));
            if (a.Features.Beat > 0.5f && last <= 0.5f) beats++;
            last = a.Features.Beat;
        }
        Assert.InRange(beats, 18, 26);
        Assert.InRange(a.Features.Tempo, 116, 124);
        Assert.True(a.Features.TempoConfidence > 0.3f);
    }
    static float[] SpeechLike(float seconds) => Render(seconds, t =>
    {
        float word = t % 1.6f;
        if (word > 1.15f) return 0;
        float syl = MathF.Max(0, MathF.Sin(2 * MathF.PI * 4 * t));
        float s = 0; for (int k = 1; k <= 20; k++) s += MathF.Sin(2 * MathF.PI * 120 * k * t) / k;
        return 0.15f * syl * s;
    });
    static float[] MusicLike(float seconds)
    {
        uint seed = 1;
        return Render(seconds, t =>
        {
            float ph = t % 0.5f, hat = t % 0.25f;
            seed = unchecked(seed * 1664525 + 1013904223);
            float noise = (seed >> 8) / (float)(1 << 24) - 0.5f;
            float chord = 0; foreach (var f in new[] { 220f, 277f, 330f, 440f }) chord += 0.08f * MathF.Sin(2 * MathF.PI * f * t);
            return chord + 0.7f * MathF.Exp(-ph / 0.08f) * MathF.Sin(2 * MathF.PI * 60 * ph) + 0.3f * MathF.Exp(-hat / 0.02f) * noise;
        });
    }
    [Fact] public void SpeechVersusMusic()
    {
        var s = new AudioAnalyzer(); Pump(s, SpeechLike(14));
        var m = new AudioAnalyzer(); Pump(m, MusicLike(14));
        Assert.True(s.Features.Speech > 0.55f, $"speech-like {s.Features.Speech}");
        Assert.True(m.Features.Speech < 0.45f, $"music-like {m.Features.Speech}");
    }
    [Fact] public void MixerBypassAndCeiling()
    {
        var mixer = new AudioMixer();
        var s = new SyncSettings(); s.Audio.Influence = 0;
        var screen = new Dictionary<int, RGB> { [0] = new RGB(0.3f, 0.2f, 0.1f) };
        var f = new AudioFeatures { Bass = 1, Beat = 1 };
        Assert.Same(screen, mixer.Mix(screen, new[] { 0 }, f, 0.02f, s));
        s.Audio.Influence = 0.9f; s.BrightnessLimit = 0.5f;
        for (int i = 0; i < 50; i++) { var o = mixer.Mix(screen, new[] { 0 }, f, 0.02f, s)[0]; Assert.True(Math.Max(o.R, Math.Max(o.G, o.B)) <= 0.5001f); }
    }
    [Fact] public void BassPulsesBrightenAndSpeechCalmsIt()
    {
        var s = new SyncSettings(); s.Audio.Influence = 0.3f; s.Audio.Smoothing = 0;
        var screen = new Dictionary<int, RGB> { [0] = new RGB(0.3f, 0.2f, 0.1f), [1] = new RGB(0.1f, 0.1f, 0.3f) };
        var f = new AudioFeatures { Bass = 1, Beat = 1, BassImpulse = 1 };
        var music = new AudioMixer().Mix(screen, new[] { 0, 1 }, f, 0.02f, s);
        var talk = new AudioMixer().Mix(screen, new[] { 0, 1 }, f with { Speech = 1 }, 0.02f, s);
        static float Lum(Dictionary<int, RGB> d) => d.Values.Sum(c => c.Luma);
        Assert.True(Lum(music) > Lum(screen)); Assert.True(Lum(music) > Lum(talk));
        Assert.True(Lum(talk) >= Lum(screen) - 0.001f);
        Assert.True(music[1].Luma > screen[1].Luma);
    }
    [Fact] public void AnalyzerSpeed()
    {
        var a = new AudioAnalyzer(); var x = MusicLike(10);
        var sw = System.Diagnostics.Stopwatch.StartNew(); Pump(a, x); sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 2500, $"{sw.ElapsedMilliseconds} ms");
    }
}
