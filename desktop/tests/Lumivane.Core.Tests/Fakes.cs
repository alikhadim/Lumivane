using Lumivane.Core;

namespace Lumivane.Core.Tests;

public sealed class FakeCapturer : IScreenCapturer
{
    public Action<string>? OnStop { get; set; }
    public Func<Frame> Make = () => Solid(0, 0, 0);
    Timer? timer;
    public bool Started, Stopped;

    public static Frame Solid(byte r, byte g, byte b, int w = 64, int h = 36)
    {
        var px = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++) { px[i * 4] = b; px[i * 4 + 1] = g; px[i * 4 + 2] = r; px[i * 4 + 3] = 255; }
        return new Frame { Bgra = px, Width = w, Height = h, Stride = w * 4 };
    }
    /// <summary>Left half red, right half blue.</summary>
    public static Frame Split(int w = 64, int h = 36)
    {
        var f = Solid(0, 0, 0, w, h);
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { int i = (y * w + x) * 4; if (x < w / 2) f.Bgra[i + 2] = 255; else f.Bgra[i] = 255; }
        return f;
    }

    public Task StartAsync(CaptureTarget target, int fps, Action<Frame, double> onFrame)
    {
        Started = true;
        timer = new Timer(_ => onFrame(Make(), 1.0), null, 0, Math.Max(10, 1000 / fps));
        return Task.CompletedTask;
    }
    public Task StopAsync() { Stopped = true; timer?.Dispose(); return Task.CompletedTask; }
    public void Dispose() => timer?.Dispose();
}

public sealed class FakeAudio : IAudioCapturer
{
    public Action<string>? OnStop { get; set; }
    public AudioInput? Started; public bool Stopped;
    public Task StartAsync(AudioInput input, Action<float[], double, double> onSamples) { Started = input; return Task.CompletedTask; }
    public Task StopAsync() { Stopped = true; return Task.CompletedTask; }
    public void Dispose() { }
}

public sealed class FakePlatform : IPlatform
{
    public string Name => "Test";
    public string DataDir { get; } = Path.Combine(Path.GetTempPath(), "lumivane-test-" + Guid.NewGuid().ToString("N"));
    public List<LiveDisplay> DisplayList { get; set; } = new()
    {
        new(new DisplayGeometry { Key = "A", Name = "Left", PixelWidth = 1920, PixelHeight = 1080, X = 0, Y = 0, Width = 1920, Height = 1080, RefreshHz = 60, IsBuiltIn = true }, 1),
        new(new DisplayGeometry { Key = "B", Name = "Right", PixelWidth = 1920, PixelHeight = 1080, X = 1920, Y = 0, Width = 1920, Height = 1080, RefreshHz = 60 }, 2),
    };
    public List<FakeCapturer> Capturers { get; } = new();
    public Func<FakeCapturer>? CapturerFactory;
    public FakeAudio Audio { get; } = new();
    public List<(string, string)> Notifications { get; } = new();
    public IReadOnlyList<LiveDisplay> Displays() => DisplayList;
    public Task<IReadOnlyList<LiveWindow>> WindowsAsync() => Task.FromResult<IReadOnlyList<LiveWindow>>(Array.Empty<LiveWindow>());
    public Task<IReadOnlyList<AudioApp>> AudioAppsAsync() => Task.FromResult<IReadOnlyList<AudioApp>>(Array.Empty<AudioApp>());
    public IScreenCapturer CreateScreenCapturer() { var c = CapturerFactory?.Invoke() ?? new FakeCapturer(); Capturers.Add(c); return c; }
    public IAudioCapturer CreateAudioCapturer() => Audio;
    public Task<Dictionary<CaptureTarget, Frame>> ThumbnailsAsync(IReadOnlyList<CaptureTarget> targets, int width) => Task.FromResult(new Dictionary<CaptureTarget, Frame>());
    public bool ScreenAllowed { get; set; } = true;
    public string? ScreenAccessHint => "allow it";
    public bool LaunchAtLogin { get; set; }
    public void Notify(string title, string body) => Notifications.Add((title, body));
    public ISecretStore Secrets { get; } = new MemorySecretStore();
#pragma warning disable CS0067
    public event Action? DisplaysChanged;
    public void RaiseDisplaysChanged() => DisplaysChanged?.Invoke();
}

public sealed class RecordingSink : IFrameSink
{
    public List<(byte Channel, RGB Rgb)[]> Frames { get; } = new();
    public void Send(IReadOnlyList<(byte Channel, RGB Rgb)> colors) { lock (Frames) Frames.Add(colors.ToArray()); }
    public (byte Channel, RGB Rgb)[]? Last { get { lock (Frames) return Frames.Count > 0 ? Frames[^1] : null; } }
    public int Count { get { lock (Frames) return Frames.Count; } }
    public void Dispose() { }
}

