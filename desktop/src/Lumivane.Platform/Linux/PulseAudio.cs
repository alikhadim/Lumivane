using System.Runtime.InteropServices;
using Lumivane.Core;

namespace Lumivane.Platform.Linux;

/// <summary>A connected PulseAudio context on its own threaded main loop.</summary>
internal sealed class PulseSession : IDisposable
{
    public IntPtr Loop { get; private set; }
    public IntPtr Context { get; private set; }
    readonly Pa.ContextNotify stateCb;
    bool disposed;

    public PulseSession(string clientName)
    {
        Loop = Pa.pa_threaded_mainloop_new();
        if (Loop == IntPtr.Zero) throw new InvalidOperationException("Cannot start the audio loop.");
        stateCb = (_, _) => Pa.pa_threaded_mainloop_signal(Loop, 0);
        Context = Pa.pa_context_new(Pa.pa_threaded_mainloop_get_api(Loop), clientName);
        Pa.pa_context_set_state_callback(Context, stateCb, IntPtr.Zero);
        Pa.pa_threaded_mainloop_lock(Loop);
        try
        {
            Pa.pa_threaded_mainloop_start(Loop);
            if (Pa.pa_context_connect(Context, null, 0, IntPtr.Zero) < 0) throw new InvalidOperationException("Cannot reach the sound server.");
            var deadline = Environment.TickCount64 + 4000;
            while (true)
            {
                int s = Pa.pa_context_get_state(Context);
                if (s == Pa.CtxReady) break;
                if (s == Pa.CtxFailed || s == Pa.CtxTerminated || Environment.TickCount64 > deadline) throw new InvalidOperationException("Cannot reach the sound server. Is PulseAudio or PipeWire running?");
                Pa.pa_threaded_mainloop_wait(Loop);
            }
        }
        catch { Pa.pa_threaded_mainloop_unlock(Loop); Dispose(); throw; }
        Pa.pa_threaded_mainloop_unlock(Loop);
    }

    public sealed record SinkInput(uint Index, uint Sink, string AppName, string Binary);

    /// <summary>Playback streams that exist right now.</summary>
    public unsafe List<SinkInput> SinkInputs()
    {
        var list = new List<SinkInput>();
        bool done = false;
        Pa.SinkInputCb cb = (_, info, eol, _) =>
        {
            if (eol != 0 || info == IntPtr.Zero) { done = true; Pa.pa_threaded_mainloop_signal(Loop, 0); return; }
            var p = (byte*)info;
            uint idx = *(uint*)(p + Pa.SinkInputIndex), sink = *(uint*)(p + Pa.SinkInputSink);
            var props = *(IntPtr*)(p + Pa.SinkInputProplist);
            string app = props == IntPtr.Zero ? "" : Str(Pa.pa_proplist_gets(props, "application.name"));
            string bin = props == IntPtr.Zero ? "" : Str(Pa.pa_proplist_gets(props, "application.process.binary"));
            list.Add(new SinkInput(idx, sink, app, bin));
        };
        Pa.pa_threaded_mainloop_lock(Loop);
        try
        {
            var op = Pa.pa_context_get_sink_input_info_list(Context, cb, IntPtr.Zero);
            var deadline = Environment.TickCount64 + 3000;
            while (!done && Environment.TickCount64 < deadline) Pa.pa_threaded_mainloop_wait(Loop);
            if (op != IntPtr.Zero) Pa.pa_operation_unref(op);
        }
        finally { Pa.pa_threaded_mainloop_unlock(Loop); }
        GC.KeepAlive(cb);
        return list;
    }

    public unsafe string? SinkName(uint sinkIndex)
    {
        string? name = null; bool done = false;
        Pa.SinkInfoCb cb = (_, info, eol, _) =>
        {
            if (eol != 0 || info == IntPtr.Zero) { done = true; Pa.pa_threaded_mainloop_signal(Loop, 0); return; }
            name = Str(*(IntPtr*)info);
        };
        Pa.pa_threaded_mainloop_lock(Loop);
        try
        {
            var op = Pa.pa_context_get_sink_info_by_index(Context, sinkIndex, cb, IntPtr.Zero);
            var deadline = Environment.TickCount64 + 3000;
            while (!done && Environment.TickCount64 < deadline) Pa.pa_threaded_mainloop_wait(Loop);
            if (op != IntPtr.Zero) Pa.pa_operation_unref(op);
        }
        finally { Pa.pa_threaded_mainloop_unlock(Loop); }
        GC.KeepAlive(cb);
        return name;
    }

    public static string Str(IntPtr p) => p == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(p) ?? "";

    public static string AppId(SinkInput s) => s.Binary.Length > 0 ? s.Binary : s.AppName;

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (Loop != IntPtr.Zero)
        {
            Pa.pa_threaded_mainloop_lock(Loop);
            if (Context != IntPtr.Zero) { Pa.pa_context_set_state_callback(Context, null, IntPtr.Zero); Pa.pa_context_disconnect(Context); Pa.pa_context_unref(Context); Context = IntPtr.Zero; }
            Pa.pa_threaded_mainloop_unlock(Loop);
            Pa.pa_threaded_mainloop_stop(Loop);
            Pa.pa_threaded_mainloop_free(Loop); Loop = IntPtr.Zero;
        }
    }
}

