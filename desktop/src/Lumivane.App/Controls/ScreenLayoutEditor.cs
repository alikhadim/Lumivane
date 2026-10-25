using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Lumivane.Core;

namespace Lumivane.App;

/// <summary>Drag a light to tell the app which part of its display it follows.</summary>
public sealed class ScreenLayoutEditor : Panel
{
    sealed record Dot(int Ch, Border B, SolidColorBrush Fill, TextBlock Num);

    readonly AppModel model;
    readonly Func<string?> sourceFilter;
    readonly Border frame;
    readonly List<Dot> dots = new();
    string key = "\0";
    int? dragging;
    Rect screen;

    public ScreenLayoutEditor(AppModel model, Func<string?> sourceFilter)
    {
        this.model = model; this.sourceFilter = sourceFilter; Height = 320;
        frame = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(3), Background = new SolidColorBrush(Colors.Black, 0.25), IsHitTestVisible = false };
        Ui.Tint(frame, Border.BorderBrushProperty, "TextSecondary");
        Background = Brushes.Transparent;
    }

    IEnumerable<LightRoute> Visible()
    {
        if (model.SelectedArea is not { } a) return Array.Empty<LightRoute>();
        var ids = a.Channels.Select(c => c.Id).ToHashSet();
        var rs = model.Profile.Routes.Where(r => ids.Contains(r.ChannelId) && r.Enabled);
        return model.Profile.Mode == SyncMode.Extend ? rs.Where(r => r.SourceId == sourceFilter()) : rs;
    }

    public void Update()
    {
        var vis = Visible().ToList();
        var k = string.Join(",", vis.Select(r => r.ChannelId));
        if (k != key)
        {
            key = k; Children.Clear(); dots.Clear();
            Children.Add(frame);
            foreach (var r in vis)
            {
                var fill = new SolidColorBrush(Ui.Res(this, "Accent") is SolidColorBrush s ? s.Color : Colors.DodgerBlue);
                var num = new TextBlock { Text = (r.ChannelId + 1).ToString(), FontSize = 11, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var b = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Background = fill, Child = num, Cursor = new Cursor(StandardCursorType.SizeAll) };
                ToolTip.SetTip(b, "Drag to move this light around the screen");
                int ch = r.ChannelId;
                b.PointerPressed += (_, e) => { dragging = ch; e.Pointer.Capture(b); };
                b.PointerReleased += (_, e) => { dragging = null; e.Pointer.Capture(null); };
                b.PointerMoved += (_, e) =>
                {
                    if (dragging != ch || screen.Width <= 0) return;
                    var p = e.GetPosition(this);
                    double u = Math.Clamp((p.X - screen.X) / screen.Width, 0, 1), v = Math.Clamp((p.Y - screen.Y) / screen.Height, 0, 1);
                    model.UpdateRoute(ch, rt => { rt.U = u; rt.V = v; });
                };
                Children.Add(b); dots.Add(new Dot(ch, b, fill, num));
            }
            InvalidateMeasure();
        }
        foreach (var d in dots)
        {
            if (model.Preview.TryGetValue(d.Ch, out var c)) { d.Fill.Color = Ui.C(c); d.B.BoxShadow = new BoxShadows(new BoxShadow { Blur = 16, Color = Color.FromArgb(204, Ui.C(c).R, Ui.C(c).G, Ui.C(c).B) }); }
            else d.B.BoxShadow = default;
        }
        InvalidateArrange();
    }

    protected override Size MeasureOverride(Size avail)
    {
        foreach (var c in Children) c.Measure(Size.Infinity);
        return new Size(double.IsInfinity(avail.Width) ? 600 : avail.Width, 320);
    }

    protected override Size ArrangeOverride(Size size)
    {
        double w = Math.Min(size.Width * 0.85, size.Height * 0.85 * 16 / 9), h = w * 9 / 16;
        screen = new Rect((size.Width - w) / 2, (size.Height - h) / 2, w, h);
        frame.Arrange(screen);
        var routes = model.Profile.Routes.ToDictionary(r => r.ChannelId);
        foreach (var d in dots)
            if (routes.TryGetValue(d.Ch, out var r)) d.B.Arrange(new Rect(screen.X + r.U * w - 15, screen.Y + r.V * h - 15, 30, 30));
        return size;
    }
}
