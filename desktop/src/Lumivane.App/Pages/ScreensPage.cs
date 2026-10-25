using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Lumivane.Core;

namespace Lumivane.App;

public static class ScreensPage
{
    public static Control Build(AppModel m)
    {
        int tab = Program.TakeTab();
        var body = Ui.Dynamic(() => tab.ToString(), () => tab == 0 ? SourceView(m) : SetupView(m));
        var seg = Ui.Segmented(new[] { "Displays", "Light layout" }, () => tab, i => { tab = i; Ui.Invalidate?.Invoke(); }, 320);
        return Ui.V(16, Ui.PageHeader("Screens", "Choose what the lights follow, and where each light sits"), seg, body);
    }

    static string HelpText(SyncMode mode) => mode switch
    {
        SyncMode.Single => "One display controls all selected lights.",
        SyncMode.Mirror => "Several displays are analysed together. Each light follows the average of the ones you tick.",
        _ => "Each light follows its own display. Drag lights or whole rooms onto a display below.",
    };

    // ---- displays and light assignment ----

    static Control SourceView(AppModel m)
    {
        Common.FireUi(m, m.RefreshWindowsAsync);
        Common.Timer(TimeSpan.FromSeconds(1), () => { m.RefreshDisplays(); Common.FireUi(m, m.RefreshThumbnailsAsync); });
        Common.FireUi(m, m.RefreshThumbnailsAsync);

        var modes = Ui.Segmented(new[] { "One display", "Mirror", "Extend" }, () => (int)m.Profile.Mode, i => m.SetMode((SyncMode)i));
        var picker = Ui.Dynamic(() => m.Profile.Mode + string.Join(",", m.SourceItems.Where(s => s.Connected).Select(s => s.Id)), () =>
        {
            if (m.Profile.Mode != SyncMode.Single) return new Border();
            var items = m.SourceItems.Where(s => s.Connected).ToList();
            var combo = Ui.Combo(items.Select(s => m.SourceName(s.Id)).ToList(), () => Math.Max(0, items.FindIndex(s => s.Id == m.EffectivePrimary)),
                i => { if (i >= 0 && i < items.Count) m.SetPrimary(items[i].Id); }, 220, "Pick the display that drives the selected lights");
            return Ui.H(10, Ui.Label("Display"), combo);
        });
        var how = Ui.Card("How should your displays drive your lights?", modes, Ui.LiveWrap(() => HelpText(m.Profile.Mode), "callout", "secondary"), picker);

        var arrangement = new ArrangementView(m);
        Ui.Bind(arrangement.Update);
        var grid = Ui.Dynamic(() => string.Join("|", m.SourceItems.Select(s => s.Id)), () =>
        {
            var g = new AdaptiveGrid(250, 14);
            foreach (var s in m.SourceItems) g.Children.Add(SourceTile(m, s.Id));
            return g;
        });
        var add = Ui.MenuButton(Ui.H(6, new IconView("windowPlus", 14, "TextPrimary"), Ui.Label("Add an app window")),
            () => m.Windows.Where(w => m.Profile.WindowSources.All(x => x.Id != w.Id)).Select(w => new Ui.MenuEntry($"{w.AppName}: {w.Title}", () => m.AddWindow(w))),
            "Use one app window, for example a video player, as a source");
        var foot = Ui.Wrap("Previews update about once a second while this page is open. Only displays that a light follows are captured while syncing.", "footnote", "secondary");
        foot.TextAlignment = TextAlignment.Right; foot.MaxWidth = 380;
        var displays = Ui.Card("Displays", arrangement, grid, Ui.Split(add, foot));

        var fallback = Ui.Dynamic(() => string.Join("|", m.SourceItems.Select(s => s.Id)), () =>
        {
            var items = m.SourceItems.ToList();
            var names = new List<string> { "Built-in display (automatic)" }; names.AddRange(items.Select(s => m.SourceName(s.Id)));
            return Ui.H(10, Ui.Label("Use"), Ui.Combo(names, () => 1 + items.FindIndex(s => s.Id == m.Profile.FallbackSource),
                i => m.EditProfile(p => p.FallbackSource = i <= 0 ? null : items[i - 1].Id), 300));
        });
        var disc = Ui.Card("If a display disconnects", fallback,
            Ui.Wrap($"Your assignments are kept. When the display returns, {Brand.Name} switches back by itself.", "footnote", "secondary"));

        return Ui.V(16, Ui.Banner(m), how, displays, disc, LightAssignment(m));
    }

