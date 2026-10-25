using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Threading;
using Lumivane.Core;

namespace Lumivane.App;

public enum Tone { Good, Warn, Off }

/// <summary>Small builders that keep the page code short. Everything that depends on the model goes through <see cref="Bind"/>.</summary>
public static class Ui
{
    /// <summary>Set by the main window: asks for the pages to be refreshed after a change that did not come from the model.</summary>
    public static Action? Invalidate;
    public static Window? MainWindow;

    public static void Bind(Action update) => Binder.Current.Add(update);
    public static void Tip(Control c, string? text) { if (!string.IsNullOrEmpty(text)) ToolTip.SetTip(c, text); }

    public static IBrush Res(Control c, string key) =>
        c.TryFindResource(key, c.ActualThemeVariant, out var v) && v is IBrush b ? b : Brushes.Gray;

    public static void Tint(AvaloniaObject c, AvaloniaProperty p, string key) => c.Bind(p, new DynamicResourceExtension(key));

    public static Color C(RGB c) => Color.FromRgb(B(c.R), B(c.G), B(c.B));
    static byte B(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255), 0, 255);

    public static readonly IBrush Green = new SolidColorBrush(Color.Parse("#30D158"));
    public static readonly IBrush Orange = new SolidColorBrush(Color.Parse("#FF9F0A"));
    public static readonly IBrush Yellow = new SolidColorBrush(Color.Parse("#FFD60A"));
    public static readonly IBrush Red = new SolidColorBrush(Color.Parse("#FF453A"));
    public static readonly IBrush Cyan = new SolidColorBrush(Color.Parse("#64D2FF"));
    public static readonly IBrush Pink = new SolidColorBrush(Color.Parse("#FF2E7A"));
    public static readonly IBrush BlueBrush = new SolidColorBrush(Color.Parse("#0A84FF"));

    public static LinearGradientBrush BrandGradient() => new()
    {
        StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#FF7A1A"), 0), new GradientStop(Color.Parse("#FF2E7A"), 0.5), new GradientStop(Color.Parse("#3B7AFF"), 1) },
    };

    // ---- text ----

    public static TextBlock Label(string text, params string[] classes)
    {
        var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in classes) t.Classes.Add(c);
        return t;
    }

    public static TextBlock Wrap(string text, params string[] classes)
    {
        var t = Label(text, classes); t.TextWrapping = TextWrapping.Wrap; return t;
    }

    public static TextBlock Live(Func<string> get, params string[] classes)
    {
        var t = Label("", classes);
        Bind(() => { var s = get(); if (t.Text != s) t.Text = s; });
        return t;
    }

    public static TextBlock LiveWrap(Func<string> get, params string[] classes)
    {
        var t = Live(get, classes); t.TextWrapping = TextWrapping.Wrap; return t;
    }

    // ---- layout ----

    public static StackPanel V(double spacing, params Control[] kids)
    {
        var s = new StackPanel { Spacing = spacing };
        foreach (var k in kids) s.Children.Add(k);
        return s;
    }

    public static StackPanel H(double spacing, params Control[] kids)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        foreach (var k in kids) s.Children.Add(k);
        return s;
    }

    /// <summary>Left content, flexible gap, right content.</summary>
    public static Grid Split(Control left, Control? right)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        g.Children.Add(left);
        if (right != null) { Grid.SetColumn(right, 1); g.Children.Add(right); }
        return g;
    }

    public static Grid Cols(string defs, params Control[] kids)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions(defs) };
        for (int i = 0; i < kids.Length; i++) { Grid.SetColumn(kids[i], i); g.Children.Add(kids[i]); }
        return g;
    }

    /// <summary>Equal-width cells in one row with a gap between them.</summary>
    public static Control EqualRow(double gap, params Control[] kids)
    {
        var g = new UniformGrid { Rows = 1, Margin = new Thickness(-gap / 2, 0) };
        foreach (var k in kids) { k.Margin = new Thickness(gap / 2, 0); g.Children.Add(k); }
        return g;
    }

    public static Control Max(double width, Control c, HorizontalAlignment h = HorizontalAlignment.Left)
    {
        c.MaxWidth = width; c.HorizontalAlignment = h; return c;
    }

    public static Border Card(string? title, params Control[] kids)
    {
        var s = new StackPanel { Spacing = 12 };
        if (title != null) s.Children.Add(Label(title.ToUpperInvariant(), "section"));
        foreach (var k in kids) s.Children.Add(k);
        var b = new Border { Child = s };
        b.Classes.Add("card");
        return b;
    }

    public static Control Divider()
    {
        var b = new Border { Height = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
        Tint(b, Border.BackgroundProperty, "Separator");
        return b;
    }

    public static Control PageHeader(string title, string? subtitle = null, Control? trailing = null)
    {
        var left = V(4, Label(title, "title"));
        if (subtitle != null) left.Children.Add(Label(subtitle, "callout", "secondary"));
        return Split(left, trailing);
    }

    /// <summary>Rebuilds its content when <paramref name="key"/> changes; otherwise only runs the inner updaters.</summary>
    public static Control Dynamic(Func<string> key, Func<Control> build)
    {
        var host = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        string? k = null; Binder? inner = null;
        Bind(() =>
        {
            var nk = key();
            if (inner != null && nk == k) { inner.Run(); return; }
            inner?.Dispose();
            k = nk; inner = new Binder();
            host.Content = inner.Build(build);
        });
        Binder.Current.OnDispose(() => inner?.Dispose());
        return host;
    }

    public static Control Show(Func<bool> visible, Control c)
    {
        Bind(() => { var v = visible(); if (c.IsVisible != v) c.IsVisible = v; });
        return c;
    }

    // ---- icons ----

    public static IconView Icon(string name, double size = 16, object? brush = null) => new(name, size, brush ?? "TextSecondary");

    // ---- buttons ----

    public static Button Btn(string text, Action click, string? tip = null, params string[] classes)
    {
        var b = new Button { Content = text };
        foreach (var c in classes) b.Classes.Add(c);
        b.Click += (_, _) => click();
        Tip(b, tip);
        return b;
    }

    public static Button IconBtn(string icon, Action click, string? tip = null, double size = 15)
    {
        var b = new Button { Content = new IconView(icon, size, "TextPrimary"), Padding = new Thickness(8, 5) };
        b.Click += (_, _) => click();
        Tip(b, tip);
        return b;
    }

    public static Button LabelBtn(string icon, string text, Action click, string? tip = null, params string[] classes)
    {
        var b = new Button { Content = H(6, new IconView(icon, 14, "TextPrimary"), Label(text)) };
        foreach (var c in classes) b.Classes.Add(c);
        b.Click += (_, _) => click();
        Tip(b, tip);
        return b;
    }

    public static Button Link(string text, Action click, string? tip = null)
    {
        var b = new Button { Content = text };
        b.Classes.Add("link");
        b.Click += (_, _) => click();
        Tip(b, tip);
        return b;
    }

    public static Button Gradient(Func<string> text, Func<bool> stop, Action click, double minWidth = 0, string? tip = null)
    {
        var b = new Button { MinWidth = minWidth };
        b.Classes.Add("gradient");
        b.Click += (_, _) => click();
        Bind(() =>
        {
            var t = text(); if (!Equals(b.Content, t)) b.Content = t;
            b.Classes.Set("stop", stop());
        });
        Tip(b, tip);
        return b;
    }

    static void Set(this Classes c, string name, bool on) { if (on && !c.Contains(name)) c.Add(name); else if (!on && c.Contains(name)) c.Remove(name); }
    public static void SetClass(this Control c, string name, bool on) => c.Classes.Set(name, on);

    // ---- inputs ----

    public static Control SliderRow(string title, Func<double> get, Action<double> set, double min, double max, string? help = null, double labelWidth = 150)
    {
        var s = new Slider { Minimum = min, Maximum = max, VerticalAlignment = VerticalAlignment.Center };
        bool sup = false;
        s.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty && !sup) set(s.Value); };
        Bind(() => { var v = get(); if (Math.Abs(s.Value - v) > 1e-4) { sup = true; s.Value = v; sup = false; } });
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions($"{labelWidth},*") };
        var l = Label(title); g.Children.Add(l);
        Grid.SetColumn(s, 1); g.Children.Add(s);
        Tip(g, help);
        return g;
    }

    public static Slider PlainSlider(Func<double> get, Action<double> set, double min, double max)
    {
        var s = new Slider { Minimum = min, Maximum = max, VerticalAlignment = VerticalAlignment.Center };
        bool sup = false;
        s.PropertyChanged += (_, e) => { if (e.Property == RangeBase.ValueProperty && !sup) set(s.Value); };
        Bind(() => { var v = get(); if (Math.Abs(s.Value - v) > 1e-4) { sup = true; s.Value = v; sup = false; } });
        return s;
    }

    /// <summary>A slider that only reports when released (bridge calls), initialised from the model.</summary>
    public static Slider CommitSlider(double initial, double min, double max, Action<double> commit)
    {
        var s = new Slider { Minimum = min, Maximum = max, Value = initial, VerticalAlignment = VerticalAlignment.Center };
        s.AddHandler(InputElement.PointerReleasedEvent, (_, _) => commit(s.Value), Avalonia.Interactivity.RoutingStrategies.Tunnel | Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        s.AddHandler(InputElement.KeyUpEvent, (_, _) => commit(s.Value), Avalonia.Interactivity.RoutingStrategies.Bubble, true);
        return s;
    }

    public static ToggleButton Switch(Func<bool> get, Action<bool> set, string? tip = null)
    {
        var t = new ToggleButton { VerticalAlignment = VerticalAlignment.Center };
        t.Classes.Add("switch");
        bool sup = false;
        t.IsCheckedChanged += (_, _) => { if (!sup) set(t.IsChecked == true); };
        Bind(() => { var v = get(); if ((t.IsChecked == true) != v) { sup = true; t.IsChecked = v; sup = false; } });
        Tip(t, tip);
        return t;
    }

    public static CheckBox Check(string text, Func<bool> get, Action<bool> set, string? tip = null, double? fontSize = null)
    {
        var t = new CheckBox { Content = text };
        bool sup = false;
        t.IsCheckedChanged += (_, _) => { if (!sup) set(t.IsChecked == true); };
        Bind(() => { var v = get(); if ((t.IsChecked == true) != v) { sup = true; t.IsChecked = v; sup = false; } });
        Tip(t, tip);
        return t;
    }

    public static Control Radio(string group, string text, Func<bool> get, Action select)
    {
        var r = new RadioButton { Content = text, GroupName = group };
        bool sup = false;
        r.IsCheckedChanged += (_, _) => { if (!sup && r.IsChecked == true) select(); };
        Bind(() => { var v = get(); if ((r.IsChecked == true) != v) { sup = true; r.IsChecked = v; sup = false; } });
        return r;
    }

    public static ComboBox Combo(IReadOnlyList<string> items, Func<int> get, Action<int> set, double width = 0, string? tip = null)
    {
        var c = new ComboBox { ItemsSource = items.ToList(), MinWidth = width, HorizontalAlignment = HorizontalAlignment.Left };
        bool sup = false;
        c.SelectionChanged += (_, _) => { if (!sup && c.SelectedIndex >= 0) set(c.SelectedIndex); };
        Bind(() => { var v = get(); if (c.SelectedIndex != v) { sup = true; c.SelectedIndex = v; sup = false; } });
        Tip(c, tip);
        return c;
    }

    public static Control Segmented(IReadOnlyList<string> items, Func<int> get, Action<int> set, double width = 0)
    {
        var grid = new UniformGrid { Rows = 1 };
        var btns = new List<Button>();
        for (int i = 0; i < items.Count; i++)
        {
            int idx = i;
            var b = new Button { Content = items[i], HorizontalContentAlignment = HorizontalAlignment.Center }; b.Classes.Add("seg");
            b.Click += (_, _) => set(idx);
            grid.Children.Add(b); btns.Add(b);
        }
        Bind(() => { int s = get(); for (int i = 0; i < btns.Count; i++) btns[i].SetClass("sel", i == s); });
        var outer = new Border { CornerRadius = new CornerRadius(7), Padding = new Thickness(2), Child = grid, HorizontalAlignment = HorizontalAlignment.Left };
        Tint(outer, Border.BackgroundProperty, "ControlBg");
        return outer;
    }

    public sealed record MenuEntry(string Text, Action Click, string? Icon = null, bool Checked = false, bool Separator = false);

    /// <summary>A button that opens a menu built at the moment it opens.</summary>
    public static Button MenuButton(Control content, Func<IEnumerable<MenuEntry>> entries, string? tip = null)
    {
        var b = new Button { Content = content };
        b.Click += (_, _) =>
        {
            var f = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
            foreach (var e in entries())
            {
                if (e.Separator) { f.Items.Add(new Separator()); continue; }
                var mi = new MenuItem { Header = e.Text };
                if (e.Icon != null) mi.Icon = new IconView(e.Icon, 14, "TextPrimary");
                else if (e.Checked) mi.Icon = new IconView("check", 14, "TextPrimary");
                var act = e.Click; mi.Click += (_, _) => act();
                f.Items.Add(mi);
            }
            f.ShowAt(b);
        };
        Tip(b, tip);
        return b;
    }

    public static Control Disclosure(string title, Control content)
    {
        var chev = new IconView("chevronRight", 12, "TextSecondary") { VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = RelativePoint.Center };
        var head = new Button { Content = H(8, chev, Label(title)), HorizontalAlignment = HorizontalAlignment.Left };
        head.Classes.Add("plain");
        var body = new Border { Child = content, IsVisible = false, Margin = new Thickness(0, 8, 0, 0) };
        head.Click += (_, _) =>
        {
            body.IsVisible = !body.IsVisible;
            chev.RenderTransform = new RotateTransform(body.IsVisible ? 90 : 0);
        };
        return V(0, head, body);
    }

    public static Control Pill(Func<(string Text, string Icon, Tone Tone)> get)
    {
        var text = new TextBlock { FontSize = 12.5, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center };
        var icon = new ContentControl();
        var b = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(11, 6), Child = H(6, icon, text) };
        string? lastIcon = null; Tone? lastTone = null;
        Bind(() =>
        {
            var (t, i, tone) = get();
            text.Text = t;
            if (i != lastIcon || tone != lastTone)
            {
                lastIcon = i; lastTone = tone;
                IBrush fg; IBrush bg;
                switch (tone)
                {
                    case Tone.Good: fg = Green; bg = new SolidColorBrush(Color.Parse("#30D158"), 0.14); break;
                    case Tone.Warn: fg = Orange; bg = new SolidColorBrush(Color.Parse("#FF9F0A"), 0.14); break;
                    default: fg = Res(b, "TextSecondary"); bg = Res(b, "TileBg"); break;
                }
                text.Foreground = fg; b.Background = bg;
                icon.Content = new IconView(i, 13, fg);
            }
        });
        return b;
    }

    public static Control Banner(AppModel model)
    {
        var t = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        var close = new Button { Content = new IconView("xmark", 12, "TextPrimary") }; close.Classes.Add("plain");
        close.Click += (_, _) => model.Notice = null;
        var g = Cols("Auto,*,Auto", new IconView("warn", 16, Orange) { Margin = new Thickness(0, 0, 10, 0) }, t, close);
        var b = new Border
        {
            Child = g, Padding = new Thickness(12), CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.Parse("#FF9F0A"), 0.16),
        };
        Bind(() => { var n = model.Notice; b.IsVisible = n != null; t.Text = n ?? ""; });
        return b;
    }

    public static Border Tile(Control content, double radius = 14)
    {
        var b = new Border { CornerRadius = new CornerRadius(radius), Child = content };
        b.Classes.Add("tile");
        return b;
    }
}
