using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Lumivane.Core;

namespace Lumivane.App;

public sealed class MainWindow : Window
{
    readonly AppModel model;
    readonly Binder binder = new();
    readonly DispatcherTimer tick = new() { Interval = TimeSpan.FromMilliseconds(33) };
    bool dirty = true, alertOpen;

    public MainWindow(AppModel model, NavSection start)
    {
        this.model = model;
        Title = Brand.Name; Width = 960; Height = 692; MinWidth = 960; MinHeight = 640;
        Icon = Tray.WindowIcon();
        Ui.MainWindow = this;
        Ui.Invalidate = () => dirty = true;
        model.PropertyChanged += (_, _) => dirty = true;

        Content = binder.Build(() => Ui.Dynamic(() => model.Onboarded && model.HasSetup ? "main" : "onboarding",
            () => model.Onboarded && model.HasSetup ? Shell.Build(model, start) : Onboarding.Build(model)));

        tick.Tick += (_, _) => Pump();
        tick.Start();
        Opened += (_, _) => { ApplyAppearance(); _ = model.BootOnceAsync(); };
    }

    void Pump()
    {
        ApplyAppearance();
        if (dirty) { dirty = false; binder.Run(); }
        if (model.Error is { } e && !alertOpen)
        {
            alertOpen = true;
            _ = Dialogs.Alert(this, "Something needs attention", e).ContinueWith(_ => Dispatcher.UIThread.Post(() => { model.Error = null; alertOpen = false; }));
        }
    }

    void ApplyAppearance()
    {
        var v = model.Appearance switch { Appearance.Light => ThemeVariant.Light, Appearance.Dark => ThemeVariant.Dark, _ => ThemeVariant.Default };
        if (Application.Current!.RequestedThemeVariant != v) Application.Current.RequestedThemeVariant = v;
    }
}
