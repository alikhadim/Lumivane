using Lumivane.Core;
using Xunit;

namespace Lumivane.Core.Tests;

public class EngineTests
{
    static SyncSettings Fast() { var s = new SyncSettings { Smoothing = 0, Fps = 60, DynamicBrightness = false }; s.Audio.Enabled = false; return s; }

    [Fact] public async Task StreamsScreenColorsToTheRightChannels()
    {
        var plat = new FakePlatform { CapturerFactory = () => new FakeCapturer { Make = () => FakeCapturer.Split() } };
        var bridge = new FakeBridge();
        var sink = new RecordingSink();
        using var eng = new SyncEngine(plat);
        var area = bridge.AreaList[0];
        await eng.StartAsync(new SyncEngine.Config { Bridge = bridge.Bridge, Creds = new Credentials("k", "00112233445566778899AABBCCDDEEFF"), Area = area, Lights = bridge.LightList, Settings = Fast(), Client = bridge, SinkFactory = _ => Task.FromResult<IFrameSink>(sink) });
        var prof = new Profile { Mode = SyncMode.Single, PrimarySource = "display:A", Settings = Fast() };
        prof.EnsureRoutes(area.Channels);
        var plan = RoutePlanner.Plan(prof, new[] { 0, 1 }, plat.DisplayList.Select(d => d.Geometry).ToList(), new HashSet<string>());
        await eng.ApplyAsync(plan, new Dictionary<string, CaptureTarget> { ["display:A"] = CaptureTarget.Display(1) }, new List<int>());
        await Wait.Until(() => sink.Count > 30);
        await eng.StopAsync();
        var last = sink.Last!;
        var ch0 = last.First(c => c.Channel == 0).Rgb; var ch1 = last.First(c => c.Channel == 1).Rgb;
        Assert.True(ch0.R > 0.8f && ch0.B < 0.2f, $"left light should be red, got {ch0}");
        Assert.True(ch1.B > 0.8f && ch1.R < 0.2f, $"right light should be blue, got {ch1}");
        Assert.Contains("streaming True", bridge.Calls); Assert.Contains("streaming False", bridge.Calls);
        Assert.True(plat.Capturers[0].Stopped);
    }

    [Fact] public async Task FallsBackToRestWhenStreamingFails()
    {
        var plat = new FakePlatform { CapturerFactory = () => new FakeCapturer { Make = () => FakeCapturer.Solid(255, 0, 0) } };
        var bridge = new FakeBridge();
        var errors = new List<string>();
        using var eng = new SyncEngine(plat) { OnError = e => { lock (errors) errors.Add(e); } };
        var area = bridge.AreaList[0];
        await eng.StartAsync(new SyncEngine.Config { Bridge = bridge.Bridge, Creds = new Credentials("k", "00112233445566778899AABBCCDDEEFF"), Area = area, Lights = bridge.LightList, Settings = Fast(), Client = bridge, SinkFactory = _ => throw HueException.StreamFailed("no dtls") });
        var prof = new Profile { Mode = SyncMode.Single, PrimarySource = "display:A", Settings = Fast() }; prof.EnsureRoutes(area.Channels);
        var plan = RoutePlanner.Plan(prof, new[] { 0, 1 }, plat.DisplayList.Select(d => d.Geometry).ToList(), new HashSet<string>());
        await eng.ApplyAsync(plan, new Dictionary<string, CaptureTarget> { ["display:A"] = CaptureTarget.Display(1) }, new List<int>());
        await Wait.Until(() => { lock (bridge.Calls) return bridge.Calls.Count(c => c.StartsWith("light ")) >= 4; });
        await eng.StopAsync();
        Assert.Contains(errors, e => e.Contains("basic mode"));
    }

