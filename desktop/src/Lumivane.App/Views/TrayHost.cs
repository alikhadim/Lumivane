using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Lumivane.Core;

namespace Lumivane.App;

/// <summary>Tray icon with a small control popup, the counterpart of the macOS menu bar item.</summary>
public static class TrayHost
{
    public static bool Available { get; private set; }

    public static void Attach(Application app, AppModel model, Window main, IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            var icon = new TrayIcon { Icon = Tray.WindowIcon(), ToolTipText = Brand.Name };
            var open = new NativeMenuItem("Open " + Brand.Name);
            var toggle = new NativeMenuItem("Start sync");
            var quit = new NativeMenuItem("Quit");
            open.Click += (_, _) => Show(main);
            toggle.Click += (_, _) => Common.FireUi(model, model.ToggleSyncAsync);
            quit.Click += async (_, _) => { await model.StopAsync(); desktop.Shutdown(); };
            icon.Menu = new NativeMenu { open, toggle, new NativeMenuItemSeparator(), quit };
            model.PropertyChanged += (_, _) => toggle.Header = model.Syncing ? "Stop sync" : "Start sync";
            TrayPopup? popup = null;
            icon.Clicked += (_, _) =>
            {
                if (popup is { IsVisible: true }) { popup.Close(); return; }
                popup = new TrayPopup(model, () => Show(main), async () => { await model.StopAsync(); desktop.Shutdown(); });
                popup.Show();
            };
            TrayIcon.SetIcons(app, new TrayIcons { icon });
            Available = true;
        }
        catch { Available = false; }
    }

    static void Show(Window w) { w.Show(); if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal; w.Activate(); }
}

public sealed class TrayPopup : Window
{
    readonly Binder binder = new();
    readonly Avalonia.Threading.DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    public TrayPopup(AppModel m, Action open, Action quit)
    {
        Width = 310; SizeToContent = SizeToContent.Height; CanResize = false; ShowInTaskbar = false; Topmost = true;
        SystemDecorations = SystemDecorations.BorderOnly; WindowStartupLocation = WindowStartupLocation.Manual;
        Content = binder.Build(() => Build(m, open, quit));
        timer.Tick += (_, _) => binder.Run(); timer.Start();
        Deactivated += (_, _) => Close();
        Closed += (_, _) => { timer.Stop(); binder.Dispose(); };
        Opened += (_, _) =>
        {
            if (Screens.Primary is { } s)
            {
                var wa = s.WorkingArea; double sc = s.Scaling;
                bool top = OperatingSystem.IsLinux();
                Position = new PixelPoint(wa.Right - (int)((Width + 12) * sc), top ? wa.Y + (int)(12 * sc) : wa.Bottom - (int)((Bounds.Height + 12) * sc));
            }
            Activate();
        };
    }

    static Control Build(AppModel m, Action open, Action quit)
    {
        var dot = new Avalonia.Controls.Shapes.Ellipse { Width = 8, Height = 8 };
        Ui.Bind(() => dot.Fill = m.Syncing ? Ui.Green : new SolidColorBrush(Color.Parse("#8E8E93"), 0.5));
        var head = Ui.Cols("Auto,*,Auto", new BrandMark(22) { Margin = new Thickness(0, 0, 8, 0) }, Ui.Label(Brand.Name, "headline"), dot);
        var go = Ui.Gradient(() => m.Syncing ? "Stop sync" : "Start sync", () => m.Syncing, () => Common.FireUi(m, m.ToggleSyncAsync));
        go.HorizontalAlignment = HorizontalAlignment.Left; go.Padding = new Thickness(18, 8);
        var styles = Enum.GetValues<ColorStyle>().ToList();
        var style = Ui.H(10, Ui.Label("Style"), Ui.Combo(styles.Select(s => s.Title()).ToList(), () => styles.IndexOf(m.Settings.Style), i => m.EditSettings(s => s.Style = styles[i]), 150));
        var content = Ui.V(12, head, go, Common.ProfileMenu(m), style, Ui.Divider(),
            Ui.SliderRow("Brightness", () => m.Settings.Brightness, v => m.EditSettings(s => s.Brightness = (float)v), 0.05, 1, null, 80),
            Ui.SliderRow("Sound", () => m.Settings.Audio.Influence, v => m.EditSettings(s => s.Audio.Influence = (float)v), 0, 1, null, 80),
            Ui.Check("Night mode", () => m.Settings.NightMode, v => m.EditSettings(s => s.NightMode = v)), Ui.Divider(),
            Ui.Btn($"Open {Brand.Name}", open, null, "plain"), Ui.Btn("Quit", quit, null, "plain"));
        var b = new Border { Padding = new Thickness(14), Child = content };
        Ui.Tint(b, Border.BackgroundProperty, "WinBg");
        return b;
    }
}
