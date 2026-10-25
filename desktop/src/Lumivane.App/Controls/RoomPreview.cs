using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Lumivane.Core;

namespace Lumivane.App;

/// <summary>The screen and its lights, drawn from the saved layout and lit with the live colors.</summary>
public sealed class RoomPreview : Panel
{
    sealed record Vis(int Ch, double U, double V, Ellipse Glow, Ellipse Dot, SolidColorBrush Fill);

    readonly AppModel model;
    readonly Border tv;
    readonly List<Vis> lights = new();
    string key = "\0";
    static readonly Color Idle = Color.Parse("#8E8E93");
    const double MaxHeight_ = 300;

    public RoomPreview(AppModel model)
    {
        this.model = model;
        HorizontalAlignment = HorizontalAlignment.Center;
        tv = new Border
        {
            CornerRadius = new CornerRadius(14), IsHitTestVisible = false, BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(Colors.White, 0.16),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromRgb(26, 26, 26), 0), new GradientStop(Color.FromRgb(10, 10, 10), 1) },
            },
            Child = new TextBlock
            {
                Text = Brand.Name, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Colors.White, 0.18),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    public void Update()
    {
        var routes = Routes();
        var k = (model.SelectedArea?.Id ?? "") + "|" + string.Join(",", routes.Select(r => $"{r.ChannelId}:{r.U:F3}:{r.V:F3}"));
        if (k != key)
        {
            key = k;
            Children.Clear(); lights.Clear();
            foreach (var r in routes)
            {
                var fill = new SolidColorBrush(Idle);
                var glow = new Ellipse { Width = 96, Height = 96, Fill = fill, Effect = new BlurEffect { Radius = 28 }, IsHitTestVisible = false };
                var dot = new Ellipse { Width = 22, Height = 22, Fill = fill, Stroke = new SolidColorBrush(Colors.White, 0.75), StrokeThickness = 1.5, Cursor = new Cursor(StandardCursorType.Hand) };
                int ch = r.ChannelId;
                dot.PointerPressed += (_, _) =>
                {
                    if (model.SelectedArea?.Channels.FirstOrDefault(c => c.Id == ch) is { } c && model.LightFor(c) is { } l) model.Flash(l);
                };
                if (model.SelectedArea?.Channels.FirstOrDefault(c => c.Id == ch) is { } cc) ToolTip.SetTip(dot, model.LightName(cc) + ". Click to flash.");
                Children.Add(glow); Children.Add(dot);
                lights.Add(new Vis(ch, r.U, r.V, glow, dot, fill));
            }
            Children.Add(tv);
            InvalidateMeasure();
        }
        bool on = model.Syncing;
        foreach (var l in lights)
        {
            l.Fill.Color = on ? Ui.C(model.Preview.TryGetValue(l.Ch, out var c) ? c : RGB.Black) : Color.FromArgb(140, Idle.R, Idle.G, Idle.B);
            l.Glow.Opacity = on ? 0.85 : 0.18;
        }
    }

    List<LightRoute> Routes()
    {
        if (model.SelectedArea is not { } a) return new();
        var ids = a.Channels.Select(c => c.Id).ToHashSet();
        return model.Profile.Routes.Where(r => r.Enabled && ids.Contains(r.ChannelId)).ToList();
    }

    Size Fit(Size avail)
    {
        double w = double.IsInfinity(avail.Width) ? 640 : avail.Width;
        double h = Math.Min(w * 9 / 16, MaxHeight_);
        return new Size(h * 16 / 9, h);
    }

    protected override Size MeasureOverride(Size avail)
    {
        foreach (var c in Children) c.Measure(Size.Infinity);
        return Fit(avail);
    }

    protected override Size ArrangeOverride(Size size)
    {
        double w = size.Width, h = size.Height, sw = w * 0.56, sh = sw * 9 / 16;
        foreach (var l in lights)
        {
            double cx = w / 2 + (l.U - 0.5) * sw * 1.5, cy = h / 2 + (l.V - 0.5) * sh * 1.5;
            l.Glow.Arrange(new Rect(cx - 48, cy - 48, 96, 96));
            l.Dot.Arrange(new Rect(cx - 11, cy - 11, 22, 22));
        }
        tv.Arrange(new Rect((w - sw) / 2, (h - sh) / 2, sw, sh));
        return size;
    }
}
