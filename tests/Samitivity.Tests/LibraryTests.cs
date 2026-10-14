using Samitivity.Core;

namespace Samitivity.Tests;
public sealed class LibraryTests : TempTest
{
    private string Source(string name, string contents = "synthetic") { var p = Path.Combine(Root, name); File.WriteAllText(p, contents); return p; }
    private LibraryService Library(Validator? validator = null) => new(Path.Combine(Root, "data"), validator ?? new());
    [Fact] public void ImportPreservesOriginalAndIsInvisibleUntilCommit()
    {
        var source = Source("voice.m4a"); var library = Library(); var category = Guid.NewGuid();
        using var staged = library.Stage(source); Assert.Empty(library.GetRecordings(category));
        Assert.True(library.Commit(category, staged)); var saved = Assert.Single(library.GetRecordings(category));
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(saved.FullPath));
    }
    [Fact] public void UnsupportedAndCorruptImportsLeaveNoPartialFiles()
    {
        var library = Library();
        Assert.Throws<InvalidDataException>(() => library.Stage(Source("bad.m4a", "bad audio")));
        Assert.Throws<InvalidDataException>(() => library.Stage(Source("script.exe")));
        Assert.Empty(Directory.GetFiles(Path.Combine(Root, "data", ".staging")));
    }
    [Fact] public void DuplicateContentIsSkippedAndSameFilenameNeverOverwrites()
    {
        var library = Library(); var category = Guid.NewGuid(); var source = Source("same.wav", "first");
        using (var staged = library.Stage(source)) Assert.True(library.Commit(category, staged));
        using (var staged = library.Stage(source)) Assert.False(library.Commit(category, staged));
        File.WriteAllText(source, "second");
        using (var staged = library.Stage(source)) Assert.True(library.Commit(category, staged));
        Assert.Equal(2, library.GetRecordings(category).Count);
    }
    [Fact] public void RenameRetainsContentIdentityAndRemovalDisappears()
    {
        var library = Library(); var category = Guid.NewGuid();
        using (var staged = library.Stage(Source("original.mp3"))) library.Commit(category, staged);
        var before = Assert.Single(library.GetRecordings(category)); var renamed = Path.Combine(library.CategoryFolder(category), "renamed.mp3");
        File.Move(before.FullPath, renamed); library.Refresh([category]);
        Assert.Equal(before.Id, Assert.Single(library.GetRecordings(category)).Id);
        File.Delete(renamed); library.Refresh([category]); Assert.Empty(library.GetRecordings(category));
    }
    [Fact] public void UnchangedFilesAreNotDecodedAgainAndModifiedFilesAreValidated()
    {
        var validator = new Validator(); var library = Library(validator); var category = Guid.NewGuid();
        using (var staged = library.Stage(Source("a.wav"))) library.Commit(category, staged);
        library.Refresh([category]); Assert.Equal(1, validator.Calls);
        File.WriteAllText(Assert.Single(library.GetRecordings(category)).FullPath, "bad corruption");
        library.Refresh([category]); Assert.Empty(library.GetRecordings(category)); Assert.Single(library.Warnings);
    }
    [Fact] public void DeleteMovesToTrashAndPreservesSource()
    {
        var library = Library(); var category = Guid.NewGuid(); var source = Source("a.wav");
        using (var staged = library.Stage(source)) library.Commit(category, staged);
        library.Delete(category, Assert.Single(library.GetRecordings(category)).Id);
        Assert.Empty(library.GetRecordings(category)); Assert.True(File.Exists(source)); Assert.Single(Directory.GetFiles(Path.Combine(Root, "data", "Trash")));
    }
    [Theory] [InlineData("../escape.wav")] [InlineData("..\\escape.wav")] [InlineData("C:escape.wav")] [InlineData("a\nb.wav")] [InlineData("a.wav.")]
    public void UnsafeNamesRejected(string name) => Assert.False(LibraryService.IsSafeLeaf(name));
    [Fact] public void InterruptedStagingIsCleanedOnRestart()
    {
        var library = Library(); var staged = library.Stage(Source("a.wav"));
        Assert.True(File.Exists(staged.Path)); _ = Library(); Assert.False(File.Exists(staged.Path));
    }
    [Fact] public void CancelledCopyIsNotPublished()
    {
        var library = Library(); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => library.Stage(Source("a.wav"), cancelled.Token));
        Assert.Empty(Directory.GetFiles(Path.Combine(Root, "data", ".staging")));
    }
    [Fact] public void ExternalDuplicateCopiesAreOneNoteAndDeleteRemovesAllCopies()
    {
        var library = Library(); var category = Guid.NewGuid();
        using (var staged = library.Stage(Source("a.wav"))) library.Commit(category, staged);
        var original = Assert.Single(library.GetRecordings(category));
        File.Copy(original.FullPath, Path.Combine(library.CategoryFolder(category), "duplicate.wav"));
        library.Refresh([category]); Assert.Single(library.GetRecordings(category));
        library.Delete(category, original.Id); library.Refresh([category]);
        Assert.Empty(library.GetRecordings(category)); Assert.Equal(2, Directory.GetFiles(Path.Combine(Root, "data", "Trash")).Length);
    }
    [Fact] public void UnchangedRefreshDoesNotRewriteIndexOrDecode()
    {
        var validator = new Validator(); var library = Library(validator); var category = Guid.NewGuid();
        using (var staged = library.Stage(Source("small.m4a"))) library.Commit(category, staged);
        var path = Path.Combine(Root, "data", "library.json");
        var sentinel = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc); File.SetLastWriteTimeUtc(path, sentinel);
        library.Refresh([category]); library.Refresh([category]);
        Assert.Equal(sentinel, File.GetLastWriteTimeUtc(path)); Assert.Equal(1, validator.Calls);
        var reopened = Library(validator); Assert.Single(reopened.GetRecordings(category)); Assert.Equal(1, validator.Calls);
    }
    [Theory] [InlineData("voice.MP3")] [InlineData("voice.wav")] [InlineData("voice.m4a")]
    public void DirectCopyIsIndexedAndPersistedWithoutImport(string filename)
    {
        var library = Library(); var category = Guid.NewGuid();
        File.WriteAllText(Path.Combine(library.CategoryFolder(category), filename), "external audio");
        library.Refresh([category]);
        Assert.Equal(filename, Assert.Single(library.GetRecordings(category)).FileName);
        Assert.Single(Library().GetRecordings(category));
    }
    [Fact] public void IncompleteCopyCanBeRetriedAndManualRefreshRevalidatesSameMetadata()
    {
        var library = Library(); var category = Guid.NewGuid();
        var path = Path.Combine(library.CategoryFolder(category), "copy.wav");
        File.WriteAllText(path, "bad audio"); library.Refresh([category]);
        Assert.Empty(library.GetRecordings(category));
        File.WriteAllText(path, "good clip"); library.Refresh([category]);
        Assert.Single(library.GetRecordings(category)); Assert.Empty(library.Warnings);
        var timestamp = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, "bad audio"); File.SetLastWriteTimeUtc(path, timestamp);
        library.Refresh([category], revalidate: true);
        Assert.Empty(library.GetRecordings(category)); Assert.Single(library.Warnings);
    }
    private sealed class DurationValidator(TimeSpan duration) : IAudioValidator
    { public TimeSpan Validate(string path, CancellationToken token) => duration; }
    [Theory] [InlineData(0)] [InlineData(7201)]
    public void ExternalFilesUseImportDurationLimits(int seconds)
    {
        var library = new LibraryService(Path.Combine(Root, "data"), new DurationValidator(TimeSpan.FromSeconds(seconds)));
        var category = Guid.NewGuid(); File.WriteAllText(Path.Combine(library.CategoryFolder(category), "a.wav"), "audio");
        library.Refresh([category]); Assert.Empty(library.GetRecordings(category)); Assert.Single(library.Warnings);
    }
    private sealed class BlockingValidator : IAudioValidator
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TimeSpan Validate(string path, CancellationToken token)
        { Entered.TrySetResult(); Release.Task.WaitAsync(token).GetAwaiter().GetResult(); return TimeSpan.FromSeconds(1); }
    }
    [Fact] public async Task IndexReadsRemainAvailableDuringSlowValidation()
    {
        var validator = new BlockingValidator(); var library = new LibraryService(Path.Combine(Root, "data"), validator);
        var category = Guid.NewGuid(); File.WriteAllText(Path.Combine(library.CategoryFolder(category), "new.wav"), "synthetic");
        var refresh = Task.Run(() => library.Refresh([category]));
        try
        {
            await validator.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var snapshot = await Task.Run(() => library.GetRecordings(category)).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Empty(snapshot);
        }
        finally { validator.Release.TrySetResult(); await refresh; }
        Assert.Single(library.GetRecordings(category));
    }
    [Fact] public void ImportRejectsSymbolicLinks()
    {
        if (OperatingSystem.IsWindows()) return; // Windows standard accounts may not create symlinks.
        var original = Source("a.wav"); var link = Path.Combine(Root, "linked.wav"); File.CreateSymbolicLink(link, original);
        Assert.Throws<IOException>(() => Library().Stage(link));
    }
}
