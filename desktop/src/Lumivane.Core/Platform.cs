using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.ComponentModel;

namespace Lumivane.Core;

/// <summary>A display (Id = platform handle) or window (Id = native window id) that can be captured.</summary>
public readonly record struct CaptureTarget(bool IsWindow, long Id)
{
    public static CaptureTarget Display(long id) => new(false, id);
    public static CaptureTarget Window(long id) => new(true, id);
}

public sealed record LiveDisplay(DisplayGeometry Geometry, long NativeId);
public sealed record LiveWindow(string Id, string Title, string AppName, string AppId, long NativeId);
public sealed record AudioApp(string Id, string Name);

/// <summary>A downscaled BGRA frame. The buffer is owned by the capturer and only valid during the callback.</summary>
public sealed class Frame
{
    public byte[] Bgra { get; set; } = Array.Empty<byte>();
    public int Width { get; set; }
    public int Height { get; set; }
    public int Stride { get; set; }
}

public interface IScreenCapturer : IDisposable
{
    /// <summary>Raised when the source disappears (window closed, display unplugged).</summary>
    Action<string>? OnStop { get; set; }
    /// <summary>Starts delivering ~256 px wide frames. The callback gets the frame and the capture latency in ms.</summary>
    Task StartAsync(CaptureTarget target, int fps, Action<Frame, double> onFrame);
    Task StopAsync();
}

public interface IAudioCapturer : IDisposable
{
    Action<string>? OnStop { get; set; }
    /// <summary>Delivers mono samples, the sample rate, and the host-clock time (seconds) at the end of the buffer.</summary>
    Task StartAsync(AudioInput input, Action<float[], double, double> onSamples);
    Task StopAsync();
}

public interface ISecretStore
{
    string? Load(string key);
    void Save(string key, string value);
    void Delete(string key);
}

/// <summary>Everything that differs between Windows, Linux and the test host.</summary>
public interface IPlatform
{
    string Name { get; }
    string DataDir { get; }
    IReadOnlyList<LiveDisplay> Displays();
    Task<IReadOnlyList<LiveWindow>> WindowsAsync();
    Task<IReadOnlyList<AudioApp>> AudioAppsAsync();
    IScreenCapturer CreateScreenCapturer();
    IAudioCapturer CreateAudioCapturer();
    Task<Dictionary<CaptureTarget, Frame>> ThumbnailsAsync(IReadOnlyList<CaptureTarget> targets, int width);
    /// <summary>False when the OS blocks screen capture until the user allows it (Wayland portals).</summary>
    bool ScreenAllowed { get; }
    string? ScreenAccessHint { get; }
    bool LaunchAtLogin { get; set; }
    void Notify(string title, string body);
    ISecretStore Secrets { get; }
    /// <summary>Where the bridge key lives, in plain words, for the privacy summary.</summary>
    string SecretsDescription => "stored on this computer";
    /// <summary>Raised when displays are added, removed or rearranged.</summary>
    event Action? DisplaysChanged;
}

public static class Clock
{
    static readonly long t0 = Stopwatch.GetTimestamp();
    /// <summary>Monotonic seconds.</summary>
    public static double Now => (Stopwatch.GetTimestamp() - t0) / (double)Stopwatch.Frequency;
}

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Raise(name); return true;
    }
}

public static class Brand
{
    public const string Name = "Lumivane";
    public const string Tagline = "Light that follows the picture";
    public const string AreaName = Name;
    public const string AppId = "com.lumivane.desktop";
}
