using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Lumivane.Core;

namespace Lumivane.App;

/// <summary>The displays laid out as the operating system arranges them.</summary>
public sealed class ArrangementView : Panel
{
    readonly AppModel model;
    readonly List<(DisplayGeometry D, Border B)> items = new();
    string key = "\0";
    readonly TextBlock empty = Ui.Label("No display found", "secondary");

    public ArrangementView(AppModel model) { this.model = model; Height = 170; }

    public void Update()
    {
        var ds = model.Displays.Where(d => d.IsConnected).ToList();
        var k = string.Join("|", ds.Select(d => $"{d.Key}:{d.X}:{d.Y}:{d.Width}:{d.Height}:{d.Name}:{model.IsSelected(d.Id)}"));
        if (k == key) return;
        key = k;
        Children.Clear(); items.Clear();
        if (ds.Count == 0) { empty.HorizontalAlignment = HorizontalAlignment.Center; empty.VerticalAlignment = VerticalAlignment.Center; Children.Add(empty); InvalidateArrange(); return; }
        for (int i = 0; i < ds.Count; i++)
        {
            var d = ds[i];
            bool sel = model.IsSelected(d.Id);
            var b = new Border
            {
                CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(2), Cursor = new Cursor(StandardCursorType.Hand),
                Background = sel ? new SolidColorBrush(Color.Parse("#0A84FF"), 0.3) : new SolidColorBrush(Color.Parse("#8E8E93"), 0.15),
                BorderBrush = sel ? Ui.BlueBrush : new SolidColorBrush(Color.Parse("#8E8E93"), 0.5),
                Padding = new Thickness(4),
                Child = new StackPanel
                {
                    Spacing = 2, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        Center(Ui.Label((i + 1).ToString(), "headline")),
                        Center(new TextBlock { Text = d.Name, FontSize = 11, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis }),
                        Center(Ui.Label(d.Resolution, "secondary", "caption")),
                    },
                },
            };
            var id = d.Id;
            b.PointerPressed += (_, _) =>
            {
                switch (model.Profile.Mode) { case SyncMode.Single: model.SetPrimary(id); break; case SyncMode.Mirror: model.ToggleMirror(id); break; }
            };
            Dnd.DropTarget(b, p => model.Drop(p, id));
            Children.Add(b); items.Add((d, b));
        }
        InvalidateMeasure();
    }

    static T Center<T>(T c) where T : Control { c.HorizontalAlignment = HorizontalAlignment.Center; return c; }

    protected override Size MeasureOverride(Size avail)
    {
        foreach (var c in Children) c.Measure(Size.Infinity);
        return new Size(double.IsInfinity(avail.Width) ? 400 : avail.Width, 170);
    }

    protected override Size ArrangeOverride(Size size)
    {
        if (items.Count == 0) { foreach (var c in Children) c.Arrange(new Rect(size)); return size; }
        double minX = items.Min(i => i.D.X), maxX = items.Max(i => i.D.X + i.D.Width), minY = items.Min(i => i.D.Y), maxY = items.Max(i => i.D.Y + i.D.Height);
        double scale = Math.Min(size.Width / (maxX - minX), size.Height / (maxY - minY)) * 0.96;
        double ox = (size.Width - (maxX - minX) * scale) / 2, oy = (size.Height - (maxY - minY) * scale) / 2;
        foreach (var (d, b) in items)
            b.Arrange(new Rect(ox + (d.X - minX) * scale, oy + (d.Y - minY) * scale, d.Width * scale, d.Height * scale));
        return size;
    }
}
