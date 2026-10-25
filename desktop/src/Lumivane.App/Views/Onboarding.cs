using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Lumivane.Core;

namespace Lumivane.App;

public static class Onboarding
{
    public static Control Build(AppModel m)
    {
        var warm = new Ellipse { Width = 520, Height = 520, Fill = new SolidColorBrush(Color.Parse("#FF7A1A")), Opacity = 0.18, Effect = new BlurEffect { Radius = 140 }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, RenderTransform = new TranslateTransform(-380, 260), IsHitTestVisible = false };
        var blue = new Ellipse { Width = 520, Height = 520, Fill = new SolidColorBrush(Color.Parse("#3B7AFF")), Opacity = 0.18, Effect = new BlurEffect { Radius = 140 }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, RenderTransform = new TranslateTransform(380, -260), IsHitTestVisible = false };
        var bg = new Border(); Ui.Tint(bg, Border.BackgroundProperty, "WinBg");

        var dots = Ui.H(6);
        Ui.Bind(() =>
        {
            dots.Children.Clear();
            foreach (SetupStep st in Enum.GetValues<SetupStep>())
                dots.Children.Add(new Border
                {
                    Width = st == m.Step ? 22 : 8, Height = 8, CornerRadius = new CornerRadius(4),
                    Background = st <= m.Step ? Ui.BrandGradient() : new SolidColorBrush(Color.Parse("#8E8E93"), 0.25),
                });
        });
        var header = Ui.Split(Ui.H(8, new BrandMark(24), new TextBlock { Text = Brand.Name, FontSize = 16, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center }), dots);
        header.Margin = new Thickness(36, 30, 36, 0);

        var step = Ui.Dynamic(() => m.Step.ToString(), () => m.Step switch
        {
            SetupStep.Welcome => Welcome(m), SetupStep.Bridge => BridgeStep(m), SetupStep.Pair => Pair(m), SetupStep.Lights => LightsStep(m),
            SetupStep.Area => AreaStep(m), SetupStep.Permission => Permission(m), _ => Done(m),
        });
        var holder = new Border { Child = step, MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20) };

        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        grid.Children.Add(header);
        var sc = new ScrollViewer { Content = holder, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        Grid.SetRow(sc, 1); grid.Children.Add(sc);
        return new Grid { ClipToBounds = true, Children = { bg, warm, blue, grid } };
    }

    static Control Hero(string icon, string title, string subtitle, IBrush? color = null)
    {
        color ??= Ui.BlueBrush;
        var glow = new Ellipse { Width = 110, Height = 110, Fill = color, Opacity = 0.45, Effect = new BlurEffect { Radius = 36 } };
        var mark = new IconView(icon, 56, color, 1.5);
        var top = new Grid { Width = 120, Height = 120, HorizontalAlignment = HorizontalAlignment.Center, Children = { glow, mark } };
        mark.HorizontalAlignment = HorizontalAlignment.Center; mark.VerticalAlignment = VerticalAlignment.Center;
        var t = new TextBlock { Text = title, FontSize = 32, FontWeight = FontWeight.Bold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center };
        var s = new TextBlock { Text = subtitle, FontSize = 17, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center };
        s.Classes.Add("secondary");
        return Ui.V(14, top, t, s);
    }

    static Button Primary(string text, Action a)
    {
        var b = new Button { Content = text, MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Center };
        b.Classes.Add("gradient"); b.Click += (_, _) => a();
        return b;
    }

    static T Center<T>(T c) where T : Control { c.HorizontalAlignment = HorizontalAlignment.Center; return c; }

    static Control Welcome(AppModel m)
    {
        var demo = Ui.Btn("Explore with Demo Mode", () => Common.FireUi(m, () => m.SetDemoAsync(true)), "Virtual lights, rooms and displays. No bridge needed, nothing is saved.", "plain");
        demo.Foreground = Ui.Res(demo, "TextSecondary");
        return Ui.V(28, Hero("tv", "Make your room glow with your screen", "Setup takes about two minutes. No account needed, and nothing leaves your computer.", new SolidColorBrush(Color.Parse("#FF7A1A"))),
            Primary("Get started", () => m.Step = SetupStep.Bridge), Center(demo));
    }

