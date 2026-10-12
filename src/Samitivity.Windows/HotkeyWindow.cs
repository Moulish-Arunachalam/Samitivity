using System.ComponentModel;
using System.Runtime.InteropServices;
using Samitivity.Core;

namespace Samitivity.Windows;

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly Dictionary<int, Guid?> bindings = [];
    public event Action<Guid?>? Activated;
    public event Action? ExplorerRestarted;
    public HotkeyWindow() => CreateHandle(new CreateParams { Caption = "Samitivity events" });
    public List<string> Apply(AppConfig config)
    {
        foreach (var id in bindings.Keys) UnregisterHotKey(Handle, id);
        bindings.Clear(); var errors = new List<string>(); var next = 1;
        // Register Stop first so an emergency action is never displaced by a category binding.
        Register(next++, null, "Stop", config.StopShortcut, errors);
        foreach (var category in config.Categories) Register(next++, category.Id, category.Name, category.Shortcut, errors);
        return errors;
    }
    private void Register(int id, Guid? category, string name, Shortcut shortcut, List<string> errors)
    {
        if (RegisterHotKey(Handle, id, (uint)shortcut.Modifiers | 0x4000, (uint)shortcut.Key)) bindings.Add(id, category);
        else errors.Add($"{name}: {shortcut} unavailable (Windows error {Marshal.GetLastWin32Error()}). Reassign in Configure.");
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x312 && bindings.TryGetValue(m.WParam.ToInt32(), out var category)) Activated?.Invoke(category);
        if ((uint)m.Msg == taskbarCreated) ExplorerRestarted?.Invoke();
        base.WndProc(ref m);
    }
    public void Dispose() { foreach (var id in bindings.Keys) UnregisterHotKey(Handle, id); bindings.Clear(); DestroyHandle(); }
}
