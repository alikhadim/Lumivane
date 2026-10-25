using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Lumivane.Core;

namespace Lumivane.Platform.Linux;

public sealed class LinuxPlatform : IPlatform, IDisposable
{
    readonly object gate = new();
    IntPtr dpy;
    List<LiveDisplay> displays = new();
    Thread? watcher;
    volatile bool stopWatching;
    event Action? displaysChanged;

    public LinuxPlatform(string? dataDir = null)
    {
        DataDir = dataDir ?? Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c ? c : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "lumivane");
        Directory.CreateDirectory(DataDir);
        Secrets = new LinuxSecretStore(DataDir);
        try { X.InitThreadsOnce(); X.InstallErrorHandler(); dpy = X.XOpenDisplay(null); } catch (DllNotFoundException) { dpy = IntPtr.Zero; } catch (EntryPointNotFoundException) { dpy = IntPtr.Zero; }
        if (dpy != IntPtr.Zero) displays = X11Displays.Enumerate(dpy);
    }

    public string Name => "Linux";
    public string DataDir { get; }
    public ISecretStore Secrets { get; }
    public string SecretsDescription => ((LinuxSecretStore)Secrets).Description;

    public static bool IsWayland => Environment.GetEnvironmentVariable("XDG_SESSION_TYPE")?.Equals("wayland", StringComparison.OrdinalIgnoreCase) == true;

    public bool ScreenAllowed => dpy != IntPtr.Zero && !IsWayland;

    public string? ScreenAccessHint =>
        dpy == IntPtr.Zero ? "No X11 display found. Run Lumivane inside an X11 session."
        : IsWayland ? "This is a Wayland session, which does not let apps read the screen. Log out and choose the X11 session (for example \"Ubuntu on Xorg\" or \"Plasma (X11)\") on the login screen."
        : null;

    public IReadOnlyList<LiveDisplay> Displays()
    {
        if (dpy == IntPtr.Zero) return Array.Empty<LiveDisplay>();
        lock (gate) { displays = X11Displays.Enumerate(dpy); return displays.ToList(); }
    }

    LiveDisplay? Find(long id) { lock (gate) { return X11Displays.Enumerate(dpy).FirstOrDefault(d => d.NativeId == id); } }

    public event Action? DisplaysChanged
    {
        add { lock (gate) { displaysChanged += value; if (watcher == null && dpy != IntPtr.Zero) StartWatcher(); } }
        remove { lock (gate) displaysChanged -= value; }
    }

    void StartWatcher()
    {
        watcher = new Thread(() =>
        {
            var d = X.XOpenDisplay(null);
            if (d == IntPtr.Zero) return;
            try
            {
                if (X.XRRQueryExtension(d, out _, out _) == 0) return;
                X.XRRSelectInput(d, X.XDefaultRootWindow(d), (int)(X.RRScreenChangeNotifyMask | X.RRCrtcChangeNotifyMask | X.RROutputChangeNotifyMask));
                var ev = Marshal.AllocHGlobal(256);
                try
                {
                    while (!stopWatching)
                    {
                        bool changed = false;
                        while (X.XPending(d) > 0) { X.XNextEvent(d, ev); changed = true; }
                        if (changed) { Thread.Sleep(150); while (X.XPending(d) > 0) X.XNextEvent(d, ev); displaysChanged?.Invoke(); }
                        Thread.Sleep(250);
                    }
                }
                finally { Marshal.FreeHGlobal(ev); }
            }
            finally { X.XCloseDisplay(d); }
        }) { IsBackground = true, Name = "x11-hotplug" };
        watcher.Start();
    }

    public Task<IReadOnlyList<LiveWindow>> WindowsAsync() => Task.Run<IReadOnlyList<LiveWindow>>(() =>
    {
        var list = new List<LiveWindow>();
        var d = X.XOpenDisplay(null);
        if (d == IntPtr.Zero) return list;
        try
        {
            var root = X.XDefaultRootWindow(d);
            long self = Environment.ProcessId;
            var seen = new HashSet<string>();
            foreach (var w in X.GetLongs(d, root, "_NET_CLIENT_LIST"))
            {
                var win = (IntPtr)w;
                if (X.XGetWindowAttributes(d, win, out var a) == 0 || a.map_state != 2 || a.width < 64 || a.height < 64) continue;
                var pid = X.GetLongs(d, win, "_NET_WM_PID");
                if (pid.Length > 0 && pid[0] == self) continue;
                var title = Text(X.GetBytes(d, win, "_NET_WM_NAME", "UTF8_STRING")) ?? Text(X.GetBytes(d, win, "WM_NAME")) ?? "";
                if (title.Length == 0) continue;
                var cls = X.GetBytes(d, win, "WM_CLASS");
                var parts = cls == null ? Array.Empty<string>() : Encoding.UTF8.GetString(cls).Split('\0', StringSplitOptions.RemoveEmptyEntries);
                var app = parts.Length > 1 ? parts[1] : parts.Length > 0 ? parts[0] : "Window";
                var id = SourceID.Window(app, title);
                if (seen.Add(id)) list.Add(new LiveWindow(id, title, app, app, w));
            }
        }
        finally { X.XCloseDisplay(d); }
        return list.OrderBy(w => w.AppName).ThenBy(w => w.Title).ToList();
    });

    static string? Text(byte[]? b) => b == null || b.Length == 0 ? null : Encoding.UTF8.GetString(b).TrimEnd('\0');

    public Task<IReadOnlyList<AudioApp>> AudioAppsAsync() => Task.Run<IReadOnlyList<AudioApp>>(() => PulseAudioCapturer.Apps());

    public IScreenCapturer CreateScreenCapturer() => new X11ScreenCapturer(Find);
    public IAudioCapturer CreateAudioCapturer() => new PulseAudioCapturer();

    public Task<Dictionary<CaptureTarget, Frame>> ThumbnailsAsync(IReadOnlyList<CaptureTarget> targets, int width) => Task.Run(() =>
    {
        var result = new Dictionary<CaptureTarget, Frame>();
        var d = X.XOpenDisplay(null);
        if (d == IntPtr.Zero) return result;
        try
        {
            var root = X.XDefaultRootWindow(d);
            var live = X11Displays.Enumerate(d);
            foreach (var t in targets)
            {
                try
                {
                    Frame? f;
                    if (t.IsWindow) f = X11ScreenCapturer.GrabWindow(d, (IntPtr)t.Id, out _, width);
                    else
                    {
                        var ld = live.FirstOrDefault(x => x.NativeId == t.Id);
                        if (ld == null) continue;
                        using var g = new Grabber(d, root, (int)ld.Geometry.X, (int)ld.Geometry.Y, ld.Geometry.PixelWidth, ld.Geometry.PixelHeight, false);
                        f = g.Grab(width);
                    }
                    if (f != null) result[t] = f;
                }
                catch { }
            }
        }
        finally { X.XCloseDisplay(d); }
        return result;
    });

    string AutostartFile => Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c ? c : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "autostart", "lumivane.desktop");

    public bool LaunchAtLogin
    {
        get => File.Exists(AutostartFile);
        set
        {
            if (!value) { if (File.Exists(AutostartFile)) File.Delete(AutostartFile); return; }
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot find the program path.");
            Directory.CreateDirectory(Path.GetDirectoryName(AutostartFile)!);
            File.WriteAllText(AutostartFile, $"[Desktop Entry]\nType=Application\nName={Brand.Name}\nComment={Brand.Tagline}\nExec=\"{exe}\"\nTerminal=false\nX-GNOME-Autostart-enabled=true\n");
        }
    }

    public void Notify(string title, string body)
    {
        try
        {
            var psi = new ProcessStartInfo("notify-send") { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("--app-name=" + Brand.Name); psi.ArgumentList.Add(title); psi.ArgumentList.Add(body);
            Process.Start(psi);
        }
        catch { }
    }

    public void Dispose() { stopWatching = true; watcher?.Join(1000); if (dpy != IntPtr.Zero) { X.XCloseDisplay(dpy); dpy = IntPtr.Zero; } }
}

