using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Lumivane.Core;

namespace Lumivane.App;

public static class HomePage
{
    public static Control Build(AppModel m)
    {
        m.SetCompare(true);
        Binder.Current.OnDispose(() => m.SetCompare(false));

        var room = new RoomPreview(m);
        Ui.Bind(room.Update);

        var pills = Ui.Dynamic(() => string.Join("|", Pills(m).Select(p => $"{p.Text}/{p.Icon}/{p.Tone}")), () =>
        {
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var p in Pills(m)) { var c = Ui.Pill(() => p); c.Margin = new Thickness(0, 0, 8, 8); wrap.Children.Add(c); }
            return wrap;
        });

        var go = Ui.Gradient(() => m.Syncing ? "Stop" : "Start syncing", () => m.Syncing, () => Common.FireUi(m, m.ToggleSyncAsync), 0,
            null);
        Ui.Bind(() => ToolTip.SetTip(go, m.Syncing ? "Stop syncing your lights" : "Start syncing your lights"));

        var hero = Ui.Card(null, V18(room, pills, Ui.Split(Common.ProfileMenu(m), go)));

        var tiles = Ui.EqualRow(12, Enum.GetValues<ColorStyle>().Select(s => StyleTile(m, s)).ToArray());
        var style = Ui.Card("Color style", tiles,
            Ui.Show(() => !m.Syncing, Ui.Wrap("Start syncing to see each style on your current screen.", "footnote", "secondary")));

        var fine = V10(
            Ui.SliderRow("Color intensity", () => m.Settings.Intensity, v => m.EditSettings(s => s.Intensity = (float)v), 0.5, 1.8, "Extra color on top of the style"),
            Ui.SliderRow("Smoothness", () => m.Settings.Smoothing, v => m.EditSettings(s => s.Smoothing = (float)v), 0, 1, "Higher is calmer, lower reacts faster"),
            Ui.SliderRow("Warmth", () => m.Settings.Warmth, v => m.EditSettings(s => s.Warmth = (float)v), -1, 1, "Shifts white balance from cool to warm"),
            Ui.SliderRow("Dominant color", () => m.Settings.Dominance, v => m.EditSettings(s => s.Dominance = (float)v), 0, 1, "Low follows the average color of an area, high the most prominent color"),
            Ui.Check("Low-light viewing", () => m.Settings.LowLightMode, v => m.EditSettings(s => s.LowLightMode = v), "Soft, capped light for dark rooms"));

        var quick = Ui.Card("Quick controls",
            Ui.SliderRow("Brightness", () => m.Settings.Brightness, v => m.EditSettings(s => s.Brightness = (float)v), 0.05, 1, "Overall light level"),
            Ui.SliderRow("Sound influence", () => m.Settings.Audio.Influence, v => m.EditSettings(s => s.Audio.Influence = (float)v), 0, 1, "How much music and sound move the lights. 0 turns it off."),
            Ui.Check("Night mode", () => m.Settings.NightMode, v => m.EditSettings(s => s.NightMode = v), "Dimmer, warmer light for late evenings"),
            Ui.Disclosure("Fine tune", fine));

        return Ui.V(20, Ui.Banner(m), hero, style, quick);
    }

    static StackPanel V18(params Control[] c) => Ui.V(18, c);
    static StackPanel V10(params Control[] c) => Ui.V(10, c);

    public static List<(string Text, string Icon, Tone Tone)> Pills(AppModel m)
    {
        var p = new List<(string, string, Tone)>();
        p.Add(m.Diagnostics?.Reachable == true ? ("Bridge connected", "router", Tone.Good) : ("Bridge not reachable", "router", Tone.Warn));
        int n = m.SelectedArea?.Channels.Count ?? 0;
        p.Add(($"{n} light{(n == 1 ? "" : "s")} in sync area", "bulbs", n > 0 ? Tone.Good : Tone.Warn));
        p.Add(m.ScreenAllowed ? ("Screen access", "shieldCheck", Tone.Good) : ("Screen access needed", "shieldWarn", Tone.Warn));
        var a = m.Settings.Audio;
        if (a.Enabled && a.Influence > 0) p.Add((m.Syncing && !m.Metrics.AudioActive ? "Sound starting" : "Sound on", "waveform", Tone.Good));
        else p.Add(("Sound off", "waveformSlash", Tone.Off));
        if (m.Syncing) p.Add(m.Metrics.Mode.StartsWith("Entertainment") ? ("Smooth streaming", "bolt", Tone.Good) : ("Basic mode", "tortoise", Tone.Warn));
        return p;
    }

    static Control StyleTile(AppModel m, ColorStyle style)
    {
        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var dots = Enumerable.Range(0, 6).Select(_ => new Avalonia.Controls.Shapes.Ellipse { Width = 14, Height = 14 }).ToList();
        foreach (var d in dots) swatches.Children.Add(d);
        var title = Ui.Label(style.Title(), "headline");
        var sum = Ui.Wrap(style.Summary(), "caption", "secondary"); sum.MaxLines = 3;
        var body = new StackPanel { Spacing = 8, Children = { swatches, title, sum } };
        var b = new Border
        {
            Padding = new Thickness(12), MinHeight = 116, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(2),
            Child = body, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent,
        };
        b.PointerPressed += (_, _) => m.EditSettings(s => s.Style = style);
        ToolTip.SetTip(b, style.Summary());
        Ui.Bind(() =>
        {
            bool sel = m.Settings.Style == style;
            b.Background = sel ? new SolidColorBrush(Color.Parse("#FF2E7A"), 0.10) : Ui.Res(b, "TileBg");
            b.BorderBrush = sel ? Ui.BrandGradient() : Brushes.Transparent;
            var v = (m.StyleCompare.TryGetValue(style, out var d) ? d : new()).OrderBy(k => k.Key).Select(k => k.Value).ToList();
            int n = Math.Min(6, v.Count);
            for (int i = 0; i < 6; i++)
            {
                dots[i].Fill = n == 0 ? new SolidColorBrush(Color.Parse("#8E8E93"), 0.25) : new SolidColorBrush(Ui.C(v[i * (v.Count - 1) / Math.Max(n - 1, 1)]));
            }
        });
        return b;
    }
}
