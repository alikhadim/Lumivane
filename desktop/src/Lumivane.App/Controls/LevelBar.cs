using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Lumivane.App;

public sealed class LevelBar : Control
{
    float value;
    readonly IBrush tint;
    public LevelBar(IBrush tint) { this.tint = tint; Height = 10; HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch; }
    public float Value { get => value; set { var v = Math.Clamp(value, 0, 1); if (Math.Abs(v - this.value) > 0.004f) { this.value = v; InvalidateVisual(); } } }

    public override void Render(DrawingContext ctx)
    {
        var r = new Rect(0, 0, Bounds.Width, Bounds.Height);
        ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#8E8E93"), 0.2), null, r, 5, 5);
        double w = Math.Max(10, r.Width * value);
        ctx.DrawRectangle(tint, null, new Rect(0, 0, w, r.Height), 5, 5);
    }
}
