using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Lumivane.Core;

namespace Lumivane.App;

public static class ProfilesPage
{
    static string Summary(AppModel m, Profile p)
    {
        string src = p.Mode switch
        {
            SyncMode.Single => p.PrimarySource is { } s ? m.SourceName(s) : "Built-in display",
            SyncMode.Mirror => p.MirrorSources.Count == 0 ? "Mirror" : "Mirror of " + string.Join(", ", p.MirrorSources.Select(m.SourceName)),
            _ => $"Extend across {p.Routes.Where(r => r.Enabled && r.SourceId != null).Select(r => r.SourceId).Distinct().Count()} displays",
        };
        var a = p.Settings.Audio;
        return $"{src}  ·  {p.Routes.Count(r => r.Enabled)} lights  ·  sound {(a.Enabled ? $"{(int)(a.Influence * 100)}%" : "off")}";
    }

    public static Control Build(AppModel m)
    {
        var list = Ui.Dynamic(() => m.Profile.Id + "|" + string.Join(",", m.Profiles.Select(p => p.Id)), () =>
        {
            var col = Ui.V(16);
            foreach (var p in m.Profiles.ToList()) col.Children.Add(Row(m, p));
            return col;
        });
        var name = new TextBox { Watermark = "For example Movie night on the TV" };
        var save = Ui.Btn("Save", () => { m.SaveCurrentAsNew(name.Text ?? ""); name.Text = ""; });
        Ui.Bind(() => save.IsEnabled = !string.IsNullOrEmpty(name.Text));
        name.TextChanged += (_, _) => save.IsEnabled = !string.IsNullOrEmpty(name.Text);
        var add = Ui.Card("Save the current setup as a new profile", Ui.Cols("*,Auto", name, new Border { Child = save, Margin = new Thickness(8, 0, 0, 0) }));

        return Ui.V(16, Ui.PageHeader("Profiles", "Save a whole setup and switch in one click"), Ui.Banner(m),
            Ui.Wrap("A profile remembers the display, which lights follow it, brightness and all sync and sound settings. Loading one restores them together.", "callout", "secondary"),
            list, add);
    }

    static Control Row(AppModel m, Profile p)
    {
        Profile Cur() => m.Profiles.FirstOrDefault(x => x.Id == p.Id) ?? p;
        bool Active() => p.Id == m.Profile.Id;

        var icon = new IconView(Common.SymbolIcon(p.Symbol), 28, "TextSecondary");
        var iconBox = new Border { Width = 36, Child = icon, VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBox { Text = p.Name, FontWeight = FontWeight.Bold, FontSize = 13, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), MinHeight = 0 };
        name.TextChanged += (_, _) => { if (name.IsFocused && name.Text != Cur().Name) m.Rename(Cur(), name.Text ?? ""); };
        var summary = Ui.Live(() => Summary(m, Cur()), "caption", "secondary");
        var active = Ui.H(6, new IconView("checkCircle", 16, Ui.Green), new TextBlock { Text = "Active", Foreground = Ui.Green, VerticalAlignment = VerticalAlignment.Center });
        var use = Ui.Btn("Use", () => m.Load(Cur()), null, "prominent");
        var del = Ui.IconBtn("trash", () => m.Delete(Cur()), "Delete this profile");
        var card = Ui.Card(null, Ui.Cols("Auto,*,Auto,Auto", iconBox, Ui.V(3, name, summary), new Border { Child = new Grid { Children = { active, use } }, Margin = new Thickness(8, 0), VerticalAlignment = VerticalAlignment.Center }, del));
        Ui.Bind(() =>
        {
            bool a = Active();
            active.IsVisible = a; use.IsVisible = !a;
            ((Border)card).BorderBrush = a ? Ui.Res(card, "Accent") : Ui.Res(card, "CardBorder");
            ((Border)card).BorderThickness = new Thickness(a ? 2 : 1);
            del.IsEnabled = m.Profiles.Count >= 2;
            if (!name.IsFocused && name.Text != Cur().Name) name.Text = Cur().Name;
        });
        return card;
    }
}
