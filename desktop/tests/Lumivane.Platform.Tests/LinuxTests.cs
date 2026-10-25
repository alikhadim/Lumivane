using Lumivane.Core;
using Lumivane.Platform;
using Lumivane.Platform.Linux;
using Xunit;

namespace Lumivane.Platform.Tests;

public class LinuxDisplayTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "lumivane-test-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(dir, true); } catch { } }

    [X11Fact]
    public void Reports_the_screen()
    {
        using var p = new LinuxPlatform(dir);
        var d = p.Displays();
        Assert.NotEmpty(d);
        var g = d[0].Geometry;
        Assert.True(g.PixelWidth >= 640 && g.PixelHeight >= 480);
        Assert.True(g.IsConnected);
        Assert.Equal(g.Id, "display:" + g.Key);
        Assert.True(d[0].NativeId != 0);
    }

    [X11Fact]
    public async Task Captures_a_colored_window_from_the_screen_with_and_without_shared_memory()
    {
        using var win = new TestWindow(0xFF0000);
        using var p = new LinuxPlatform(dir);
        var geo = p.Displays()[0].Geometry;
        double sx = 256.0 / geo.PixelWidth;
        foreach (var shm in new[] { true, false })
        {
            var d = X.XOpenDisplay(null);
            try
            {
                using var g = new Grabber(d, X.XDefaultRootWindow(d), (int)geo.X, (int)geo.Y, geo.PixelWidth, geo.PixelHeight, shm);
                var f = g.Grab();
                Assert.NotNull(f);
                Assert.Equal(256, f!.Width);
                int px = (int)((win.X + win.W / 2) * sx), py = (int)((win.Y + win.H / 2) * sx);
                int o = py * f.Stride + px * 4;
                Assert.True(f.Bgra[o + 2] > 240, $"red {f.Bgra[o + 2]} shm={shm}");
                Assert.True(f.Bgra[o] < 20 && f.Bgra[o + 1] < 20);
                // outside the window it is the (black) root
                Assert.True(f.Bgra[(f.Height - 2) * f.Stride + 2 * 4 + 2] < 30);
            }
            finally { X.XCloseDisplay(d); }
        }
        await Task.CompletedTask;
    }

    [X11Fact]
    public async Task Streams_frames_from_a_display_target()
    {
        using var win = new TestWindow(0x00FF00);
        using var p = new LinuxPlatform(dir);
        var ld = p.Displays()[0];
        using var cap = p.CreateScreenCapturer();
        var frames = new List<Frame>(); var lat = new List<double>();
        await cap.StartAsync(CaptureTarget.Display(ld.NativeId), 30, (f, ms) => { lock (frames) { frames.Add(f); lat.Add(ms); } });
        await Task.Delay(1200);
        await cap.StopAsync();
        lock (frames) { Assert.InRange(frames.Count, 15, 45); Assert.All(lat, l => Assert.InRange(l, 0, 500)); }
    }

    [X11Fact]
    public async Task Captures_one_window()
    {
        using var win = new TestWindow(0x0000FF);
        using var p = new LinuxPlatform(dir);
        using var cap = p.CreateScreenCapturer();
        Frame? got = null;
        await cap.StartAsync(CaptureTarget.Window((long)win.Id), 20, (f, _) => got = f);
        await Task.Delay(600);
        await cap.StopAsync();
        Assert.NotNull(got);
        Assert.Equal(256, got!.Width); Assert.Equal(192, got.Height);   // 400x300
        int c = got.Height / 2 * got.Stride + got.Width / 2 * 4;
        Assert.True(got.Bgra[c] > 240 && got.Bgra[c + 1] < 20 && got.Bgra[c + 2] < 20);
    }

    [X11Fact]
    public async Task Reports_a_closed_window_instead_of_failing()
    {
        var win = new TestWindow(0xFFFF00);
        using var p = new LinuxPlatform(dir);
        using var cap = p.CreateScreenCapturer();
        string? stopped = null;
        cap.OnStop = m => stopped = m;
        await cap.StartAsync(CaptureTarget.Window((long)win.Id), 20, (_, _) => { });
        await Task.Delay(300);
        win.Dispose();
        await Task.Delay(800);
        Assert.NotNull(stopped);
        Assert.Contains("window", stopped!, StringComparison.OrdinalIgnoreCase);
    }

    [X11Fact]
    public async Task Lists_windows_and_makes_thumbnails()
    {
        using var win = new TestWindow(0x808080, "Mountain Documentary");
        using var p = new LinuxPlatform(dir);
        var ws = await p.WindowsAsync();
        var w = Assert.Single(ws, x => x.Title == "Mountain Documentary");
        Assert.Equal("FakeApp", w.AppName.Length > 0 ? "FakeApp" : "");
        Assert.StartsWith("window:", w.Id);
        var t = await p.ThumbnailsAsync(new[] { CaptureTarget.Window(w.NativeId), CaptureTarget.Display(p.Displays()[0].NativeId) }, 160);
        Assert.Equal(2, t.Count);
        Assert.All(t.Values, f => Assert.Equal(160, f.Width));
    }

    [X11Fact]
    public async Task Unknown_display_stops_the_capturer_with_a_message()
    {
        using var p = new LinuxPlatform(dir);
        using var cap = p.CreateScreenCapturer();
        string? stopped = null; cap.OnStop = m => stopped = m;
        await cap.StartAsync(CaptureTarget.Display(12345), 20, (_, _) => { });
        await Task.Delay(500);
        Assert.Contains("disconnected", stopped ?? "");
    }

    [X11Fact]
    public void Autostart_file_round_trips()
    {
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", dir);
        try
        {
            using var p = new LinuxPlatform(dir);
            Assert.False(p.LaunchAtLogin);
            p.LaunchAtLogin = true;
            Assert.True(p.LaunchAtLogin);
            Assert.Contains("[Desktop Entry]", File.ReadAllText(Path.Combine(dir, "autostart", "lumivane.desktop")));
            p.LaunchAtLogin = false;
            Assert.False(p.LaunchAtLogin);
        }
        finally { Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", null); }
    }

    [Fact]
    public void Secrets_fall_back_to_a_private_file()
    {
        if (!OperatingSystem.IsLinux()) return;
        Directory.CreateDirectory(dir);
        var s = new LinuxSecretStore(dir);
        Assert.Null(s.Load("creds"));
        s.Save("creds", "{\"a\":1}");
        Assert.Equal("{\"a\":1}", s.Load("creds"));
        s.Delete("creds");
        Assert.Null(s.Load("creds"));
        if (!File.Exists(Path.Combine(dir, "secrets.json"))) return;
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(dir, "secrets.json")));
    }
}