/// <summary>Uses the desktop keyring (libsecret's secret-tool) when it exists, otherwise a file only the user can read.</summary>
internal sealed class LinuxSecretStore : ISecretStore
{
    readonly string file;
    readonly bool keyring;
    public string Description => keyring ? "kept in your desktop keyring" : "kept in a file only your user account can read";

    public LinuxSecretStore(string dir)
    {
        file = Path.Combine(dir, "secrets.json");
        keyring = Tool.Exists("secret-tool") && Tool.Run("secret-tool", new[] { "search", "--all", "service", "lumivane-probe" }, null, 2000).Code <= 1 && SessionBusAvailable();
    }

    static bool SessionBusAvailable() => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS"));

    public string? Load(string key)
    {
        if (keyring)
        {
            var r = Tool.Run("secret-tool", new[] { "lookup", "service", "lumivane", "account", key }, null, 4000);
            if (r.Code == 0 && r.Out.Length > 0) return r.Out;
        }
        return ReadFile().TryGetValue(key, out var v) ? v : null;
    }

    public void Save(string key, string value)
    {
        if (keyring && Tool.Run("secret-tool", new[] { "store", "--label=Lumivane " + key, "service", "lumivane", "account", key }, value, 4000).Code == 0) return;
        var d = ReadFile(); d[key] = value; WriteFile(d);
    }

    public void Delete(string key)
    {
        if (keyring) Tool.Run("secret-tool", new[] { "clear", "service", "lumivane", "account", key }, null, 4000);
        var d = ReadFile(); if (d.Remove(key)) WriteFile(d);
    }

    Dictionary<string, string> ReadFile()
    {
        try { return File.Exists(file) ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? new() : new(); }
        catch { return new(); }
    }

    void WriteFile(Dictionary<string, string> d)
    {
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, System.Text.Json.JsonSerializer.Serialize(d));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(tmp, file, true);
    }
}

internal static class Tool
{
    public static bool Exists(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':').Any(p => p.Length > 0 && File.Exists(Path.Combine(p, name)));

    public static (int Code, string Out) Run(string exe, IEnumerable<string> args, string? stdin, int timeoutMs)
    {
        try
        {
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = stdin != null, CreateNoWindow = true };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            if (stdin != null) { p.StandardInput.Write(stdin); p.StandardInput.Close(); }
            var o = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return (-1, ""); }
            return (p.ExitCode, o.Result.TrimEnd('\n'));
        }
        catch { return (-1, ""); }
    }
}
