using System.Runtime.InteropServices;

namespace Lumivane.Platform.Linux;

/// <summary>Xlib, XShm, XRandR and SysV shared memory, called directly so no helper programs are needed.</summary>
internal static unsafe class X
{
    const string X11 = "libX11.so.6", Xext = "libXext.so.6", Xrr = "libXrandr.so.2", Libc = "libc";

    public const int ZPixmap = 2;
    public const ulong AllPlanes = ~0UL;
    public const long RRScreenChangeNotifyMask = 1, RRCrtcChangeNotifyMask = 2, RROutputChangeNotifyMask = 4;

    [StructLayout(LayoutKind.Sequential)]
    public struct XImage
    {
        public int width, height, xoffset, format;
        public IntPtr data;
        public int byte_order, bitmap_unit, bitmap_bit_order, bitmap_pad, depth, bytes_per_line, bits_per_pixel;
        public ulong red_mask, green_mask, blue_mask;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ShmSegmentInfo { public ulong shmseg; public int shmid; public IntPtr shmaddr; public int readOnly; }

    [StructLayout(LayoutKind.Sequential)]
    public struct XWindowAttributes
    {
        public int x, y, width, height, border_width, depth;
        public IntPtr visual, root;
        public int @class, bit_gravity, win_gravity, backing_store;
        public ulong backing_planes, backing_pixel;
        public int save_under;
        public IntPtr colormap;
        public int map_installed, map_state;
        public long all_event_masks, your_event_mask, do_not_propagate_mask;
        public int override_redirect;
        public IntPtr screen;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RRScreenResources { public ulong timestamp, configTimestamp; public int ncrtc; public IntPtr crtcs; public int noutput; public IntPtr outputs; public int nmode; public IntPtr modes; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RRCrtcInfo { public ulong timestamp; public int x, y; public uint width, height; public ulong mode; public ushort rotation; public int noutput; public IntPtr outputs; public ushort rotations; public int npossible; public IntPtr possible; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RRModeInfo { public ulong id; public uint width, height; public ulong dotClock; public uint hSyncStart, hSyncEnd, hTotal, hSkew, vSyncStart, vSyncEnd, vTotal; public IntPtr name; public uint nameLength; public ulong modeFlags; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RROutputInfo { public ulong timestamp, crtc; public IntPtr name; public int nameLen; public ulong mm_width, mm_height; public ushort connection, subpixel_order; public int ncrtc; public IntPtr crtcs; public int nclone; public IntPtr clones; public int nmode, npreferred; public IntPtr modes; }

    public delegate int ErrorHandler(IntPtr display, IntPtr errorEvent);

    [DllImport(X11)] public static extern IntPtr XOpenDisplay(string? name);
    [DllImport(X11)] public static extern int XCloseDisplay(IntPtr d);
    [DllImport(X11)] public static extern int XInitThreads();
    [DllImport(X11)] public static extern int XDefaultScreen(IntPtr d);
    [DllImport(X11)] public static extern IntPtr XDefaultRootWindow(IntPtr d);
    [DllImport(X11)] public static extern IntPtr XDefaultVisual(IntPtr d, int screen);
    [DllImport(X11)] public static extern int XDefaultDepth(IntPtr d, int screen);
    [DllImport(X11)] public static extern int XDisplayWidth(IntPtr d, int screen);
    [DllImport(X11)] public static extern int XDisplayHeight(IntPtr d, int screen);
    [DllImport(X11)] public static extern IntPtr XGetImage(IntPtr d, IntPtr drawable, int x, int y, uint w, uint h, ulong planeMask, int format);
    [DllImport(X11)] public static extern int XDestroyImage(IntPtr img);
    [DllImport(X11)] public static extern int XFree(IntPtr p);
    [DllImport(X11)] public static extern int XSync(IntPtr d, int discard);
    [DllImport(X11)] public static extern int XPending(IntPtr d);
    [DllImport(X11)] public static extern int XNextEvent(IntPtr d, IntPtr ev);
    [DllImport(X11)] public static extern IntPtr XSetErrorHandler(ErrorHandler? h);
    [DllImport(X11)] public static extern IntPtr XInternAtom(IntPtr d, string name, int onlyIfExists);
    [DllImport(X11)] public static extern int XGetWindowAttributes(IntPtr d, IntPtr w, out XWindowAttributes a);
    [DllImport(X11)] public static extern int XTranslateCoordinates(IntPtr d, IntPtr src, IntPtr dst, int sx, int sy, out int dx, out int dy, out IntPtr child);
    [DllImport(X11)]
    public static extern int XGetWindowProperty(IntPtr d, IntPtr w, IntPtr property, long offset, long length, int delete, IntPtr reqType,
        out IntPtr actualType, out int actualFormat, out ulong nitems, out ulong bytesAfter, out IntPtr prop);
    [DllImport(X11)] public static extern IntPtr XCreateSimpleWindow(IntPtr d, IntPtr parent, int x, int y, uint w, uint h, uint border, ulong borderPixel, ulong background);
    [DllImport(X11)] public static extern int XMapWindow(IntPtr d, IntPtr w);
    [DllImport(X11)] public static extern int XDestroyWindow(IntPtr d, IntPtr w);
    [DllImport(X11)] public static extern int XFlush(IntPtr d);
    [DllImport(X11)] public static extern int XChangeProperty(IntPtr d, IntPtr w, IntPtr property, IntPtr type, int format, int mode, byte[] data, int n);
    [DllImport(X11)] public static extern int XStoreName(IntPtr d, IntPtr w, string name);

    [DllImport(Xext)] public static extern int XShmQueryExtension(IntPtr d);
    [DllImport(Xext)] public static extern IntPtr XShmCreateImage(IntPtr d, IntPtr visual, uint depth, int format, IntPtr data, IntPtr info, uint w, uint h);
    [DllImport(Xext)] public static extern int XShmAttach(IntPtr d, IntPtr info);
    [DllImport(Xext)] public static extern int XShmDetach(IntPtr d, IntPtr info);
    [DllImport(Xext)] public static extern int XShmGetImage(IntPtr d, IntPtr drawable, IntPtr image, int x, int y, ulong planeMask);

    [DllImport(Xrr)] public static extern IntPtr XRRGetScreenResourcesCurrent(IntPtr d, IntPtr window);
    [DllImport(Xrr)] public static extern void XRRFreeScreenResources(IntPtr res);
    [DllImport(Xrr)] public static extern IntPtr XRRGetCrtcInfo(IntPtr d, IntPtr res, ulong crtc);
    [DllImport(Xrr)] public static extern void XRRFreeCrtcInfo(IntPtr info);
    [DllImport(Xrr)] public static extern IntPtr XRRGetOutputInfo(IntPtr d, IntPtr res, ulong output);
    [DllImport(Xrr)] public static extern void XRRFreeOutputInfo(IntPtr info);
    [DllImport(Xrr)] public static extern ulong XRRGetOutputPrimary(IntPtr d, IntPtr window);
    [DllImport(Xrr)] public static extern int XRRQueryExtension(IntPtr d, out int eventBase, out int errorBase);
    [DllImport(Xrr)] public static extern void XRRSelectInput(IntPtr d, IntPtr window, int mask);
    [DllImport(Xrr)]
    public static extern int XRRGetOutputProperty(IntPtr d, ulong output, IntPtr property, long offset, long length, int delete, int pending, IntPtr reqType,
        out IntPtr actualType, out int actualFormat, out ulong nitems, out ulong bytesAfter, out IntPtr prop);

    [DllImport(Libc)] public static extern int shmget(int key, nuint size, int flags);
    [DllImport(Libc)] public static extern IntPtr shmat(int id, IntPtr addr, int flags);
    [DllImport(Libc)] public static extern int shmdt(IntPtr addr);
    [DllImport(Libc)] public static extern int shmctl(int id, int cmd, IntPtr buf);

    static int threadsInit;
    public static void InitThreadsOnce() { if (Interlocked.Exchange(ref threadsInit, 1) == 0) XInitThreads(); }

    static readonly object gate = new();
    static bool handlerInstalled;
    static ErrorHandler? keep;
    static IntPtr previous;

    /// <summary>Xlib's default error handler kills the process (for example when a window closes mid-capture). Replace it with one that ignores errors.</summary>
    public static void InstallErrorHandler()
    {
        lock (gate)
        {
            if (handlerInstalled) return;
            keep = (_, _) => 0;
            previous = XSetErrorHandler(keep);
            handlerInstalled = true;
        }
    }

    public static T[] ReadArray<T>(IntPtr p, int n) where T : unmanaged
    {
        var r = new T[Math.Max(0, n)];
        for (int i = 0; i < r.Length; i++) r[i] = ((T*)p)[i];
        return r;
    }

    /// <summary>Reads a property as bytes; null when absent.</summary>
    public static byte[]? GetBytes(IntPtr d, IntPtr w, string prop, string? type = null, int maxBytes = 4096)
    {
        var a = XInternAtom(d, prop, 1);
        if (a == IntPtr.Zero) return null;
        var t = type == null ? IntPtr.Zero : XInternAtom(d, type, 1);
        if (XGetWindowProperty(d, w, a, 0, maxBytes / 4, 0, t, out _, out int fmt, out ulong n, out _, out var p) != 0 || p == IntPtr.Zero) return null;
        try
        {
            long bytes = fmt == 32 ? (long)n * IntPtr.Size : fmt == 16 ? (long)n * 2 : (long)n;
            if (bytes <= 0) return null;
            var r = new byte[bytes]; Marshal.Copy(p, r, 0, r.Length); return r;
        }
        finally { XFree(p); }
    }

    public static long[] GetLongs(IntPtr d, IntPtr w, string prop)
    {
        var a = XInternAtom(d, prop, 1);
        if (a == IntPtr.Zero) return Array.Empty<long>();
        if (XGetWindowProperty(d, w, a, 0, 4096, 0, IntPtr.Zero, out _, out int fmt, out ulong n, out _, out var p) != 0 || p == IntPtr.Zero) return Array.Empty<long>();
        try
        {
            if (fmt != 32) return Array.Empty<long>();
            var r = new long[n]; for (int i = 0; i < r.Length; i++) r[i] = ((long*)p)[i];
            return r;
        }
        finally { XFree(p); }
    }
}
