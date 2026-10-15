using System.Drawing.Drawing2D;

namespace Samitivity.Windows;

internal static class GlassTheme
{
    internal static readonly Color Ivory = Color.FromArgb(255, 238, 229);
    internal static readonly Color Wine = Color.FromArgb(116, 27, 50);
    internal static readonly Color Border = Color.FromArgb(140, 239, 191, 191);
    internal static GraphicsPath Rounded(Rectangle bounds, float radius)
    {
        var p = new GraphicsPath();
        var d = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        p.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        p.AddArc(bounds.Right-d, bounds.Top, d, d, 270, 90);
        p.AddArc(bounds.Right-d, bounds.Bottom-d, d, d, 0, 90);
        p.AddArc(bounds.Left, bounds.Bottom-d, d, d, 90, 90); p.CloseFigure(); return p;
    }
    internal static void PaintBackdrop(Control control, Graphics g, Rectangle area, int tint = 48)
    {
        var origin = Point.Empty; Control? current = control;
        while (current is not null && current is not RoseSurface)
        { origin.Offset(current.Left, current.Top); current = current.Parent; }
        if (current is RoseSurface surface)
        {
            var state = g.Save(); g.SetClip(area, CombineMode.Intersect);
            g.DrawImageUnscaled(surface.Backdrop, -origin.X, -origin.Y); g.Restore(state);
        }
        else g.Clear(Color.FromArgb(91, 38, 48));
        using var shade = new SolidBrush(Color.FromArgb(tint, 57, 9, 24)); g.FillRectangle(shade, area);
    }
    internal static void Outline(Control control)
    {
        if (control.Width < 2 || control.Height < 2) return;
        using var path = Rounded(new Rectangle(0, 0, control.Width, control.Height), 7 * control.DeviceDpi / 96f);
        var old = control.Region; control.Region = new Region(path); old?.Dispose();
    }
}

/// <summary>Native listbox selection/keyboard/scrolling, with the entire client area painted from the wallpaper.</summary>
internal sealed class GlassCategoryList : ListBox
{
    public GlassCategoryList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        DrawMode = DrawMode.OwnerDrawFixed; BorderStyle = BorderStyle.None; IntegralHeight = false; ForeColor = GlassTheme.Ivory;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        GlassTheme.PaintBackdrop(this, e.Graphics, ClientRectangle, 25);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var inset = Math.Max(5, DeviceDpi / 16);
        for (var i = TopIndex; i < Items.Count; i++)
        {
            var r = new Rectangle(inset, (i - TopIndex) * ItemHeight + inset, Width - inset * 2, ItemHeight - inset);
            if (r.Top >= Height) break;
            if (i == SelectedIndex)
            {
                using var path = GlassTheme.Rounded(r, 6 * DeviceDpi / 96f);
                using var ink = new SolidBrush(Color.FromArgb(165, 116, 27, 50)); e.Graphics.FillPath(ink, path);
                using var pen = new Pen(Color.FromArgb(220, 224, 126, 150)); e.Graphics.DrawPath(pen, path);
                if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(r, -3, -3), ForeColor, Color.Transparent);
            }
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[i]), Font, Rectangle.Inflate(r, -10, 0), ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
        using var border = new Pen(GlassTheme.Border);
        using var outline = GlassTheme.Rounded(new Rectangle(0, 0, Width-1, Height-1), 8 * DeviceDpi / 96f);
        e.Graphics.DrawPath(border, outline);
    }
    protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); GlassTheme.Outline(this); Invalidate(); }
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg is 0x115 or 0x20A or 0x100 or 0x201) Invalidate(); // scroll, wheel, keyboard, click
    }
}

