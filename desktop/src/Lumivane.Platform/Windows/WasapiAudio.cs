using System.Runtime.Versioning;
using Lumivane.Core;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace Lumivane.Platform.Windows;

/// <summary>System sound (WASAPI loopback of the default output) and microphone (default input), mixed down to mono.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WasapiAudioCapturer : IAudioCapturer, IMMNotificationClient
{
    static readonly Guid PcmSubFormat = new("00000001-0000-0010-8000-00aa00389b71");
    readonly object gate = new();
    MMDeviceEnumerator? enumerator;
    WasapiCapture? cap;
    SilenceFiller? filler;
    ProcessLoopbackCapture? appCap;
    AudioInput? input;
    Action<float[], double, double>? sink;
    volatile bool stopping;
    public Action<string>? OnStop { get; set; }

    // On a pool thread: the COM audio calls want a free-threaded apartment, and NAudio posts some events to the caller's synchronization context.
    public Task StartAsync(AudioInput input, Action<float[], double, double> onSamples) => Task.Run(() =>
    {
        lock (gate)
        {
            stopping = false; this.input = input; sink = onSamples;
            if (input.Kind == "app")
            {
                appCap = new ProcessLoopbackCapture(input.App ?? "", onSamples) { OnStop = m => { if (!stopping) OnStop?.Invoke(m); } };
                appCap.Start();
                return;
            }
            Open();
            enumerator = new MMDeviceEnumerator();
            enumerator.RegisterEndpointNotificationCallback(this);
        }
    });

    void Open()
    {
        var c = input!.Kind == "microphone" ? new WasapiCapture() : new WasapiLoopbackCapture();
        var fmt = c.WaveFormat;
        int rate = fmt.SampleRate;
        filler ??= new SilenceFiller(sink!, rate);
        var f = filler;
        if (f.Rate != rate) { f.Dispose(); f = filler = new SilenceFiller(sink!, rate); }
        c.DataAvailable += (_, e) =>
        {
            if (stopping || e.BytesRecorded == 0) return;
            f.Data(ToMono(e.Buffer, e.BytesRecorded, fmt), rate, Clock.Now);
        };
        c.RecordingStopped += (_, e) =>
        {
            if (stopping || !ReferenceEquals(c, cap)) return;
            if (e.Exception != null) OnStop?.Invoke("The audio device stopped: " + e.Exception.Message);
        };
        cap = c;
        c.StartRecording();
    }

    /// <summary>Averages all channels into one. Handles the formats WASAPI shared mode hands out: float32 (normal), 16, 24 and 32-bit integer.</summary>
    internal static float[] ToMono(byte[] buf, int bytes, WaveFormat fmt)
    {
        int ch = Math.Max(1, fmt.Channels), bits = fmt.BitsPerSample, bps = bits / 8;
        bool isInt = fmt.Encoding == WaveFormatEncoding.Pcm || (fmt is WaveFormatExtensible x && x.SubFormat == PcmSubFormat);
        int frames = bytes / (bps * ch);
        var mono = new float[frames];
        int o = 0;
        for (int i = 0; i < frames; i++)
        {
            float sum = 0;
            for (int c = 0; c < ch; c++, o += bps)
            {
                sum += bits switch
                {
                    32 when !isInt => BitConverter.ToSingle(buf, o),
                    32 => BitConverter.ToInt32(buf, o) / 2147483648f,
                    24 => ((buf[o] << 8 | buf[o + 1] << 16 | buf[o + 2] << 24) >> 8) / 8388608f,
                    16 => BitConverter.ToInt16(buf, o) / 32768f,
                    _ => 0f,
                };
            }
            mono[i] = sum / ch;
        }
        return mono;
    }

    void Restart()
    {
        lock (gate)
        {
            if (stopping || input == null || input.Kind == "app") return;
            try
            {
                var old = cap; cap = null;
                try { old?.StopRecording(); } catch { }
                old?.Dispose();
                Open();
            }
            catch (Exception e) { OnStop?.Invoke("The audio device changed and could not be reopened: " + e.Message); }
        }
    }

    // The default output or input changed (headphones plugged in, Bluetooth connected): follow it.
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (role != Role.Multimedia && role != Role.Console) return;
        bool mine = input?.Kind == "microphone" ? flow == DataFlow.Capture : flow == DataFlow.Render;
        if (mine) Task.Run(async () => { await Task.Delay(300); Restart(); });
    }
    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public Task StopAsync() { Cleanup(); return Task.CompletedTask; }
    public void Dispose() => Cleanup();

    void Cleanup()
    {
        lock (gate)
        {
            stopping = true;
            try { enumerator?.UnregisterEndpointNotificationCallback(this); } catch { }
            enumerator?.Dispose(); enumerator = null;
            var c = cap; cap = null;
            try { c?.StopRecording(); } catch { }
            c?.Dispose();
            appCap?.Dispose(); appCap = null;
            filler?.Dispose(); filler = null;
        }
    }

    /// <summary>Apps that currently hold an active playback session on the default output, one entry per executable.</summary>
    public static List<AudioApp> Apps()
    {
        var result = new Dictionary<string, AudioApp>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var en = new MMDeviceEnumerator();
            using var dev = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = dev.AudioSessionManager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                if (s.State != AudioSessionState.AudioSessionStateActive || s.IsSystemSoundsSession) continue;
                uint pid = s.GetProcessID;
                if (pid == 0 || pid == Environment.ProcessId) continue;
                var name = ProcessLoopbackCapture.ExeName((int)pid);
                if (name.Length == 0 || result.ContainsKey(name)) continue;
                var shown = !string.IsNullOrWhiteSpace(s.DisplayName) && !s.DisplayName.StartsWith("@") ? s.DisplayName : name;
                result[name] = new AudioApp(name, shown);
            }
        }
        catch { }
        return result.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
