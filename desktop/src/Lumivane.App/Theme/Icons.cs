using Avalonia.Media;

namespace Lumivane.App;

/// <summary>Vector icons drawn for this app on a 24x24 grid (Apple's SF Symbols are licensed for Apple platforms only).
/// Filled icons use Fill, outline icons use a 1.7 px round stroke.</summary>
public static class Icons
{
    public sealed record Def(string Path, bool Filled);

    static string Gear()
    {
        // 8-tooth gear outline plus center hole, computed
        var sb = new System.Text.StringBuilder();
        const int teeth = 8; double ro = 10.2, ri = 7.6, cx = 12, cy = 12;
        for (int i = 0; i < teeth; i++)
        {
            double a0 = 2 * Math.PI * i / teeth, w = 2 * Math.PI / teeth;
            double[] ang = { a0 - w * 0.17, a0 - w * 0.11, a0 + w * 0.11, a0 + w * 0.17 };
            double[] rad = { ri, ro, ro, ri };
            for (int k = 0; k < 4; k++)
            {
                double x = cx + rad[k] * Math.Cos(ang[k]), y = cy + rad[k] * Math.Sin(ang[k]);
                sb.Append(i == 0 && k == 0 ? "M" : "L").Append(x.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(' ').Append(y.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
            }
        }
        sb.Append("Z M12 8.6a3.4 3.4 0 1 0 0 6.8a3.4 3.4 0 1 0 0-6.8Z");
        return sb.ToString();
    }

    public static readonly Dictionary<string, Def> All = new()
    {
        ["tv"] = new("M5 3.5h14a3 3 0 0 1 3 3v8.5a3 3 0 0 1-3 3h-4.2v1.8h2.7a.9.9 0 0 1 0 1.8h-11a.9.9 0 0 1 0-1.8h2.7V18H5a3 3 0 0 1-3-3V6.5a3 3 0 0 1 3-3Z M12 6.8l1.05 2.6 2.6 1.05-2.6 1.05L12 14.1l-1.05-2.6-2.6-1.05 2.6-1.05Z", true),
        ["display2"] = new("M2.5 5.5a1.5 1.5 0 0 1 1.5-1.5h10a1.5 1.5 0 0 1 1.5 1.5V13a1.5 1.5 0 0 1-1.5 1.5H4A1.5 1.5 0 0 1 2.5 13Z M16.5 8.5h3.5a1.5 1.5 0 0 1 1.5 1.5v7a1.5 1.5 0 0 1-1.5 1.5h-9a1.5 1.5 0 0 1-1.5-1.5V16 M7.5 14.5V17 M5 17h5", false),
        ["waveform"] = new("M4 10v4 M7.5 7v10 M11 4v16 M14.5 8v8 M18 6v12 M21 10.5v3", false),
        ["waveformSlash"] = new("M4 10v4 M7.5 7v10 M11 4v16 M14.5 8v8 M18 6v12 M21 10.5v3 M3 3l18 18", false),
        ["bulbs"] = new("M8.5 2.5a5.5 5.5 0 0 0-3.2 10v2.2a1 1 0 0 0 1 1h4.4a1 1 0 0 0 1-1V12.5a5.5 5.5 0 0 0-3.2-10Z M6.6 18.2h3.8a.9.9 0 0 1 0 1.8H6.6a.9.9 0 0 1 0-1.8Z M16.5 7.5a4.6 4.6 0 0 1 2.7 8.3v1.9a.9.9 0 0 1-.9.9h-3.6a.9.9 0 0 1-.9-.9v-1.9a4.6 4.6 0 0 1 2.7-8.3Z M15.2 20.2h2.6a.8.8 0 0 1 0 1.6h-2.6a.8.8 0 0 1 0-1.6Z", true),
        ["bulb"] = new("M12 2.5a6.5 6.5 0 0 0-3.7 11.8v2.2a1 1 0 0 0 1 1h5.4a1 1 0 0 0 1-1v-2.2A6.5 6.5 0 0 0 12 2.5Z M9.5 20.5h5 M10.5 22h3", false),
        ["bulbFill"] = new("M12 2a7 7 0 0 0-4 12.75V17a1.5 1.5 0 0 0 1.5 1.5h5A1.5 1.5 0 0 0 16 17v-2.25A7 7 0 0 0 12 2Z M9.6 20h4.8a.9.9 0 0 1 0 1.8H9.6a.9.9 0 0 1 0-1.8Z", true),
        ["stack"] = new("M6 3.5h12a2 2 0 0 1 2 2v1H4v-1a2 2 0 0 1 2-2Z M4.5 8.5h15a2 2 0 0 1 2 2v1h-19v-1a2 2 0 0 1 2-2Z M3 13.5h18v5a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2Z", true),
        ["gear"] = new(Gear(), true),
        ["refresh"] = new("M20 12a8 8 0 1 1-2.6-5.9 M20 4v4.5h-4.5", false),
        ["router"] = new("M4 13h16a2 2 0 0 1 2 2v3a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2v-3a2 2 0 0 1 2-2Z M6 16.5h.01 M9.5 16.5h.01 M7 13V8 M17 13V8 M12 13V6 M8.5 5.5a5 5 0 0 1 7 0", false),
        ["shieldCheck"] = new("M12 2.5l8 3v6c0 5-3.4 8.6-8 10-4.6-1.4-8-5-8-10v-6Z M8.3 12.2l2.7 2.7 4.9-5.3", false),
        ["shieldWarn"] = new("M12 2.5l8 3v6c0 5-3.4 8.6-8 10-4.6-1.4-8-5-8-10v-6Z M12 8v5 M12 16.2h.01", false),
        ["bolt"] = new("M13.5 2L5 13.5h6L10 22l9-12h-6.2Z", true),
        ["tortoise"] = new("M4 16c0-4.4 3.6-8 8-8s8 3.6 8 8Z M3 16h18 M19 12.5l2.5-1.5 M5 16.5v2 M9 16.5v2 M15 16.5v2", false),
        ["film"] = new("M4 4h16a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1Z M7 4v16 M17 4v16 M3 8.5h4 M3 12h4 M3 15.5h4 M17 8.5h4 M17 12h4 M17 15.5h4", false),
        ["note"] = new("M9 18V5l10-2v13 M9 18a3 3 0 1 1-3-3 3 3 0 0 1 3 3Z M19 16a3 3 0 1 1-3-3 3 3 0 0 1 3 3Z", false),
        ["gamepad"] = new("M7 7h10a5 5 0 0 1 4.8 3.6l1 4.3a3 3 0 0 1-5 2.8L16 16H8l-1.8 1.7a3 3 0 0 1-5-2.8l1-4.3A5 5 0 0 1 7 7Z M7.5 10v3 M6 11.5h3 M16 11h.01 M18 12.5h.01", false),
        ["leaf"] = new("M5 19C4 11 9 4 20 4c0 11-6 16-13 15 M5 19c2-5 5-8 9-10", false),
        ["sparkle"] = new("M12 3l1.9 6.1L20 11l-6.1 1.9L12 19l-1.9-6.1L4 11l6.1-1.9Z", true),
        ["house"] = new("M3 11.5l9-8 9 8 M5.5 10v9.5a1 1 0 0 0 1 1H10v-5h4v5h3.5a1 1 0 0 0 1-1V10", true),
        ["zone"] = new("M4 4h6v6H4Z M14 4h6v6h-6Z M4 14h6v6H4Z M14 14h6v6h-6Z", false),
        ["pencil"] = new("M4 20l1-4.5L16.5 4a2 2 0 0 1 3 0l.5.5a2 2 0 0 1 0 3L8.5 19Z M14.5 6l3.5 3.5", false),
        ["trash"] = new("M4 7h16 M9 7V4.5h6V7 M6.5 7l.8 12.5a1.5 1.5 0 0 0 1.5 1.4h6.4a1.5 1.5 0 0 0 1.5-1.4L17.5 7 M10 11v6 M14 11v6", false),
        ["checkCircle"] = new("M12 2.5a9.5 9.5 0 1 0 0 19 9.5 9.5 0 0 0 0-19Z M7.8 12.3l2.9 2.9 5.5-6", true),
        ["display"] = new("M3.5 4.5h17a1.5 1.5 0 0 1 1.5 1.5v9a1.5 1.5 0 0 1-1.5 1.5h-17A1.5 1.5 0 0 1 2 15V6a1.5 1.5 0 0 1 1.5-1.5Z M8 20h8 M12 16.5V20", false),
        ["displayWarn"] = new("M3.5 4.5h17a1.5 1.5 0 0 1 1.5 1.5v9a1.5 1.5 0 0 1-1.5 1.5h-17A1.5 1.5 0 0 1 2 15V6a1.5 1.5 0 0 1 1.5-1.5Z M8 20h8 M12 16.5V20 M12 8v3.5 M12 13.6h.01", false),
        ["rect3"] = new("M2.5 5h19v14h-19Z M9 5v14 M15.5 5v14", false),
        ["photo"] = new("M4 4.5h16a1.5 1.5 0 0 1 1.5 1.5v12a1.5 1.5 0 0 1-1.5 1.5H4A1.5 1.5 0 0 1 2.5 18V6A1.5 1.5 0 0 1 4 4.5Z M2.5 16l5.5-5 4.5 4.5 3-2.5 6 5 M16 9.5h.01", false),
        ["plus"] = new("M12 4.5v15 M4.5 12h15", false),
        ["xmark"] = new("M6 6l12 12 M18 6L6 18", false),
        ["warn"] = new("M12 2.8l10 17.7H2Z M12 9.5v5 M12 17.6h.01", true),
        ["play"] = new("M12 2.5a9.5 9.5 0 1 0 0 19 9.5 9.5 0 0 0 0-19Z M10 8.2v7.6l6-3.8Z", true),
        ["stop"] = new("M12 2.5a9.5 9.5 0 1 0 0 19 9.5 9.5 0 0 0 0-19Z M9 9h6v6H9Z", true),
        ["windowPlus"] = new("M3 5h14a1.5 1.5 0 0 1 1.5 1.5V9 M3 5v10.5A1.5 1.5 0 0 0 4.5 17H12 M3 8.5h15 M18 13v7 M14.5 16.5h7", false),
        ["check"] = new("M5 12.5l4.5 4.5L19 7", false),
        ["tvbox"] = new("M3.5 4h17A1.5 1.5 0 0 1 22 5.5v8a1.5 1.5 0 0 1-1.5 1.5h-17A1.5 1.5 0 0 1 2 13.5v-8A1.5 1.5 0 0 1 3.5 4Z M5 19.5h14a1 1 0 0 0 1-1V17H4v1.5a1 1 0 0 0 1 1Z", false),
        ["button"] = new("M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18Z M12 8.5a3.5 3.5 0 1 0 0 7 3.5 3.5 0 0 0 0-7Z", false),
        ["record"] = new("M3 8V5.5A1.5 1.5 0 0 1 4.5 4H8 M16 4h3.5A1.5 1.5 0 0 1 21 5.5V8 M21 16v2.5a1.5 1.5 0 0 1-1.5 1.5H16 M8 20H4.5A1.5 1.5 0 0 1 3 18.5V16 M12 8.5a3.5 3.5 0 1 0 0 7 3.5 3.5 0 0 0 0-7Z", false),
        ["circle"] = new("M12 3.5a8.5 8.5 0 1 0 0 17 8.5 8.5 0 0 0 0-17Z", false),
        ["radioOn"] = new("M12 3.5a8.5 8.5 0 1 0 0 17 8.5 8.5 0 0 0 0-17Z M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8Z", true),
        ["chevronRight"] = new("M9 5l7 7-7 7", false),
        ["chevronUpDown"] = new("M8 9.5l4-4 4 4 M8 14.5l4 4 4-4", false),
        ["chevronDown"] = new("M5 9l7 7 7-7", false),
        ["cpu"] = new("M7 7h10v10H7Z M10 10h4v4h-4Z M9 3v4 M15 3v4 M9 17v4 M15 17v4 M3 9h4 M3 15h4 M17 9h4 M17 15h4", false),
        ["lockShield"] = new("M12 2.5l8 3v6c0 5-3.4 8.6-8 10-4.6-1.4-8-5-8-10v-6Z M9.5 11.5h5v4h-5Z M10.5 11.5V10a1.5 1.5 0 0 1 3 0v1.5", false),
        ["network"] = new("M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18Z M3 12h18 M12 3c2.6 2.4 4 5.6 4 9s-1.4 6.6-4 9c-2.6-2.4-4-5.6-4-9s1.4-6.6 4-9Z", false),
        ["hand"] = new("M8 11V5.5a1.5 1.5 0 0 1 3 0V10 M11 9.5v-6a1.5 1.5 0 0 1 3 0V10 M14 10V5a1.5 1.5 0 0 1 3 0v8 M17 9.5a1.5 1.5 0 0 1 3 0V15a7 7 0 0 1-7 7h-1.5a6 6 0 0 1-4.6-2.2L4 15.5a1.6 1.6 0 0 1 2.4-2.1L8 15", false),
        ["sidebar"] = new("M4.5 4.5h15a2 2 0 0 1 2 2v11a2 2 0 0 1-2 2h-15a2 2 0 0 1-2-2v-11a2 2 0 0 1 2-2Z M9.5 4.5v15", false),
        ["copy"] = new("M9 9h10a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H9a1 1 0 0 1-1-1V10a1 1 0 0 1 1-1Z M5 15V5a1 1 0 0 1 1-1h10", false),
    };

    static readonly Dictionary<string, Geometry> cache = new();

    public static Geometry Geo(string name)
    {
        if (cache.TryGetValue(name, out var g)) return g;
        var d = All.TryGetValue(name, out var def) ? def : All["circle"];
        return cache[name] = Geometry.Parse(d.Filled ? "F1 " + d.Path : d.Path);
    }

    public static bool IsFilled(string name) => All.TryGetValue(name, out var d) && d.Filled;
}
