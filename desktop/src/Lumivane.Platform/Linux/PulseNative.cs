using System.Runtime.InteropServices;

namespace Lumivane.Platform.Linux;

/// <summary>libpulse (PulseAudio and PipeWire's pulse server). Structure offsets are for the stable 64-bit ABI.</summary>
internal static class Pa
{
    const string Lib = "libpulse.so.0";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void ContextNotify(IntPtr ctx, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void SinkInputCb(IntPtr ctx, IntPtr info, int eol, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void SinkInfoCb(IntPtr ctx, IntPtr info, int eol, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void StreamRequest(IntPtr stream, nuint bytes, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void StreamNotify(IntPtr stream, IntPtr user);

    [StructLayout(LayoutKind.Sequential)] public struct SampleSpec { public int format; public uint rate; public byte channels; }
    [StructLayout(LayoutKind.Sequential)] public struct BufferAttr { public uint maxlength, tlength, prebuf, minreq, fragsize; }

    public const int SampleFloat32LE = 5;
    public const int CtxReady = 4, CtxFailed = 5, CtxTerminated = 6;
    public const int StreamReady = 2, StreamFailed = 3, StreamTerminated = 4;
    public const int FlagAdjustLatency = 0x2000;

    [DllImport(Lib)] public static extern IntPtr pa_threaded_mainloop_new();
    [DllImport(Lib)] public static extern void pa_threaded_mainloop_free(IntPtr m);
    [DllImport(Lib)] public static extern int pa_threaded_mainloop_start(IntPtr m);
    [DllImport(Lib)] public static extern void pa_threaded_mainloop_stop(IntPtr m);
    [DllImport(Lib)] public static extern void pa_threaded_mainloop_lock(IntPtr m);
    [DllImport(Lib)] public static extern void pa_threaded_mainloop_unlock(IntPtr m);
    [DllImport(Lib)] public static extern void pa_threaded_mainloop_wait(IntPtr m);
    [DllImport(Lib)] public static extern void pa_threaded_mainloop_signal(IntPtr m, int waitForAccept);
    [DllImport(Lib)] public static extern IntPtr pa_threaded_mainloop_get_api(IntPtr m);

    [DllImport(Lib)] public static extern IntPtr pa_context_new(IntPtr api, string name);
    [DllImport(Lib)] public static extern void pa_context_set_state_callback(IntPtr ctx, ContextNotify? cb, IntPtr user);
    [DllImport(Lib)] public static extern int pa_context_connect(IntPtr ctx, string? server, int flags, IntPtr spawn);
    [DllImport(Lib)] public static extern int pa_context_get_state(IntPtr ctx);
    [DllImport(Lib)] public static extern void pa_context_disconnect(IntPtr ctx);
    [DllImport(Lib)] public static extern void pa_context_unref(IntPtr ctx);
    [DllImport(Lib)] public static extern IntPtr pa_context_get_sink_input_info_list(IntPtr ctx, SinkInputCb cb, IntPtr user);
    [DllImport(Lib)] public static extern IntPtr pa_context_get_sink_info_by_index(IntPtr ctx, uint idx, SinkInfoCb cb, IntPtr user);
    [DllImport(Lib)] public static extern void pa_operation_unref(IntPtr op);

    [DllImport(Lib)] public static extern IntPtr pa_stream_new(IntPtr ctx, string name, ref SampleSpec spec, IntPtr map);
    [DllImport(Lib)] public static extern int pa_stream_set_monitor_stream(IntPtr s, uint sinkInput);
    [DllImport(Lib)] public static extern int pa_stream_connect_record(IntPtr s, string? dev, ref BufferAttr attr, int flags);
    [DllImport(Lib)] public static extern void pa_stream_set_read_callback(IntPtr s, StreamRequest? cb, IntPtr user);
    [DllImport(Lib)] public static extern void pa_stream_set_state_callback(IntPtr s, StreamNotify? cb, IntPtr user);
    [DllImport(Lib)] public static extern int pa_stream_get_state(IntPtr s);
    [DllImport(Lib)] public static extern int pa_stream_peek(IntPtr s, out IntPtr data, out nuint bytes);
    [DllImport(Lib)] public static extern int pa_stream_drop(IntPtr s);
    [DllImport(Lib)] public static extern int pa_stream_disconnect(IntPtr s);
    [DllImport(Lib)] public static extern void pa_stream_unref(IntPtr s);

    [DllImport(Lib)] public static extern IntPtr pa_proplist_gets(IntPtr list, string key);

    // pa_sink_input_info: index @0, name @8, sink @24, proplist @344
    public const int SinkInputIndex = 0, SinkInputSink = 24, SinkInputProplist = 344;
}
