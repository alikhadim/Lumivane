using System.Runtime.Versioning;
using Lumivane.Core;

namespace Lumivane.Platform.Windows;

/// <summary>
/// Screen and window capture through GDI. The desktop is stretched into a 512 px wide bitmap, then reduced to 256 px with the shared
/// downsampler. Content the GPU draws on a protected overlay (DRM video) comes back black, as on every OS.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class GdiGrabber : IDisposable
{
    W.Dib? dib;

    W.Dib Buffer(int w, int h)
    {
        if (dib == null || dib.Width != w || dib.Height != h) { dib?.Dispose(); dib = new W.Dib(w, h); }
        return dib;
    }

    public Frame? Display(int x, int y, int w, int h, int targetWidth = Downsampler.TargetWidth)
    {
        if (w <= 0 || h <= 0) return null;
        int dw = Math.Min(w, targetWidth * 2), dh = Math.Max(1, (int)Math.Round((double)h * dw / w));
        var b = Buffer(dw, dh);
        var screen = W.GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) return null;
        try
        {
            W.SetStretchBltMode(b.Dc, W.COLORONCOLOR);
            if (!W.StretchBlt(b.Dc, 0, 0, dw, dh, screen, x, y, w, h, W.SRCCOPY)) return null;
        }
        finally { W.ReleaseDC(IntPtr.Zero, screen); }
        return Downsampler.Reduce((byte*)b.Bits, dw, dh, b.Stride, 4, targetWidth);
    }

    /// <summary>One frame of a window, drawn by the window itself so other windows on top do not matter. Null while minimized.</summary>
    public Frame? Window(IntPtr hwnd, out bool gone, int targetWidth = Downsampler.TargetWidth)
    {
        gone = !W.IsWindow(hwnd);
        if (gone || W.IsIconic(hwnd) || !W.GetWindowRect(hwnd, out var r) || r.Width <= 0 || r.Height <= 0) return null;
        // Full size, so the window renders at its native resolution; the downsampler does the reduction.
        var b = Buffer(r.Width, r.Height);
        new Span<byte>((void*)b.Bits, b.Stride * b.Height).Clear();
        if (!W.PrintWindow(hwnd, b.Dc, W.PW_RENDERFULLCONTENT)) return null;
        return Downsampler.Reduce((byte*)b.Bits, b.Width, b.Height, b.Stride, 4, targetWidth);
    }

    public void Dispose() { dib?.Dispose(); dib = null; }
}

[SupportedOSPlatform("windows")]
internal sealed class GdiScreenCapturer : IScreenCapturer
{
    readonly Func<long, LiveDisplay?> findDisplay;
    Thread? thread;
    volatile bool stop;
    public Action<string>? OnStop { get; set; }

    public GdiScreenCapturer(Func<long, LiveDisplay?> findDisplay) { this.findDisplay = findDisplay; }

    public Task StartAsync(CaptureTarget target, int fps, Action<Frame, double> onFrame)
    {
        stop = false;
        thread = new Thread(() => Run(target, Math.Clamp(fps, 5, 120), onFrame)) { IsBackground = true, Name = "gdi-capture", Priority = ThreadPriority.AboveNormal };
        thread.Start();
        return Task.CompletedTask;
    }

    void Run(CaptureTarget target, int fps, Action<Frame, double> onFrame)
    {
        using var grabber = new GdiGrabber();
        LiveDisplay? disp = null;
        double recheck = 0, period = 1.0 / fps, next = Clock.Now;
        int fails = 0;
        while (!stop)
        {
            double t0 = Clock.Now;
            Frame? f;
            try
            {
                if (target.IsWindow)
                {
                    f = grabber.Window((IntPtr)target.Id, out bool gone);
                    if (gone) { OnStop?.Invoke("The window was closed."); return; }
                }
                else
                {
                    if (disp == null || t0 >= recheck)
                    {
                        disp = findDisplay(target.Id);
                        recheck = t0 + 1;
                        if (disp == null) { OnStop?.Invoke("The display was disconnected."); return; }
                    }
                    var g = disp.Geometry;
                    f = grabber.Display((int)g.X, (int)g.Y, g.PixelWidth, g.PixelHeight);
                }
            }
            catch (Exception e) { OnStop?.Invoke("Screen capture failed: " + e.Message); return; }

            if (f != null) { fails = 0; onFrame(f, (Clock.Now - t0) * 1000); }
            else if (!target.IsWindow && ++fails >= 30) { OnStop?.Invoke("Screen capture stopped working."); return; }
            else if (target.IsWindow) fails = 0;   // a minimized window simply has nothing to show

            next += period;
            double wait = next - Clock.Now;
            if (wait < -0.25) next = Clock.Now;
            else if (wait > 0) Thread.Sleep(TimeSpan.FromSeconds(wait));
        }
    }

    public Task StopAsync() { stop = true; thread?.Join(1000); return Task.CompletedTask; }
    public void Dispose() { stop = true; thread?.Join(1000); }
}
