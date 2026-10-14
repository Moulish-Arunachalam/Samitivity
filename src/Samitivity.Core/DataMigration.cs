using System.Security.Cryptography;

namespace Samitivity.Core;

/// <summary>Run under the application instance mutex, before opening any stores.</summary>
public static class DataMigration
{
    public const string Receipt = "storage-migration-v1.txt";
    public static void Run(string legacy, string destination)
    {
        RejectLinks(legacy); RejectLinks(destination);
        Directory.CreateDirectory(destination);
        var receipt = Path.Combine(destination, Receipt);
        if (File.Exists(receipt) || !Directory.Exists(legacy)) return;
        // The source remains an untouched recovery copy. No completion receipt is written on failure.
        CopyTree(legacy, destination, destination, "");
        WriteNew(receipt, System.Text.Encoding.UTF8.GetBytes(
            "Storage migration completed. The previous data folder is retained as a backup. " +
            "Existing Samitivity settings and history take precedence. Conflicting files are preserved in RecoveredData; " +
            "recordings with conflicting names are kept alongside the current recordings. " +
            "If categories differ, use the previous config in RecoveredData to identify folders and import their recordings."));
    }
    private static void CopyTree(string source, string target, string root, string relative)
    {
        RejectLinks(source); RejectLinks(target); Directory.CreateDirectory(target);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            RejectLinks(entry);
            var name = Path.GetFileName(entry);
            var rel = Path.Combine(relative, name);
            var output = Path.Combine(target, name);
            RejectLinks(output);
            if (Directory.Exists(entry)) { CopyTree(entry, output, root, rel); continue; }
            if (File.Exists(output))
            {
                if (Hash(entry) == Hash(output)) continue;
                // Stable conflict names make retries idempotent, including after a partial migration.
                output = relative.StartsWith("VoiceNotes" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(target, "recovered-" + Hash(entry) + Path.GetExtension(name))
                    : Path.Combine(root, "RecoveredData", rel);
                RejectLinks(output);
                if (File.Exists(output))
                {
                    if (Hash(entry) == Hash(output)) continue;
                    throw new IOException("A recovery destination already contains different data. Migration stopped without overwriting it.");
                }
            }
            CopyNew(entry, output);
        }
    }
    private static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
    private static void CopyNew(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + ".migration-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { input.CopyTo(output); output.Flush(true); }
            File.SetLastWriteTimeUtc(temp, File.GetLastWriteTimeUtc(source));
            File.Move(temp, target, false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static void WriteNew(string path, byte[] bytes)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(bytes); file.Flush(true);
    }
    private static void RejectLinks(string path)
    {
        for (var p = Path.GetFullPath(path); p is not null; p = Path.GetDirectoryName(p))
            if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked migration paths are not supported.");
    }
}
