using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lumivane.Core;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Lumivane.Platform.Windows;

/// <summary>
/// Captures what one app plays (Windows 10 version 2004 and later), through the process loopback virtual device.
/// Needs the app to hold an active audio session when capture starts.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ProcessLoopbackCapture : IDisposable
{
    const uint LoopbackFlags = 0x00020000 | 0x00040000 | 0x80000000 | 0x08000000;   // LOOPBACK | EVENTCALLBACK | AUTOCONVERTPCM | SRC_DEFAULT_QUALITY
    const int Rate = 48000, Channels = 2;

    readonly string exe;
    readonly Action<float[], double, double> sink;
    IAudioClient? client;
    IAudioCaptureClient? reader;
    Thread? thread;
    EventWaitHandle? wake;
    SilenceFiller? filler;
    volatile bool stop;
    public Action<string>? OnStop { get; set; }

    public ProcessLoopbackCapture(string exe, Action<float[], double, double> sink) { this.exe = exe; this.sink = sink; }

    public static string ExeName(int pid) => W.ProcessName((uint)pid);

    static uint FindPid(string exe)
    {
        using var en = new MMDeviceEnumerator();
        using var dev = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var sessions = dev.AudioSessionManager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            if (s.State == AudioSessionState.AudioSessionStateActive && s.GetProcessID != 0 && string.Equals(ExeName((int)s.GetProcessID), exe, StringComparison.OrdinalIgnoreCase))
                return s.GetProcessID;
        }
        throw new InvalidOperationException($"{exe} is not playing sound right now.");
    }

    public void Start()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) throw new InvalidOperationException("Capturing one app needs Windows 10 version 2004 or later.");
        uint pid = FindPid(exe);
        client = Activate(pid);

        var fmt = Marshal.AllocHGlobal(18);
        try
        {
            Marshal.WriteInt16(fmt, 0, 3);                         // WAVE_FORMAT_IEEE_FLOAT
            Marshal.WriteInt16(fmt, 2, Channels);
            Marshal.WriteInt32(fmt, 4, Rate);
            Marshal.WriteInt32(fmt, 8, Rate * Channels * 4);
            Marshal.WriteInt16(fmt, 12, Channels * 4);
            Marshal.WriteInt16(fmt, 14, 32);
            Marshal.WriteInt16(fmt, 16, 0);
            Check(client.Initialize(0, LoopbackFlags, 200000, 0, fmt, IntPtr.Zero), "initialize the app audio stream");
        }
        finally { Marshal.FreeHGlobal(fmt); }

        wake = new EventWaitHandle(false, EventResetMode.AutoReset);
        Check(client.SetEventHandle(wake.SafeWaitHandle.DangerousGetHandle()), "set the audio event");
        var iid = typeof(IAudioCaptureClient).GUID;
        Check(client.GetService(ref iid, out var o), "get the audio reader");
        reader = (IAudioCaptureClient)o;
        filler = new SilenceFiller(sink, Rate);
        Check(client.Start(), "start the app audio stream");

        thread = new Thread(Run) { IsBackground = true, Name = "app-audio", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    unsafe void Run()
    {
        try
        {
            while (!stop)
            {
                if (!wake!.WaitOne(250)) continue;
                while (!stop && reader!.GetNextPacketSize(out var n) == 0 && n > 0)
                {
                    if (reader.GetBuffer(out var data, out var frames, out var flags, out _, out _) != 0) break;
                    var mono = new float[frames];
                    if ((flags & 2) == 0)    // 2 = AUDCLNT_BUFFERFLAGS_SILENT
                    {
                        var p = (float*)data;
                        for (int i = 0; i < frames; i++) mono[i] = (p[i * Channels] + p[i * Channels + 1]) * 0.5f;
                    }
                    reader.ReleaseBuffer(frames);
                    if (!stop) filler!.Data(mono, Rate, Clock.Now);
                }
            }
        }
        catch (Exception e) { if (!stop) OnStop?.Invoke("The app's audio stopped: " + e.Message); }
    }

    static void Check(int hr, string what)
    {
        if (hr >= 0) return;
        throw new InvalidOperationException($"Cannot {what} (0x{hr:X8}).");
    }

    static IAudioClient Activate(uint pid)
    {
        var args = new ActivationParams { ActivationType = 1, TargetProcessId = pid, ProcessLoopbackMode = 0 };   // 1 = process loopback, 0 = include the process tree
        var blob = Marshal.AllocHGlobal(Marshal.SizeOf<ActivationParams>());
        var pv = Marshal.AllocHGlobal(Marshal.SizeOf<PropVariant>());
        try
        {
            Marshal.StructureToPtr(args, blob, false);
            Marshal.StructureToPtr(new PropVariant { vt = 65, cbSize = (uint)Marshal.SizeOf<ActivationParams>(), blob = blob }, pv, false);   // VT_BLOB
            var handler = new Handler();
            var iid = typeof(IAudioClient).GUID;
            Check(ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ref iid, pv, handler, out var op), "reach the app audio device");
            if (!handler.Done.Wait(5000)) throw new InvalidOperationException("Windows did not answer the app audio request.");
            op.GetActivateResult(out int hr, out object o);
            Check(hr, "open the app audio device");
            return (IAudioClient)o;
        }
        finally { Marshal.FreeHGlobal(blob); Marshal.FreeHGlobal(pv); }
    }

    public void Dispose()
    {
        stop = true;
        wake?.Set();
        thread?.Join(1000);
        try { client?.Stop(); } catch { }
        if (reader != null) { Marshal.ReleaseComObject(reader); reader = null; }
        if (client != null) { Marshal.ReleaseComObject(client); client = null; }
        wake?.Dispose(); wake = null;
        filler?.Dispose(); filler = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ActivationParams { public int ActivationType; public uint TargetProcessId; public int ProcessLoopbackMode; }

    // PROPVARIANT with a BLOB payload on 64-bit Windows: vt at 0, cbSize at 8, pointer at 16.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct PropVariant
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public uint cbSize;
        [FieldOffset(16)] public IntPtr blob;
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = true)]
    static extern int ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid riid, IntPtr activationParams,
        IActivateAudioInterfaceCompletionHandler handler, out IActivateAudioInterfaceAsyncOperation op);

    [ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAgileObject { }

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IActivateAudioInterfaceCompletionHandler { void ActivateCompleted(IActivateAudioInterfaceAsyncOperation op); }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IActivateAudioInterfaceAsyncOperation { void GetActivateResult(out int hr, [MarshalAs(UnmanagedType.IUnknown)] out object iface); }

    [ClassInterface(ClassInterfaceType.None)]
    sealed class Handler : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public readonly ManualResetEventSlim Done = new(false);
        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation op) => Done.Set();
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint frames);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }
}