public sealed class FakeBridge : IHueBridge
{
    public BridgeInfo Bridge { get; } = new("AABBCCDDEEFF0011", "10.0.0.2", "Fake", "1976154040", "1.65.0", "BSB002");
    public List<string> Calls { get; } = new();
    public List<HueLight> LightList { get; set; } = new()
    {
        new("l1", "Bar Left", "hue_play", true, 80, null, "e1"), new("l2", "Bar Right", "hue_play", true, 80, null, "e2"), new("l3", "Bedside", "table_shade", false, 40, null, null),
    };
    public List<EntertainmentArea> AreaList { get; set; } = new()
    {
        new("11111111-1111-1111-1111-111111111111", "TV", "screen", false, new[]
        {
            new EntertainmentChannel(0, -0.9, 0.8, 0, new[] { "e1" }), new EntertainmentChannel(1, 0.9, 0.8, 0, new[] { "e2" }),
        }),
    };
    public Task<(BridgeInfo Info, double LatencyMs)> ConfigAsync(CancellationToken ct = default) => Task.FromResult((Bridge, 3.0));
    public Task<IReadOnlyList<HueLight>> LightsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HueLight>>(LightList);
    public Task<IReadOnlyList<HueGroup>> GroupsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HueGroup>>(new[] { new HueGroup("g1", "Living", GroupKind.Room, null, new[] { "l1", "l2" }, "gl1") });
    public Task<IReadOnlyList<HueScene>> ScenesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<HueScene>>(Array.Empty<HueScene>());
    public Task<IReadOnlyList<EntertainmentArea>> AreasAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<EntertainmentArea>>(AreaList);
    public Task<Diagnostics> DiagnosticsAsync(CancellationToken ct = default) => Task.FromResult(new Diagnostics(Bridge, true, 3, LightList.Count, 2, AreaList));
    public Task<string> ApplicationIdAsync(CancellationToken ct = default) => Task.FromResult("app-id");
    public Task SetLightAsync(string id, bool? on = null, double? brightness = null, RGB? rgb = null, Gamut? gamut = null, int? durationMs = null, CancellationToken ct = default) { lock (Calls) Calls.Add($"light {id} {on} {brightness:F0}"); return Task.CompletedTask; }
    public Task SetGroupAsync(string g, bool? on = null, double? brightness = null, CancellationToken ct = default) { lock (Calls) Calls.Add($"group {g} {on} {brightness}"); return Task.CompletedTask; }
    public Task RecallAsync(string id, CancellationToken ct = default) { lock (Calls) Calls.Add($"recall {id}"); return Task.CompletedTask; }
    public Task CreateZoneAsync(string name, IReadOnlyList<string> ids, CancellationToken ct = default) { lock (Calls) Calls.Add($"createZone {name}"); return Task.CompletedTask; }
    public Task UpdateZoneAsync(string id, string? name = null, IReadOnlyList<string>? ids = null, CancellationToken ct = default) { lock (Calls) Calls.Add($"updateZone {id}"); return Task.CompletedTask; }
    public Task DeleteZoneAsync(string id, CancellationToken ct = default) { lock (Calls) Calls.Add($"deleteZone {id}"); return Task.CompletedTask; }
    public Task SetStreamingAsync(string id, bool active, CancellationToken ct = default) { lock (Calls) Calls.Add($"streaming {active}"); return Task.CompletedTask; }
    public Task CreateAreaAsync(string name, IReadOnlyList<HueLight> lights, CancellationToken ct = default)
    {
        lock (Calls) Calls.Add($"createArea {name} {lights.Count}");
        AreaList.Add(new EntertainmentArea(Guid.NewGuid().ToString(), name, "screen", false, lights.Select((l, i) => new EntertainmentChannel(i, 0, 0.8, 0, new[] { l.EntertainmentServiceId! })).ToList()));
        return Task.CompletedTask;
    }
    public Task UpdateAreaAsync(string id, string name, IReadOnlyList<HueLight> lights, CancellationToken ct = default) { lock (Calls) Calls.Add($"updateArea {id} {lights.Count}"); return Task.CompletedTask; }
}

public static class Wait
{
    public static async Task Until(Func<bool> cond, int ms = 5000)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (!cond()) { if (DateTime.UtcNow > end) throw new TimeoutException("condition not met"); await Task.Delay(10); }
    }
}
