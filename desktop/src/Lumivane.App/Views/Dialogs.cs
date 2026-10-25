using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lumivane.Core;

namespace Lumivane.App;

public static class Dialogs
{
    static Window Base(string title, double width)
    {
        var w = new Window
        {
            Title = title, Width = width, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
        };
        return w;
    }

    public static async Task Alert(Window owner, string title, string message)
    {
        var w = Base(Brand.Name, 380);
        var ok = new Button { Content = "OK", MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Right };
        ok.Classes.Add("prominent");
        ok.Click += (_, _) => w.Close();
        w.Content = new Border
        {
            Padding = new Thickness(22),
            Child = Ui.V(10,
                Ui.Label(title, "title3"),
                Ui.Wrap(message, "callout", "secondary"),
                new Border { Height = 6 }, ok),
        };
        await w.ShowDialog(owner);
    }

    /// <summary>Checklist of lights with an optional name field. Used for zones and sync areas.</summary>
    public static async Task PickLights(Window owner, string title, string initialName, IEnumerable<string> initialIds, IReadOnlyList<HueLight> allLights,
        bool showName, bool onlyEntertainment, Action<string, IReadOnlyList<string>> onSave, Action? onDelete = null)
    {
        var lights = onlyEntertainment ? allLights.Where(l => l.SupportsEntertainment).ToList() : allLights.ToList();
        var selected = new HashSet<string>(initialIds);
        var w = Base(title, 420);
        var name = new TextBox { Text = initialName, Watermark = "Name", IsVisible = showName };
        var save = new Button { Content = "Save", MinWidth = 70 }; save.Classes.Add("prominent");
        void Refresh() => save.IsEnabled = selected.Count > 0;
        var list = new StackPanel { Spacing = 2 };
        foreach (var l in lights)
        {
            var id = l.Id;
            var cb = new CheckBox { Content = l.Name, IsChecked = selected.Contains(id), Margin = new Thickness(8, 5) };
            cb.IsCheckedChanged += (_, _) => { if (cb.IsChecked == true) selected.Add(id); else selected.Remove(id); Refresh(); };
            list.Children.Add(cb);
        }
        var scroll = new ScrollViewer { Content = list, Height = 240, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        var listBox = new Border { Child = scroll, CornerRadius = new CornerRadius(8) };
        Ui.Tint(listBox, Border.BackgroundProperty, "FieldBg");
        var cancel = new Button { Content = "Cancel", MinWidth = 70 };
        cancel.Click += (_, _) => w.Close();
        save.Click += (_, _) => { onSave(string.IsNullOrWhiteSpace(name.Text) ? "My zone" : name.Text!, selected.ToList()); w.Close(); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel); buttons.Children.Add(save);
        Control bottom = buttons;
        if (onDelete != null)
        {
            var del = new Button { Content = "Delete", Foreground = Ui.Red };
            del.Click += (_, _) => { onDelete(); w.Close(); };
            bottom = Ui.Split(del, buttons);
        }
        Refresh();
        w.Content = new Border { Padding = new Thickness(22), Child = Ui.V(14, Ui.Label(title, "title2"), name, listBox, bottom) };
        await w.ShowDialog(owner);
    }
}
