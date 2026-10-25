using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Lumivane.App;

/// <summary>The K mark, drawn in code so it stays sharp at any size.</summary>
public sealed class BrandMark : Control
{
    public BrandMark(double size) { Width = Height = size; IsHitTestVisible = false; }

    public override void Render(DrawingContext ctx)
    {
        double s = Math.Min(Bounds.Width, Bounds.Height);
        var g = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, s, RelativeUnit.Absolute), EndPoint = new RelativePoint(s, 0, RelativeUnit.Absolute),
            GradientStops = { new GradientStop(Color.Parse("#FF7A1A"), 0), new GradientStop(Color.Parse("#FF2E7A"), 0.5), new GradientStop(Color.Parse("#3B7AFF"), 1) },
        };
        var geo = new StreamGeometry();
        using (var c = geo.Open())
        {
            c.BeginFigure(new Point(0.33 * s, 0.22 * s), false);
            c.LineTo(new Point(0.33 * s, 0.77 * s)); c.LineTo(new Point(0.68 * s, 0.77 * s));
            c.EndFigure(false);
        }
        ctx.DrawGeometry(null, new Pen(g, 0.11 * s, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geo);
        ctx.DrawEllipse(g, null, new Point(0.66 * s, 0.30 * s), 0.06 * s, 0.06 * s);
    }
}
