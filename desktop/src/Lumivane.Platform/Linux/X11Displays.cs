using System.Runtime.InteropServices;
using System.Text;
using Lumivane.Core;

namespace Lumivane.Platform.Linux;

/// <summary>Monitors from XRandR: geometry, refresh rate and the friendly name from the monitor's EDID.</summary>
internal static unsafe class X11Displays
{
    public static List<LiveDisplay> Enumerate(IntPtr dpy)
    {
        var list = new List<LiveDisplay>();
        var root = X.XDefaultRootWindow(dpy);
        if (X.XRRQueryExtension(dpy, out _, out _) == 0) return Fallback(dpy, list);
        var resP = X.XRRGetScreenResourcesCurrent(dpy, root);
        if (resP == IntPtr.Zero) return Fallback(dpy, list);
        try
        {
            var res = *(X.RRScreenResources*)resP;
            var modes = new Dictionary<ulong, X.RRModeInfo>();
            for (int i = 0; i < res.nmode; i++) { var m = ((X.RRModeInfo*)res.modes)[i]; modes[m.id] = m; }
            ulong primary = X.XRRGetOutputPrimary(dpy, root);
            int n = 0;
            for (int i = 0; i < res.ncrtc; i++)
            {
                ulong crtc = ((ulong*)res.crtcs)[i];
                var cp = X.XRRGetCrtcInfo(dpy, resP, crtc);
                if (cp == IntPtr.Zero) continue;
                try
                {
                    var c = *(X.RRCrtcInfo*)cp;
                    if (c.noutput == 0 || c.mode == 0 || c.width == 0) continue;
                    var outs = X.ReadArray<ulong>(c.outputs, c.noutput);
                    ulong lead = outs.Contains(primary) ? primary : outs[0];
                    var (name, edidName) = OutputNames(dpy, resP, lead);
                    double hz = modes.TryGetValue(c.mode, out var mi) ? Refresh(mi) : 0;
                    n++;
                    var title = edidName ?? name;
                    var g = new DisplayGeometry
                    {
                        Key = name, Name = title, PixelWidth = (int)c.width, PixelHeight = (int)c.height,
                        X = c.x, Y = c.y, Width = c.width, Height = c.height, RefreshHz = hz, IsBuiltIn = IsInternal(name), IsConnected = true,
                    };
                    list.Add(new LiveDisplay(g, Id(name)));
                }
                finally { X.XRRFreeCrtcInfo(cp); }
            }
        }
        finally { X.XRRFreeScreenResources(resP); }
        return list.Count > 0 ? list : Fallback(dpy, list);
    }

    static List<LiveDisplay> Fallback(IntPtr dpy, List<LiveDisplay> list)
    {
        int s = X.XDefaultScreen(dpy);
        int w = X.XDisplayWidth(dpy, s), h = X.XDisplayHeight(dpy, s);
        list.Add(new LiveDisplay(new DisplayGeometry { Key = "screen0", Name = "Display", PixelWidth = w, PixelHeight = h, X = 0, Y = 0, Width = w, Height = h, IsBuiltIn = false, IsConnected = true }, Id("screen0")));
        return list;
    }

    public static bool IsInternal(string output) =>
        output.StartsWith("eDP", StringComparison.OrdinalIgnoreCase) || output.StartsWith("LVDS", StringComparison.OrdinalIgnoreCase) || output.StartsWith("DSI", StringComparison.OrdinalIgnoreCase);

    public static double Refresh(X.RRModeInfo m)
    {
        if (m.hTotal == 0 || m.vTotal == 0) return 0;
        double v = m.vTotal;
        if ((m.modeFlags & 0x20) != 0) v *= 2;   // double scan
        if ((m.modeFlags & 0x10) != 0) v /= 2;   // interlaced
        return m.dotClock / (m.hTotal * v);
    }

    /// <summary>Stable numeric handle for an output name, used as the capture target id.</summary>
    public static long Id(string name)
    {
        ulong h = 1469598103934665603UL;
        foreach (var c in name) { h ^= c; h *= 1099511628211UL; }
        return (long)(h & 0x7FFFFFFFFFFFFFFFUL);
    }

    static (string Name, string? Edid) OutputNames(IntPtr dpy, IntPtr res, ulong output)
    {
        var op = X.XRRGetOutputInfo(dpy, res, output);
        if (op == IntPtr.Zero) return ("screen0", null);
        try
        {
            var o = *(X.RROutputInfo*)op;
            var name = Marshal.PtrToStringAnsi(o.name, o.nameLen) ?? "screen0";
            return (name, ReadEdidName(dpy, output));
        }
        finally { X.XRRFreeOutputInfo(op); }
    }

    static string? ReadEdidName(IntPtr dpy, ulong output)
    {
        var atom = X.XInternAtom(dpy, "EDID", 1);
        if (atom == IntPtr.Zero) return null;
        if (X.XRRGetOutputProperty(dpy, output, atom, 0, 128, 0, 0, IntPtr.Zero, out _, out int fmt, out ulong n, out _, out var p) != 0 || p == IntPtr.Zero) return null;
        try
        {
            if (fmt != 8 || n < 128) return null;
            var edid = new byte[n]; Marshal.Copy(p, edid, 0, (int)n);
            return ParseEdidName(edid);
        }
        finally { X.XFree(p); }
    }

    /// <summary>The monitor name descriptor (tag 0xFC) of a base EDID block.</summary>
    public static string? ParseEdidName(byte[] edid)
    {
        if (edid.Length < 128) return null;
        foreach (int off in new[] { 54, 72, 90, 108 })
        {
            if (edid[off] == 0 && edid[off + 1] == 0 && edid[off + 2] == 0 && edid[off + 3] == 0xFC)
            {
                var s = Encoding.ASCII.GetString(edid, off + 5, 13);
                int end = s.IndexOfAny(new[] { '\n', '\0' });
                s = (end >= 0 ? s[..end] : s).Trim();
                return s.Length > 0 ? s : null;
            }
        }
        return null;
    }
}
