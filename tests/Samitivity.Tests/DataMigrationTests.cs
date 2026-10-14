using Samitivity.Core;

namespace Samitivity.Tests;
public sealed class DataMigrationTests : TempTest
{
    private string Old => Path.Combine(Root, "legacy");
    private string New => Path.Combine(Root, "Samitivity");
    private void Put(string root, string file, string text)
    { var p = Path.Combine(root, file); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, text); }
    [Fact] public void FirstMigrationPreservesAllDataAndOriginals()
    {
        foreach (var file in new[] { "config.json", "state.json", "library.json", "Trash/a.wav", "VoiceNotes/category/a.wav" }) Put(Old, file, file);
        DataMigration.Run(Old, New);
        foreach (var file in Directory.GetFiles(Old, "*", SearchOption.AllDirectories))
            Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(New, Path.GetRelativePath(Old, file))));
        Assert.True(File.Exists(Path.Combine(New, DataMigration.Receipt)));
    }
    [Fact] public void BothRootsPreserveConflictsAndRetryDoesNotDuplicate()
    {
        Put(Old, "config.json", "old settings"); Put(New, "config.json", "new settings");
        Put(Old, "state.json", "old history"); Put(New, "state.json", "new history");
        Put(Old, "VoiceNotes/category/a.wav", "old audio"); Put(New, "VoiceNotes/category/a.wav", "new audio");
        DataMigration.Run(Old, New);
        Assert.Equal("new settings", File.ReadAllText(Path.Combine(New, "config.json")));
        Assert.Equal("old settings", File.ReadAllText(Path.Combine(New, "RecoveredData/config.json")));
        Assert.Equal("old history", File.ReadAllText(Path.Combine(New, "RecoveredData/state.json")));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(New, "VoiceNotes/category")).Length);
        File.Delete(Path.Combine(New, DataMigration.Receipt)); // interruption before completion receipt
        DataMigration.Run(Old, New);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(New, "VoiceNotes/category")).Length);
        Put(Old, "late.wav", "after migration"); DataMigration.Run(Old, New);
        Assert.False(File.Exists(Path.Combine(New, "late.wav")));
    }
    [Fact] public void NewOnlyAndNeitherAreSafe()
    {
        DataMigration.Run(Old, New); Assert.True(Directory.Exists(New));
        Put(New, "config.json", "current"); DataMigration.Run(Old, New);
        Assert.Equal("current", File.ReadAllText(Path.Combine(New, "config.json")));
    }
    [Fact] public void LinkedSourceIsRejectedWithoutCompletionReceipt()
    {
        if (OperatingSystem.IsWindows()) return;
        Put(Old, "safe.wav", "safe"); Put(Root, "outside.wav", "private");
        File.CreateSymbolicLink(Path.Combine(Old, "linked.wav"), Path.Combine(Root, "outside.wav"));
        Assert.Throws<IOException>(() => DataMigration.Run(Old, New));
        Assert.False(File.Exists(Path.Combine(New, DataMigration.Receipt)));
        Assert.False(File.Exists(Path.Combine(New, "linked.wav")));
    }
}
