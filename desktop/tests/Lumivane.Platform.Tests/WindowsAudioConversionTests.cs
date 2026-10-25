#pragma warning disable CA1416 // the converter is plain arithmetic; the Windows attribute is on the class that hosts it
using Lumivane.Platform.Windows;
using NAudio.Wave;
using Xunit;

namespace Lumivane.Platform.Tests;

public class WindowsAudioConversionTests
{
    static byte[] Floats(params float[] v) { var b = new byte[v.Length * 4]; Buffer.BlockCopy(v, 0, b, 0, b.Length); return b; }

    [Fact]
    public void Stereo_float_is_averaged_to_mono()
    {
        var b = Floats(1f, 0f, 0.5f, -0.5f, 0.25f, 0.25f);
        var m = WasapiAudioCapturer.ToMono(b, b.Length, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        Assert.Equal(new[] { 0.5f, 0f, 0.25f }, m);
    }

    [Fact]
    public void Sixteen_bit_pcm_is_scaled_to_unit_range()
    {
        var b = new byte[4]; BitConverter.GetBytes((short)16384).CopyTo(b, 0); BitConverter.GetBytes((short)-32768).CopyTo(b, 2);
        var m = WasapiAudioCapturer.ToMono(b, b.Length, new WaveFormat(44100, 16, 1));
        Assert.Equal(new[] { 0.5f, -1f }, m);
    }

    [Fact]
    public void Partial_frames_at_the_end_are_ignored()
    {
        var b = Floats(1f, 1f, 0.5f);   // two channels: the third float is half a frame
        var m = WasapiAudioCapturer.ToMono(b, b.Length, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        Assert.Single(m);
    }
}
