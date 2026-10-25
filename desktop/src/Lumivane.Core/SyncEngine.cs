using System.Diagnostics;

namespace Lumivane.Core;

public sealed class Metrics
{
    public double CaptureFps, CaptureMs, ReduceMs, GpuMs, EndToEndMs, SendHz, CpuPercent, AudioToLightMs;
    public int CaptureSessions;
    public string Mode = "Idle";
    public AudioFeatures Audio = new();
    public bool AudioActive;
    public Metrics Copy() => (Metrics)MemberwiseClone();
}

/// <summary>
/// Per-display capture -> zone reduction -> per-light stats -> ColorEngine + system-audio analysis (FFT) -> AudioMixer
/// -> Entertainment stream (or REST fallback). Capture sessions exist only for sources a light actually follows.
/// A fixed-rate loop drives output because capture sends no frames for static content, and the bridge drops a stream that goes quiet.
/// </summary>
public sealed class SyncEngine : IDisposable
{
    public sealed class Config
    {
        public required BridgeInfo Bridge { get; init; }
        public required Credentials Creds { get; init; }
        public required EntertainmentArea Area { get; init; }
        public required IReadOnlyList<HueLight> Lights { get; init; }
        public required SyncSettings Settings { get; init; }
        /// <summary>Demo Mode: synthetic frames and audio, nothing is captured or sent.</summary>
        public bool Demo { get; init; }
        public IHueBridge? Client { get; init; }
        /// <summary>Test hook: replaces the DTLS connection.</summary>
        public Func<string, Task<IFrameSink>>? SinkFactory { get; init; }
    }

    public Action<Metrics, Dictionary<int, RGB>>? OnUpdate;
    public Action<string>? OnError;
    public Action<string>? OnSourceLost;
    public Action<Dictionary<ColorStyle, Dictionary<int, RGB>>>? OnCompare;

    readonly IPlatform platform;
    readonly ColorEngine color = new();
    readonly AudioMixer mixer = new();
    readonly object lk = new();
    readonly object tickGate = new();
    Thread? loop; CancellationTokenSource? loopCts;
    IFrameSink? sink;
    IHueBridge? client;
    string areaId = "";
    Dictionary<int, List<HueLight>> lightsByChannel = new();

    // video
    readonly Dictionary<string, IScreenCapturer> captures = new();
    Dictionary<string, List<ZoneRect>> zones = new();
    Dictionary<int, List<(string Src, int Idx)>> refs = new();
    List<int> driven = new();
    Dictionary<string, ZoneStats[]> statsBySource = new();
    Dictionary<string, double> fresh = new();
    int fps = 50;
    bool demo; double demoStart;

    // audio
    IAudioCapturer? audioCapture;
    AudioAnalyzer? analyzer;
    AudioInput? audioInput;
    AudioFeatures features = new();
    double? audioPending;
    float lastBeat;

    // shared
    SyncSettings settings = new();
    Metrics m = new();
    int frames, sends;
    double lastReport = Clock.Now, lastTick = Clock.Now, lastUi, lastCpuSeconds, lastCompare;
    TimeSpan lastCpu = TimeSpan.Zero;
    bool fallback; int fbIndex; double fbLast; bool fbBusy;
    RoutePlan currentPlan = new();
    List<int> lastAudioOnly = new();
    bool compareOn;
    readonly Dictionary<ColorStyle, ColorEngine> compareEngines = new();

    public SyncEngine(IPlatform platform) { this.platform = platform; }

    public bool IsRunning { get { lock (lk) return loop != null; } }
    public bool CompareEnabled { get { lock (lk) return compareOn; } set { lock (lk) compareOn = value; } }
    public void Update(SyncSettings s) { lock (lk) settings = s.Clone(); }

