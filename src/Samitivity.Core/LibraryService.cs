using System.Security.Cryptography;

namespace Samitivity.Core;

public interface IAudioValidator { TimeSpan Validate(string path, CancellationToken cancellationToken); }
public sealed record IndexedRecording(string Id, Guid CategoryId, string FileName, long Length, long ModifiedUtcTicks, long DurationTicks);
public sealed class LibraryIndex
{
    public int Version { get; set; } = 1;
    public List<IndexedRecording> Recordings { get; set; } = [];
    public void Validate()
    {
        if (Version != 1 || Recordings is null || Recordings.Count > 100000 || Recordings.Any(x => x is null ||
            x.CategoryId == Guid.Empty || x.Id is null || x.Id.Length != 64 || !x.Id.All(Uri.IsHexDigit) ||
            !LibraryService.IsSafeLeaf(x.FileName) || x.DurationTicks <= 0 || x.Length <= 0))
            throw new InvalidDataException("Invalid library index.");
    }
}
public sealed class StagedImport(string path, string originalName, string hash, TimeSpan duration) : IDisposable
{
    public string Path { get; } = path;
    public string OriginalName { get; } = originalName;
    public string Hash { get; } = hash;
    public TimeSpan Duration { get; } = duration;
    public void Dispose() { if (File.Exists(Path)) File.Delete(Path); }
}
public sealed class LibraryService : IRecordingSource
{
    public const long MaxFileBytes = 256L * 1024 * 1024;
    public static readonly string[] Extensions = [".m4a", ".mp3", ".wav"];
    private readonly object gate = new();
    private readonly object mutation = new();
    private readonly string root;
    private readonly IAudioValidator validator;
    private readonly AtomicJsonStore<LibraryIndex> store;
    private LibraryIndex index;
    private readonly List<string> warnings = [];
    public IReadOnlyList<string> Warnings { get { lock (gate) return warnings.ToArray(); } }
    public LibraryService(string dataDirectory, IAudioValidator validator)
    {
        root = Path.GetFullPath(dataDirectory); this.validator = validator;
        Directory.CreateDirectory(root);
        store = new(Path.Combine(root, "library.json"), () => new(), x => x.Validate());
        var loaded = store.Load(); index = loaded.Value;
        if (loaded.Warning is not null) warnings.Add(loaded.Warning);
        Directory.CreateDirectory(Path.Combine(root, "VoiceNotes"));
        Directory.CreateDirectory(Path.Combine(root, ".staging"));
        RejectLinks(root);
        // A single application instance owns staging. An interrupted copy was never published.
        foreach (var p in Directory.EnumerateFiles(Path.Combine(root, ".staging"), "*.part.*")) File.Delete(p);
    }
    public static bool IsSafeLeaf(string? name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 180 &&
        name is not "." and not ".." && !name.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c)) &&
        !name.EndsWith('.') && !name.EndsWith(' ');
    public static string SafeName(string name)
    {
        var chars = Path.GetFileName(name).Select(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray();
        var cleaned = new string(chars).Trim().TrimEnd('.');
        return cleaned.Length > 100 ? cleaned[..80] + Path.GetExtension(cleaned) : cleaned;
    }
    public string CategoryFolder(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("Missing category ID.");
        var path = Path.Combine(root, "VoiceNotes", id.ToString("N"));
        Directory.CreateDirectory(path); RejectLinks(path); return path;
    }
    public StagedImport Stage(string source, CancellationToken cancellationToken = default, IProgress<string>? progress = null)
    {
        var started = Diagnostics.Now;
        progress?.Report("Importing");
        source = Path.GetFullPath(source);
        if (source.StartsWith(@"\\", StringComparison.Ordinal) || source.StartsWith("//", StringComparison.Ordinal) ||
            (OperatingSystem.IsWindows() && new DriveInfo(Path.GetPathRoot(source)!).DriveType == DriveType.Network))
            throw new InvalidDataException("Import from a local disk or USB drive; network paths are not supported.");
        var extension = Path.GetExtension(source).ToLowerInvariant();
        if (!Extensions.Contains(extension)) throw new InvalidDataException("Choose M4A, MP3 or WAV recordings.");
        if (!File.Exists(source) || new FileInfo(source).Length is <= 0 or > MaxFileBytes)
            throw new InvalidDataException("Recordings must be nonempty and at most 256 MiB.");
        RejectLinks(source);
        var target = Path.Combine(root, ".staging", Guid.NewGuid().ToString("N") + ".part" + extension);
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[65536]; long copied = 0; int read;
                while ((read = input.Read(buffer)) != 0)
                {
                    cancellationToken.ThrowIfCancellationRequested(); copied += read;
                    if (copied > MaxFileBytes) throw new InvalidDataException("Recording exceeds 256 MiB.");
                    output.Write(buffer, 0, read);
                }
                output.Flush(true);
            }
            Diagnostics.Mark("import_copy", started);
            progress?.Report("Validating");
            var validation = Diagnostics.Now;
            var duration = validator.Validate(target, cancellationToken);
            Diagnostics.Mark("import_validation", validation);
            if (duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(2)) throw new InvalidDataException("Recording must be shorter than two hours and contain audio.");
            using var stream = File.OpenRead(target);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            Diagnostics.Mark("import_total", started);
            return new(target, SafeName(Path.GetFileName(source)), hash, duration);
        }
        catch { if (File.Exists(target)) File.Delete(target); throw; }
    }
    public bool Commit(Guid category, StagedImport staged)
    {
        lock (mutation)
        lock (gate)
        {
            var folder = CategoryFolder(category);
            if (index.Recordings.Any(x => x.CategoryId == category && x.Id == staged.Hash && File.Exists(Path.Combine(folder, x.FileName)))) return false;
            // Content prefix avoids reserved device names and duplicate basenames. The original bytes are unchanged.
            var name = staged.Hash[..16] + "-" + staged.OriginalName;
            var path = Path.Combine(folder, name);
            if (File.Exists(path)) name = Guid.NewGuid().ToString("N") + "-" + staged.OriginalName;
            path = Path.Combine(folder, name);
            File.Move(staged.Path, path, false);
            var info = new FileInfo(path);
            index.Recordings.Add(new(staged.Hash, category, name, info.Length, info.LastWriteTimeUtc.Ticks, staged.Duration.Ticks));
            store.Save(index); return true;
        }
    }
    public void Refresh(IEnumerable<Guid> categories, CancellationToken cancellationToken = default)
    {
        lock (mutation)
        {
            LibraryIndex previous;
            lock (gate) previous = index;
            var updated = new LibraryIndex(); var newWarnings = new List<string>();
            foreach (var category in categories)
            {
                var folder = CategoryFolder(category);
                foreach (var path in Directory.EnumerateFiles(folder).Where(x => Extensions.Contains(Path.GetExtension(x).ToLowerInvariant())))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        RejectLinks(path);
                        var info = new FileInfo(path);
                        if (!IsSafeLeaf(info.Name) || info.Length is <= 0 or > MaxFileBytes) throw new InvalidDataException();
                        var old = previous.Recordings.Find(x => x.CategoryId == category && x.FileName == info.Name && x.Length == info.Length && x.ModifiedUtcTicks == info.LastWriteTimeUtc.Ticks);
                        IndexedRecording note;
                        if (old is not null) note = old;
                        else
                        {
                            var duration = validator.Validate(path, cancellationToken);
                            using var stream = File.OpenRead(path);
                            var hash = Convert.ToHexString(SHA256.HashData(stream));
                            note = new(hash, category, info.Name, info.Length, info.LastWriteTimeUtc.Ticks, duration.Ticks);
                        }
                        updated.Recordings.Add(note);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception e) when (e is not OutOfMemoryException)
                    { if (newWarnings.Count < 50) newWarnings.Add($"{Path.GetFileName(path)}: unavailable or unsupported ({e.GetType().Name})."); }
                }
            }
            if (!previous.Recordings.SequenceEqual(updated.Recordings)) store.Save(updated);
            lock (gate) { index = updated; warnings.Clear(); warnings.AddRange(newWarnings); }
        }
    }
    public IReadOnlyList<Recording> GetRecordings(Guid category)
    {
        lock (gate)
        {
            var folder = Path.Combine(root, "VoiceNotes", category.ToString("N"));
            return index.Recordings.Where(x => x.CategoryId == category).GroupBy(x => x.Id).Select(g => g.First())
                .Select(x => new Recording(x.Id, category, x.FileName, Path.Combine(folder, x.FileName), TimeSpan.FromTicks(x.DurationTicks))).ToArray();
        }
    }
    public void Delete(Guid category, string id)
    {
        lock (mutation)
        lock (gate)
        {
            var matches = index.Recordings.Where(x => x.CategoryId == category && x.Id == id).ToArray();
            var trash = Path.Combine(root, "Trash"); Directory.CreateDirectory(trash); RejectLinks(trash);
            foreach (var note in matches)
            {
                var path = Path.Combine(CategoryFolder(category), note.FileName);
                RejectLinks(path);
                if (File.Exists(path)) File.Move(path, Path.Combine(trash, Guid.NewGuid().ToString("N") + "-" + note.FileName));
                index.Recordings.Remove(note);
            }
            store.Save(index);
        }
    }
    private static void RejectLinks(string path)
    {
        for (var p = Path.GetFullPath(path); p is not null; p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked library paths are not supported.");
    }
}
