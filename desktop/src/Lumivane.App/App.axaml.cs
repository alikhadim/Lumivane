using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Lumivane.Core;
using Lumivane.Platform;

namespace Lumivane.App;

public partial class App : Application
{
    public static AppModel? Model { get; private set; }
    public static IPlatform? Platform { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var launch = Program.Launch;
            var platform = Platform ?? PlatformFactory.Create(launch.DataDir);
            var store = new Store(launch.DataDir ?? platform.DataDir);
            var model = new AppModel(platform, null, a => Dispatcher.UIThread.Post(a), launch, store);
            Model = model;
            if (Enum.TryParse<Appearance>(launch.Appearance, true, out var ap)) model.Appearance = ap;
            var window = new MainWindow(model, Shell.Parse(launch.Section));
            desktop.MainWindow = window;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Closing += (_, e) =>
            {   // keep syncing in the tray when the window is closed during a sync
                if (model.Syncing && TrayHost.Available) { e.Cancel = true; window.Hide(); }
            };
            desktop.Exit += (_, _) => { try { model.StopAsync().Wait(2000); } catch { } };
            TrayHost.Attach(this, model, window, desktop);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
