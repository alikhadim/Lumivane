namespace Lumivane.Core;

public sealed record AudioFeatures
{
    public float Level { get; init; }                  // 0..1 from -60..0 dBFS
    public float VolumeDB { get; init; } = -120;
    public float Bass { get; init; }                   // 20-250 Hz, 0..1 (adaptive gain)
    public float Mid { get; init; }                    // 250-4000 Hz
    public float Treble { get; init; }                 // 4-16 kHz
    public float Beat { get; init; }                   // pulse at each detected beat, decays in ~120 ms
    public float BassImpulse { get; init; }            // pulse on strong bass onsets
    public float Transient { get; init; }              // pulse on broadband / treble onsets
    public float Tempo { get; init; }                  // BPM, 0 = unknown
    public float TempoConfidence { get; init; }
    public float Energy { get; init; }                 // slow loudness average, ~3 s
    public float Speech { get; init; }                 // heuristic speech probability, 0 = music
    public static readonly AudioFeatures Silent = new();
}

sealed class FFT
{
    readonly int n;
    readonly float[] cosT, sinT;
    readonly int[] rev;

    public FFT(int size)
    {
        n = size;
        cosT = new float[n / 2]; sinT = new float[n / 2];
        for (int i = 0; i < n / 2; i++)
        {
            cosT[i] = (float)Math.Cos(2 * Math.PI * i / n);
            sinT[i] = (float)-Math.Sin(2 * Math.PI * i / n);
        }
        int bits = 0; while ((1 << bits) < n) bits++;
        rev = new int[n];
        for (int i = 0; i < n; i++) { int r = 0, x = i; for (int b = 0; b < bits; b++) { r = (r << 1) | (x & 1); x >>= 1; } rev[i] = r; }
    }

    public void Transform(float[] re, float[] im)
    {
        for (int i = 0; i < n; i++)
        {
            int j = rev[i];
            if (j > i) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            int half = len / 2, step = n / len;
            for (int i = 0; i < n; i += len)
                for (int k = 0; k < half; k++)
                {
                    float wr = cosT[k * step], wi = sinT[k * step];
                    int a = i + k, b = a + half;
                    float tr = re[b] * wr - im[b] * wi, ti = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                }
        }
    }
}

/// <summary>Streaming analysis of mono PCM. 1024-point FFT (Hann) every 256 samples (5.3 ms at 48 kHz). Not thread safe.</summary>
public sealed class AudioAnalyzer
{
    public float SampleRate { get; }
    const int N = 1024, Hop = 256;
    readonly FFT fft = new(N);
    readonly float[] window = new float[N];
    readonly float winNorm;
    readonly float[] ring = new float[N];
    long written; int sinceHop;
    readonly float[] re = new float[N], im = new float[N];
    float[] prev = new float[N / 2 + 1];
    readonly float hopDur;
    readonly (int lo, int hi) bassBins, midBins, trebBins, subBins;
    float pb = 1e-3f, pm = 1e-3f, pt = 1e-3f;
    readonly float peakDecay;

    readonly float[] onsetHist = new float[188]; int onsetIdx;
    float sum, sumSq, sinceBeat = 10;
    readonly float[] env = new float[1120]; int envIdx, envFilled;
    int framesSinceTempo;
    float trebFluxAvg, bassFluxAvg;

    readonly float[] slow = new float[144]; int slowIdx, slowFilled; float slowAcc; int slowCount;
    float speechScore = 0.3f;

    public AudioFeatures Features { get; private set; } = new();