internal sealed class PulseAudioCapturer : IAudioCapturer
{
    const uint Rate = 48000;
    PulseSession? session;
    IntPtr stream;
    Pa.StreamRequest? readCb;
    Pa.StreamNotify? stateCb;
    SilenceFiller? sink;
    volatile bool stopping;
    public Action<string>? OnStop { get; set; }

    public Task StartAsync(AudioInput input, Action<float[], double, double> onSamples)
    {
        stopping = false;
        session = new PulseSession("Lumivane");
        string? dev; uint monitor = 0; bool hasMonitor = false;
        switch (input.Kind)
        {
            case "microphone": dev = "@DEFAULT_SOURCE@"; break;
            case "app":
                var si = session.SinkInputs().FirstOrDefault(s => PulseSession.AppId(s) == input.App || s.AppName == input.App)
                    ?? throw new InvalidOperationException($"{input.App} is not playing sound right now.");
                var sinkName = session.SinkName(si.Sink) ?? throw new InvalidOperationException("Cannot find the output of that app.");
                dev = sinkName + ".monitor"; monitor = si.Index; hasMonitor = true; break;
            default: dev = "@DEFAULT_MONITOR@"; break;
        }

        var spec = new Pa.SampleSpec { format = Pa.SampleFloat32LE, rate = Rate, channels = 1 };
        var attr = new Pa.BufferAttr { maxlength = uint.MaxValue, tlength = uint.MaxValue, prebuf = uint.MaxValue, minreq = uint.MaxValue, fragsize = 1024 };
        readCb = (s, _, _) => OnRead(s);
        stateCb = (s, _) =>
        {
            int st = Pa.pa_stream_get_state(s);
            if (st == Pa.StreamFailed || st == Pa.StreamTerminated) { if (!stopping) OnStop?.Invoke("The audio source stopped."); }
            Pa.pa_threaded_mainloop_signal(session!.Loop, 0);
        };
        var loop = session.Loop;
        Pa.pa_threaded_mainloop_lock(loop);
        try
        {
            stream = Pa.pa_stream_new(session.Context, "Lumivane analysis", ref spec, IntPtr.Zero);
            if (stream == IntPtr.Zero) throw new InvalidOperationException("Cannot create the audio stream.");
            Pa.pa_stream_set_state_callback(stream, stateCb, IntPtr.Zero);
            Pa.pa_stream_set_read_callback(stream, readCb, IntPtr.Zero);
            if (hasMonitor) Pa.pa_stream_set_monitor_stream(stream, monitor);
            if (Pa.pa_stream_connect_record(stream, dev, ref attr, Pa.FlagAdjustLatency) < 0) throw new InvalidOperationException("Cannot open the audio source.");
            var deadline = Environment.TickCount64 + 4000;
            while (true)
            {
                int st = Pa.pa_stream_get_state(stream);
                if (st == Pa.StreamReady) break;
                if (st == Pa.StreamFailed || st == Pa.StreamTerminated || Environment.TickCount64 > deadline) throw new InvalidOperationException("The audio source is not available.");
                Pa.pa_threaded_mainloop_wait(loop);
            }
        }
        catch { Pa.pa_threaded_mainloop_unlock(loop); Cleanup(); throw; }
        sink = new SilenceFiller(onSamples, Rate);
        Pa.pa_threaded_mainloop_unlock(loop);
        return Task.CompletedTask;
    }

    unsafe void OnRead(IntPtr s)
    {
        // runs on the main loop thread with the loop locked
        while (true)
        {
            if (Pa.pa_stream_peek(s, out var data, out var bytes) < 0) return;
            if (bytes == 0) return;
            if (data == IntPtr.Zero) { Pa.pa_stream_drop(s); continue; }   // hole in the stream
            var samples = new float[bytes / 4];
            Marshal.Copy(data, samples, 0, samples.Length);
            Pa.pa_stream_drop(s);
            if (!stopping) sink?.Data(samples, Rate, Clock.Now);
        }
    }

    void Cleanup()
    {
        stopping = true;
        sink?.Dispose(); sink = null;
        if (session != null)
        {
            if (stream != IntPtr.Zero)
            {
                Pa.pa_threaded_mainloop_lock(session.Loop);
                Pa.pa_stream_set_read_callback(stream, null, IntPtr.Zero);
                Pa.pa_stream_set_state_callback(stream, null, IntPtr.Zero);
                Pa.pa_stream_disconnect(stream); Pa.pa_stream_unref(stream); stream = IntPtr.Zero;
                Pa.pa_threaded_mainloop_unlock(session.Loop);
            }
            session.Dispose(); session = null;
        }
    }

    public Task StopAsync() { Cleanup(); return Task.CompletedTask; }
    public void Dispose() => Cleanup();

    /// <summary>Apps with an active playback stream, one entry per app.</summary>
    public static List<AudioApp> Apps()
    {
        try
        {
            using var s = new PulseSession("Lumivane apps");
            return s.SinkInputs().Where(x => PulseSession.AppId(x).Length > 0)
                .GroupBy(PulseSession.AppId).Select(g => new AudioApp(g.Key, g.First().AppName.Length > 0 ? g.First().AppName : g.Key))
                .OrderBy(a => a.Name).ToList();
        }
        catch { return new(); }
    }
}