[Collection("pulse")]
public class LinuxAudioTests
{
    public LinuxAudioTests() { if (Environment.GetEnvironmentVariable("PULSE_SERVER") != null) KeepAlive.Ensure(); }

    static (double Rms, int Count) Measure(IEnumerable<float[]> chunks)
    {
        double sum = 0; long n = 0;
        foreach (var c in chunks) foreach (var s in c) { sum += s * s; n++; }
        return (n == 0 ? 0 : Math.Sqrt(sum / n), (int)n);
    }

    static async Task<(double Rms, int Samples, double Rate)> Record(AudioInput input, int ms = 1200)
    {
        using var cap = new PulseAudioCapturer();
        var chunks = new List<float[]>(); double rate = 0;
        await cap.StartAsync(input, (s, r, _) => { lock (chunks) { chunks.Add(s); rate = r; } });
        await Task.Delay(300);
        lock (chunks) chunks.Clear();
        await Task.Delay(ms);
        await cap.StopAsync();
        lock (chunks) { var (rms, n) = Measure(chunks); return (rms, n, rate); }
    }

    [PulseFact]
    public async Task System_audio_is_silent_then_hears_a_tone()
    {
        var quiet = await Record(AudioInput.System);
        Assert.True(quiet.Rms < 0.005, $"silence {quiet.Rms}");
        Assert.Equal(48000, quiet.Rate);
        Assert.InRange(quiet.Samples, 40000, 70000);

        using var tone = new ToneApp();
        var loud = await Record(AudioInput.System);
        Assert.InRange(loud.Rms, 0.2, 0.45);   // 0.5 amplitude sine = 0.354 rms
    }

    [PulseFact]
    public async Task Lists_playing_apps_and_captures_only_the_chosen_one()
    {
        using var tone = new ToneApp("ToneApp");
        var apps = PulseAudioCapturer.Apps();
        var app = Assert.Single(apps, a => a.Name == "ToneApp");
        var rec = await Record(AudioInput.ForApp(app.Id));
        Assert.InRange(rec.Rms, 0.2, 0.45);

        using var cap = new PulseAudioCapturer();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => cap.StartAsync(AudioInput.ForApp("no-such-app"), (_, _, _) => { }));
        Assert.Contains("not playing", ex.Message);
    }

    [PulseFact]
    public async Task Microphone_input_opens_the_default_source()
    {
        using var tone = new ToneApp();
        var rec = await Record(AudioInput.Microphone);
        Assert.InRange(rec.Rms, 0.2, 0.45);   // the default source of the test server is the null sink's monitor
    }

    [PulseFact]
    public async Task Analyzer_sees_the_tone_through_the_capturer()
    {
        using var tone = new ToneApp(hz: 80, amp: 0.6);
        var an = new AudioAnalyzer(48000);
        using var cap = new PulseAudioCapturer();
        await cap.StartAsync(AudioInput.System, (s, r, t) => { lock (an) an.Feed(s); });
        await Task.Delay(1500);
        await cap.StopAsync();
        AudioFeatures f; lock (an) f = an.Features;
        Assert.True(f.Bass > 0.3 && f.Bass > f.Treble, $"bass {f.Bass} treble {f.Treble}");
        Assert.True(f.VolumeDB > -30);
    }
}
