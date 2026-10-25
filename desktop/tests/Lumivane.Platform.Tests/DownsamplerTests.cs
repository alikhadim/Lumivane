using Lumivane.Core;
using Lumivane.Platform;
using Lumivane.Platform.Linux;
using Xunit;

namespace Lumivane.Platform.Tests;

public class DownsamplerTests
{
    static byte[] Solid(int w, int h, byte b, byte g, byte r)
    {
        var d = new byte[w * h * 4];
        for (int i = 0; i < d.Length; i += 4) { d[i] = b; d[i + 1] = g; d[i + 2] = r; d[i + 3] = 255; }
        return d;
    }

    [Fact]
    public void Keeps_aspect_and_width_cap()
    {
        var f = Downsampler.Reduce(Solid(3840, 2160, 1, 2, 3), 3840, 2160, 3840 * 4);
        Assert.Equal(256, f.Width); Assert.Equal(144, f.Height); Assert.Equal(256 * 4, f.Stride);
        Assert.Equal(f.Width * f.Height * 4, f.Bgra.Length);
    }

    [Fact]
    public void Solid_color_survives()
    {
        var f = Downsampler.Reduce(Solid(1920, 1080, 10, 200, 90), 1920, 1080, 1920 * 4);
        for (int i = 0; i < f.Bgra.Length; i += 4) { Assert.Equal(10, f.Bgra[i]); Assert.Equal(200, f.Bgra[i + 1]); Assert.Equal(90, f.Bgra[i + 2]); Assert.Equal(255, f.Bgra[i + 3]); }
    }

    [Fact]
    public void Halves_stay_in_their_half()
    {
        int w = 1280, h = 720; var d = new byte[w * h * 4];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { int o = (y * w + x) * 4; d[o + 2] = (byte)(x < w / 2 ? 255 : 0); d[o] = (byte)(x < w / 2 ? 0 : 255); d[o + 3] = 255; }
        var f = Downsampler.Reduce(d, w, h, w * 4);
        int mid = f.Height / 2 * f.Stride;
        Assert.Equal(255, f.Bgra[mid + 20 * 4 + 2]); Assert.Equal(0, f.Bgra[mid + 20 * 4]);
        Assert.Equal(255, f.Bgra[mid + 235 * 4]); Assert.Equal(0, f.Bgra[mid + 235 * 4 + 2]);
    }

    [Fact]
    public void Small_images_are_not_upscaled()
    {
        var f = Downsampler.Reduce(Solid(64, 36, 0, 0, 255), 64, 36, 64 * 4);
        Assert.Equal(64, f.Width); Assert.Equal(36, f.Height);
    }

    [Fact]
    public void Respects_row_padding()
    {
        int w = 100, h = 50, stride = 480; var d = new byte[stride * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { int o = y * stride + x * 4; d[o + 1] = 255; d[o + 3] = 255; }
        var f = Downsampler.Reduce(d, w, h, stride);
        Assert.All(Enumerable.Range(0, f.Width * f.Height), i => Assert.Equal(255, f.Bgra[i * 4 + 1]));
    }

    [Fact]
    public void Parses_edid_monitor_name()
    {
        var e = new byte[128];
        int o = 72; e[o + 3] = 0xFC;
        var name = System.Text.Encoding.ASCII.GetBytes("DELL U2720Q\n  ");
        Array.Copy(name, 0, e, o + 5, 13);
        Assert.Equal("DELL U2720Q", X11Displays.ParseEdidName(e));
        Assert.Null(X11Displays.ParseEdidName(new byte[128]));
    }

    [Theory]
    [InlineData("eDP-1", true)] [InlineData("LVDS-1", true)] [InlineData("HDMI-A-1", false)] [InlineData("DP-2", false)]
    public void Detects_internal_panels(string output, bool internalPanel) => Assert.Equal(internalPanel, X11Displays.IsInternal(output));

    [Fact]
    public void Refresh_rate_from_mode_timings()
    {
        var m = new X.RRModeInfo { dotClock = 148500000, hTotal = 2200, vTotal = 1125 };
        Assert.Equal(60.0, X11Displays.Refresh(m), 3);
    }

    [Fact]
    public void Display_ids_are_stable_and_distinct()
    {
        Assert.Equal(X11Displays.Id("DP-1"), X11Displays.Id("DP-1"));
        Assert.NotEqual(X11Displays.Id("DP-1"), X11Displays.Id("DP-2"));
        Assert.True(X11Displays.Id("HDMI-1") > 0);
    }
}
