using Avalonia;
using Avalonia.Controls;

namespace Lumivane.App;

/// <summary>Equal-width cells, as many columns as fit the minimum width.</summary>
public sealed class AdaptiveGrid : Panel
{
    readonly double min, gap;
    public AdaptiveGrid(double minWidth, double spacing) { min = minWidth; gap = spacing; }

    int Cols(double w) => Math.Max(1, (int)Math.Floor((w + gap) / (min + gap)));

    protected override Size MeasureOverride(Size avail)
    {
        double w = double.IsInfinity(avail.Width) ? 800 : avail.Width;
        int cols = Cols(w); double cell = (w - (cols - 1) * gap) / cols, y = 0, rowH = 0;
        for (int i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(new Size(cell, double.PositiveInfinity));
            rowH = Math.Max(rowH, Children[i].DesiredSize.Height);
            if ((i + 1) % cols == 0 || i == Children.Count - 1) { y += rowH + gap; rowH = 0; }
        }
        return new Size(w, Math.Max(0, y - gap));
    }

    protected override Size ArrangeOverride(Size size)
    {
        int cols = Cols(size.Width); double cell = (size.Width - (cols - 1) * gap) / cols, y = 0;
        for (int start = 0; start < Children.Count; start += cols)
        {
            int end = Math.Min(start + cols, Children.Count);
            double rowH = 0;
            for (int i = start; i < end; i++) rowH = Math.Max(rowH, Children[i].DesiredSize.Height);
            for (int i = start; i < end; i++) Children[i].Arrange(new Rect((i - start) * (cell + gap), y, cell, rowH));
            y += rowH + gap;
        }
        return size;
    }
}
