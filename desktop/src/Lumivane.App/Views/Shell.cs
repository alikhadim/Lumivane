using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using Avalonia.Threading;
using Lumivane.Core;

namespace Lumivane.App;

/// <summary>Sidebar, toolbar and the page area.</summary>
public static class Shell
{
    static readonly (NavSection S, string Icon)[] Nav =
    {
        (NavSection.Home, "tv"), (NavSection.Screens, "display2"), (NavSection.Sound, "waveform"),
        (NavSection.Lights, "bulbs"), (NavSection.Profiles, "stack"), (NavSection.Settings, "gear"),
    };

    public static NavSection Parse(string? s) =>
        Enum.TryParse<NavSection>(s, true, out var v) ? v : NavSection.Home;

    public static Control Build(AppModel m, NavSection start)
    {
        var section = start;
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("216,*") };

        // ---- sidebar ----
        var navList = new StackPanel { Spacing = 2, Margin = new Thickness(10, 4) };
        foreach (var (s, icon) in Nav)
        {
            var sec = s;
            var plain = new IconView(icon, 18, Ui.BlueBrush);
            var white = new IconView(icon, 18, Brushes.White) { IsVisible = false };
            var text = new TextBlock { Text = s.ToString(), FontSize = 13, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center };
            var b = new Button { Content = Ui.Cols("26,*", new Grid { Children = { plain, white }, HorizontalAlignment = HorizontalAlignment.Left }, text) };
            b.Classes.Add("nav");
            b.Click += (_, _) => { section = sec; Ui.Invalidate?.Invoke(); };
            Ui.Bind(() =>
            {
                bool sel = section == sec;
                b.SetClass("sel", sel); plain.IsVisible = !sel; white.IsVisible = sel;
                text.Foreground = sel ? Brushes.White : Ui.Res(b, "TextPrimary");
            });
            navList.Children.Add(b);
        }

        var brand = Ui.H(10, new BrandMark(30), new TextBlock { Text = Brand.Name, FontSize = 19, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center });
        brand.Margin = new Thickness(16, 14, 16, 6);

        var dot = new Ellipse { Width = 9, Height = 9, VerticalAlignment = VerticalAlignment.Center };
        var status = Ui.Label("", "callout"); status.FontWeight = FontWeight.Medium;
        var prof = Ui.Label("", "caption", "secondary"); prof.MaxLines = 1; prof.TextTrimming = TextTrimming.CharacterEllipsis;
        Ui.Bind(() =>
        {
            dot.Fill = m.Syncing ? Ui.Green : new SolidColorBrush(Color.Parse("#8E8E93"), 0.5);
            dot.Effect = m.Syncing ? new DropShadowEffect { Color = Color.Parse("#30D158"), BlurRadius = 8, OffsetX = 0, OffsetY = 0, Opacity = 0.8 } : null;
            status.Text = m.Syncing ? "Syncing" : "Not syncing";
            prof.Text = m.Profile.Name;
        });
        var footer = Ui.H(10, dot, Ui.V(1, status, prof)); footer.Margin = new Thickness(14);

        var sidebar = new Border { Child = new DockPanel { Children = { brand, footer, navList } } };
        DockPanel.SetDock(brand, Dock.Top); DockPanel.SetDock(footer, Dock.Bottom);
        Ui.Tint(sidebar, Border.BackgroundProperty, "SidebarBg");
        Grid.SetColumn(sidebar, 0);

        // ---- detail ----
        var backdrop = new AmbientBackdrop(m);
        Ui.Bind(backdrop.Update);

        var toggle = Round("sidebar", "Show or hide the sidebar", () => root.ColumnDefinitions[0].Width = root.ColumnDefinitions[0].Width.Value > 0 ? new GridLength(0) : new GridLength(216));
        var refresh = Round("refresh", "Reload lights, scenes and rooms from the bridge", () => Common.FireUi(m, m.RefreshAsync));
        var title = new TextBlock { Text = Brand.Name, FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0) };
        var toolbar = Ui.Cols("Auto,*,Auto", toggle, title, refresh);
        toolbar.Margin = new Thickness(14, 10);

        var page = new ContentControl { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Binder? inner = null; NavSection? shown = null;
        Ui.Bind(() =>
        {
            if (inner != null && shown == section) { inner.Run(); return; }
            inner?.Dispose(); shown = section; inner = new Binder();
            var content = inner.Build(() => Page(m, section));
            var holder = new Border { Child = content, Padding = new Thickness(28), MaxWidth = 960 + 56, HorizontalAlignment = HorizontalAlignment.Stretch };
            holder.Opacity = 0; holder.RenderTransform = TransformOperations.Parse("translateY(10px)");
            holder.Transitions = new Transitions { new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(280) },
                                                   new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(280) } };
            page.Content = holder;
            Dispatcher.UIThread.Post(() => { holder.Opacity = 1; holder.RenderTransform = TransformOperations.Parse("translateY(0px)"); }, DispatcherPriority.Background);
        });
        Binder.Current.OnDispose(() => inner?.Dispose());

        var scroll = new ScrollViewer { Content = new Border { Child = page, MaxWidth = 1016, HorizontalAlignment = HorizontalAlignment.Stretch }, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var detail = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var layers = new Grid { Children = { backdrop, scroll } };
        Grid.SetRow(layers, 1); detail.Children.Add(layers);
        detail.Children.Add(toolbar);
        Grid.SetColumn(detail, 1);
        var detailBg = new Border { Child = detail };
        Ui.Tint(detailBg, Border.BackgroundProperty, "WinBg");
        Grid.SetColumn(detailBg, 1);

        root.Children.Add(sidebar); root.Children.Add(detailBg);
        return root;
    }

    static Control Page(AppModel m, NavSection s) => s switch
    {
        NavSection.Home => HomePage.Build(m),
        NavSection.Screens => ScreensPage.Build(m),
        NavSection.Sound => SoundPage.Build(m),
        NavSection.Lights => LightsPage.Build(m),
        NavSection.Profiles => ProfilesPage.Build(m),
        _ => SettingsPage.Build(m),
    };

    static Button Round(string icon, string tip, Action click)
    {
        var b = new Button { Content = new IconView(icon, 16, "TextPrimary"), Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
        b.Click += (_, _) => click();
        Ui.Tip(b, tip);
        return b;
    }
}
