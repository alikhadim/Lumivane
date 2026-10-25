using Lumivane.Core;

namespace Lumivane.Platform;

/// <summary>Reduces a captured BGRA image to the ~256 px frames the analysis works on. A small grid of taps per output pixel keeps thin bright detail from flickering.</summary>
public static unsafe class Downsampler
{
    public const int TargetWidth = 256;

    public static Frame Reduce(byte* src, int srcW, int srcH, int srcStride, int bytesPerPixel = 4, int targetWidth = TargetWidth)
    {
        int dw = Math.Min(targetWidth, srcW);
        double scale = (double)srcW / dw;
        int dh = Math.Max(1, (int)Math.Round(srcH / scale));
        var f = new Frame { Width = dw, Height = dh, Stride = dw * 4, Bgra = new byte[dw * dh * 4] };
        int taps = scale >= 6 ? 3 : scale >= 2.5 ? 2 : 1;
        fixed (byte* dst = f.Bgra)
        {
            for (int y = 0; y < dh; y++)
            {
                double y0 = y * scale, y1 = Math.Min(srcH, (y + 1) * scale);
                for (int x = 0; x < dw; x++)
                {
                    double x0 = x * scale, x1 = Math.Min(srcW, (x + 1) * scale);
                    int b = 0, g = 0, r = 0, n = 0;
                    for (int ty = 0; ty < taps; ty++)
                    {
                        int sy = Math.Min(srcH - 1, (int)(y0 + (y1 - y0) * (ty + 0.5) / taps));
                        byte* row = src + (long)sy * srcStride;
                        for (int tx = 0; tx < taps; tx++)
                        {
                            int sx = Math.Min(srcW - 1, (int)(x0 + (x1 - x0) * (tx + 0.5) / taps));
                            byte* p = row + sx * bytesPerPixel;
                            b += p[0]; g += p[1]; r += p[2]; n++;
                        }
                    }
                    byte* o = dst + (y * dw + x) * 4;
                    o[0] = (byte)(b / n); o[1] = (byte)(g / n); o[2] = (byte)(r / n); o[3] = 255;
                }
            }
        }
        return f;
    }

    public static Frame Reduce(byte[] src, int srcW, int srcH, int srcStride, int bytesPerPixel = 4, int targetWidth = TargetWidth)
    {
        fixed (byte* p = src) return Reduce(p, srcW, srcH, srcStride, bytesPerPixel, targetWidth);
    }
}