    public async Task StartAsync(Config cfg)
    {
        demo = cfg.Demo;
        client = demo ? null : cfg.Client;
        areaId = cfg.Area.Id;
        var chans = cfg.Area.Channels;
        lightsByChannel = chans.ToDictionary(c => c.Id, c => cfg.Lights.Where(l => l.EntertainmentServiceId != null && c.Services.Contains(l.EntertainmentServiceId)).ToList());
        color.Reset(); mixer.Reset(); compareEngines.Clear();
        lock (lk)
        {
            settings = cfg.Settings.Clone(); fps = Math.Clamp(cfg.Settings.Fps, 10, 60);
            fallback = false; m = new Metrics(); frames = 0; sends = 0; currentPlan = new RoutePlan();
        }

        string mode = "Entertainment streaming";
        if (demo) { mode = "Entertainment streaming (demo)"; demoStart = Clock.Now; }
        else if (client != null)
        {
            try
            {
                await client.SetStreamingAsync(cfg.Area.Id, true).ConfigureAwait(false);
                var appId = await client.ApplicationIdAsync().ConfigureAwait(false);
                if (cfg.SinkFactory != null) sink = await cfg.SinkFactory(appId).ConfigureAwait(false);
                else
                {
                    var s = EntertainmentStreamer.Create(cfg.Bridge.Ip, appId, cfg.Creds.ClientKey, cfg.Area.Id)
                        ?? throw HueException.StreamFailed("The stored key is invalid. Pair again in Settings.");
                    s.OnFailure = msg => OnError?.Invoke($"Light stream interrupted: {msg}");
                    await s.StartAsync().ConfigureAwait(false);
                    sink = s;
                }
            }
            catch (Exception e)
            {
                try { await client.SetStreamingAsync(cfg.Area.Id, false).ConfigureAwait(false); } catch { }
                fallback = true; mode = "Basic mode (slower)";
                OnError?.Invoke($"Smooth streaming unavailable, using basic mode. {e.Message}");
            }
        }
        lock (lk) m.Mode = mode;

        double period = 1.0 / fps;
        loopCts = new CancellationTokenSource();
        var ct = loopCts.Token;
        lock (lk) lastTick = Clock.Now;
        lastCpu = Process.GetCurrentProcess().TotalProcessorTime; lastCpuSeconds = Clock.Now;
        loop = new Thread(() => Loop(period, ct)) { IsBackground = true, Name = "lumivane.send", Priority = ThreadPriority.AboveNormal };
        loop.Start();
    }

    void Loop(double period, CancellationToken ct)
    {
        double next = Clock.Now + period;
        while (!ct.IsCancellationRequested)
        {
            double wait = next - Clock.Now;
            if (wait > 0.002) Thread.Sleep(Math.Max(1, (int)((wait - 0.0015) * 1000)));
            while (Clock.Now < next && !ct.IsCancellationRequested) Thread.SpinWait(40);
            try { Tick(false); } catch (Exception e) { OnError?.Invoke($"Sync loop error: {e.Message}"); }
            next += period;
            if (next < Clock.Now) next = Clock.Now + period;
        }
    }

