using Samitivity.Core;

namespace Samitivity.Windows;
internal sealed class ShortcutEditor : FlowLayoutPanel
{
    private readonly CheckBox ctrl = new GlassCheckBox() { Text = "Ctrl", AutoSize = true };
    private readonly CheckBox alt = new GlassCheckBox() { Text = "Alt", AutoSize = true };
    private readonly CheckBox shift = new GlassCheckBox() { Text = "Shift", AutoSize = true };
    private readonly CheckBox win = new GlassCheckBox() { Text = "Win", AutoSize = true };
    private readonly ComboBox key = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private sealed record KeyChoice(int Code, string Label) { public override string ToString() => Label; }
    public event Action? Changed;
    public ShortcutEditor()
    {
        AutoSize = true; WrapContents = false; BackColor = Color.Transparent;
        foreach (var box in new[] { ctrl, alt, shift, win }) { box.Padding = new(3, 0, 3, 0); box.MinimumSize = new(62, 26); }
        key.FlatStyle = FlatStyle.Flat; key.BackColor = Color.FromArgb(91, 32, 47); key.ForeColor = GlassTheme.Ivory;
        key.DrawMode = DrawMode.OwnerDrawFixed;
        key.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            if (e.State.HasFlag(DrawItemState.ComboBoxEdit)) GlassTheme.PaintBackdrop(key, e.Graphics, e.Bounds, 65);
            else { using var fill = new SolidBrush(e.State.HasFlag(DrawItemState.Selected) ? GlassTheme.Wine : Color.FromArgb(72, 26, 39)); e.Graphics.FillRectangle(fill, e.Bounds); }
            TextRenderer.DrawText(e.Graphics, key.Items[e.Index]?.ToString() ?? "", key.Font, e.Bounds, GlassTheme.Ivory, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            e.DrawFocusRectangle();
        };
        foreach (var code in Enumerable.Range(65, 26).Concat(Enumerable.Range(48, 10)).Concat(Enumerable.Range(112, 11)).Concat([8, 46]))
            key.Items.Add(new KeyChoice(code, code switch { 8 => "Backspace", 46 => "Delete", >= 112 => "F" + (code - 111), _ => ((char)code).ToString() }));
        Controls.AddRange([ctrl, alt, shift, win, key]);
        foreach (var box in new[] { ctrl, alt, shift, win }) box.CheckedChanged += (_, _) => Changed?.Invoke();
        key.SelectedIndexChanged += (_, _) => Changed?.Invoke(); key.SelectedIndex = 0;
    }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Shortcut Value
    {
        get => new((ctrl.Checked ? Modifiers.Control : 0) | (alt.Checked ? Modifiers.Alt : 0) | (shift.Checked ? Modifiers.Shift : 0) | (win.Checked ? Modifiers.Windows : 0), ((KeyChoice)key.SelectedItem!).Code);
        set
        {
            ctrl.Checked = value.Modifiers.HasFlag(Modifiers.Control); alt.Checked = value.Modifiers.HasFlag(Modifiers.Alt);
            shift.Checked = value.Modifiers.HasFlag(Modifiers.Shift); win.Checked = value.Modifiers.HasFlag(Modifiers.Windows);
            key.SelectedItem = key.Items.Cast<KeyChoice>().First(x => x.Code == value.Key);
        }
    }
}
