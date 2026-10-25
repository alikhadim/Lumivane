using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Media;

namespace Lumivane.App;

/// <summary>A 24x24 vector icon from <see cref="Icons"/>, scaled to <c>size</c>. The brush is a theme resource key or a fixed brush.</summary>
public sealed class IconView : Viewbox
{
    readonly Path path = new();

    public IconView(string name, double size, object brush, double stroke = 1.7)
    {
        Width = Height = size;
        Stretch = Stretch.Uniform;
        path.Data = Icons.Geo(name);
        if (Icons.IsFilled(name)) SetBrush(Path.FillProperty, brush);
        else
        {
            SetBrush(Path.StrokeProperty, brush);
            path.StrokeThickness = stroke; path.StrokeLineCap = PenLineCap.Round; path.StrokeJoin = PenLineJoin.Round;
        }
        Child = new Canvas { Width = 24, Height = 24, Children = { path } };
        IsHitTestVisible = false;
    }

    void SetBrush(AvaloniaProperty p, object brush)
    {
        if (brush is string key) Ui.Tint(path, p, key);
        else if (brush is IBrush b) path.SetValue(p, b);
    }

}
