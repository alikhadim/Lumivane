using Lumivane.Core;

namespace Lumivane.Platform;

/// <summary>
/// Loopback capture delivers nothing while nothing plays (PulseAudio suspends idle sinks, WASAPI sends no packets), which would freeze
/// the analysis on its last loud frame. This forwards real samples and fills the gaps with silence at real-time pace.
/// </summary>
public sealed class SilenceFiller : IDisposable
{
    readonly Action<float[], double, double> sink;
    readonly double rate;
    readonly object gate = new();
    readonly Timer timer;
    double lastEmit;
    bool disposed;

    public SilenceFiller(Action<float[], double, double> sink, double rate)
    {
        this.sink = sink; this.rate = rate; lastEmit = Clock.Now;
        timer = new Timer(_ => Tick(), null, 20, 20);
    }

    public double Rate => rate;

    public void Data(float[] samples, double sampleRate, double time)
    {
        lock (gate)
        {
            if (disposed) return;
            sink(samples, sampleRate, time); lastEmit = Clock.Now;
        }
    }

    void Tick()
    {
        lock (gate)
        {
            if (disposed) return;
            double now = Clock.Now, gap = now - lastEmit;
            if (gap < 0.1) return;
            int n = (int)(gap * rate);
            if (n < 1) return;
            sink(new float[n], rate, now);
            lastEmit = now;
        }
    }

    public void Dispose() { lock (gate) disposed = true; timer.Dispose(); }
}
