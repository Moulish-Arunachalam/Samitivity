using System.Drawing.Drawing2D;

namespace Samitivity.Windows;

/// <summary>Static, local glass treatment; no desktop capture or composition API dependency.</summary>
internal sealed class RoseSurface : Panel
{
    private readonly Image roses;
    private Bitmap? backdrop;
    public RoseSurface()
    {
        using var stream = typeof(RoseSurface).Assembly.GetManifestResourceStream("Samitivity.Windows.Assets.BurgBg.png")!;
        using var image = Image.FromStream(stream);
        roses = new Bitmap(image);
        DoubleBuffered = true;
        ResizeRedraw = true;
    }
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        backdrop?.Dispose(); backdrop = null;
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (Width <= 0 || Height <= 0) return;
        if (backdrop is null)
        {
            backdrop = new Bitmap(Width, Height);
            using var g = Graphics.FromImage(backdrop);
            g.DrawImage(roses, ClientRectangle);
            var inset = (int)(20 * DeviceDpi / 96f);
            var glass = Rectangle.Inflate(ClientRectangle, -inset, -inset);
            using var shape = Rounded(glass, 24 * DeviceDpi / 96f);
            g.SetClip(shape);
            // Downsample then interpolate once: a soft backdrop cached for this window size.
            using var soft = new Bitmap(Math.Max(1, Width / 24), Math.Max(1, Height / 24));
            using (var blur = Graphics.FromImage(soft)) blur.DrawImage(roses, new Rectangle(0, 0, soft.Width, soft.Height));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(soft, ClientRectangle);
            using var tint = new SolidBrush(Color.FromArgb(228, 255, 246, 242));
            g.FillPath(tint, shape);
            g.ResetClip(); g.SmoothingMode = SmoothingMode.AntiAlias;
            using var edge = new Pen(Color.FromArgb(190, Color.White), 1);
            g.DrawPath(edge, shape);
        }
        e.Graphics.DrawImageUnscaled(backdrop, 0, 0);
    }
    private static GraphicsPath Rounded(Rectangle r, float radius)
    {
        var p = new GraphicsPath();
        p.AddArc(r.Left, r.Top, radius, radius, 180, 90);
        p.AddArc(r.Right-radius, r.Top, radius, radius, 270, 90);
        p.AddArc(r.Right-radius, r.Bottom-radius, radius, radius, 0, 90);
        p.AddArc(r.Left, r.Bottom-radius, radius, radius, 90, 90); p.CloseFigure(); return p;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { backdrop?.Dispose(); roses.Dispose(); }
        base.Dispose(disposing);
    }
}