    static string Detail(SourceItem it) => it.Kind switch
    {
        SourceKind.Display => $"{it.Display!.Resolution}  ·  {(Math.Round(it.Display.RefreshHz) == 0 ? "" : $"{(int)Math.Round(it.Display.RefreshHz)} Hz")}{(it.Display.IsUltrawide ? "  ·  Ultrawide" : "")}{(it.Display.IsBuiltIn ? "  ·  Built-in" : "")}",
        SourceKind.Desktop => "All displays as one canvas",
        _ => it.Window!.AppName,
    };

    static Control SourceTile(AppModel m, string id)
    {
        var item = m.SourceItems.First(s => s.Id == id);
        string Key() => id + m.IsSelected(id) + m.Profile.Mode + m.SourceName(id) + m.SourceItems.FirstOrDefault(s => s.Id == id)?.Connected
                        + string.Join(",", Assigned(m, id).Select(c => m.LightName(c)));
        return Ui.Dynamic(Key, () =>
        {
            var it = m.SourceItems.FirstOrDefault(s => s.Id == id) ?? item;
            bool selected = m.IsSelected(id);
            var img = new Image { Stretch = Stretch.UniformToFill };
            var ph = new IconView(it.Kind == SourceKind.Desktop ? "rect3" : it.Connected ? "display" : "displayWarn", 38, "TextSecondary");
            var thumb = new Grid { Children = { new Border { Background = new SolidColorBrush(Colors.Black, 0.35) }, ph, img } };
            var thumbBox = new Border { CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = new LayoutBox(thumb), Opacity = it.Connected ? 1 : 0.5 };
            Frame? last = null;
            Ui.Bind(() =>
            {
                Frame? f = it.Target is { } t && m.Thumbnails.TryGetValue(t, out var fr) ? fr : null;
                if (ReferenceEquals(f, last)) return;
                last = f; img.Source = f == null ? null : Common.ToBitmap(f); ph.IsVisible = f == null;
            });
            var tone = it.Connected ? Ui.Green : Ui.Orange;
            var status = Ui.H(4, new IconView(it.Connected ? "circle" : "circle", 8, tone) { VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = it.Connected ? "Connected" : "Disconnected", FontSize = 10.5, Foreground = tone, VerticalAlignment = VerticalAlignment.Center });
            var head = Ui.Split(Ui.V(2, Ui.Label(m.SourceName(id), "headline"), Ui.Label(Detail(it), "caption", "secondary")), status);
            var col = Ui.V(8, thumbBox, head);
            var assigned = Assigned(m, id);
            if (assigned.Count > 0)
            {
                var chips = new WrapPanel();
                foreach (var ch in assigned)
                    chips.Children.Add(new Border
                    {
                        Background = new SolidColorBrush(Color.Parse("#FFD60A"), 0.2), CornerRadius = new CornerRadius(10), Padding = new Thickness(6, 2), Margin = new Thickness(0, 0, 4, 4),
                        Child = Ui.H(4, new IconView("bulbFill", 11, Ui.Yellow), new TextBlock { Text = m.LightName(ch), FontSize = 10.5 }),
                    });
                col.Children.Add(chips);
            }
            var card = new Border { Padding = new Thickness(10), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2), Child = col, Background = selected ? new SolidColorBrush(Color.Parse("#0A84FF"), 0.18) : new SolidColorBrush(Color.Parse("#8E8E93"), 0.08), BorderBrush = selected ? Ui.BlueBrush : Brushes.Transparent };
            var wrap = new Grid { Children = { card } };
            if (selected && m.Profile.Mode != SyncMode.Extend) wrap.Children.Add(new IconView("checkCircle", 18, Ui.BlueBrush) { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 14, 14, 0) });
            wrap.Background = Brushes.Transparent; wrap.Cursor = new Cursor(StandardCursorType.Hand);
            wrap.PointerPressed += (_, e) =>
            {
                if (!it.Connected || !e.GetCurrentPoint(wrap).Properties.IsLeftButtonPressed) return;
                switch (m.Profile.Mode) { case SyncMode.Single: m.SetPrimary(id); break; case SyncMode.Mirror: m.ToggleMirror(id); break; }
            };
            Dnd.DropTarget(wrap, p => m.Drop(p, id));
            if (it.Kind == SourceKind.Window)
                wrap.ContextMenu = new ContextMenu { Items = { MenuItemOf("Remove window", () => m.RemoveWindow(id)) } };
            ToolTip.SetTip(wrap, m.Profile.Mode == SyncMode.Extend ? "Drag lights here to follow this source" : "Click to use this source");
            return wrap;
        });
    }

    static MenuItem MenuItemOf(string text, Action a) { var mi = new MenuItem { Header = text }; mi.Click += (_, _) => a(); return mi; }

    static List<EntertainmentChannel> Assigned(AppModel m, string id)
    {
        if (m.Profile.Mode != SyncMode.Extend || m.SelectedArea is not { } a) return new();
        return a.Channels.Where(ch => m.Route(ch.Id) is { } r && r.SourceId == id && r.Enabled).ToList();
    }

    static Control LightAssignment(AppModel m)
    {
        return Ui.Card("Lights", Ui.Dynamic(
            () => (m.SelectedArea?.Id ?? "-") + m.Profile.Mode + string.Join(",", m.SelectedArea?.Channels.Select(c => c.Id + m.LightName(c) + m.Route(c.Id)?.Enabled + m.Route(c.Id)?.SourceId) ?? Array.Empty<string>())
                  + string.Join(",", m.Groups.Select(g => g.Id)) + string.Join(",", m.SourceItems.Select(s => s.Id)),
            () =>
            {
                if (m.SelectedArea is not { } area) return Ui.Label("Set up a sync area first under Sync Lights.", "secondary");
                var col = Ui.V(10);
                if (m.Profile.Mode == SyncMode.Extend)
                {
                    var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    foreach (var g in m.Groups)
                    {
                        var chip = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 5), Child = Ui.H(6, new IconView(g.Kind == GroupKind.Room ? "house" : "stack", 13, "TextPrimary"), Ui.Label(g.Name)) };
                        Ui.Tint(chip, Border.BackgroundProperty, "TileBg");
                        Dnd.Draggable(chip, "grp:" + g.Id);
                        ToolTip.SetTip(chip, $"Drag onto a display to send every light in this {g.Kind.ToString().ToLower()} there");
                        chips.Children.Add(chip);
                    }
                    col.Children.Add(new ScrollViewer { Content = chips, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
                }
                foreach (var ch in area.Channels)
                {
                    int cid = ch.Id;
                    var sw = Ui.Switch(() => m.Route(cid)?.Enabled ?? true, v => m.UpdateRoute(cid, r => r.Enabled = v), "Turn this light's screen sync on or off");
                    var row = Ui.H(12, sw, new IconView("bulbFill", 16, Ui.Yellow) { VerticalAlignment = VerticalAlignment.Center }, Ui.Label(m.LightName(ch)));
                    var right = Ui.H(8);
                    if (m.Profile.Mode == SyncMode.Extend)
                    {
                        var items = m.SourceItems.ToList();
                        var name = m.Route(cid)?.SourceId is { } sid ? m.SourceName(sid) : "Not assigned";
                        right.Children.Add(Ui.MenuButton(Ui.H(6, new IconView("display", 14, "TextPrimary"), Ui.Label(name)),
                            () => new[] { new Ui.MenuEntry("Not assigned", () => m.Assign(cid, null)), new Ui.MenuEntry("", () => { }, Separator: true) }
                                .Concat(items.Select(s => new Ui.MenuEntry(m.SourceName(s.Id), () => m.Assign(cid, s.Id))))));
                    }
                    right.Children.Add(Ui.IconBtn("sparkle", () => { if (m.LightFor(ch) is { } l) m.Flash(l); }, "Flash this light"));
                    var rowB = new Border { Child = Ui.Split(row, right) }; rowB.Classes.Add("row");
                    Dnd.Draggable(rowB, "ch:" + cid);
                    col.Children.Add(rowB);
                }
                col.Children.Add(Ui.Wrap($"The bridge streams one Entertainment area at a time, so every light used by any display belongs to the area \"{area.Name}\". Which display drives which light is set here and saved in the profile.", "footnote", "secondary"));
                return col;
            }));
    }

    // ---- light layout ----

    static Control SetupView(AppModel m)
    {
        string editSource = "";
        var heading = Ui.Split(Ui.Label("Lights for screen sync", "title3"),
            Ui.LabelBtn("plus", "New area", () => CreateArea(m), "Create another set of lights for screen sync"));

        var content = Ui.Dynamic(() => (m.Areas.Count == 0 ? "none" : m.SelectedArea?.Id + string.Join(",", m.Areas.Select(a => a.Id + a.Name)) + m.Profile.Mode), () =>
        {
            if (m.Areas.Count == 0)
                return Ui.Card(null, Ui.Label("No sync area yet", "headline"), Ui.Btn("Set up automatically", () => Common.FireUi(m, m.CreateRecommendedAreaAsync), null, "prominent"));
            var area = m.SelectedArea!;
            var areas = m.Areas.ToList();
            var picker = Ui.Combo(areas.Select(a => a.Name).ToList(), () => Math.Max(0, areas.FindIndex(a => a.Id == m.SelectedArea?.Id)),
                i => m.EditProfile(p => p.AreaId = areas[i].Id), 220);
            var choose = Ui.Btn("Choose lights", () => EditArea(m, area), "Add or remove lights that can follow your screens");
            Ui.Bind(() => { picker.IsEnabled = choose.IsEnabled = !m.Syncing; });

            string InUse() => m.Profile.Mode == SyncMode.Extend
                ? (m.Profile.Routes.Where(r => r.Enabled && r.SourceId != null).Select(r => r.SourceId!).Distinct().OrderBy(x => x).ToList() is { Count: > 0 } l ? (l.Contains(editSource) ? editSource : l[0]) : "")
                : "";
            var editor = new ScreenLayoutEditor(m, () => m.Profile.Mode == SyncMode.Extend ? InUse() : null);
            Ui.Bind(editor.Update);

            Control position = new Border();
            if (m.Profile.Mode == SyncMode.Extend)
            {
                var srcs = m.Profile.Routes.Where(r => r.Enabled && r.SourceId != null).Select(r => r.SourceId!).Distinct().OrderBy(x => x).ToList();
                if (srcs.Count > 0)
                    position = Ui.H(10, Ui.Label("Positioning lights on"),
                        Ui.Combo(srcs.Select(m.SourceName).ToList(), () => Math.Max(0, srcs.IndexOf(InUse())), i => { editSource = srcs[i]; Ui.Invalidate?.Invoke(); }, 260));
            }
            var areaCard = Ui.Card("Area", Ui.H(10, picker, choose),
                Ui.Wrap($"Drag a light to tell {Brand.Name} which part of its display it should follow.", "callout", "secondary"), position, editor);

            var gains = Ui.V(10);
            foreach (var ch in area.Channels)
            {
                int cid = ch.Id;
                var s = Ui.PlainSlider(() => m.Settings.ChannelGain.TryGetValue(cid, out var g) ? g : 1, v => m.EditSettings(x => x.ChannelGain[cid] = (float)v), 0.1, 1);
                var name = Ui.Label(m.LightName(ch), "") ; name.Width = 180; name.TextTrimming = TextTrimming.CharacterEllipsis;
                gains.Children.Add(Ui.Cols("180,*,Auto", name, s, Ui.IconBtn("sparkle", () => { if (m.LightFor(ch) is { } l) m.Flash(l); }, "Flash this light")));
            }
            return Ui.V(16, areaCard, Ui.Card("Per-light brightness", gains));
        });
        return Ui.V(16, heading, content);
    }

    static void CreateArea(AppModel m)
    {
        if (Ui.MainWindow is not { } owner) return;
        _ = Dialogs.PickLights(owner, "New sync area", "Living room", Array.Empty<string>(), m.Lights, true, true, (n, ids) => m.SaveArea(null, n, ids));
    }

    static void EditArea(AppModel m, EntertainmentArea a)
    {
        if (Ui.MainWindow is not { } owner) return;
        var ids = m.Lights.Where(l => a.Channels.Any(c => c.Services.Contains(l.EntertainmentServiceId ?? "-"))).Select(l => l.Id);
        _ = Dialogs.PickLights(owner, $"Lights in {a.Name}", a.Name, ids, m.Lights, false, true, (_, sel) => m.SaveArea(a.Id, a.Name, sel));
    }
}

/// <summary>Forces a 16:9 box for its content regardless of the image size.</summary>
public sealed class LayoutBox : Decorator
{
    public LayoutBox(Control child) { Child = child; }
    protected override Size MeasureOverride(Size avail)
    {
        double w = double.IsInfinity(avail.Width) ? 200 : avail.Width;
        Child?.Measure(new Size(w, w * 9 / 16));
        return new Size(w, w * 9 / 16);
    }
    protected override Size ArrangeOverride(Size size) { Child?.Arrange(new Rect(size)); return size; }
}