    /// <summary>Opens capture sessions for newly needed sources and closes ones no longer used. Safe to call repeatedly.</summary>
    public async Task ApplyAsync(RoutePlan plan, IReadOnlyDictionary<string, CaptureTarget> targets, IReadOnlyList<int> audioOnlyChannels)
    {
        bool unchanged;
        lock (lk)
            unchanged = SamePlan(plan, currentPlan) && audioOnlyChannels.SequenceEqual(lastAudioOnly) && (demo || captures.Keys.ToHashSet().SetEquals(plan.PhysicalSources));
        if (unchanged) return;

        var z = new Dictionary<string, List<ZoneRect>>();
        var r = new Dictionary<int, List<(string, int)>>();
        foreach (var (ch, pts) in plan.Samples.OrderBy(k => k.Key))
            foreach (var p in pts)
            {
                if (!z.TryGetValue(p.Source, out var list)) z[p.Source] = list = new();
                int idx = list.IndexOf(p.Rect);
                if (idx < 0) { list.Add(p.Rect); idx = list.Count - 1; }
                if (!r.TryGetValue(ch, out var rl)) r[ch] = rl = new();
                rl.Add((p.Source, idx));
            }
        var needed = plan.PhysicalSources;
        int rate;
        lock (lk)
        {
            zones = z; refs = r;
            driven = plan.Samples.Count == 0 ? audioOnlyChannels.ToList() : plan.Samples.Keys.OrderBy(k => k).ToList();
            statsBySource = statsBySource.Where(kv => needed.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            currentPlan = plan; lastAudioOnly = audioOnlyChannels.ToList();
            rate = fps;
        }
        if (demo) { lock (lk) m.CaptureSessions = needed.Count; return; }

        foreach (var k in captures.Keys.Where(k => !needed.Contains(k)).ToList())
        {
            var c = captures[k]; captures.Remove(k);
            await c.StopAsync().ConfigureAwait(false); c.Dispose();
        }
        foreach (var k in needed.Where(k => !captures.ContainsKey(k)).ToList())
        {
            if (!targets.TryGetValue(k, out var t)) { OnError?.Invoke("A selected screen or window isn't available."); continue; }
            var c = platform.CreateScreenCapturer();
            var key = k;
            c.OnStop = _ => OnSourceLost?.Invoke(key);
            try
            {
                await c.StartAsync(t, rate, (f, latency) => Handle(key, f, latency)).ConfigureAwait(false);
                captures[k] = c;
            }
            catch (Exception e) { c.Dispose(); OnError?.Invoke($"Couldn't capture that screen: {e.Message}"); }
        }
        lock (lk) m.CaptureSessions = captures.Count;
    }

    static bool SamePlan(RoutePlan a, RoutePlan b)
    {
        if (a.Samples.Count != b.Samples.Count) return false;
        foreach (var (k, v) in a.Samples)
            if (!b.Samples.TryGetValue(k, out var w) || !v.SequenceEqual(w)) return false;
        return a.Substitutions.SequenceEqual(b.Substitutions) && a.Unavailable.SequenceEqual(b.Unavailable);
    }

    /// <summary>Starts, stops or switches audio capture to match the profile.</summary>
    public async Task ApplyAudioAsync(AudioSettings a)
    {
        AudioInput? want = a.Enabled && a.Influence > 0 ? a.Input : null;
        if (demo) { lock (lk) m.AudioActive = want != null; return; }
        if (Equals(want, audioInput)) return;
        audioInput = null;
        if (audioCapture != null) { await audioCapture.StopAsync().ConfigureAwait(false); audioCapture.Dispose(); audioCapture = null; }
        lock (lk) { features = new AudioFeatures(); m.AudioActive = false; audioPending = null; }
        if (want == null) return;
        var cap = platform.CreateAudioCapturer();
        cap.OnStop = msg => OnError?.Invoke($"Audio capture stopped: {msg}");
        try
        {
            await cap.StartAsync(want, HandleAudio).ConfigureAwait(false);
            audioCapture = cap; audioInput = want;
            lock (lk) m.AudioActive = true;
        }
        catch (Exception e) { cap.Dispose(); OnError?.Invoke($"Audio reactivity is off: {e.Message}"); }
    }

    public async Task StopAsync()
    {
        Thread? t; CancellationTokenSource? cts;
        lock (lk) { t = loop; loop = null; cts = loopCts; loopCts = null; }
        cts?.Cancel();
        t?.Join(1000);
        cts?.Dispose();
        foreach (var c in captures.Values) { try { await c.StopAsync().ConfigureAwait(false); } catch { } c.Dispose(); }
        captures.Clear();
        audioInput = null;
        if (audioCapture != null) { try { await audioCapture.StopAsync().ConfigureAwait(false); } catch { } audioCapture.Dispose(); audioCapture = null; }
        sink?.Dispose(); sink = null;
        if (client != null && areaId.Length > 0) { try { await client.SetStreamingAsync(areaId, false).ConfigureAwait(false); } catch { } }
        client = null;
        lock (lk) { statsBySource = new(); fresh = new(); currentPlan = new RoutePlan(); driven = new(); }
    }

    public void Dispose() { try { StopAsync().GetAwaiter().GetResult(); } catch { } }

    // ---- video frames ----

    void Handle(string source, Frame f, double latencyMs)
    {
        double t0 = Clock.Now;
        List<ZoneRect> rects;
        lock (lk) { if (!zones.TryGetValue(source, out var zr) || zr.Count == 0) return; rects = zr; }
        var raw = ZoneAccumulator.ReduceBgra(f.Bgra, f.Width, f.Height, f.Stride, rects);
        var st = new ZoneStats[rects.Count];
        for (int i = 0; i < st.Length; i++) st[i] = ZoneAccumulator.Stats(raw, i);
        double t1 = Clock.Now;
        lock (lk)
        {
            statsBySource[source] = st;
            if (!fresh.ContainsKey(source)) fresh[source] = t1 - latencyMs / 1000;
            frames++; m.CaptureMs = latencyMs; m.ReduceMs = (t1 - t0) * 1000;
        }
    }

    // ---- audio ----

    void HandleAudio(float[] mono, double rate, double end)
    {
        if (analyzer == null || analyzer.SampleRate != (float)rate) analyzer = new AudioAnalyzer((float)rate);
        if (!analyzer.Feed(mono)) return;
        var f = analyzer.Features;
        bool onBeat;
        lock (lk)
        {
            features = f;
            audioPending ??= end;
            onBeat = f.Beat > lastBeat + 0.3f;
            lastBeat = f.Beat;
        }
        if (onBeat) ThreadPool.UnsafeQueueUserWorkItem(_ => { try { Tick(true); } catch { } }, null);   // don't wait for the next loop slot
    }

    // ---- output ----

    void Tick(bool early)
    {
        if (early) { if (!Monitor.TryEnter(tickGate)) return; }
        else Monitor.Enter(tickGate);
        try { TickLocked(early); } finally { Monitor.Exit(tickGate); }
    }

    void TickLocked(bool early)
    {
        double now = Clock.Now;
        SyncSettings s; bool fb, cmp, demoOn; List<int> ids; Dictionary<int, List<(string Src, int Idx)>> rf;
        Dictionary<string, ZoneStats[]> stats; AudioFeatures feat; Dictionary<string, List<ZoneRect>> demoZones; double? pendingAudio; Dictionary<string, double> freshFrames;
        float dt;
        lock (lk)
        {
            if (early && now - lastTick < 0.008) return;
            dt = (float)(now - lastTick); lastTick = now;
            s = settings; fb = fallback; ids = driven; rf = refs; cmp = compareOn;
            stats = new Dictionary<string, ZoneStats[]>(statsBySource); feat = features;
            demoOn = demo; demoZones = zones;
            pendingAudio = audioPending; audioPending = null;
            freshFrames = fresh; fresh = new();
        }
        if (ids.Count == 0) return;
        if (demoOn)
        {
            float t = (float)(now - demoStart);
            stats = demoZones.ToDictionary(e => e.Key, e => e.Value.Select(r => DemoScene.Stats(DemoData.SceneFor(e.Key), r, t)).ToArray());
            feat = DemoSignal.Audio(t);
        }

        var perChannel = new List<(int Id, ZoneStats Stats)>(ids.Count);
        foreach (var id in ids)
        {
            var parts = new List<ZoneStats>();
            if (rf.TryGetValue(id, out var rl))
                foreach (var (src, i) in rl) if (stats.TryGetValue(src, out var arr) && i < arr.Length) parts.Add(arr[i]);
            perChannel.Add((id, ZoneStats.Combine(parts)));
        }
        var colors = color.Process(perChannel, dt, s);
        if (cmp && now - lastCompare >= 0.2)
        {
            float cdt = (float)(now - lastCompare); lastCompare = now;
            var res = new Dictionary<ColorStyle, Dictionary<int, RGB>>();
            foreach (var st in Enum.GetValues<ColorStyle>())
            {
                if (!compareEngines.TryGetValue(st, out var e)) compareEngines[st] = e = new ColorEngine();
                var s2 = s.Clone(); s2.Style = st;
                res[st] = e.Process(perChannel, cdt, s2);
            }
            OnCompare?.Invoke(res);
        }
        if (s.Audio.Enabled && s.Audio.Influence > 0) colors = mixer.Mix(colors, ids, feat, dt, s);

        if (fb) FallbackSend(colors, now);
        else if (sink != null)
        {
            sink.Send(ids.Select(i => ((byte)i, colors.TryGetValue(i, out var c) ? c : RGB.Black)).ToList());
            double sent = Clock.Now;
            lock (lk)
            {
                sends++;
                if (freshFrames.Count > 0) m.EndToEndMs = Math.Max(0, (sent - freshFrames.Values.Min()) * 1000);
                if (pendingAudio is double p) m.AudioToLightMs = Math.Max(0, (sent - p) * 1000);
            }
        }

        Metrics snapshot; bool emit;
        lock (lk)
        {
            if (now - lastReport >= 0.5)
            {
                double span = now - lastReport;
                m.CaptureFps = frames / span; m.SendHz = sends / span;
                frames = 0; sends = 0; lastReport = now;
                var cpu = Process.GetCurrentProcess().TotalProcessorTime;
                m.CpuPercent = (cpu - lastCpu).TotalSeconds / span * 100; lastCpu = cpu;
            }
            m.Audio = feat;
            emit = now - lastUi >= 0.1;
            if (emit) lastUi = now;
            snapshot = m.Copy();
        }
        if (emit) OnUpdate?.Invoke(snapshot, colors);
    }

    /// <summary>REST fallback: the bridge accepts roughly 10 light commands per second in total.</summary>
    void FallbackSend(Dictionary<int, RGB> colors, double now)
    {
        int chId;
        lock (lk)
        {
            if (now - fbLast < 0.11 || fbBusy || driven.Count == 0) return;
            fbLast = now; fbBusy = true;
            chId = driven[fbIndex % driven.Count]; fbIndex++;
        }
        var cl = client;
        if (!colors.TryGetValue(chId, out var c) || cl == null) { lock (lk) fbBusy = false; return; }
        float peak = MathF.Max(c.R, MathF.Max(c.G, c.B));
        var chroma = peak > 0 ? c.Scaled(1 / peak) : new RGB(1, 1, 1);
        var targets = lightsByChannel.TryGetValue(chId, out var l) ? l : new();
        _ = Task.Run(async () =>
        {
            try { foreach (var light in targets) await cl.SetLightAsync(light.Id, peak > 0.02f, peak * 100, chroma, light.Gamut, 150).ConfigureAwait(false); }
            catch { }
            finally { lock (lk) fbBusy = false; }
        });
    }
}
