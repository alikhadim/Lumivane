using Lumivane.Core;

namespace Lumivane.Platform;

/// <summary>No capture, no audio, no displays. Used for Demo Mode on systems without a backend and for UI tests.</summary>
public sealed class NullPlatform : IPlatform
{
    public NullPlatform(string? dataDir = null) { DataDir = dataDir ?? Path.Combine(Path.GetTempPath(), "lumivane-null"); }
    public string Name => "none";
    public string DataDir { get; }
    public IReadOnlyList<LiveDisplay> Displays() => Array.Empty<LiveDisplay>();
    public Task<IReadOnlyList<LiveWindow>> WindowsAsync() => Task.FromResult<IReadOnlyList<LiveWindow>>(Array.Empty<LiveWindow>());
    public Task<IReadOnlyList<AudioApp>> AudioAppsAsync() => Task.FromResult<IReadOnlyList<AudioApp>>(Array.Empty<AudioApp>());
    public IScreenCapturer CreateScreenCapturer() => throw new NotSupportedException("Screen capture is not available on this system.");
    public IAudioCapturer CreateAudioCapturer() => throw new NotSupportedException("Audio capture is not available on this system.");
    public Task<Dictionary<CaptureTarget, Frame>> ThumbnailsAsync(IReadOnlyList<CaptureTarget> targets, int width) => Task.FromResult(new Dictionary<CaptureTarget, Frame>());
    public bool ScreenAllowed => true;
    public string? ScreenAccessHint => null;
    public bool LaunchAtLogin { get; set; }
    public void Notify(string title, string body) { }
    public ISecretStore Secrets { get; } = new MemorySecretStore();
    public event Action? DisplaysChanged { add { } remove { } }
}
