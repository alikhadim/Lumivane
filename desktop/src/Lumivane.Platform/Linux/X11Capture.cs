using System.Runtime.InteropServices;
using Lumivane.Core;

namespace Lumivane.Platform.Linux;

/// <summary>Reads pixels from the root window (a display) or one window. Uses shared memory when the server offers it.</summary>
internal sealed unsafe class Grabber : IDisposable
{
    readonly IntPtr dpy, drawable;
    readonly int x, y, w, h;
    IntPtr img, info;
    readonly bool shm;

    public Grabber(IntPtr dpy, IntPtr drawable, int x, int y, int w, int h, bool tryShm)
    {
        this.dpy = dpy; this.drawable = drawable; this.x = x; this.y = y; this.w = w; this.h = h;
        if (tryShm && X.XShmQueryExtension(dpy) != 0) shm = Setup();
    }

    bool Setup()
    {
        int scr = X.XDefaultScreen(dpy);
        info = Marshal.AllocHGlobal(sizeof(X.ShmSegmentInfo));
        *(X.ShmSegmentInfo*)info = default;
        img = X.XShmCreateImage(dpy, X.XDefaultVisual(dpy, scr), (uint)X.XDefaultDepth(dpy, scr), X.ZPixmap, IntPtr.Zero, info, (uint)w, (uint)h);
        if (img == IntPtr.Zero) { Free(); return false; }
        var im = (X.XImage*)img;
        long size = (long)im->bytes_per_line * im->height;
        var si = (X.ShmSegmentInfo*)info;
        si->shmid = X.shmget(0, (nuint)size, 0x200 | 0x180);
        if (si->shmid < 0) { Free(); return false; }
        si->shmaddr = X.shmat(si->shmid, IntPtr.Zero, 0);
        if (si->shmaddr == new IntPtr(-1)) { X.shmctl(si->shmid, 0, IntPtr.Zero); si->shmaddr = IntPtr.Zero; Free(); return false; }
        im->data = si->shmaddr; si->readOnly = 0;
        bool ok = X.XShmAttach(dpy, info) != 0;
        X.XSync(dpy, 0);
        X.shmctl(si->shmid, 0, IntPtr.Zero);    // freed by the kernel when both sides detach
        if (!ok) { Free(); return false; }
        return true;
    }

    void Free()
    {
        if (info != IntPtr.Zero)
        {
            var si = (X.ShmSegmentInfo*)info;
            if (si->shmaddr != IntPtr.Zero) { X.XShmDetach(dpy, info); X.XSync(dpy, 0); X.shmdt(si->shmaddr); si->shmaddr = IntPtr.Zero; }
        }
        if (img != IntPtr.Zero) { ((X.XImage*)img)->data = IntPtr.Zero; X.XDestroyImage(img); img = IntPtr.Zero; }
        if (info != IntPtr.Zero) { Marshal.FreeHGlobal(info); info = IntPtr.Zero; }
    }

    public Frame? Grab(int targetWidth = Downsampler.TargetWidth)
    {
        if (shm)
        {
            if (X.XShmGetImage(dpy, drawable, img, x, y, X.AllPlanes) == 0) return null;
            return Convert((X.XImage*)img, targetWidth);
        }
        var p = X.XGetImage(dpy, drawable, x, y, (uint)w, (uint)h, X.AllPlanes, X.ZPixmap);
        if (p == IntPtr.Zero) return null;
        try { return Convert((X.XImage*)p, targetWidth); }
        finally { X.XDestroyImage(p); }
    }

    static Frame? Convert(X.XImage* im, int targetWidth)
    {
        if (im->bits_per_pixel != 32) return null;
        var f = Downsampler.Reduce((byte*)im->data, im->width, im->height, im->bytes_per_line, 4, targetWidth);
        if (im->red_mask == 0xFF)   // RGB byte order: swap into BGRA
            for (int i = 0; i < f.Bgra.Length; i += 4) (f.Bgra[i], f.Bgra[i + 2]) = (f.Bgra[i + 2], f.Bgra[i]);
        return f;
    }

    public void Dispose() => Free();
}

internal sealed unsafe class X11ScreenCapturer : IScreenCapturer
{
    readonly Func<long, LiveDisplay?> findDisplay;
    Thread? thread;
    volatile bool stop;
    public Action<string>? OnStop { get; set; }

    public X11ScreenCapturer(Func<long, LiveDisplay?> findDisplay) { this.findDisplay = findDisplay; }

    public Task StartAsync(CaptureTarget target, int fps, Action<Frame, double> onFrame)
    {
        X.InstallErrorHandler();
        stop = false;
        thread = new Thread(() => Run(target, Math.Clamp(fps, 5, 120), onFrame)) { IsBackground = true, Name = "x11-capture", Priority = ThreadPriority.AboveNormal };
        thread.Start();
        return Task.CompletedTask;
    }

    void Run(CaptureTarget target, int fps, Action<Frame, double> onFrame)
    {
        var dpy = X.XOpenDisplay(null);
        if (dpy == IntPtr.Zero) { OnStop?.Invoke("Cannot connect to the X server."); return; }
        Grabber? g = null;
        try
        {
            var root = X.XDefaultRootWindow(dpy);
            int fails = 0;
            double period = 1.0 / fps, next = Clock.Now;
            while (!stop)
            {
                double t0 = Clock.Now;
                Frame? f;
                if (target.IsWindow)
                {
                    f = GrabWindow(dpy, (IntPtr)target.Id, out bool gone);
                    if (gone) { OnStop?.Invoke("The window was closed."); return; }
                }
                else
                {
                    if (g == null)
                    {
                        var d = findDisplay(target.Id);
                        if (d == null) { OnStop?.Invoke("The display was disconnected."); return; }
                        g = new Grabber(dpy, root, (int)d.Geometry.X, (int)d.Geometry.Y, d.Geometry.PixelWidth, d.Geometry.PixelHeight, true);
                    }
                    f = g.Grab();
                }
                if (f != null) { fails = 0; onFrame(f, (Clock.Now - t0) * 1000); }
                else if (++fails >= 30) { OnStop?.Invoke("Screen capture stopped working."); return; }
                next += period;
                double wait = next - Clock.Now;
                if (wait < -0.25) next = Clock.Now;
                else if (wait > 0) Thread.Sleep(TimeSpan.FromSeconds(wait));
            }
        }
        finally { g?.Dispose(); X.XCloseDisplay(dpy); }
    }

    /// <summary>One frame of a window. Returns null (not gone) while it is minimized.</summary>
    public static Frame? GrabWindow(IntPtr dpy, IntPtr win, out bool gone, int targetWidth = Downsampler.TargetWidth)
    {
        gone = false;
        if (X.XGetWindowAttributes(dpy, win, out var a) == 0) { gone = true; return null; }
        if (a.map_state != 2 || a.width <= 0 || a.height <= 0) return null;   // 2 = IsViewable
        using var g = new Grabber(dpy, win, 0, 0, a.width, a.height, false);
        return g.Grab(targetWidth);
    }

    public Task StopAsync()
    {
        stop = true;
        thread?.Join(1000);
        return Task.CompletedTask;
    }

    public void Dispose() { stop = true; thread?.Join(1000); }
}
