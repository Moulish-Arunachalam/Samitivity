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

/// <summary>Native report selection and scrolling with deterministic composited client painting.</summary>
internal sealed class GlassRecordingList : ListView
{
    private readonly ImageList rowHeight = new();
    public GlassRecordingList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BorderStyle = BorderStyle.None; HeaderStyle = ColumnHeaderStyle.None;
        ForeColor = GlassTheme.Ivory; BackColor = Color.FromArgb(65, 25, 35);
        rowHeight.ImageSize = new Size(1, 27); SmallImageList = rowHeight;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        GlassTheme.PaintBackdrop(this, e.Graphics, ClientRectangle, 65);
        foreach (ListViewItem item in Items)
        {
            var row = item.Bounds;
            if (row.Bottom < 0 || row.Top >= Height) continue;
            if (item.Selected)
            {
                using var selected = new SolidBrush(Color.FromArgb(130, GlassTheme.Wine));
                e.Graphics.FillRectangle(selected, new Rectangle(0, row.Top, ClientSize.Width, row.Height));
            }
            var x = 0;
            for (var i = 0; i < Columns.Count; i++)
            {
                var bounds = new Rectangle(x + 10, row.Top, Math.Max(1, Columns[i].Width - 20), row.Height);
                TextRenderer.DrawText(e.Graphics, i < item.SubItems.Count ? item.SubItems[i].Text : "", Font, bounds, ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                x += Columns[i].Width;
            }
            using var line = new Pen(Color.FromArgb(35, GlassTheme.Ivory));
            e.Graphics.DrawLine(line, 0, row.Bottom-1, ClientSize.Width, row.Bottom-1);
            if (Focused && item.Selected) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(row, -2, -2));
        }
    }
    internal void RefreshBackdrop() { Invalidate(); Parent?.Invalidate(); }
    protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Columns.Count == 3)
        {
            Columns[0].Width = (int)(ClientSize.Width * .53);
            Columns[1].Width = (int)(ClientSize.Width * .21);
            Columns[2].Width = Math.Max(0, ClientSize.Width - Columns[0].Width - Columns[1].Width - 1);
        }
        Invalidate(); Parent?.Invalidate();
    }
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg is 0x115 or 0x114 or 0x20A or 0x100 or 0x201 or 0x202) Invalidate();
    }
    protected override void Dispose(bool disposing) { if (disposing) rowHeight.Dispose(); base.Dispose(disposing); }
}

/// <summary>Composited header outside the native list avoids opaque unused header space.</summary>
internal sealed class GlassRecordingPanel : Panel
{
    private readonly GlassRecordingList list;
    public GlassRecordingPanel(GlassRecordingList list)
    {
        this.list = list; DoubleBuffered = true; Dock = DockStyle.Fill;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent;
        Padding = new Padding(1, 28, 1, 1); Controls.Add(list);
    }
    protected override void OnResize(EventArgs e) { base.OnResize(e); GlassTheme.Outline(this); Invalidate(); }
    protected override void OnPaintBackground(PaintEventArgs e) => GlassTheme.PaintBackdrop(this, e.Graphics, ClientRectangle, 45);
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var x = Padding.Left;
        foreach (ColumnHeader column in list.Columns)
        {
            TextRenderer.DrawText(e.Graphics, column.Text, Font, new Rectangle(x+10, 0, Math.Max(1, column.Width-20), Padding.Top), GlassTheme.Ivory,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            x += column.Width;
        }
        using var border = new Pen(GlassTheme.Border);
        e.Graphics.DrawLine(border, 0, Padding.Top-1, Width, Padding.Top-1);
        using var shape = GlassTheme.Rounded(new Rectangle(0, 0, Width-1, Height-1), 7 * DeviceDpi / 96f);
        e.Graphics.DrawPath(border, shape);
    }
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