    [Fact] public async Task OnlyNeededSourcesAreCapturedAndReleased()
    {
        var plat = new FakePlatform();
        var bridge = new FakeBridge(); var sink = new RecordingSink();
        using var eng = new SyncEngine(plat);
        var area = bridge.AreaList[0];
        await eng.StartAsync(new SyncEngine.Config { Bridge = bridge.Bridge, Creds = new Credentials("k", "00112233445566778899AABBCCDDEEFF"), Area = area, Lights = bridge.LightList, Settings = Fast(), Client = bridge, SinkFactory = _ => Task.FromResult<IFrameSink>(sink) });
        var geo = plat.DisplayList.Select(d => d.Geometry).ToList();
        var p = new Profile { Mode = SyncMode.Single, PrimarySource = "display:A", Settings = Fast() }; p.EnsureRoutes(area.Channels);
        var t = new Dictionary<string, CaptureTarget> { ["display:A"] = CaptureTarget.Display(1), ["display:B"] = CaptureTarget.Display(2) };
        await eng.ApplyAsync(RoutePlanner.Plan(p, new[] { 0, 1 }, geo, new HashSet<string>()), t, new List<int>());
        Assert.Single(plat.Capturers);
        p.PrimarySource = "display:B";
        await eng.ApplyAsync(RoutePlanner.Plan(p, new[] { 0, 1 }, geo, new HashSet<string>()), t, new List<int>());
        Assert.Equal(2, plat.Capturers.Count); Assert.True(plat.Capturers[0].Stopped);
        await eng.ApplyAsync(RoutePlanner.Plan(p, new[] { 0, 1 }, geo, new HashSet<string>()), t, new List<int>());   // unchanged: no new session
        Assert.Equal(2, plat.Capturers.Count);
        await eng.StopAsync();
    }

    [Fact] public async Task DemoModeNeedsNoNetworkOrCapture()
    {
        var plat = new FakePlatform();
        using var eng = new SyncEngine(plat);
        Dictionary<int, RGB>? seen = null; Metrics? met = null;
        eng.OnUpdate = (m, c) => { seen = c; met = m; };
        var area = DemoData.Areas()[0];
        await eng.StartAsync(new SyncEngine.Config { Bridge = DemoData.Bridge, Creds = DemoData.Credentials, Area = area, Lights = DemoData.Lights, Settings = Fast(), Demo = true });
        var prof = DemoData.Profiles()[0];
        var plan = RoutePlanner.Plan(prof, area.Channels.Select(c => c.Id).ToList(), DemoData.LiveDisplays.Select(d => d.Geometry).ToList(), new HashSet<string>());
        await eng.ApplyAsync(plan, new Dictionary<string, CaptureTarget>(), new List<int>());
        await Wait.Until(() => seen != null && seen.Count == area.Channels.Count && seen.Values.Any(c => c.Luma > 0.05f));
        Assert.Empty(plat.Capturers);
        Assert.Equal("Entertainment streaming (demo)", met!.Mode);
        await eng.StopAsync();
    }

    [Fact] public async Task AudioCaptureStartsAndStopsWithSettings()
    {
        var plat = new FakePlatform();
        var bridge = new FakeBridge(); var sink = new RecordingSink();
        using var eng = new SyncEngine(plat);
        await eng.StartAsync(new SyncEngine.Config { Bridge = bridge.Bridge, Creds = new Credentials("k", "00112233445566778899AABBCCDDEEFF"), Area = bridge.AreaList[0], Lights = bridge.LightList, Settings = Fast(), Client = bridge, SinkFactory = _ => Task.FromResult<IFrameSink>(sink) });
        var a = new AudioSettings { Enabled = true, Influence = 0.5f, Input = AudioInput.ForApp("x.y") };
        await eng.ApplyAudioAsync(a);
        Assert.Equal(AudioInput.ForApp("x.y"), plat.Audio.Started);
        await eng.ApplyAudioAsync(new AudioSettings { Enabled = false });
        Assert.True(plat.Audio.Stopped);
        await eng.StopAsync();
    }
}

public class AppModelTests
{
    static AppModel Make(FakePlatform plat, FakeBridge? bridge = null, LaunchOptions? launch = null) =>
        new(plat, (b, c) => bridge ?? new FakeBridge(), launch: launch);

