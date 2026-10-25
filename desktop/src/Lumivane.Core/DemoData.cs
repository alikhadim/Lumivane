namespace Lumivane.Core;

/// <summary>Synthetic content for Demo Mode. Nothing here touches a device, the network, the screen or the secret store.</summary>
public static class DemoData
{
    public static readonly BridgeInfo Bridge = new("DEMO0000000000", "192.168.0.2", "Demo Bridge", "demo", "demo", "Demo");
    public static readonly Credentials Credentials = new("demo", "demo");

    static HueLight L(int n, string name, string arch, bool ent = true, bool on = true, double bri = 80) =>
        new($"demo-light-{n}", name, arch, on, bri, null, ent ? $"demo-ent-{n}" : null);

    public static readonly IReadOnlyList<HueLight> Lights = new[]
    {
        L(1, "Bar Left", "hue_play"), L(2, "Bar Right", "hue_play"), L(3, "Floor Lamp", "floor_shade"),
        L(4, "Light Strip", "hue_lightstrip"), L(5, "Corner Lamp", "table_shade"), L(6, "Desk Lamp", "desk_lamp"),
        L(7, "Monitor Glow", "hue_lightstrip"), L(8, "Shelf Strip", "hue_lightstrip"),
        L(9, "Bedside Left", "table_shade", false, false, 40), L(10, "Bedside Right", "table_shade", false, false, 40),
    };

    public static List<HueGroup> Groups() => new()
    {
        new("demo-room-living", "Living Room", GroupKind.Room, "living_room", new[] { "demo-light-1", "demo-light-2", "demo-light-3", "demo-light-4", "demo-light-5" }, "demo-gl-1"),
        new("demo-room-office", "Office", GroupKind.Room, "office", new[] { "demo-light-6", "demo-light-7", "demo-light-8" }, "demo-gl-2"),
        new("demo-room-bed", "Bedroom", GroupKind.Room, "bedroom", new[] { "demo-light-9", "demo-light-10" }, "demo-gl-3"),
        new("demo-zone-cinema", "Cinema Corner", GroupKind.Zone, null, new[] { "demo-light-1", "demo-light-2", "demo-light-4", "demo-light-5" }, "demo-gl-4"),
    };

    public static List<HueScene> Scenes() => new()
    {
        new("demo-scene-1", "Relax", "demo-room-living", "room", false),
        new("demo-scene-2", "Evening Glow", "demo-room-living", "room", true),
        new("demo-scene-3", "Concentrate", "demo-room-office", "room", false),
        new("demo-scene-4", "Energize", "demo-room-office", "room", false),
        new("demo-scene-5", "Nightlight", "demo-room-bed", "room", false),
    };

    // x: -1 left ... 1 right, z: -1 floor ... 1 ceiling
    static readonly (double, double)[] Positions = { (-0.6, -0.2), (0.6, -0.2), (-0.95, 0.4), (0, -0.9), (0.95, 0.6), (-0.5, -0.7), (0.5, 0.8), (0, 0.95) };

    public static List<EntertainmentArea> Areas() => new()
    {
        new("demo-area-screen", "Screen Sync", "screen", false,
            Positions.Select((p, i) => new EntertainmentChannel(i, p.Item1, 0.8, p.Item2, new[] { $"demo-ent-{i + 1}" })).ToList()),
        new("demo-area-music", "Music Corner", "music", false,
            new[] { 0, 1, 3, 4 }.Select((src, i) => new EntertainmentChannel(i, Positions[src].Item1, 0.8, Positions[src].Item2, new[] { $"demo-ent-{src + 1}" })).ToList()),
    };

    public static Diagnostics Diagnostics(IReadOnlyList<EntertainmentArea> areas) =>
        new(Bridge, true, null, Lights.Count, Lights.Count(l => l.SupportsEntertainment), areas);

    static DisplayGeometry D(string key, string name, int pw, int ph, double x, double y, double w, double h, double hz, bool builtIn) =>
        new() { Key = key, Name = name, PixelWidth = pw, PixelHeight = ph, X = x, Y = y, Width = w, Height = h, RefreshHz = hz, IsBuiltIn = builtIn };

    public static readonly IReadOnlyList<LiveDisplay> LiveDisplays = new[]
    {
        new LiveDisplay(D("demo-display-1", "Built-in Display", 3024, 1964, 0, 0, 1512, 982, 120, true), 9001),
        new LiveDisplay(D("demo-display-2", "Desk Monitor", 3840, 2160, 1512, -98, 1920, 1080, 60, false), 9002),
        new LiveDisplay(D("demo-display-3", "Wall TV", 3840, 2160, 3432, -98, 1920, 1080, 60, false), 9003),
    };

    public static readonly IReadOnlyList<LiveWindow> Windows = new[]
    {
        new LiveWindow(SourceID.Window("demo.video", "Mountain Documentary"), "Mountain Documentary", "Video Player", "demo.video", 9101),
        new LiveWindow(SourceID.Window("demo.game", "Night Drive"), "Night Drive", "Game", "demo.game", 9102),
    };

    public static readonly IReadOnlyList<AudioApp> AudioApps = new[] { new AudioApp("demo.music", "Music Player"), new AudioApp("demo.video", "Video Player") };

    /// <summary>Scene shown on each virtual source. Stable across launches.</summary>
    public static int SceneFor(string id)
    {
        for (int i = 0; i < LiveDisplays.Count; i++) if (LiveDisplays[i].Geometry.Id == id) return i;
        for (int i = 0; i < Windows.Count; i++) if (Windows[i].Id == id) return 3 - i * 2;   // video -> ember, game -> neon
        return id.Select(c => (int)c).Aggregate(0, (a, b) => (a + b) % 4);
    }

    /// <summary>Movie profile with all three displays in use, so Extend mode looks populated.</summary>
    public static List<Profile> Profiles()
    {
        var all = Profile.Presets();
        var area = Areas()[0];
        var desk = LiveDisplays[1].Geometry.Id; var tv = LiveDisplays[2].Geometry.Id;
        foreach (var p in all) { p.AreaId = area.Id; p.EnsureRoutes(area.Channels); p.PrimarySource = desk; }
        all[0].Mode = SyncMode.Extend;
        foreach (var r in all[0].Routes) r.SourceId = new[] { 0, 1, 5, 6 }.Contains(r.ChannelId) ? desk : tv;
        return all;
    }

    /// <summary>One RGBA frame of a demo scene, for thumbnails.</summary>
    public static Frame Thumbnail(int scene, float t, double aspect, int w = 160)
    {
        int h = Math.Max(16, (int)(w * aspect));
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = DemoScene.Color(scene, (x + 0.5f) / w, (y + 0.5f) / h, t);
                int i = (y * w + x) * 4;
                px[i] = (byte)(Math.Clamp(c.B, 0, 1) * 255); px[i + 1] = (byte)(Math.Clamp(c.G, 0, 1) * 255); px[i + 2] = (byte)(Math.Clamp(c.R, 0, 1) * 255); px[i + 3] = 255;
            }
        return new Frame { Bgra = px, Width = w, Height = h, Stride = w * 4 };
    }
}
