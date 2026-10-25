using System.Text.Json.Serialization;

namespace Lumivane.Core;

public enum SyncMode
{
    /// <summary>One source drives every selected light.</summary>
    Single,
    /// <summary>Several sources are analysed together; each light follows the average of them.</summary>
    Mirror,
    /// <summary>Each light follows its own assigned source.</summary>
    Extend,
}

/// <summary>Stable source identifiers. Displays use a stable key so mappings survive reconnects and reboots.</summary>
public static class SourceID
{
    public const string Desktop = "desktop";
    public static string Display(string key) => $"display:{key}";
    public static string Window(string app, string title) => $"window:{app}|{title}";
    public static string? DisplayKey(string id) => id.StartsWith("display:", StringComparison.Ordinal) ? id[8..] : null;
    public static bool IsWindow(string id) => id.StartsWith("window:", StringComparison.Ordinal);
}

public sealed class DisplayGeometry
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }
    /// <summary>Global desktop frame in points, origin top-left of the main display.</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double RefreshHz { get; set; }
    public bool IsBuiltIn { get; set; }
    public bool IsConnected { get; set; } = true;
    [JsonIgnore] public string Id => SourceID.Display(Key);
    [JsonIgnore] public bool IsUltrawide => PixelHeight > 0 && (double)PixelWidth / PixelHeight >= 2.0;
    [JsonIgnore] public string Resolution => $"{PixelWidth}×{PixelHeight}";
}

public sealed record WindowSource(string Id, string Title, string AppName);

public sealed class LightRoute
{
    public int ChannelId { get; set; }
    /// <summary>Position on the assigned source, 0..1 (u: left to right, v: top to bottom).</summary>
    public double U { get; set; }
    public double V { get; set; }
    /// <summary>Extend mode: the source this light follows. null = not assigned.</summary>
    public string? SourceId { get; set; }
    public bool Enabled { get; set; } = true;
}

public sealed class Profile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Symbol { get; set; } = "sparkles.tv";
    public string? AreaId { get; set; }
    public SyncMode Mode { get; set; } = SyncMode.Single;
    public string? PrimarySource { get; set; }
    public List<string> MirrorSources { get; set; } = new();
    public string? FallbackSource { get; set; }
    public List<WindowSource> WindowSources { get; set; } = new();
    public List<LightRoute> Routes { get; set; } = new();
    public SyncSettings Settings { get; set; } = new();

    /// <summary>Adds default routes for channels that have none. Existing routes (including stale ones) are kept.</summary>
    public void EnsureRoutes(IEnumerable<EntertainmentChannel> channels)
    {
        foreach (var r in ZoneLayout.DefaultRoutes(channels))
            if (!Routes.Any(x => x.ChannelId == r.ChannelId)) Routes.Add(r);
    }

    public Profile Clone() => Json.Clone(this);
    public bool SameAs(Profile o) => Json.Write(this) == Json.Write(o);

    public static List<Profile> Presets()
    {
        var movie = new SyncSettings { Style = ColorStyle.Cinematic, Smoothing = 0.7f, Dominance = 0.6f };
        movie.Audio.Influence = 0.15f; movie.Audio.MusicStrength = 0.4f; movie.Audio.Smoothing = 0.6f;
        var music = new SyncSettings { Style = ColorStyle.Enhanced, Smoothing = 0.3f, Dominance = 0.3f };
        music.Audio.Influence = 0.85f; music.Audio.MusicStrength = 0.8f; music.Audio.BeatIntensity = 1.2f; music.Audio.Smoothing = 0.35f;
        var gaming = new SyncSettings { Style = ColorStyle.Gaming, Smoothing = 0.15f, Fps = 60, DynamicBrightness = false };
        gaming.Audio.Influence = 0.3f; gaming.Audio.BeatIntensity = 1; gaming.Audio.Smoothing = 0.4f;
        var ambient = new SyncSettings { Style = ColorStyle.Enhanced, Smoothing = 0.9f, Brightness = 0.4f, LowLightMode = true, Intensity = 0.9f, Dominance = 0.7f };
        ambient.Audio.Influence = 0.2f; ambient.Audio.BassSensitivity = 0.7f; ambient.Audio.BeatIntensity = 0.5f; ambient.Audio.Smoothing = 0.85f; ambient.Audio.MusicStrength = 0.3f;
        return new()
        {
            new Profile { Name = "Movie Mode", Symbol = "film", Settings = movie },
            new Profile { Name = "Music Mode", Symbol = "music.note", Settings = music },
            new Profile { Name = "Gaming Mode", Symbol = "gamecontroller", Settings = gaming },
            new Profile { Name = "Relaxed Ambient", Symbol = "leaf", Settings = ambient },
        };
    }
}

// ---- Route planning ----

public readonly record struct SamplePoint(string Source, ZoneRect Rect);
public readonly record struct Substitution(string Requested, string Actual);