    [Fact] public async Task DemoModeStartsAndStopsSync()
    {
        var plat = new FakePlatform();
        var m = Make(plat, launch: new LaunchOptions { Demo = true });
        Assert.True(m.Demo); Assert.True(m.Onboarded); Assert.True(m.HasSetup);
        Assert.Equal(3, m.Displays.Count); Assert.Equal(2, m.Areas.Count);
        await m.StartAsync();
        Assert.True(m.Syncing); Assert.Null(m.Error);
        await Wait.Until(() => m.Preview.Count == 8 && m.Preview.Values.Any(c => c.Luma > 0.05f));
        Assert.Equal(2, m.Metrics.CaptureSessions);       // movie profile: desk + TV
        await m.StopAsync();
        Assert.False(m.Syncing); Assert.Empty(m.Preview);
        Assert.False(File.Exists(Path.Combine(plat.DataDir, "profiles.json")));   // demo never persists
    }

    [Fact] public async Task BootLoadsBridgeDataAndPicksScreenArea()
    {
        var plat = new FakePlatform();
        plat.Secrets.Save("creds", Json.Write(new Credentials("key", "00112233445566778899AABBCCDDEEFF")));
        var st = new Store(plat.DataDir); st.Set("bridge", new BridgeInfo("AA", "10.0.0.2")); st.Set("onboarded", true);
        var m = Make(plat);
        await m.BootAsync();
        Assert.Equal(3, m.Lights.Count); Assert.Single(m.Areas);
        Assert.Equal(m.Areas[0].Id, m.Profile.AreaId);
        Assert.Equal(2, m.Profile.Routes.Count);
        Assert.NotNull(m.Diagnostics);
    }

    [Fact] public async Task ProfileEditsAutosaveAndReload()
    {
        var plat = new FakePlatform();
        var m = Make(plat);
        m.EditSettings(s => { s.Style = ColorStyle.Cinematic; s.Brightness = 0.7f; });
        m.SaveCurrentAsNew("Evening");
        var m2 = Make(plat);
        Assert.Contains(m2.Profiles, p => p.Name == "Evening");
        Assert.Equal("Evening", m2.Profile.Name);
        Assert.Equal(0.7f, m2.Settings.Brightness);
        await Task.CompletedTask;
    }

    [Fact] public void DeleteKeepsAtLeastOneProfile()
    {
        var m = Make(new FakePlatform());
        foreach (var p in m.Profiles.ToList()) m.Delete(p);
        Assert.Single(m.Profiles);
    }

    [Fact] public async Task DisplayUnplugFallsBackAndAnnounces()
    {
        var plat = new FakePlatform();
        plat.Secrets.Save("creds", Json.Write(new Credentials("key", "00112233445566778899AABBCCDDEEFF")));
        var st = new Store(plat.DataDir); st.Set("bridge", new BridgeInfo("AA", "10.0.0.2")); st.Set("onboarded", true);
        var bridge = new FakeBridge();
        var m = Make(plat, bridge);
        await m.BootAsync();
        m.SetPrimary("display:B");
        plat.DisplayList.RemoveAt(1);
        await m.ReconfigureAsync();
        Assert.Contains(m.Displays, d => d.Key == "B" && !d.IsConnected);             // remembered, shown as disconnected
        Assert.Equal("display:B", m.Profile.PrimarySource);                           // mapping untouched
        Assert.Contains("not available", m.Notice);
        Assert.Contains(plat.Notifications, n => n.Item1 == "Display disconnected");
    }

    [Fact] public async Task DropOnDisplaySwitchesToExtendAndAssigns()
    {
        var m = Make(new FakePlatform(), launch: new LaunchOptions { Demo = true });
        await m.SetDemoAsync(true);
        m.EditProfile(p => p.Mode = SyncMode.Single);
        Assert.True(m.Drop("ch:2", "display:demo-display-1"));
        Assert.Equal(SyncMode.Extend, m.Profile.Mode);
        Assert.Equal("display:demo-display-1", m.Route(2)!.SourceId);
        Assert.Contains("Extend", m.Notice);
        Assert.False(m.Drop("nonsense", "x"));
    }

    [Fact] public async Task StartWithoutBridgeReportsError()
    {
        var m = Make(new FakePlatform());
        await m.StartAsync();
        Assert.Equal("Set up your bridge first.", m.Error);
    }
}