    static Control BridgeStep(AppModel m)
    {
        string manual = "";
        Common.FireUi(m, async () => { if (m.Bridges.Count == 0) { await m.FindBridgesAsync(); if (m.Bridges.Count == 1) m.Step = SetupStep.Pair; } });

        var body = Ui.Dynamic(() => $"{m.Searching}|{string.Join(",", m.Bridges.Select(b => b.Id))}", () =>
        {
            var col = Ui.V(22, Hero("router", m.Searching ? "Looking for your Hue Bridge" : "Choose your Hue Bridge",
                m.Searching ? "Make sure this computer and the bridge are on the same Wi-Fi or network." : "Found on your network."));
            if (m.Searching) col.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 180, HorizontalAlignment = HorizontalAlignment.Center });
            foreach (var b in m.Bridges.ToList())
            {
                var bb = b;
                var btn = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(14), CornerRadius = new CornerRadius(14) };
                btn.Content = Ui.Cols("Auto,*,Auto", new IconView("router", 24, "TextPrimary") { Margin = new Thickness(0, 0, 12, 0) },
                    Ui.V(0, Ui.Label(b.Name ?? "Hue Bridge", "headline"), Ui.Label(b.Ip, "caption", "secondary")), new IconView("chevronRight", 14, "TextSecondary"));
                btn.Click += (_, _) => { m.Select(bb); m.Step = SetupStep.Pair; };
                col.Children.Add(btn);
            }
            if (!m.Searching && m.Bridges.Count == 0)
            {
                col.Children.Add(Center(Ui.Label("No bridge found yet.", "secondary")));
                col.Children.Add(Primary("Search again", () => Common.FireUi(m, () => m.FindBridgesAsync())));
                var foot = Ui.Wrap("If your firewall asks to allow local network access, choose Allow. The bridge and this computer must be on the same network.", "footnote", "secondary");
                foot.TextAlignment = TextAlignment.Center; col.Children.Add(foot);
                col.Children.Add(Center(Ui.Link("Try online lookup", () => Common.FireUi(m, () => m.FindBridgesAsync(true)),
                    "Asks Philips' discovery service for your bridge. This shares your network's public IP address with Philips.")));
            }
            var ip = new TextBox { Watermark = "For example 192.168.1.20", MinWidth = 200 };
            ToolTip.SetTip(ip, "You can find the bridge address in the Hue app under Settings > My Hue system.");
            ip.TextChanged += (_, _) => manual = ip.Text ?? "";
            var connect = Ui.Btn("Connect", () => Common.FireUi(m, async () => { await m.AddManualAsync(manual); if (m.Bridge != null && m.Bridges.Count == 1) m.Step = SetupStep.Pair; }));
            col.Children.Add(Ui.Max(380, Ui.Disclosure("Enter the address manually", Ui.Cols("*,Auto", ip, new Border { Child = connect, Margin = new Thickness(8, 0, 0, 0) })), HorizontalAlignment.Center));
            return col;
        });
        return body;
    }

    static Control Pair(AppModel m)
    {
        var cts = new CancellationTokenSource();
        Binder.Current.OnDispose(cts.Cancel);
        void Start() { if (!m.Pairing) Common.FireUi(m, () => m.PairAsync(cts.Token)); }
        Start();
        var action = Ui.Dynamic(() => m.Pairing.ToString(), () => m.Pairing
            ? Ui.V(8, new ProgressBar { IsIndeterminate = true, Width = 180, HorizontalAlignment = HorizontalAlignment.Center }, Center(Ui.Label("Waiting for the button...", "secondary")))
            : Primary("I pressed it", Start));
        return Ui.V(24, Hero("button", "Press the button on your bridge", $"Press the large round button on top of the Hue Bridge. {Brand.Name} connects automatically.", Ui.BlueBrush),
            action, Center(Ui.Link("Back", () => m.Step = SetupStep.Bridge)));
    }

    static Control LightsStep(AppModel m)
    {
        var list = Ui.V(0);
        foreach (var l in m.Lights)
        {
            var row = Ui.Split(Ui.H(8, new IconView(l.IsOn ? "bulbFill" : "bulb", 16, l.IsOn ? Ui.Yellow : "TextSecondary"), Ui.Label(l.Name)),
                l.SupportsEntertainment ? Ui.H(4, new IconView("tv", 13, "TextSecondary"), Ui.Label("Screen sync", "caption", "secondary")) : null);
            row.Margin = new Thickness(0, 5);
            list.Children.Add(row); list.Children.Add(Ui.Divider());
        }
        var scroll = new ScrollViewer { Content = list, MaxHeight = 220, Margin = new Thickness(16, 0) };
        return Ui.V(18, Hero("checkCircle", "Connected", $"{m.Lights.Count} lights found, {m.Lights.Count(l => l.SupportsEntertainment)} can follow your screen.", Ui.Green),
            scroll, Primary("Continue", () => m.Step = SetupStep.Area));
    }

    static Control AreaStep(AppModel m)
    {
        return Ui.Dynamic(() => $"{string.Join(",", m.Areas.Select(a => a.Id))}|{m.SelectedArea?.Id}|{m.Busy}", () =>
        {
            var col = Ui.V(20, Hero("tvbox", "Choose the lights for your screen",
                m.Areas.Count == 0 ? $"{Brand.Name} can set this up for you." : $"Pick an existing Entertainment area, or let {Brand.Name} create one."));
            foreach (var a in m.Areas.ToList())
            {
                var aa = a;
                var b = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12), CornerRadius = new CornerRadius(12) };
                b.Content = Ui.Cols("Auto,*,Auto", new IconView(m.SelectedArea?.Id == a.Id ? "radioOn" : "circle", 18, "TextPrimary") { Margin = new Thickness(0, 0, 10, 0) },
                    Ui.Label(a.Name, "headline"), Ui.Label($"{a.Channels.Count} lights", "secondary"));
                b.Click += (_, _) => m.EditProfile(p => p.AreaId = aa.Id);
                col.Children.Add(b);
            }
            if (m.Areas.Count == 0)
                col.Children.Add(m.Busy ? new ProgressBar { IsIndeterminate = true, Width = 180, HorizontalAlignment = HorizontalAlignment.Center } : Primary("Set up automatically", () => Common.FireUi(m, m.CreateRecommendedAreaAsync)));
            else col.Children.Add(Primary("Continue", () => m.Step = SetupStep.Permission));
            col.Children.Add(Center(Ui.Link(m.Areas.Count == 0 ? "Skip for now" : "Create a new area with all my lights",
                () => { if (m.Areas.Count == 0) m.Step = SetupStep.Permission; else Common.FireUi(m, m.CreateRecommendedAreaAsync); })));
            return col;
        });
    }

    static Control Permission(AppModel m)
    {
        var action = Ui.Dynamic(() => m.ScreenAllowed.ToString(), () => m.ScreenAllowed
            ? Center(Ui.H(6, new IconView("checkCircle", 18, Ui.Green), new TextBlock { Text = "Access granted", Foreground = Ui.Green, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center }))
            : Ui.V(10, Primary("Check screen access", () => m.RequestScreenAccess()),
                Ui.LiveWrap(() => m.Platform.ScreenAccessHint ?? "", "footnote", "secondary")));
        return Ui.V(22, Hero("record", "Allow screen access", $"{Brand.Name} reads colors from your screen to light the room. Nothing is recorded or sent anywhere."),
            action, Ui.Banner(m), Center(Ui.Link("Continue", () => m.Step = SetupStep.Done)));
    }

    static Control Done(AppModel m) =>
        Ui.V(26, Hero("sparkle", "You're ready", "Start syncing from the main screen or the tray icon.", Ui.Pink),
            Primary($"Open {Brand.Name}", () => { m.FinishOnboarding(); m.RefreshDisplays(); }));
}