public sealed class RoutePlan
{
    /// <summary>Per driven light channel: the screen areas it averages. Lights absent here are left untouched.</summary>
    public Dictionary<int, List<SamplePoint>> Samples { get; } = new();
    public List<Substitution> Substitutions { get; } = new();
    /// <summary>Requested sources that are gone and had no replacement.</summary>
    public List<string> Unavailable { get; } = new();
    /// <summary>Only these sources need a capture session.</summary>
    public HashSet<string> PhysicalSources => Samples.Values.SelectMany(v => v.Select(p => p.Source)).ToHashSet();
}

public static class RoutePlanner
{
    /// <summary>Pure function of the (preserved) profile and what is connected right now.
    /// Mappings are never rewritten: when a display returns, the same profile resolves back to it.</summary>
    public static RoutePlan Plan(Profile p, IReadOnlyList<int> channelIds, IReadOnlyList<DisplayGeometry> displays, ISet<string> windows)
    {
        var plan = new RoutePlan();
        List<DisplayGeometry> live = displays.Where(d => d.IsConnected).ToList();
        if (p.Settings.Audio.Enabled && p.Settings.Audio.Influence >= 0.99f) return plan;   // audio only, no capture needed

        bool Available(string id)
        {
            if (id == SourceID.Desktop) return live.Count > 0;
            if (SourceID.DisplayKey(id) is { } k) return live.Any(d => d.Key == k);
            return windows.Contains(id);
        }
        string? Fallback()
        {
            if (p.FallbackSource is { } f && Available(f)) return f;
            var b = live.FirstOrDefault(d => d.IsBuiltIn);
            if (b != null) return SourceID.Display(b.Key);
            var m = live.FirstOrDefault(d => d.X == 0 && d.Y == 0) ?? live.FirstOrDefault();
            return m != null ? SourceID.Display(m.Key) : null;
        }
        var cache = new Dictionary<string, string?>();
        string? Resolve(string? requested)
        {
            if (requested == null) return Fallback();
            if (cache.TryGetValue(requested, out var c)) return c;
            string? o;
            if (Available(requested)) o = requested;
            else if (Fallback() is { } alt) { o = alt; plan.Substitutions.Add(new Substitution(requested, alt)); }
            else { o = null; plan.Unavailable.Add(requested); }
            cache[requested] = o;
            return o;
        }
        SamplePoint? Point(string src, double u, double v)
        {
            if (src == SourceID.Desktop)
            {
                var dp = DesktopPoint(u, v, live);
                if (dp is not { } d) return null;
                return new SamplePoint(SourceID.Display(d.Key), ZoneLayout.Rect(d.U, d.V));
            }
            return new SamplePoint(src, ZoneLayout.Rect(u, v));
        }

        var routes = new Dictionary<int, LightRoute>();
        foreach (var r in p.Routes) routes.TryAdd(r.ChannelId, r);
        foreach (var ch in channelIds)
        {
            var r = routes.TryGetValue(ch, out var rr) ? rr : new LightRoute { ChannelId = ch, U = 0.5, V = 0.5 };
            if (!r.Enabled) continue;
            var srcs = new List<string>();
            switch (p.Mode)
            {
                case SyncMode.Single:
                    if (Resolve(p.PrimarySource) is { } s1) srcs.Add(s1);
                    break;
                case SyncMode.Mirror:
                    var requested = p.MirrorSources.Count == 0 ? new List<string?> { p.PrimarySource } : p.MirrorSources.Select(x => (string?)x).ToList();
                    foreach (var q in requested) if (Resolve(q) is { } s && !srcs.Contains(s)) srcs.Add(s);
                    break;
                case SyncMode.Extend:
                    if (r.SourceId == null) continue;
                    if (Resolve(r.SourceId) is { } s2) srcs.Add(s2);
                    break;
            }
            var pts = srcs.Select(s => Point(s, r.U, r.V)).Where(x => x != null).Select(x => x!.Value).ToList();
            if (pts.Count > 0) plan.Samples[ch] = pts;
        }
        return plan;
    }

    /// <summary>Maps a point on the whole-desktop bounding box to the display under it (nearest display across gaps).</summary>
    public static (string Key, double U, double V)? DesktopPoint(double u, double v, IReadOnlyList<DisplayGeometry> displays)
    {
        if (displays.Count == 0) return null;
        double minX = displays.Min(d => d.X), maxX = displays.Max(d => d.X + d.Width);
        double minY = displays.Min(d => d.Y), maxY = displays.Max(d => d.Y + d.Height);
        double gx = minX + u * (maxX - minX), gy = minY + v * (maxY - minY);
        DisplayGeometry? best = null; double bestDist = 0;
        foreach (var d in displays)
        {
            double cx = Math.Min(Math.Max(gx, d.X), d.X + d.Width), cy = Math.Min(Math.Max(gy, d.Y), d.Y + d.Height);
            double dist = Math.Sqrt((gx - cx) * (gx - cx) + (gy - cy) * (gy - cy));
            if (best == null || dist < bestDist) { best = d; bestDist = dist; }
        }
        var b = best!;
        return (b.Key, Math.Min(Math.Max((gx - b.X) / b.Width, 0), 1), Math.Min(Math.Max((gy - b.Y) / b.Height, 0), 1));
    }
}