    public AudioAnalyzer(float sampleRate = 48000)
    {
        SampleRate = sampleRate;
        for (int i = 0; i < N; i++) window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (N - 1)));
        winNorm = window.Sum() / 2;
        hopDur = Hop / sampleRate;
        int Bin(float hz) => Math.Min(N / 2, Math.Max(1, (int)MathF.Round(hz * N / sampleRate, MidpointRounding.AwayFromZero)));
        bassBins = (Bin(20), Bin(250)); midBins = (Bin(250), Bin(4000));
        trebBins = (Bin(4000), Math.Min(N / 2, Bin(16000))); subBins = (Bin(30), Bin(90));
        peakDecay = MathF.Exp(-hopDur / 8);
    }

    /// <summary>Returns true if at least one analysis frame ran.</summary>
    public bool Feed(ReadOnlySpan<float> samples)
    {
        bool ran = false;
        foreach (var s in samples)
        {
            ring[(int)(written % N)] = s;
            written++; sinceHop++;
            if (written >= N && sinceHop >= Hop) { sinceHop = 0; AnalyzeFrame(); ran = true; }
        }
        return ran;
    }

    void AnalyzeFrame()
    {
        float power = 0;
        for (int i = 0; i < N; i++)
        {
            float x = ring[(int)((written - N + i) % N)];
            re[i] = x * window[i]; im[i] = 0;
            power += x * x;
        }
        fft.Transform(re, im);
        float rms = MathF.Sqrt(power / N);
        float db = 20 * MathF.Log10(rms + 1e-9f);

        var mag = new float[N / 2 + 1];
        for (int k = 0; k <= N / 2; k++) mag[k] = MathF.Sqrt(re[k] * re[k] + im[k] * im[k]) / winNorm;
        float Band((int lo, int hi) r)
        {
            if (r.hi <= r.lo) return 0;
            float e = 0; for (int k = r.lo; k < r.hi; k++) e += mag[k] * mag[k];
            return MathF.Sqrt(e / (r.hi - r.lo));
        }
        float Flux((int lo, int hi) r)
        {
            if (r.hi <= r.lo) return 0;
            float f = 0;
            for (int k = r.lo; k < r.hi; k++) { float d = MathF.Log(1 + 100 * mag[k]) - MathF.Log(1 + 100 * prev[k]); if (d > 0) f += d; }
            return f / (r.hi - r.lo);
        }
        float b = Band(bassBins), m = Band(midBins), t = Band(trebBins);
        float bf = Flux(bassBins), mf = Flux(midBins), tf = Flux(trebBins);
        prev = mag;

        pb = MathF.Max(pb * peakDecay, b); pm = MathF.Max(pm * peakDecay, m); pt = MathF.Max(pt * peakDecay, t);
        var f = Features;
        float gate = db < -70 ? 0 : 1;
        float beat = f.Beat, bassImpulse = f.BassImpulse, transient = f.Transient, tempo = f.Tempo, tconf = f.TempoConfidence, energy = f.Energy, speech = f.Speech;
        float level = Math.Clamp((db + 60) / 60, 0, 1);

        // onset strength and adaptive-threshold beat picking
        float onset = (0.7f * bf + 0.3f * mf) * gate;
        float old = onsetHist[onsetIdx];
        sum += onset - old; sumSq += onset * onset - old * old;
        onsetHist[onsetIdx] = onset; onsetIdx = (onsetIdx + 1) % onsetHist.Length;
        float cnt = onsetHist.Length;
        float mean = sum / cnt, sd = MathF.Sqrt(MathF.Max(0, sumSq / cnt - mean * mean));
        float thr = mean + 1.5f * sd + 0.02f;
        sinceBeat += hopDur;
        float decay = MathF.Exp(-hopDur / 0.12f);
        beat *= decay; bassImpulse *= decay; transient *= MathF.Exp(-hopDur / 0.08f);
        if (onset > thr && sinceBeat > 0.25f && gate > 0)
        {
            beat = MathF.Max(beat, MathF.Min(1, 0.5f + (onset - thr) / (thr + 1e-3f)));
            sinceBeat = 0;
        }
        // strong bass onset relative to its own recent average
        bassFluxAvg += (bf - bassFluxAvg) * (1 - MathF.Exp(-hopDur / 1.0f));
        if (bf > 3 * bassFluxAvg + 0.03f && gate > 0) bassImpulse = MathF.Max(bassImpulse, MathF.Min(1, (bf - bassFluxAvg) / (bf + 1e-3f) + 0.3f));
        trebFluxAvg += (tf - trebFluxAvg) * (1 - MathF.Exp(-hopDur / 1.0f));
        if (tf > 2.5f * trebFluxAvg + 0.02f && gate > 0) transient = MathF.Max(transient, MathF.Min(1, (tf - trebFluxAvg) / (tf + 1e-3f) + 0.3f));

        // tempo: autocorrelation of the onset envelope
        env[envIdx] = onset; envIdx = (envIdx + 1) % env.Length; envFilled = Math.Min(env.Length, envFilled + 1);
        framesSinceTempo++;
        if (framesSinceTempo >= (int)(0.5f / hopDur) && envFilled == env.Length) { framesSinceTempo = 0; EstimateTempo(ref tempo, ref tconf); }

        // loudness envelope for energy and speech/music heuristics
        energy += (level - energy) * (1 - MathF.Exp(-hopDur / 3));
        slowAcc += level * level; slowCount++;
        if (slowCount >= 4)
        {
            slow[slowIdx] = MathF.Sqrt(slowAcc / slowCount); slowIdx = (slowIdx + 1) % slow.Length;
            slowFilled = Math.Min(slow.Length, slowFilled + 1); slowAcc = 0; slowCount = 0;
            if (slowFilled == slow.Length && slowIdx % 8 == 0) speech = UpdateSpeech(Band(subBins) / MathF.Max(b, 1e-6f), speech);
        }
        Features = f with
        {
            VolumeDB = db, Level = level,
            Bass = MathF.Min(1, b / MathF.Max(pb, 1e-3f)) * gate,
            Mid = MathF.Min(1, m / MathF.Max(pm, 1e-3f)) * gate,
            Treble = MathF.Min(1, t / MathF.Max(pt, 1e-3f)) * gate,
            Beat = beat, BassImpulse = bassImpulse, Transient = transient, Tempo = tempo, TempoConfidence = tconf, Energy = energy, Speech = speech,
        };
    }

    void EstimateTempo(ref float tempo, ref float conf)
    {
        float rate = 1 / hopDur;
        int len = env.Length;
        var x = new float[len];
        for (int i = 0; i < len; i++) x[i] = env[(envIdx + i) % len];
        float mean = x.Sum() / len;
        for (int i = 0; i < len; i++) x[i] -= mean;
        float zero = 0; foreach (var v in x) zero += v * v;
        if (zero <= 1e-6f) { tempo = 0; conf = 0; return; }
        int minLag = (int)(rate * 60 / 190), maxLag = (int)(rate * 60 / 60);
        int best = 0; float bestScore = 0, bestRaw = 0;
        for (int lag = minLag; lag <= maxLag; lag++)
        {
            float ac = 0;
            for (int i = lag; i < len; i++) ac += x[i] * x[i - lag];
            ac /= zero;
            float bpm = rate * 60 / lag;
            float prior = MathF.Exp(-0.5f * MathF.Pow(MathF.Log2(bpm / 120) / 0.6f, 2));
            if (ac * prior > bestScore) { bestScore = ac * prior; best = lag; bestRaw = ac; }
        }
        if (best > 0 && bestRaw > 0.05f) { tempo = rate * 60 / best; conf = MathF.Min(1, bestRaw); }
        else { tempo = 0; conf = 0; }
    }

    /// <summary>Heuristic, not a trained model: speech has syllable-rate (3-6 Hz) loudness modulation, frequent pauses and deep valleys;
    /// music keeps sustained energy and modulates at beat rate. Accuracy on real material is unvalidated.</summary>
    float UpdateSpeech(float sub, float current)
    {
        int len = slow.Length;
        var e = new float[len];
        for (int i = 0; i < len; i++) e[i] = slow[(slowIdx + i) % len];
        float mean = e.Sum() / len;
        if (mean <= 0.02f) { speechScore *= 0.9f; return speechScore; }
        float pause = e.Count(v => v < 0.3f * mean) / (float)len;
        var sorted = (float[])e.Clone(); Array.Sort(sorted);
        float p10 = sorted[len / 10];
        float depth = 1 - p10 / mean;
        float rate = 1 / (hopDur * 4);
        float Power(float hz)
        {
            float cr = 0, ci = 0;
            for (int i = 0; i < len; i++) { float a = 2 * MathF.PI * hz * i / rate; float v = e[i] - mean; cr += v * MathF.Cos(a); ci += v * MathF.Sin(a); }
            return cr * cr + ci * ci;
        }
        float hi = 0; foreach (var hz in new[] { 3f, 3.5f, 4f, 4.5f, 5f, 5.5f, 6f }) hi += Power(hz);
        float lo = 0; foreach (var hz in new[] { 0.5f, 1f, 1.5f, 2f, 2.5f }) lo += Power(hz);
        float mod = hi / (hi + lo + 1e-9f);
        float score = 0.35f * MathF.Min(1, pause * 3) + 0.3f * mod + 0.2f * MathF.Min(1, depth) + 0.15f * (1 - MathF.Min(1, sub * 4));
        score = Math.Clamp(score, 0, 1);
        float target = 1 / (1 + MathF.Exp(-10 * (score - 0.5f)));
        speechScore += (target - speechScore) * 0.5f;
        return speechScore;
    }
}