/// <summary>The native report control retains selection, accessibility and scrolling.</summary>
internal sealed class GlassRecordingList : ListView
{
    private Image? ownedBackdrop;
    private Bitmap? sampledSurface;
    private Point sampledOrigin;
    public GlassRecordingList()
    {
        OwnerDraw = true; DoubleBuffered = true; BorderStyle = BorderStyle.None;
        ForeColor = GlassTheme.Ivory; BackColor = Color.FromArgb(65, 25, 35);
        DrawColumnHeader += (_, e) =>
        {
            GlassTheme.PaintBackdrop(this, e.Graphics, e.Bounds, 45);
            using var line = new Pen(GlassTheme.Border); e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom-1, e.Bounds.Right, e.Bounds.Bottom-1);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", Font, Rectangle.Inflate(e.Bounds, -10, 0), ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        DrawItem += (_, e) => { if (View != View.Details) e.DrawDefault = true; };
        DrawSubItem += (_, e) =>
        {
            GlassTheme.PaintBackdrop(this, e.Graphics, e.Bounds, e.Item?.Selected == true ? 100 : 65);
            using var line = new Pen(Color.FromArgb(35, GlassTheme.Ivory)); e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom-1, e.Bounds.Right, e.Bounds.Bottom-1);
            TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", Font, Rectangle.Inflate(e.Bounds, -10, 0), ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && e.Item?.Selected == true) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(e.Bounds, -2, -2));
        };
    }
    internal void RefreshBackdrop()
    {
        if (Width < 1 || Height < 1 || Parent is null) return;
        var origin = Point.Empty; Control? ancestor = this;
        while (ancestor is not null && ancestor is not RoseSurface)
        { origin.Offset(ancestor.Left, ancestor.Top); ancestor = ancestor.Parent; }
        var source = (ancestor as RoseSurface)?.Backdrop;
        if (ownedBackdrop?.Size == Size && ReferenceEquals(source, sampledSurface) && origin == sampledOrigin) return;
        sampledSurface = source; sampledOrigin = origin;
        var bitmap = new Bitmap(Width, Height);
        using (var g = Graphics.FromImage(bitmap)) GlassTheme.PaintBackdrop(this, g, ClientRectangle, 65);
        BackgroundImage = bitmap;
        ownedBackdrop?.Dispose(); ownedBackdrop = bitmap;
        GlassTheme.Outline(this); Invalidate();
    }
    protected override void Dispose(bool disposing)
    { if (disposing) { BackgroundImage = null; ownedBackdrop?.Dispose(); } base.Dispose(disposing); }
}

internal sealed class GlassStatus : ScrollableControl
{
    public GlassStatus()
    {
        DoubleBuffered = true; AutoScroll = true; ForeColor = GlassTheme.Ivory;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent;
        TabStop = true; AccessibleRole = AccessibleRole.StaticText;
    }
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); UpdateExtent(); Invalidate(); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateExtent(); GlassTheme.Outline(this); }
    private void UpdateExtent()
    {
        var height = TextRenderer.MeasureText(Text, Font, new Size(Math.Max(20, Width-36), 0), TextFormatFlags.WordBreak).Height;
        AutoScrollMinSize = new Size(0, height+16);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        GlassTheme.PaintBackdrop(this, e.Graphics, ClientRectangle, 45);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(10, 7+AutoScrollPosition.Y, Math.Max(20, ClientSize.Width-20), Math.Max(ClientSize.Height, AutoScrollMinSize.Height)), ForeColor,
            TextFormatFlags.WordBreak | TextFormatFlags.Top | TextFormatFlags.Left);
        using var line = new Pen(GlassTheme.Border);
        using var path = GlassTheme.Rounded(new Rectangle(0, 0, ClientSize.Width-1, ClientSize.Height-1), 7 * DeviceDpi / 96f);
        e.Graphics.DrawPath(line, path);
    }
    protected override void OnScroll(ScrollEventArgs e) { base.OnScroll(e); Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.C) { Clipboard.SetText(Text); e.Handled = true; }
        var delta = e.KeyCode switch { Keys.Down => 20, Keys.Up => -20, Keys.PageDown => Height, Keys.PageUp => -Height, _ => 0 };
        if (delta != 0) { AutoScrollPosition = new Point(0, Math.Max(0, -AutoScrollPosition.Y+delta)); Invalidate(); e.Handled = true; }
        base.OnKeyDown(e);
    }
}

