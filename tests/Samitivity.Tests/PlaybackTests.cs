using System.Threading.Channels;
using Samitivity.Core;

namespace Samitivity.Tests;
public sealed class PlaybackTests : TempTest
{
    private sealed class Source(List<Recording> notes) : IRecordingSource
    { public IReadOnlyList<Recording> GetRecordings(Guid id) => notes.Where(x => x.CategoryId == id).ToArray(); }
    private sealed class Session(Recording recording)
    {
        public Recording Recording { get; } = recording;
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class Audio : IAudioBackend
    {
        public Channel<Session> Starts { get; } = Channel.CreateUnbounded<Session>();
        public int Active; public int Peak; public bool Fail;
        public async Task PlayAsync(Recording recording, CancellationToken token)
        {
            var count = Interlocked.Increment(ref Active); Peak = Math.Max(Peak, count);
            try
            {
                token.ThrowIfCancellationRequested();
                if (Fail) { Fail = false; throw new InvalidDataException("Synthetic decoder error"); }
                var session = new Session(recording); Starts.Writer.TryWrite(session);
                await session.Done.Task.WaitAsync(token);
            }
            finally { Interlocked.Decrement(ref Active); }
        }
        public async Task<Session> Next() => await Starts.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }
    private Recording Note(Guid category, string id)
    { var path = Path.Combine(Root, id + ".wav"); File.WriteAllText(path, "synthetic"); return new(id, category, Path.GetFileName(path), path, TimeSpan.FromSeconds(1)); }
    [Fact] public async Task SwitchesNeverOverlapAndInterruptedSelectionIsConsumed()
    {
        var category = Guid.NewGuid(); var audio = new Audio(); var notes = new List<Recording> { Note(category, "a"), Note(category, "b") };
        await using var player = new PlaybackController(Engine(), new Source(notes), audio);
        var firstRequest = player.Play(category); var first = await audio.Next();
        var secondRequest = player.Play(category); var second = await audio.Next();
        Assert.NotEqual(first.Recording.Id, second.Recording.Id); Assert.False(await firstRequest);
        await player.Stop(); Assert.False(await secondRequest); Assert.Equal(1, audio.Peak); Assert.Equal(0, audio.Active);
    }
    [Fact] public async Task TwentyRapidRequestsEndSilentOnEmergencyStop()
    {
        var category = Guid.NewGuid(); var audio = new Audio();
        await using var player = new PlaybackController(Engine(), new Source([Note(category, "a")]), audio);
        var started = player.Play(category); await audio.Next();
        var requests = Enumerable.Range(0, 20).Select(_ => player.Play(category)).ToArray();
        Assert.True(await player.Stop().WaitAsync(TimeSpan.FromSeconds(5)));
        await Task.WhenAll(requests.Append(started)); Assert.Equal(0, audio.Active); Assert.Equal(1, audio.Peak);
    }
    [Fact] public async Task ConcurrentRequestsAreSerialized()
    {
        var category = Guid.NewGuid(); var audio = new Audio();
        await using var player = new PlaybackController(Engine(), new Source([Note(category, "a")]), audio);
        var submitted = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => Task.Run(() => new[] { player.Play(category) })));
        await player.Stop(); await Task.WhenAll(submitted.SelectMany(x => x)); Assert.InRange(audio.Peak, 0, 1); Assert.Equal(0, audio.Active);
    }
    [Fact] public async Task LateCompletionFromOldSessionCannotStopNewSession()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var audio = new Audio();
        await using var player = new PlaybackController(Engine(), new Source([Note(a, "a"), Note(b, "b")]), audio);
        var oldRequest = player.Play(a); var old = await audio.Next();
        var newRequest = player.Play(b); var current = await audio.Next();
        old.Done.TrySetResult(); Assert.False(await oldRequest); Assert.Equal(1, audio.Active); Assert.False(newRequest.IsCompleted);
        current.Done.SetResult(); Assert.True(await newRequest); Assert.Equal(0, audio.Active);
    }
    [Fact] public async Task CompletedSessionReturnsIdleAndStopCanBeRepeated()
    {
        var category = Guid.NewGuid(); var audio = new Audio(); var statuses = new List<PlayerStatus>();
        await using var player = new PlaybackController(Engine(), new Source([Note(category, "a")]), audio);
        player.StatusChanged += statuses.Add; var request = player.Play(category); (await audio.Next()).Done.SetResult();
        Assert.True(await request); Assert.Equal(PlayerPhase.Idle, statuses[^1].Phase);
        Assert.True(await player.Stop()); Assert.True(await player.Stop());
    }
    [Fact] public async Task DecoderFailureDoesNotPoisonSubsequentRequest()
    {
        var category = Guid.NewGuid(); var audio = new Audio { Fail = true };
        await using var player = new PlaybackController(Engine(), new Source([Note(category, "a")]), audio);
        Assert.False(await player.Play(category)); var retry = player.Play(category); (await audio.Next()).Done.SetResult(); Assert.True(await retry);
    }
    [Fact] public async Task MissingFilesAndEmptyLibrariesDoNotStartAudio()
    {
        var category = Guid.NewGuid(); var note = Note(category, "a"); File.Delete(note.FullPath); var audio = new Audio();
        await using var player = new PlaybackController(Engine(), new Source([note]), audio);
        Assert.True(await player.Play(category)); Assert.True(await player.Play(Guid.NewGuid())); Assert.Equal(0, audio.Peak);
    }
    [Fact] public async Task PreviewDoesNotConsumeRandomBag()
    {
        var category = Guid.NewGuid(); var audio = new Audio(); var engine = Engine();
        await using var player = new PlaybackController(engine, new Source([Note(category, "a"), Note(category, "b")]), audio);
        var request = player.Preview(category, "a"); (await audio.Next()).Done.SetResult(); await request;
        Assert.False(File.Exists(Path.Combine(Root, "state.json")));
    }
    [Fact] public async Task DisposeCancelsPlaybackAndIsIdempotent()
    {
        var category = Guid.NewGuid(); var audio = new Audio(); var player = new PlaybackController(Engine(), new Source([Note(category, "a")]), audio);
        var request = player.Play(category); await audio.Next(); await player.DisposeAsync(); await player.DisposeAsync();
        Assert.False(await request); Assert.Equal(0, audio.Active); Assert.Throws<ObjectDisposedException>(() => { _ = player.Play(category); });
    }
}
