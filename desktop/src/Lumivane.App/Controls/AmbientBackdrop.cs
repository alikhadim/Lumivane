using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Lumivane.Core;

namespace Lumivane.App;

/// <summary>Soft color wash behind the window that follows what the lights are showing.</summary>
public sealed class AmbientBackdrop : Panel
{
    readonly Ellipse[] blobs = new Ellipse[3];
    readonly Color[] shown = new Color[3];
    readonly AppModel model;

    public AmbientBackdrop(AppModel model)
    {
        this.model = model;
        IsHitTestVisible = false; ClipToBounds = true;
        for (int i = 0; i < 3; i++)
        {
            blobs[i] = new Ellipse { Width = 460, Height = 460, Opacity = 0, Effect = new BlurEffect { Radius = 120 }, Fill = new SolidColorBrush(Colors.Transparent) };
            Children.Add(blobs[i]);
        }
    }

    public void Update()
    {
        var v = model.Preview.OrderBy(k => k.Key).Select(k => k.Value).ToList();
        bool on = model.Syncing && v.Count > 0;
        if (!on) { foreach (var b in blobs) if (b.Opacity != 0) b.Opacity = 0; return; }
        int a = v.Count / 3, b2 = v.Count * 2 / 3;
        var target = new[] { Avg(v, 0, Math.Max(a, 1)), Avg(v, Math.Max(a, 0), Math.Max(b2, 1)), Avg(v, Math.Min(b2, v.Count - 1), v.Count) };
        for (int i = 0; i < 3; i++)
        {
            bool first = blobs[i].Opacity == 0;
            shown[i] = first ? target[i] : Lerp(shown[i], target[i], 0.3);
            ((SolidColorBrush)blobs[i].Fill!).Color = shown[i];
            blobs[i].Opacity = 0.26;
        }
    }

    static Color Avg(List<RGB> v, int from, int to)
    {
        if (to <= from) return Colors.Transparent;
        float r = 0, g = 0, b = 0; int n = to - from;
        for (int i = from; i < to; i++) { r += v[i].R; g += v[i].G; b += v[i].B; }
        return Ui.C(new RGB(r / n, g / n, b / n));
    }

    static Color Lerp(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    protected override Size MeasureOverride(Size s) { foreach (var c in Children) c.Measure(new Size(460, 460)); return new Size(0, 0); }

    protected override Size ArrangeOverride(Size size)
    {
        for (int i = 0; i < 3; i++)
        {
            double cx = size.Width / 2 + (i - 1) * 380, cy = size.Height / 2 + (i == 1 ? -240 : 220);
            blobs[i].Arrange(new Rect(cx - 230, cy - 230, 460, 460));
        }
        return size;
    }
}
