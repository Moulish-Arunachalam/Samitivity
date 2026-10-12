using Samitivity.Core;

namespace Samitivity.Windows;
internal sealed class ShortcutEditor : FlowLayoutPanel
{
    private readonly CheckBox ctrl = new() { Text = "Ctrl", AutoSize = true };
    private readonly CheckBox alt = new() { Text = "Alt", AutoSize = true };
    private readonly CheckBox shift = new() { Text = "Shift", AutoSize = true };
    private readonly CheckBox win = new() { Text = "Win", AutoSize = true };
    private readonly ComboBox key = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private sealed record KeyChoice(int Code, string Label) { public override string ToString() => Label; }
    public event Action? Changed;
    public ShortcutEditor()
    {
        AutoSize = true; WrapContents = false;
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
