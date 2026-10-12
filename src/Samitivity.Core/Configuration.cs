using System.Text.Json;

namespace Samitivity.Core;

[Flags]
public enum Modifiers { None = 0, Alt = 1, Control = 2, Shift = 4, Windows = 8 }
public sealed record Shortcut(Modifiers Modifiers, int Key)
{
    public void Validate()
    {
        if (Modifiers == Modifiers.None || ((int)Modifiers & ~15) != 0)
            throw new InvalidDataException("A shortcut needs Ctrl, Alt, Shift or Win plus one key.");
        // Deliberate allowlist: no bare keys, modifiers, F12, mouse buttons or reserved system sequences.
        if (!(Key is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or >= 0x70 and <= 0x7A or 8 or 0x2E))
            throw new InvalidDataException("Use a letter, digit, F1–F11, Backspace or Delete.");
        if (Key == 0x2E && Modifiers.HasFlag(Modifiers.Control) && Modifiers.HasFlag(Modifiers.Alt))
            throw new InvalidDataException("Ctrl+Alt+Delete is reserved by Windows.");
    }
    public override string ToString() => $"{Modifiers} + {Key switch { 8 => "Backspace", 46 => "Delete", >= 112 and <= 122 => "F" + (Key - 111), _ => ((char)Key).ToString() }}";
}
public sealed record Category(Guid Id, string Name, Shortcut Shortcut);
public sealed record AppConfig
{
    public int Version { get; init; } = 1;
    public List<Category> Categories { get; init; } = [];
    public Shortcut StopShortcut { get; init; } = new(Modifiers.Control | Modifiers.Alt, 8);
    public static AppConfig Defaults() => new()
    {
        Categories = [
            new(Guid.Parse("05c22a8f-210d-4dab-bc9a-116a9c236101"), "Miss Me", new(Modifiers.Control | Modifiers.Alt, 0x53)),
            new(Guid.Parse("05c22a8f-210d-4dab-bc9a-116a9c236102"), "Motivation", new(Modifiers.Control | Modifiers.Shift | Modifiers.Windows, 0x53)),
            new(Guid.Parse("05c22a8f-210d-4dab-bc9a-116a9c236103"), "Just Because", new(Modifiers.Alt | Modifiers.Shift, 0x53))]
    };
    public void Validate()
    {
        if (Version != 1 || Categories is null || Categories.Count > 32 || StopShortcut is null)
            throw new InvalidDataException("Unsupported or malformed configuration.");
        StopShortcut.Validate();
        var ids = new HashSet<Guid>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var shortcuts = new HashSet<Shortcut> { StopShortcut };
        foreach (var c in Categories)
        {
            if (c is null || c.Id == Guid.Empty || !ids.Add(c.Id) || string.IsNullOrWhiteSpace(c.Name) ||
                c.Name.Length > 60 || c.Name.Any(char.IsControl) || !names.Add(c.Name.Trim()) || c.Shortcut is null)
                throw new InvalidDataException("Categories need unique IDs and names (1–60 characters).");
            c.Shortcut.Validate();
            if (!shortcuts.Add(c.Shortcut)) throw new InvalidDataException("Every shortcut, including Stop, must be unique.");
        }
    }
    public AppConfig Copy() => JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(this))!;
}
