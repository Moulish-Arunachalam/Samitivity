using System.Text.Json;

namespace Samitivity.Core;

public sealed record LoadResult<T>(T Value, string? Warning);

/// <summary>Same-directory rename commit, flushed contents, and a last-valid backup. One process owns each store.</summary>
public sealed class AtomicJsonStore<T>(string path, Func<T> defaults, Action<T> validate) where T : class
{
    private readonly object gate = new();
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public LoadResult<T> Load()
    {
        lock (gate)
        {
            if (TryRead(path, out var value)) return new(value!, null);
            if (TryRead(path + ".bak", out value)) return new(value!, "Recovered from a backup; the primary file was missing or invalid.");
            return new(defaults(), File.Exists(path) || File.Exists(path + ".bak") ? "Invalid data; using defaults. Voice files were preserved." : null);
        }
    }
    public void Save(T value)
    {
        lock (gate)
        {
            validate(value);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            // Never replace a good backup with a damaged primary.
            if (TryRead(path, out var previous)) WriteAtomic(path + ".bak", previous!);
            WriteAtomic(path, value);
        }
    }
    private bool TryRead(string file, out T? value)
    {
        value = null;
        try
        {
            if (!File.Exists(file) || new FileInfo(file).Length > 16 * 1024 * 1024) return false;
            value = JsonSerializer.Deserialize<T>(File.ReadAllBytes(file), Options);
            if (value is null) return false;
            validate(value); return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException) { return false; }
    }
    private static void WriteAtomic(string file, T value)
    {
        var temp = file + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, value, Options); stream.Flush(true); }
            File.Move(temp, file, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