/// <summary>
/// Blends the screen-derived colors with audio dynamics: out = screen * (1 - e) + audioLayer * e, e = influence scaled by speech/music state.
/// Bass: room-wide warm brightness pulses and bursts. Mid: slow hue drift. Treble: short sparkles. Beat: global pulse.
/// </summary>
public sealed class AudioMixer
{
    float bass, mid, treble, hue, lastTransient;
    readonly Dictionary<int, float> sparkle = new();
    int cursor;

    public void Reset() { bass = mid = treble = 0; sparkle.Clear(); lastTransient = 0; }

    public static float Ceiling(SyncSettings s) => ColorEngine.Ceiling(s);

    static float SmoothStep(float a, float b, float x) { float t = Math.Clamp((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); }

    public Dictionary<int, RGB> Mix(Dictionary<int, RGB> screen, IReadOnlyList<int> order, AudioFeatures f, float dt, SyncSettings s)
    {
        var a = s.Audio;
        if (!a.Enabled || a.Influence <= 0 || order.Count == 0) return screen;
        dt = Math.Clamp(dt, 0.001f, 0.25f);
        float speech = Math.Clamp(f.Speech, 0, 1);
        float responsiveness = (1 - speech) * (0.6f + 0.8f * a.MusicStrength) + speech * 0.25f;
        float e = Math.Clamp(a.Influence * responsiveness, 0, 0.95f);

        float rel = 0.05f + a.Smoothing * 0.45f;
        float Follow(float x, float target) => target > x ? target : x + (target - x) * (1 - MathF.Exp(-dt / rel));
        bass = Follow(bass, f.Bass); mid = Follow(mid, f.Mid); treble = Follow(treble, f.Treble);
        float bassBoost = MathF.Min(1.5f, bass * a.BassSensitivity);
        float trebleBoost = MathF.Min(1.5f, treble * a.TrebleSensitivity);
        float beatPulse = f.Beat * a.BeatIntensity;
        float impulse = MathF.Min(1, f.BassImpulse * a.BassSensitivity);
        hue = (hue + dt * (0.02f + 0.25f * mid * a.MusicStrength * (1 - speech))) % 1f;

        if (f.Transient > lastTransient + 0.2f && trebleBoost > 0.25f)
        {
            int ch = order[cursor % order.Count]; cursor += 7;
            sparkle[ch] = MathF.Min(1, 0.5f + 0.5f * trebleBoost);
        }
        lastTransient = f.Transient;

        RGB room = RGB.Black;
        foreach (var id in order) { var c = screen.TryGetValue(id, out var v) ? v : RGB.Black; room = new RGB(room.R + c.R, room.G + c.G, room.B + c.B); }
        room = room.Scaled(1f / order.Count);
        float blend = SmoothStep(0.3f, 0.9f, e);
        float drive = MathF.Min(1, 0.55f * bassBoost + 0.35f * mid + 0.5f * beatPulse);
        float ceiling = Ceiling(s);
        var o = new Dictionary<int, RGB>(screen);
        for (int i = 0; i < order.Count; i++)
        {
            int ch = order[i];
            var sc = screen.TryGetValue(ch, out var sv) ? sv : RGB.Black;
            var palette = ColorMath.Rgb((hue + 0.13f * i) % 1f, 0.85f, 0.15f + 0.85f * drive);
            var layer = sc.Mixed(palette, blend);
            layer = layer.Mixed(new RGB(1, 0.45f, 0.12f), 0.25f * MathF.Min(1, bassBoost));
            layer = layer.Scaled(1 + 1.1f * bassBoost + 0.8f * beatPulse);
            var c = sc.Mixed(layer, e);
            c = new RGB(c.R + room.R * 0.7f * impulse * e, c.G + room.G * 0.7f * impulse * e, c.B + room.B * 0.7f * impulse * e);
            float sp = (sparkle.TryGetValue(ch, out var spv) ? spv : 0) * MathF.Exp(-dt / 0.09f);
            sparkle[ch] = sp;
            if (sp > 0.01f)
            {
                var accent = new RGB(1, 1, 1).Mixed(sc, 0.4f).Scaled(sp * MathF.Min(1, 0.3f + e));
                c = new RGB(c.R + accent.R, c.G + accent.G, c.B + accent.B);
            }
            float m = MathF.Max(c.R, MathF.Max(c.G, c.B));
            if (m > ceiling) c = c.Scaled(ceiling / m);
            o[ch] = c.Clamped();
        }
        return o;
    }
}
