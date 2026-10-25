using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Lumivane.App;

public static class Tray
{
    /// <summary>The K mark rendered once as the window and tray icon.</summary>
    public static WindowIcon WindowIcon()
    {
        var size = new PixelSize(64, 64);
        var bmp = new RenderTargetBitmap(size, new Vector(96, 96));
        var mark = new BrandMark(64);
        mark.Measure(new Size(64, 64)); mark.Arrange(new Rect(0, 0, 64, 64));
        var bg = new Border { Width = 64, Height = 64, CornerRadius = new CornerRadius(14), Background = new SolidColorBrush(Color.FromRgb(28, 28, 30)), Child = mark };
        bg.Measure(new Size(64, 64)); bg.Arrange(new Rect(0, 0, 64, 64));
        bmp.Render(bg);
        return new WindowIcon(bmp);
    }
}
