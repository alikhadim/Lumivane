using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Lumivane.Core;

namespace Lumivane.App;

public static class LightsPage
{
    public static Control Build(AppModel m)
    {
        int tab = Program.TakeTab();
        var seg = Ui.Segmented(new[] { "Lights", "Rooms & zones", "Scenes" }, () => tab, i => { tab = i; Ui.Invalidate?.Invoke(); }, 420);
        var body = Ui.Dynamic(() => tab.ToString(), () => tab switch { 0 => LightList(m), 1 => Rooms(m), _ => Scenes(m) });
        return Ui.V(16, Ui.PageHeader("Lights", "Control every light, room, zone and scene"), seg, body);
    }

    static Control LightList(AppModel m) => Ui.Dynamic(() => string.Join(",", m.Lights.Select(l => l.Id)), () =>
    {
        if (m.Lights.Count == 0) return Ui.Label("No lights found. Use the refresh button in the toolbar.", "secondary");
        var col = Ui.V(14);
        foreach (var l in m.Lights) col.Children.Add(LightRow(m, l.Id));
        return col;
    });

    static Control LightRow(AppModel m, string id)
    {
        HueLight L() => m.Lights.FirstOrDefault(x => x.Id == id) ?? m.Lights.First();
        var icon = Ui.Dynamic(() => L().IsOn.ToString(), () => new IconView(L().IsOn ? "bulbFill" : "bulb", 22, L().IsOn ? Ui.Yellow : "TextSecondary"));
        var sync = Ui.Label("Supports screen sync", "caption", "secondary");
        sync.IsVisible = L().SupportsEntertainment;
        var info = Ui.V(0, Ui.Label(L().Name, "headline"), sync);
        var slider = Ui.CommitSlider(L().Brightness, 1, 100, v => m.SetLight(L(), brightness: v));
        slider.Width = 180; ToolTip.SetTip(slider, "Brightness");
        Ui.Bind(() => { if (!slider.IsPointerOver && Math.Abs(slider.Value - L().Brightness) > 0.5) slider.Value = L().Brightness; });
        var swatch = new Border { Width = 30, Height = 18, CornerRadius = new CornerRadius(4), Background = Brushes.White, BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Colors.White, 0.35) };
        var view = new ColorView { IsAlphaVisible = false, IsAlphaEnabled = false, Color = Colors.White, Width = 280 };
        bool init = true;
        view.ColorChanged += (_, e) =>
        {
            swatch.Background = new SolidColorBrush(e.NewColor);
            if (!init) m.SetLight(L(), rgb: new RGB(e.NewColor.R / 255f, e.NewColor.G / 255f, e.NewColor.B / 255f));
        };
        init = false;
        var picker = new Button { Content = swatch, Padding = new Thickness(6, 5), Flyout = new Flyout { Content = view } };
        ToolTip.SetTip(picker, "Pick a color");
        var sw = Ui.Switch(() => L().IsOn, v => m.SetLight(L(), on: v));
        var right = Ui.H(14, slider, picker, Ui.IconBtn("sparkle", () => m.Flash(L()), "Flash this light to identify it"), sw);
        var left = Ui.H(14, new Border { Width = 30, Child = icon }, info);
        return Ui.Card(null, Ui.Split(left, right));
    }

    static Control Rooms(AppModel m)
    {
        return Ui.Dynamic(() => string.Join(",", m.Groups.Select(g => g.Id + g.Name + g.LightIds.Count)), () =>
        {
            var col = Ui.V(14);
            col.Children.Add(Ui.Split(new Border(), Ui.LabelBtn("plus", "New zone", () => EditZone(m, null), "Group any lights into a zone you can switch and dim together")));
            foreach (var g in m.Groups.OrderBy(g => g.Kind).ThenBy(g => g.Name)) col.Children.Add(GroupRow(m, g));
            return col;
        });
    }

    static Control GroupRow(AppModel m, HueGroup g)
    {
        var slider = Ui.CommitSlider(m.Brightness(g), 1, 100, v => m.SetGroup(g, brightness: v)); slider.Width = 180;
        Control edit = g.Kind == GroupKind.Zone ? Ui.IconBtn("pencil", () => EditZone(m, g), "Choose which lights are in this zone") : new Border { Width = 0 };
        var sw = Ui.Switch(() => m.IsOn(g), v => m.SetGroup(g, on: v));
        var left = Ui.H(14, new Border { Width = 30, Child = new IconView(g.Kind == GroupKind.Room ? "house" : "stack", 22, "TextPrimary") },
            Ui.V(0, Ui.Label(g.Name, "headline"), Ui.Label($"{(g.Kind == GroupKind.Room ? "Room" : "Zone")}, {g.LightIds.Count} lights", "caption", "secondary")));
        return Ui.Card(null, Ui.Split(left, Ui.H(14, slider, edit, sw)));
    }

    static void EditZone(AppModel m, HueGroup? g)
    {
        if (Ui.MainWindow is not { } owner) return;
        if (g == null) _ = Dialogs.PickLights(owner, "New zone", "", Array.Empty<string>(), m.Lights, true, false, (n, ids) => m.SaveZone(null, n, ids));
        else _ = Dialogs.PickLights(owner, $"Edit {g.Name}", g.Name, g.LightIds, m.Lights, true, false, (n, ids) => m.SaveZone(g.Id, n, ids), () => m.DeleteZone(g));
    }

    static Control Scenes(AppModel m)
    {
        return Ui.Dynamic(() => string.Join(",", m.Scenes.Select(s => s.Id + s.IsActive)), () =>
        {
            if (m.Scenes.Count == 0) return Ui.Label("No scenes found. Create scenes in the Hue app and they appear here.", "secondary");
            var names = m.Groups.ToDictionary(g => g.Id, g => g.Name);
            var col = Ui.V(18);
            foreach (var grp in m.Scenes.GroupBy(s => names.TryGetValue(s.GroupId, out var n) ? n : "Other").OrderBy(g => g.Key))
            {
                var grid = new AdaptiveGrid(150, 10);
                foreach (var s in grp.OrderBy(s => s.Name))
                {
                    var b = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12), CornerRadius = new CornerRadius(12) };
                    b.Content = Ui.H(8, new IconView(s.IsActive ? "checkCircle" : "photo", 16, s.IsActive ? Ui.Green : (object)"TextSecondary"), new TextBlock { Text = s.Name, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                    var sc = s; b.Click += (_, _) => m.Recall(sc);
                    ToolTip.SetTip(b, $"Activate {s.Name} in {grp.Key}");
                    grid.Children.Add(b);
                }
                col.Children.Add(Ui.V(8, Ui.Label(grp.Key, "headline", "secondary"), grid));
            }
            return col;
        });
    }
}
