using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Lumivane.Core;

namespace Lumivane.App;

public static class Common
{
    /// <summary>Maps the profile symbol names stored by the Mac app to the app's own icons.</summary>
    public static string SymbolIcon(string symbol) => symbol switch
    {
        "film" => "film", "music.note" => "note", "gamecontroller" => "gamepad", "leaf" => "leaf", _ => "tv",
    };

    /// <summary>Runs async work from a click and shows failures instead of losing them.</summary>
    public static void Fire(AppModel m, Func<Task> work)
    {
        _ = Task.Run(async () => { try { await work(); } catch (Exception e) { Dispatcher.UIThread.Post(() => m.Error = e.Message); } });
    }

    public static void FireUi(AppModel m, Func<Task> work)
    {
        async void Go() { try { await work(); } catch (Exception e) { m.Error = e.Message; } }
        Go();
    }

    public static Control ProfileMenu(AppModel m)
    {
        var icon = Ui.Dynamic(() => m.Profile.Symbol, () => new IconView(SymbolIcon(m.Profile.Symbol), 14, "TextPrimary"));
        var content = Ui.H(8, icon, Ui.Live(() => m.Profile.Name), new IconView("chevronUpDown", 11, "TextSecondary") { VerticalAlignment = VerticalAlignment.Center });
        return Ui.MenuButton(content, () => m.Profiles.Select(p => new Ui.MenuEntry(p.Name, () => m.Load(p), Icon: p.Id == m.Profile.Id ? "check" : SymbolIcon(p.Symbol))),
            "Switch profile: display, lights and sync settings load together");
    }

    public static WriteableBitmap? ToBitmap(Frame f)
    {
        if (f.Width <= 0 || f.Height <= 0 || f.Bgra.Length < f.Stride * f.Height) return null;
        var bmp = new WriteableBitmap(new PixelSize(f.Width, f.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var fb = bmp.Lock();
        for (int y = 0; y < f.Height; y++)
            System.Runtime.InteropServices.Marshal.Copy(f.Bgra, y * f.Stride, fb.Address + y * fb.RowBytes, f.Width * 4);
        return bmp;
    }

    public static Window? Owner(Control c) => TopLevel.GetTopLevel(c) as Window;

    public static void Timer(TimeSpan every, Action tick)
    {
        var t = new DispatcherTimer { Interval = every };
        t.Tick += (_, _) => tick();
        t.Start();
        Binder.Current.OnDispose(t.Stop);
    }
}
