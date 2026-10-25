using Xunit;

namespace Lumivane.Platform.Tests;

public class SilenceFillerTests
{
    [Fact]
    public async Task Fills_idle_time_with_silence_at_real_time_pace()
    {
        long samples = 0; float peak = 0;
        using var f = new SilenceFiller((s, _, _) => { lock (this) { samples += s.Length; foreach (var x in s) peak = Math.Max(peak, Math.Abs(x)); } }, 48000);
        await Task.Delay(1000);
        lock (this) { Assert.InRange(samples, 48000 * 0.5, 48000 * 1.3); Assert.Equal(0, peak); }
    }

    [Fact]
    public async Task Passes_real_samples_through_untouched_and_adds_nothing_while_data_flows()
    {
        long samples = 0; float peak = 0;
        using var f = new SilenceFiller((s, _, _) => { lock (this) { samples += s.Length; foreach (var x in s) peak = Math.Max(peak, Math.Abs(x)); } }, 48000);
        var chunk = Enumerable.Repeat(0.5f, 480).ToArray();
        for (int i = 0; i < 50; i++) { f.Data(chunk, 48000, 0); await Task.Delay(10); }
        lock (this) { Assert.True(samples >= 50 * 480); Assert.Equal(0.5f, peak); }
    }
}
