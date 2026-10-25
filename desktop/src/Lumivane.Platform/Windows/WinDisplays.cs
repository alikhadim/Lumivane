using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lumivane.Core;

namespace Lumivane.Platform.Windows;

/// <summary>Monitors from EnumDisplayMonitors. Coordinates are physical pixels (the process is made per-monitor DPI aware).</summary>
[SupportedOSPlatform("windows")]
internal static class WinDisplays
{
    public static List<LiveDisplay> Enumerate()
    {
        var found = new List<(IntPtr H, W.MONITORINFOEX Info)>();
        W.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr h, IntPtr dc, ref W.RECT r, IntPtr data) =>
        {
            var mi = new W.MONITORINFOEX { cbSize = Marshal.SizeOf<W.MONITORINFOEX>() };
            if (W.GetMonitorInfoW(h, ref mi)) found.Add((h, mi));
            return true;
        }, IntPtr.Zero);

        var list = new List<LiveDisplay>();
        int n = 0;
        foreach (var (h, mi) in found.OrderByDescending(f => (f.Info.dwFlags & W.MONITORINFOF_PRIMARY) != 0).ThenBy(f => f.Info.rcMonitor.Left).ThenBy(f => f.Info.rcMonitor.Top))
        {
            n++;
            var dd = new W.DISPLAY_DEVICE { cb = Marshal.SizeOf<W.DISPLAY_DEVICE>() };
            bool have = W.EnumDisplayDevicesW(mi.szDevice, 0, ref dd, W.EDD_GET_DEVICE_INTERFACE_NAME);
            var dm = new W.DEVMODE { dmSize = (ushort)Marshal.SizeOf<W.DEVMODE>() };
            double hz = W.EnumDisplaySettingsW(mi.szDevice, W.ENUM_CURRENT_SETTINGS, ref dm) ? dm.dmDisplayFrequency : 0;
            if (hz <= 1) hz = 0;    // 0 and 1 mean "hardware default"

            string key = have && dd.DeviceID.Length > 0 ? dd.DeviceID : mi.szDevice;
            string name = have && dd.DeviceString.Length > 0 && !dd.DeviceString.StartsWith("Generic", StringComparison.OrdinalIgnoreCase) ? dd.DeviceString : $"Display {n}";
            var r = mi.rcMonitor;
            var g = new DisplayGeometry
            {
                Key = key, Name = name, PixelWidth = r.Width, PixelHeight = r.Height,
                X = r.Left, Y = r.Top, Width = r.Width, Height = r.Height, RefreshHz = hz, IsBuiltIn = false, IsConnected = true,
            };
            list.Add(new LiveDisplay(g, h.ToInt64()));
        }
        return list;
    }

    /// <summary>Changes whenever a monitor is added, removed, moved or switches mode.</summary>
    public static string Signature(List<LiveDisplay> d) =>
        string.Join("|", d.Select(x => $"{x.Geometry.Key}@{x.Geometry.X},{x.Geometry.Y},{x.Geometry.PixelWidth}x{x.Geometry.PixelHeight}@{x.Geometry.RefreshHz}"));
}
