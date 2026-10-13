namespace Samitivity.Core;

public sealed record Recording(string Id, Guid CategoryId, string FileName, string FullPath, TimeSpan Duration);
public interface IRecordingSource { IReadOnlyList<Recording> GetRecordings(Guid category); }
public interface IAudioBackend
{
    // Return only after playback has stopped and all session resources have been released, including on cancellation.
    Task PlayAsync(Recording recording, CancellationToken cancellationToken);
}
public interface IPlaybackNotifications { event Action? Submitted; event Action? FirstBufferRead; }
public enum PlayerPhase { Idle, Loading, Playing, Error }
public sealed record PlayerStatus(long Generation, PlayerPhase Phase, string Message);

/// <summary>Single worker, one pending latest-wins request; cancellation never starts a concurrent audio session.</summary>
public sealed class PlaybackController : IAsyncDisposable
{
    private sealed record Request(long Generation, Guid? Category, string? PreviewId, TaskCompletionSource<bool> Done, long Received);
    private readonly object gate = new();
    private readonly SemaphoreSlim signal = new(0, 1);
    private readonly ShuffleEngine shuffle;
    private readonly IRecordingSource library;
    private readonly IAudioBackend audio;
    private readonly Task worker;
    private Request? pending;
    private CancellationTokenSource? active;
    private long generation;
    private bool disposed;
    public event Action<PlayerStatus>? StatusChanged;
    public PlaybackController(ShuffleEngine shuffle, IRecordingSource library, IAudioBackend audio)
    { this.shuffle = shuffle; this.library = library; this.audio = audio; worker = Task.Run(RunAsync); }
    public Task<bool> Play(Guid category) => Enqueue(category, null);
    // Explicit preview is deterministic and does not advance the random playlist.
    public Task<bool> Preview(Guid category, string id) => Enqueue(category, id);
    public Task<bool> Stop() => Enqueue(null, null);
    private Task<bool> Enqueue(Guid? category, string? preview)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending?.Done.TrySetResult(false);
            pending = new(++generation, category, preview, done, Diagnostics.Now);
            Diagnostics.Mark(category is null ? "stop_received" : "request_received", request: generation);
            active?.Cancel();
            if (signal.CurrentCount == 0) signal.Release();
            return done.Task;
        }
    }
    private void Report(Request r, PlayerPhase phase, string message)
    {
        lock (gate)
        { if (r.Generation != generation || disposed) return; }
        StatusChanged?.Invoke(new(r.Generation, phase, message));
    }
    private async Task RunAsync()
    {
        while (true)
        {
            await signal.WaitAsync().ConfigureAwait(false);
            Request? r; CancellationTokenSource cancellation;
            lock (gate)
            {
                if (disposed) return;
                r = pending; pending = null;
                if (r is null) continue;
                active = cancellation = new();
            }
            try
            {
                if (r.Category is { } category)
                {
                    Report(r, PlayerPhase.Loading, "Preparing recording…");
                    cancellation.Token.ThrowIfCancellationRequested();
                    var notes = library.GetRecordings(category).Where(x => File.Exists(x.FullPath)).ToList();
                    var id = r.PreviewId ?? shuffle.Select(category, notes.Select(x => x.Id));
                    var note = notes.Find(x => x.Id == id);
                    if (note is null) Report(r, PlayerPhase.Idle, "No available recordings in this category.");
                    else
                    {
                        cancellation.Token.ThrowIfCancellationRequested();
                        Diagnostics.Mark("recording_selected", r.Received, r.Generation);
                        void Submitted() { Diagnostics.Mark("request_to_output_submission", r.Received, r.Generation); Report(r, PlayerPhase.Playing, "Playback active"); }
                        void FirstBufferRead() => Diagnostics.Mark("request_to_first_pcm_read", r.Received, r.Generation);
                        var notifications = audio as IPlaybackNotifications;
                        if (notifications is not null) { notifications.Submitted += Submitted; notifications.FirstBufferRead += FirstBufferRead; }
                        try { await audio.PlayAsync(note, cancellation.Token).ConfigureAwait(false); }
                        finally { if (notifications is not null) { notifications.Submitted -= Submitted; notifications.FirstBufferRead -= FirstBufferRead; } }
                        Report(r, PlayerPhase.Idle, "Ready");
                    }
                }
                else Report(r, PlayerPhase.Idle, "Stopped · ready");
                r.Done.TrySetResult(true);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { r.Done.TrySetResult(false); }
            catch (Exception e)
            {
                Report(r, PlayerPhase.Error, $"Playback unavailable ({e.GetType().Name}). Check the recording and Windows output device, then try again.");
                r.Done.TrySetResult(false);
            }
            finally
            {
                lock (gate) { active = null; cancellation.Dispose(); }
            }
        }
    }
    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (!disposed)
            {
                disposed = true; active?.Cancel(); pending?.Done.TrySetResult(false); pending = null;
                if (signal.CurrentCount == 0) signal.Release();
            }
        }
        await worker.ConfigureAwait(false);
        // Keep semaphore alive for idempotent disposal; no unmanaged wait handle was requested.
    }
}
