using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lumivane.Core;

namespace Lumivane.App;

public static class SettingsPage
{
    public static string Version => typeof(SettingsPage).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "dev";

    public static Control Build(AppModel m)
    {
        var copiedLabel = Ui.H(4, new IconView("check", 14, Ui.Green), new TextBlock { Text = "Copied", Foreground = Ui.Green });
        copiedLabel.IsVisible = false;
        async void Copy(string text)
        {
            if (TopLevel.GetTopLevel(copiedLabel)?.Clipboard is { } cb) await cb.SetTextAsync(text);
            copiedLabel.IsVisible = true;
            await Task.Delay(2000); copiedLabel.IsVisible = false;
        }

        var general = Ui.Card("General",
            Ui.Check($"Open {Brand.Name} at login", () => m.LaunchAtLogin, v => m.LaunchAtLogin = v, $"Starts {Brand.Name} in the tray when you sign in"),
            Ui.H(10, Ui.Label("Appearance"), Ui.Combo(new[] { "Match system", "Light", "Dark" }, () => (int)m.Appearance, i => m.Appearance = (Appearance)i, 160)));

        var output = Ui.Card("Light output (saved per profile)",
            Ui.SliderRow("Brightness limit", () => m.Settings.BrightnessLimit, v => m.EditSettings(s => s.BrightnessLimit = (float)v), 0.1, 1, "Lights never go brighter than this, including sound pulses"),
            Ui.Check("Adapt to bright and dark scenes", () => m.Settings.DynamicBrightness, v => m.EditSettings(s => s.DynamicBrightness = v), "Slowly raises light in dark scenes and lowers it in bright ones"),
            Ui.SliderRow("Tone curve", () => m.Settings.Gamma, v => m.EditSettings(s => s.Gamma = (float)v), 0.7, 1.6, "Below 1 lifts dark tones, above 1 makes them deeper. 1 uses the style as designed."),
            Ui.H(10, Ui.Label("Update rate"), Ui.Combo(new[] { 20, 30, 40, 50, 60 }.Select(x => $"{x} per second").ToList(),
                () => Math.Max(0, Array.IndexOf(new[] { 20, 30, 40, 50, 60 }, m.Settings.Fps)), i => m.EditSettings(s => s.Fps = new[] { 20, 30, 40, 50, 60 }[i]), 160, "Applies the next time sync starts")));

        var demo = Ui.Card("Demo Mode",
            Ui.Check("Use virtual bridge, lights and displays", () => m.Demo, v => Common.FireUi(m, () => m.SetDemoAsync(v))),
            Ui.LiveWrap(() => m.Demo ? "Demo Mode is on. Nothing is captured, saved or sent to any device. Your real setup is untouched."
                : "For screenshots, tutorials and trying the interface without a bridge. Your real setup is not changed.", "footnote", "secondary"));

        var privacy = Ui.Card("Privacy", PrivacySummary(m));

        var report = new SelectableTextBlock { FontFamily = new FontFamily("Cascadia Mono, Consolas, DejaVu Sans Mono, monospace"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
        Ui.Bind(() => report.Text = m.Diagnostics?.Report(false) ?? "No details yet");
        var bridge = Ui.Card("Hue Bridge", report,
            Ui.Split(Ui.H(8,
                Ui.Btn("Copy shareable report", () => { if (m.Diagnostics is { } d) Copy(d.Report(true)); }, "Hides your IP address, bridge ID and area names"),
                Ui.Btn("Copy full details", () => { if (m.Diagnostics is { } d) Copy(d.Report(false)); }), copiedLabel),
                Ui.Btn("Pair again or switch bridge", () => Common.FireUi(m, m.ForgetBridgeAsync))));

        var perf = Ui.Card("Performance", Ui.Disclosure("Live numbers", Performance(m)));

        var about = Ui.Card("About",
            Ui.H(12, new BrandMark(40), Ui.V(2, Ui.Label($"{Brand.Name} {Version}", "headline"), Ui.Label(Brand.Tagline, "callout", "secondary"))),
            Ui.Wrap("Not affiliated with or endorsed by Signify or Philips Hue. Philips Hue is a trademark of Signify.", "footnote", "secondary"));

        return Ui.V(16, Ui.PageHeader("Settings"), general, output, demo, privacy, bridge, perf, about);
    }

    static Control Performance(AppModel m)
    {
        var tiles = new List<TextBlock>();
        Control Stat(string title, Func<string> value)
        {
            var v = new TextBlock { FontSize = 16, FontWeight = FontWeight.Bold };
            Ui.Bind(() => v.Text = value());
            var b = new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(12), Child = Ui.V(4, Ui.Label(title, "caption", "secondary"), v) };
            Ui.Tint(b, Border.BackgroundProperty, "TileBg");
            return b;
        }
        Control Row(Control a, Control b) => Ui.EqualRow(12, a, b);
        var mt = () => m.Metrics;
        return Ui.V(12,
            Ui.Show(() => !m.Syncing, Ui.Label("Start syncing to see live numbers.", "secondary")),
            Row(Stat("Capture rate", () => $"{mt().CaptureFps:F0} fps"), Stat("Light updates", () => $"{mt().SendHz:F0} / s")),
            Row(Stat("Screens captured", () => $"{mt().CaptureSessions}"), Stat($"{Brand.Name} CPU", () => $"{mt().CpuPercent:F1} %")),
            Row(Stat("Capture delay", () => $"{mt().CaptureMs:F1} ms"), Stat("Screen to bridge", () => $"{mt().EndToEndMs:F1} ms")),
            Row(Stat("Color analysis", () => $"{mt().ReduceMs:F2} ms"), Stat("GPU per frame", () => mt().GpuMs > 0 ? $"{mt().GpuMs:F2} ms" : "n/a")),
            Row(Stat("Sound to bridge", () => mt().AudioActive ? $"{mt().AudioToLightMs:F1} ms" : "off"), Stat("Bridge response", () => m.NetworkMs is { } n ? $"{n:F0} ms" : "n/a")),
            Ui.LiveWrap(() => $"Mode: {mt().Mode}. Times end at the bridge; the radio hop from bridge to bulb is outside the app's control.", "footnote", "secondary"));
    }

    static Control PrivacySummary(AppModel m)
    {
        Control Row(string icon, string title, string text, IBrush tone) =>
            Ui.Cols("34,*", new IconView(icon, 18, tone) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) },
                Ui.V(2, Ui.Label(title, "callout").Bold(), Ui.Wrap(text, "callout", "secondary")));
        return Ui.V(12,
            Ui.Wrap("All screen analysis, audio analysis and lighting decisions happen on this computer.", "headline"),
            Row("cpu", "Processed here", "A small, reduced copy of each frame is averaged into colors and discarded. System audio is turned into levels and beats and discarded. Nothing is recorded or stored.", Ui.Green),
            Row("router", "Sent to your Hue Bridge", "Light colors and on/off commands, over your local network only.", Ui.BlueBrush),
            Row("lockShield", "Never leaves this computer", $"Screen images, audio, window titles and app names. Your bridge key is {m.Platform.SecretsDescription}.", Ui.Green),
            Row("network", "Optional and off by default", "Online bridge lookup through Philips' discovery service. It reveals your network's public IP address to Philips, so it only runs when you ask for it during setup.", Ui.Orange),
            Row("hand", "None of this exists", "No analytics, no telemetry, no account, no advertising identifiers.", Ui.Green));
    }

    static TextBlock Bold(this TextBlock t) { t.FontWeight = FontWeight.SemiBold; return t; }
}
