using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Lumivane.App;

/// <summary>Text payload drag and drop ("ch:3", "grp:abc") between lights, rooms and displays.</summary>
public static class Dnd
{
    public static void Draggable(Control c, string payload)
    {
        Point? start = null;
        c.PointerPressed += (_, e) => { if (e.GetCurrentPoint(c).Properties.IsLeftButtonPressed) start = e.GetPosition(c); };
        c.PointerReleased += (_, _) => start = null;
        c.PointerMoved += async (_, e) =>
        {
            if (start is not { } s || (e.GetPosition(c) - s).X is var dx && (e.GetPosition(c) - s).Y is var dy && dx * dx + dy * dy < 36) return;
            start = null;
            var d = new DataObject(); d.Set(DataFormats.Text, payload);
#pragma warning disable CS0618
            await DragDrop.DoDragDrop(e, d, DragDropEffects.Copy);
#pragma warning restore CS0618
        };
    }

    public static void DropTarget(Control c, Func<string, bool> accept)
    {
        DragDrop.SetAllowDrop(c, true);
        c.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.Data.GetText() is { } ? DragDropEffects.Copy : DragDropEffects.None);
        c.AddHandler(DragDrop.DropEvent, (_, e) => { if (e.Data.GetText() is { } t && accept(t)) e.Handled = true; });
    }
}
