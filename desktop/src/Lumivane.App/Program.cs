using Avalonia;
using Lumivane.Core;

namespace Lumivane.App;

public static class Program
{
    public static LaunchOptions Launch { get; private set; } = new();

    static int? pendingTab;
    /// <summary>The tab asked for with --tab, once; later visits start on the first tab.</summary>
    public static int TakeTab() { var t = pendingTab ?? 0; pendingTab = null; return t; }

    [STAThread]
    public static int Main(string[] args)
    {
        Launch = LaunchOptions.Parse(args); pendingTab = Launch.Tab;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}
