using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Samitivity.Windows;

/// <summary>Composes only the fixed wallpaper. Child text and controls are never blurred.</summary>
internal sealed class RoseSurface : Panel
{
    private readonly Image roses;
    private Bitmap? backdrop;
    internal Bitmap Backdrop => backdrop ??= Compose(ClientSize, DeviceDpi);
    internal int CompositionCount { get; private set; }
    public RoseSurface()
    {
        using var stream = typeof(RoseSurface).Assembly.GetManifestResourceStream("Samitivity.Windows.Assets.BurgBg.png")!;
        using var image = Image.FromStream(stream);
        roses = new Bitmap(image);
        DoubleBuffered = true; ResizeRedraw = true;
    }
    protected override void OnSizeChanged(EventArgs e) { ClearBackdrop(); base.OnSizeChanged(e); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { ClearBackdrop(); base.OnDpiChangedAfterParent(e); }
    private void ClearBackdrop() { backdrop?.Dispose(); backdrop = null; Invalidate(true); }
    protected override void OnPaintBackground(PaintEventArgs e)
    { if (Width > 0 && Height > 0) e.Graphics.DrawImageUnscaled(Backdrop, 0, 0); }
    private Bitmap Compose(Size size, int dpi)
    {
        CompositionCount++;
        var result = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(roses, new Rectangle(Point.Empty, result.Size));
        var inset = (int)Math.Round(24 * dpi / 96f);
        var glass = Rectangle.Inflate(new Rectangle(Point.Empty, result.Size), -inset, -inset);
        if (glass.Width <= 0 || glass.Height <= 0) return result;
        // Three separable box passes approximate a Gaussian, with clamped edges and uniform radius.
        using var blurred = Blur(result, Math.Max(1, (int)Math.Round(6 * dpi / 96f)));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        for (var i = 5; i >= 1; i--)
        {
            var shadow = Rectangle.Inflate(glass, i, i); shadow.Offset(0, 2);
            using var shadowPath = GlassTheme.Rounded(shadow, 24 * dpi / 96f);
            using var ink = new SolidBrush(Color.FromArgb(7, 28, 4, 12)); g.FillPath(ink, shadowPath);
        }
        using var shape = GlassTheme.Rounded(glass, 24 * dpi / 96f);
        var state = g.Save(); g.SetClip(shape);
        g.DrawImageUnscaled(blurred, 0, 0);
        using (var tint = new SolidBrush(Color.FromArgb(64, 69, 13, 31))) g.FillPath(tint, shape);
        using (var light = new SolidBrush(Color.FromArgb(12, 255, 229, 216))) g.FillPath(light, shape);
        g.Restore(state);
        using var edge = new Pen(Color.FromArgb(135, 255, 222, 210), Math.Max(1, dpi / 96f)); g.DrawPath(edge, shape);
        return result;
    }
    private static Bitmap Blur(Bitmap source, int radius)
    {
        var result = (Bitmap)source.Clone();
        var bounds = new Rectangle(Point.Empty, result.Size);
        var bits = result.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[bits.Stride * bits.Height]; var scratch = new byte[pixels.Length];
            Marshal.Copy(bits.Scan0, pixels, 0, pixels.Length);
            for (var pass = 0; pass < 3; pass++)
            {
                Sweep(pixels, scratch, bits.Width, bits.Height, bits.Stride, radius, true);
                Sweep(scratch, pixels, bits.Width, bits.Height, bits.Stride, radius, false);
            }
            Marshal.Copy(pixels, 0, bits.Scan0, pixels.Length);
        }
        finally { result.UnlockBits(bits); }
        return result;
    }
    private static void Sweep(byte[] input, byte[] output, int width, int height, int stride, int radius, bool horizontal)
    {
        var lines = horizontal ? height : width; var length = horizontal ? width : height;
        var step = horizontal ? 4 : stride; var divisor = radius * 2 + 1;
        for (var line = 0; line < lines; line++)
        for (var channel = 0; channel < 4; channel++)
        {
            var start = (horizontal ? line * stride : line * 4) + channel; var sum = 0;
            for (var k = -radius; k <= radius; k++) sum += input[start + Math.Clamp(k, 0, length - 1) * step];
            for (var p = 0; p < length; p++)
            {
                output[start + p * step] = (byte)(sum / divisor);
                sum += input[start + Math.Clamp(p + radius + 1, 0, length - 1) * step]
                    - input[start + Math.Clamp(p - radius, 0, length - 1) * step];
            }
        }
    }
    protected override void Dispose(bool disposing)
    { if (disposing) { backdrop?.Dispose(); roses.Dispose(); } base.Dispose(disposing); }
}
