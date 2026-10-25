using Lumivane.Core;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Lumivane.Platform.Linux;
using Xunit;

namespace Lumivane.Platform.Tests;

public sealed class X11FactAttribute : FactAttribute
{
    public X11FactAttribute()
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))) Skip = "needs an X server (run under xvfb-run)";
    }
}

public sealed class PulseFactAttribute : FactAttribute
{
    public PulseFactAttribute()
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PULSE_SERVER"))) Skip = "needs a PulseAudio server with a sink named testsink (set PULSE_SERVER)";
    }
}

/// <summary>A plain colored top-level window on the X server under test.</summary>
internal sealed class TestWindow : IDisposable
{
    public IntPtr Display, Id;
    public readonly int X = 100, Y = 100, W = 400, H = 300;

    public TestWindow(uint rgb, string title = "Test Window", bool advertise = true)
    {
        Display = Lumivane.Platform.Linux.X.XOpenDisplay(null);
        var root = Lumivane.Platform.Linux.X.XDefaultRootWindow(Display);
        Id = Lumivane.Platform.Linux.X.XCreateSimpleWindow(Display, root, X, Y, (uint)W, (uint)H, 0, 0, rgb);
        Lumivane.Platform.Linux.X.XMapWindow(Display, Id);
        if (advertise)
        {
            var atomWin = Lumivane.Platform.Linux.X.XInternAtom(Display, "WINDOW", 0);
            var atomStr = Lumivane.Platform.Linux.X.XInternAtom(Display, "STRING", 0);
            var utf8 = Lumivane.Platform.Linux.X.XInternAtom(Display, "UTF8_STRING", 0);
            Lumivane.Platform.Linux.X.XChangeProperty(Display, root, Lumivane.Platform.Linux.X.XInternAtom(Display, "_NET_CLIENT_LIST", 0), atomWin, 32, 0, BitConverter.GetBytes((long)Id), 1);
            var name = System.Text.Encoding.UTF8.GetBytes(title);
            Lumivane.Platform.Linux.X.XChangeProperty(Display, Id, Lumivane.Platform.Linux.X.XInternAtom(Display, "_NET_WM_NAME", 0), utf8, 8, 0, name, name.Length);
            var cls = System.Text.Encoding.ASCII.GetBytes("fakeapp\0FakeApp\0");
            Lumivane.Platform.Linux.X.XChangeProperty(Display, Id, Lumivane.Platform.Linux.X.XInternAtom(Display, "WM_CLASS", 0), atomStr, 8, 0, cls, cls.Length);
        }
        Lumivane.Platform.Linux.X.XSync(Display, 0);
        Thread.Sleep(300);
        Lumivane.Platform.Linux.X.XSync(Display, 0);
    }

    public void Dispose()
    {
        Lumivane.Platform.Linux.X.XDestroyWindow(Display, Id);
        Lumivane.Platform.Linux.X.XCloseDisplay(Display);
    }
}

/// <summary>
/// An always-running silent stream. A null sink flaps between idle and suspended when nothing plays, which makes the test server deliver
/// unreliably; real output devices stay up, so this keeps the sink in the state a desktop is in while any app has audio open.
/// </summary>
internal static class KeepAlive
{
    static readonly Lazy<Process?> proc = new(() =>
    {
        // a private copy of pacat, so the app list (which groups by executable) keeps it apart from the tone under test
        var exe = Path.Combine(Path.GetTempPath(), "lumivane-keepalive");
        File.Copy(Process.Start(new ProcessStartInfo("which", "pacat") { RedirectStandardOutput = true })!.StandardOutput.ReadToEnd().Trim(), exe, true);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(exe, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var psi = new ProcessStartInfo(exe) { RedirectStandardInput = true, UseShellExecute = false };
        foreach (var a in new[] { "--playback", "--device=testsink", "--format=float32le", "--rate=48000", "--channels=1", "--client-name=KeepAlive" }) psi.ArgumentList.Add(a);
        var p = Process.Start(psi);
        var feeder = new Thread(() =>
        {
            var zeros = new byte[19200];
            try { while (true) { p!.StandardInput.BaseStream.Write(zeros); p.StandardInput.BaseStream.Flush(); } } catch { }
        }) { IsBackground = true };
        feeder.Start();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { p!.Kill(); } catch { } };
        Thread.Sleep(500);
        return p;
    });

    public static void Ensure() => _ = proc.Value;
}

internal sealed class ToneApp : IDisposable
{
    readonly Process proc;
    readonly Thread feeder;
    volatile bool stop;

    readonly string name;

    static bool Playing(string name)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("pactl", "list sink-inputs") { RedirectStandardOutput = true, UseShellExecute = false })!;
            var o = p.StandardOutput.ReadToEnd(); p.WaitForExit();
            return o.Contains($"application.name = \"{name}\"");
        }
        catch { return false; }
    }

    static bool Audible()
    {
        double peak = 0;
        using var cap = new PulseAudioCapturer();
        cap.StartAsync(AudioInput.System, (s, _, _) => { foreach (var x in s) peak = Math.Max(peak, Math.Abs(x)); }).Wait();
        Thread.Sleep(150);
        cap.StopAsync().Wait();
        return peak > 0.01;
    }

    static void WaitFor(Func<bool> cond, int ms = 5000) { var end = Environment.TickCount64 + ms; while (!cond() && Environment.TickCount64 < end) Thread.Sleep(50); }

    public ToneApp(string clientName = "ToneApp", double hz = 440, double amp = 0.5)
    {
        KeepAlive.Ensure();
        name = clientName;
        var psi = new ProcessStartInfo("pacat") { RedirectStandardInput = true, UseShellExecute = false, RedirectStandardError = true };
        foreach (var a in new[] { "--playback", "--device=testsink", "--format=float32le", "--rate=48000", "--channels=1", "--client-name=" + clientName, "--latency-msec=20" }) psi.ArgumentList.Add(a);
        proc = Process.Start(psi)!;
        feeder = new Thread(() =>
        {
            var buf = new float[4800]; long n = 0;
            var bytes = new byte[buf.Length * 4];
            try
            {
                while (!stop)
                {
                    for (int i = 0; i < buf.Length; i++, n++) buf[i] = (float)(amp * Math.Sin(2 * Math.PI * hz * n / 48000));
                    Buffer.BlockCopy(buf, 0, bytes, 0, bytes.Length);
                    proc.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                    proc.StandardInput.BaseStream.Flush();
                }
            }
            catch { }
        }) { IsBackground = true };
        feeder.Start();
        WaitFor(() => Playing(name));
        WaitFor(Audible, 8000);   // pacat's stream takes over a second to reach the sink after it appears
    }

    public void Dispose() { stop = true; try { proc.Kill(); } catch { } try { proc.WaitForExit(2000); } catch { } WaitFor(() => !Playing(name)); }
}
