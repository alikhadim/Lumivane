using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Lumivane.Core;

namespace Lumivane.App;

public static class SoundPage
{
    public static Control Build(AppModel m)
    {
        Common.FireUi(m, m.RefreshAudioAppsAsync);
        var a = () => m.Settings.Audio;

        var enable = Ui.Check("Lights react to sound", () => a().Enabled, v => m.EditSettings(s => s.Audio.Enabled = v), "Turn off to follow only the picture");
        enable.FontWeight = FontWeight.Bold;
        var intro = Ui.Card(null, enable,
            Ui.Wrap($"{Brand.Name} reads the digital audio your computer is playing, before it reaches the speakers. Room noise, voices and speaker echo don't affect it, and it works with headphones and HDMI.", "callout", "secondary"));

        var radios = Ui.V(8,
            Ui.Radio("src", "Everything my computer plays (recommended)", () => a().Input.Kind == "system", () => m.EditSettings(s => s.Audio.Input = AudioInput.System)),
            Ui.Radio("src", "One app only", () => a().Input.Kind == "app", () =>
            {
                var app = m.AudioApps.FirstOrDefault(x => x.Id == a().Input.App) ?? m.AudioApps.FirstOrDefault();
                if (app != null) m.EditSettings(s => s.Audio.Input = AudioInput.ForApp(app.Id));
            }),
            Ui.Radio("src", "Microphone (fallback)", () => a().Input.Kind == "microphone", () => m.EditSettings(s => s.Audio.Input = AudioInput.Microphone)));
        var detail = Ui.Dynamic(() => a().Input.Kind + string.Join(",", m.AudioApps.Select(x => x.Id)), () =>
        {
            var k = a().Input.Kind;
            if (k == "app")
            {
                var apps = m.AudioApps.ToList();
                return Ui.Max(360, Ui.H(10, Ui.Label("App"), Ui.Combo(apps.Select(x => x.Name).ToList(), () => Math.Max(0, apps.FindIndex(x => x.Id == a().Input.App)),
                    i => m.EditSettings(s => s.Audio.Input = AudioInput.ForApp(apps[i].Id)), 220, "Only this app's sound drives the lights")));
            }
            if (k == "microphone")
            {
                var t = Ui.Wrap("Listens to the room, so it also hears voices and speaker echo. Use it only if system audio isn't available.", "footnote");
                t.Foreground = Ui.Orange; return t;
            }
            return new Border();
        });
        var source = Ui.Card("Audio source", radios, detail);

        var shape = Ui.Card("How much sound shapes the light",
            Ui.SliderRow("Audio influence", () => a().Influence, v => m.EditSettings(s => s.Audio.Influence = (float)v), 0, 1, "0 follows only the picture. Movies work well at 10-30%, music at 70-90%."),
            Ui.SliderRow("Bass sensitivity", () => a().BassSensitivity, v => m.EditSettings(s => s.Audio.BassSensitivity = (float)v), 0, 2, "Warm room-wide pulses on kicks and explosions"),
            Ui.SliderRow("Treble sensitivity", () => a().TrebleSensitivity, v => m.EditSettings(s => s.Audio.TrebleSensitivity = (float)v), 0, 2, "Quick sparkles on hi-hats and sharp sounds"),
            Ui.SliderRow("Beat intensity", () => a().BeatIntensity, v => m.EditSettings(s => s.Audio.BeatIntensity = (float)v), 0, 2, "Strength of the pulse on each beat"),
            Ui.SliderRow("Smoothing", () => a().Smoothing, v => m.EditSettings(s => s.Audio.Smoothing = (float)v), 0, 1, "Higher makes the lights relax more slowly after a hit"),
            Ui.SliderRow("Music mode strength", () => a().MusicStrength, v => m.EditSettings(s => s.Audio.MusicStrength = (float)v), 0, 1, "How much more the lights react when music is playing than when people talk"));

        var bass = new LevelBar(Ui.Orange); var mid = new LevelBar(Ui.Green); var treble = new LevelBar(Ui.Cyan);
        Control Row(string t, LevelBar b) { var l = Ui.Label(t); return Ui.Cols("70,*", l, b); }
        var dot = new Ellipse { Width = 9, Height = 9, Fill = Ui.Pink, VerticalAlignment = VerticalAlignment.Center };
        var beat = Ui.H(6, dot, Ui.Label("Beat")); 
        var tempo = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var kind = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var kindBox = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2), Child = kind };
        Ui.Tint(kindBox, Border.BackgroundProperty, "TileBg");
        var db = new TextBlock { VerticalAlignment = VerticalAlignment.Center }; db.Classes.Add("secondary");
        var lat = new TextBlock { FontSize = 12.5 }; lat.Classes.Add("secondary");
        Ui.Bind(() =>
        {
            var f = m.Metrics.Audio;
            bass.Value = f.Bass; mid.Value = f.Mid; treble.Value = f.Treble;
            beat.Opacity = f.Beat > 0.3 ? 1 : 0;
            tempo.Text = f.Tempo > 0 ? $"{f.Tempo:F0} BPM" : "Tempo unknown";
            kind.Text = f.Speech > 0.5 ? "Speech" : "Music";
            db.Text = $"{Math.Max(f.VolumeDB, -99):F0} dB";
            lat.Text = m.Syncing && m.Metrics.AudioActive ? $"Sound to bridge {m.Metrics.AudioToLightMs:F0} ms" : "";
        });
        var info = Ui.Split(Ui.H(18, beat, tempo, kindBox, db), lat);
        var live = Ui.Card("Live", Row("Bass", bass), Row("Mid", mid), Row("Treble", treble), info,
            Ui.Show(() => !m.Syncing, Ui.Wrap("Start syncing to see live levels.", "footnote", "secondary")));

        return Ui.V(16, Ui.PageHeader("Sound", "Let music and effects move the light"), Ui.Banner(m), intro, source, shape, live);
    }
}