internal sealed class GlassButton : Button
{
    private bool hover;
    public GlassButton()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; BackColor = Color.Transparent;
        ForeColor = GlassTheme.Ivory; Padding = new(9, 5, 9, 5); Margin = new(3);
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        GlassTheme.PaintBackdrop(this, e.Graphics, ClientRectangle, 0);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = GlassTheme.Rounded(new Rectangle(1, 1, Width-3, Height-3), 7 * DeviceDpi / 96f);
        using var fill = new SolidBrush(Color.FromArgb(Enabled ? 220 : 110, hover ? Color.FromArgb(153, 43, 69) : GlassTheme.Wine));
        e.Graphics.FillPath(fill, path);
        using var border = new Pen(GlassTheme.Border); e.Graphics.DrawPath(border, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Color.FromArgb(207, 176, 174),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5));
    }
}

/// <summary>Composited resting field; the native editor supplies caret, IME, selection and clipboard while editing.</summary>
internal sealed class GlassTextInput : UserControl
{
    private readonly TextBox editor = new() { BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(91, 32, 47), ForeColor = GlassTheme.Ivory, Visible = false };
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal int MaxLength { get => editor.MaxLength; set => editor.MaxLength = value; }
    public GlassTextInput()
    {
        DoubleBuffered = true; Height = 30; TabStop = true; ForeColor = GlassTheme.Ivory;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent;
        Controls.Add(editor); editor.TextChanged += (_, _) => { if (Text != editor.Text) Text = editor.Text; };
    }
    protected override void OnTextChanged(EventArgs e) { if (editor.Text != Text) editor.Text = Text; base.OnTextChanged(e); Invalidate(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); editor.Font = Font; PositionEditor(); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); PositionEditor(); GlassTheme.Outline(this); }
    private void PositionEditor() => editor.SetBounds(9, Math.Max(1, (Height-editor.PreferredHeight)/2), Math.Max(1, Width-18), editor.PreferredHeight);
    protected override void OnEnter(EventArgs e) { base.OnEnter(e); editor.Visible = true; editor.Focus(); }
    protected override void OnLeave(EventArgs e) { editor.Visible = false; base.OnLeave(e); Invalidate(); }
    internal void SelectAll() => editor.SelectAll();
    protected override void OnPaint(PaintEventArgs e)
    {
        GlassTheme.PaintBackdrop(this, e.Graphics, ClientRectangle, 65);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = GlassTheme.Rounded(new Rectangle(0, 0, Width-1, Height-1), 6 * DeviceDpi / 96f);
        using var border = new Pen(GlassTheme.Border); e.Graphics.DrawPath(border, path);
        if (!editor.Visible) TextRenderer.DrawText(e.Graphics, Text, Font, Rectangle.Inflate(ClientRectangle, -9, 0), ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class GlassCheckBox : CheckBox
{
    public GlassCheckBox() { BackColor = Color.Transparent; ForeColor = GlassTheme.Ivory; }
    protected override void OnPaint(PaintEventArgs e)
    {
        GlassTheme.PaintBackdrop(this, e.Graphics, ClientRectangle, 0);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var size = (int)(17 * DeviceDpi / 96f);
        var bounds = new Rectangle(1, (Height-size)/2, size, size);
        using var path = GlassTheme.Rounded(bounds, 4 * DeviceDpi / 96f);
        using var ink = new SolidBrush(Color.FromArgb(Checked ? 230 : 30, GlassTheme.Wine)); e.Graphics.FillPath(ink, path);
        using var edge = new Pen(GlassTheme.Border); e.Graphics.DrawPath(edge, path);
        if (Checked)
        {
            using var tick = new Pen(GlassTheme.Ivory, Math.Max(1.5f, DeviceDpi/64f));
            e.Graphics.DrawLines(tick, new PointF[] {new(bounds.Left+size*.22f, bounds.Top+size*.52f), new(bounds.Left+size*.43f, bounds.Top+size*.72f), new(bounds.Left+size*.8f, bounds.Top+size*.28f) });
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(size+7, 0, Width-size-7, Height), ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
    }
}
