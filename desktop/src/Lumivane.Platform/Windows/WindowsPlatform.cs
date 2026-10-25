using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lumivane.Core;
using Microsoft.Win32;

namespace Lumivane.Platform.Windows;

[SupportedOSPlatform("windows")]
public sealed class WindowsPlatform : IPlatform, IDisposable
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    readonly object gate = new();
    Thread? watcher;
    volatile bool stopWatching;
    event Action? displaysChanged;

    public WindowsPlatform(string? dataDir = null)
    {
        try { W.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }   // per-monitor v2: coordinates are physical pixels
        W.timeBeginPeriod(1);                                                  // 1 ms timer resolution for steady frame pacing
        DataDir = dataDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Brand.Name);
        Directory.CreateDirectory(DataDir);
        Secrets = new DpapiSecretStore(DataDir);
    }

    public string Name => "Windows";
    public string DataDir { get; }
    public ISecretStore Secrets { get; }
    public string SecretsDescription => "encrypted with your Windows account";
    public bool ScreenAllowed => true;
    public string? ScreenAccessHint => null;

    public IReadOnlyList<LiveDisplay> Displays() => WinDisplays.Enumerate();
    LiveDisplay? Find(long id) => WinDisplays.Enumerate().FirstOrDefault(d => d.NativeId == id);

    public event Action? DisplaysChanged
    {
        add { lock (gate) { displaysChanged += value; if (watcher == null) StartWatcher(); } }
        remove { lock (gate) displaysChanged -= value; }
    }

    // Polling a cheap enumeration avoids needing a message window for WM_DISPLAYCHANGE.
    void StartWatcher()
    {
        watcher = new Thread(() =>
        {
            var last = WinDisplays.Signature(WinDisplays.Enumerate());
            while (!stopWatching)
            {
                Thread.Sleep(1500);
                try
                {
                    var now = WinDisplays.Signature(WinDisplays.Enumerate());
                    if (now != last) { last = now; displaysChanged?.Invoke(); }
                }
                catch { }
            }
        }) { IsBackground = true, Name = "display-watch" };
        watcher.Start();
    }

    public Task<IReadOnlyList<LiveWindow>> WindowsAsync() => Task.Run<IReadOnlyList<LiveWindow>>(() =>
    {
        var list = new List<LiveWindow>();
        var seen = new HashSet<string>();
        uint self = (uint)Environment.ProcessId;
        W.EnumWindows((hwnd, _) =>
        {
            if (!W.IsWindowVisible(hwnd) || W.IsIconic(hwnd)) return true;
            int ex = W.ExStyle(hwnd);
            if ((ex & W.WS_EX_TOOLWINDOW) != 0 && (ex & W.WS_EX_APPWINDOW) == 0) return true;
            if (W.GetWindow(hwnd, W.GW_OWNER) != IntPtr.Zero && (ex & W.WS_EX_APPWINDOW) == 0) return true;
            if (W.DwmGetWindowAttribute(hwnd, W.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;   // on another virtual desktop or hidden by the shell
            var title = W.Title(hwnd);
            if (title.Length == 0 || !W.GetWindowRect(hwnd, out var r) || r.Width < 64 || r.Height < 64) return true;
            W.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == self) return true;
            var app = W.ProcessName(pid);
            if (app.Length == 0) app = "Window";
            var id = SourceID.Window(app, title);
            if (seen.Add(id)) list.Add(new LiveWindow(id, title, app, app, hwnd.ToInt64()));
            return true;
        }, IntPtr.Zero);
        return list.OrderBy(w => w.AppName, StringComparer.OrdinalIgnoreCase).ThenBy(w => w.Title).ToList();
    });

    public Task<IReadOnlyList<AudioApp>> AudioAppsAsync() => Task.Run<IReadOnlyList<AudioApp>>(() => WasapiAudioCapturer.Apps());

    public IScreenCapturer CreateScreenCapturer() => new GdiScreenCapturer(Find);
    public IAudioCapturer CreateAudioCapturer() => new WasapiAudioCapturer();

    public Task<Dictionary<CaptureTarget, Frame>> ThumbnailsAsync(IReadOnlyList<CaptureTarget> targets, int width) => Task.Run(() =>
    {
        var result = new Dictionary<CaptureTarget, Frame>();
        using var grabber = new GdiGrabber();
        var live = WinDisplays.Enumerate();
        foreach (var t in targets)
        {
            try
            {
                Frame? f;
                if (t.IsWindow) f = grabber.Window((IntPtr)t.Id, out _, width);
                else
                {
                    var d = live.FirstOrDefault(x => x.NativeId == t.Id);
                    if (d == null) continue;
                    var g = d.Geometry;
                    f = grabber.Display((int)g.X, (int)g.Y, g.PixelWidth, g.PixelHeight, width);
                }
                if (f != null) result[t] = f;
            }
            catch { }
        }
        return result;
    });

    public bool LaunchAtLogin
    {
        get
        {
            try { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue(Brand.Name) != null; }
            catch { return false; }
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (!value) { k.DeleteValue(Brand.Name, false); return; }
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot find the program path.");
            k.SetValue(Brand.Name, $"\"{exe}\"");
        }
    }

    public void Notify(string title, string body)
    {
        try
        {
            // A toast through the PowerShell app id, which Windows already knows. Text goes through XML escaping and a single-quoted string.
            static string Esc(string s) => SecurityElement.Escape(s).Replace("'", "''");
            var script =
                "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null;" +
                "$x = New-Object Windows.Data.Xml.Dom.XmlDocument;" +
                $"$x.LoadXml('<toast><visual><binding template=\"ToastText02\"><text id=\"1\">{Esc(title)}</text><text id=\"2\">{Esc(body)}</text></binding></visual></toast>');" +
                "$t = New-Object Windows.UI.Notifications.ToastNotification $x;" +
                "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\\WindowsPowerShell\\v1.0\\powershell.exe').Show($t)";
            var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("-NoProfile"); psi.ArgumentList.Add("-NonInteractive"); psi.ArgumentList.Add("-EncodedCommand");
            psi.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
            Process.Start(psi);
        }
        catch { }
    }

    public void Dispose() { stopWatching = true; W.timeEndPeriod(1); }
}

/// <summary>Each value is encrypted with DPAPI for the current Windows user, so the file is useless on any other account or machine.</summary>
[SupportedOSPlatform("windows")]
internal sealed class DpapiSecretStore : ISecretStore
{
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Lumivane.secrets.v1");
    readonly string file;

    public DpapiSecretStore(string dir) { file = Path.Combine(dir, "secrets.json"); }

    public string? Load(string key)
    {
        if (!Read().TryGetValue(key, out var v)) return null;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(v), Entropy, DataProtectionScope.CurrentUser)); }
        catch (CryptographicException) { return null; }
        catch (FormatException) { return null; }
    }

    public void Save(string key, string value)
    {
        var d = Read();
        d[key] = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));
        Write(d);
    }

    public void Delete(string key) { var d = Read(); if (d.Remove(key)) Write(d); }

    Dictionary<string, string> Read()
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? new() : new(); }
        catch { return new(); }
    }

    void Write(Dictionary<string, string> d)
    {
        var tmp = file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(d));
        File.Move(tmp, file, true);
    }
}
