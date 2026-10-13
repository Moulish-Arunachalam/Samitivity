using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Samitivity.Core;

namespace Samitivity.Windows;

public sealed class WindowsAudioValidator : IAudioValidator
{
    public TimeSpan Validate(string path, CancellationToken cancellationToken)
    {
        using var reader = AudioPreparation.Open(path);
        reader.WaveFormatChanged += (_, _) => throw new InvalidDataException("Mid-stream audio format changes are unsupported.");
        var format = reader.WaveFormat;
        if (format.SampleRate is < 8000 or > 192000 || format.Channels is < 1 or > 8)
            throw new InvalidDataException("Unsupported sample rate or channel layout.");
        var buffer = new byte[64 * 1024]; long decoded = 0; int count;
        // Decode through EOF, in bounded memory. A readable header alone is not validation.
        while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested(); decoded += count;
            if (decoded > (long)format.AverageBytesPerSecond * 7200) throw new InvalidDataException("Recording is longer than two hours.");
        }
        if (decoded == 0) throw new InvalidDataException("The recording contains no decodable audio.");
        return TimeSpan.FromSeconds((double)decoded / format.AverageBytesPerSecond);
    }
}

internal sealed record PreparedAudio(byte[] Bytes, WaveFormat Format);

internal static class AudioPreparation
{
    // All decoder creation, reads and disposal occur on MTA worker threads.
    internal static MediaFoundationReader Open(string path)
    {
        var start = Diagnostics.Now;
        var reader = new MediaFoundationReader(path, new MediaFoundationReader.MediaFoundationReaderSettings { SingleReaderObject = true });
        Diagnostics.Mark("decoder_initialization", start);
        return reader;
    }
}

public sealed class WindowsAudioBackend : IAudioBackend, IPlaybackNotifications, IDisposable
{
    private const int MaxPreparedBytes = 8 * 1024 * 1024;
    private readonly AudioCache<PreparedAudio> cache = new(48 * 1024 * 1024);
    private readonly SemaphoreSlim decode = new(1, 1);
    private WasapiOut? output;
    private MMDevice? endpoint;
    private SwitchingProvider? source;
    private int invalidated;
    private readonly SemaphoreSlim initializeOutput = new(1, 1);
    public event Action<double>? StartMeasured;
    public event Action? Submitted;
    public event Action? FirstBufferRead;
    public void Invalidate() => Interlocked.Exchange(ref invalidated, 1);

    internal async Task<PreparedAudio?> PrepareAsync(Recording recording, CancellationToken token)
    {
        await decode.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var info = new FileInfo(recording.FullPath);
            var key = recording.FullPath + ":" + info.Length + ":" + info.LastWriteTimeUtc.Ticks;
            if (cache.Get(key) is { } hit) { Diagnostics.Mark("audio_cache_hit"); return hit; }
            // Long recordings retain bounded streaming instead of consuming the cache.
            if (recording.Duration.TotalSeconds > 30) return null;
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                using var reader = AudioPreparation.Open(recording.FullPath);
                reader.WaveFormatChanged += (_, _) => throw new InvalidDataException("Mid-stream audio format changes are unsupported.");
                if (reader.Length > MaxPreparedBytes) return null;
                using var memory = new MemoryStream();
                var buffer = new byte[65536]; int count;
                while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    if (memory.Length + count > MaxPreparedBytes) return null;
                    memory.Write(buffer, 0, count);
                }
                if (memory.Length == 0) throw new InvalidDataException("Empty recording.");
                var prepared = new PreparedAudio(memory.ToArray(), reader.WaveFormat);
                cache.Put(key, prepared, prepared.Bytes.Length);
                return prepared;
            }, token).ConfigureAwait(false);
        }
        finally { decode.Release(); }
    }
    public async Task WarmAsync(IEnumerable<Recording> recordings, CancellationToken token)
    {
        // Bounded startup work; foreground requests can cancel background warming.
        foreach (var note in recordings.Take(12))
        {
            try { await PrepareAsync(note, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            catch (Exception e) when (e is not OutOfMemoryException) { Diagnostics.Mark("audio_warm_failed"); }
        }
    }
    public async Task PlayAsync(Recording recording, CancellationToken cancellationToken)
    {
        var started = Diagnostics.Now;
        Diagnostics.Mark("playback_preparation_started");
        // WaitAsync releases the controller immediately even if a native decoder open is slow.
        // The decoder task retains sole ownership of its resources and observes cancellation between reads.
        var preparation = PrepareAsync(recording, cancellationToken);
        _ = preparation.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        var prepared = await preparation.WaitAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        using WaveStream reader = prepared is null
            ? AudioPreparation.Open(recording.FullPath)
            : new RawSourceWaveStream(new MemoryStream(prepared.Bytes, false), prepared.Format);
        var fade = new FadeInOutSampleProvider(reader.ToSampleProvider(), true);
        fade.BeginFadeIn(5);
        var wave = fade.ToWaveProvider();
        Diagnostics.Mark("playback_preparation_completed", started);
        if (Interlocked.Exchange(ref invalidated, 0) != 0 || source is null || !source.WaveFormat.Equals(wave.WaveFormat)) ResetOutput();
        if (output is null)
        {
            var initialization = CreateOutputAsync(wave.WaveFormat, cancellationToken);
            try
            {
                var preparedOutput = await initialization.WaitAsync(cancellationToken).ConfigureAwait(false);
                output = preparedOutput.Output; endpoint = preparedOutput.Endpoint; source = preparedOutput.Source;
            }
            catch
            {
                // A late native Init owns its output until it can safely release it; it never calls Play.
                _ = initialization.ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully) { t.Result.Output.Dispose(); t.Result.Endpoint.Dispose(); }
                    else _ = t.Exception;
                }, TaskScheduler.Default);
                throw;
            }
        }
        var ended = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Stopped(object? sender, StoppedEventArgs args) => ended.TrySetResult(args.Exception);
        output.PlaybackStopped += Stopped;
        source!.Set(wave, () => FirstBufferRead?.Invoke());
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.Play();
            Diagnostics.Mark("output_play_submitted", started);
            Submitted?.Invoke();
            StartMeasured?.Invoke(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            var error = await ended.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (error is not null) { Invalidate(); throw new IOException("Windows audio output failed.", error); }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { Invalidate(); throw; }
        finally
        {
            // Stop joins the render thread before changing the source; no arbitrary delay on switching.
            if (cancellationToken.IsCancellationRequested && output.PlaybackState == NAudio.Wave.PlaybackState.Playing)
            { fade.BeginFadeOut(5); await Task.Delay(8).ConfigureAwait(false); } // Bounded de-click ramp, not a retry/backoff.
            output.Stop();
            output.PlaybackStopped -= Stopped;
            source.Set(null);
        }
    }
    private async Task<(WasapiOut Output, MMDevice Endpoint, SwitchingProvider Source)> CreateOutputAsync(WaveFormat format, CancellationToken token)
    {
        await initializeOutput.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var started = Diagnostics.Now;
                using var enumerator = new MMDeviceEnumerator();
                var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                WasapiOut? candidate = null;
                try
                {
                    candidate = new WasapiOut(device, AudioClientShareMode.Shared, true, 30);
                    var provider = new SwitchingProvider(format); candidate.Init(provider);
                    token.ThrowIfCancellationRequested();
                    Diagnostics.Mark("audio_output_initialization", started);
                    return (candidate, device, provider);
                }
                catch { candidate?.Dispose(); device.Dispose(); throw; }
            }, token).ConfigureAwait(false);
        }
        finally { initializeOutput.Release(); }
    }
    private void ResetOutput() { output?.Dispose(); output = null; endpoint?.Dispose(); endpoint = null; source = null; }
    public void Dispose() => ResetOutput();
}

internal sealed class SwitchingProvider(WaveFormat format) : IWaveProvider
{
    private IWaveProvider? current;
    public WaveFormat WaveFormat { get; } = format;
    private Action? firstRead;
    public void Set(IWaveProvider? value, Action? first = null) { current = value; firstRead = first; }
    public int Read(byte[] buffer, int offset, int count)
    {
        var read = current?.Read(buffer, offset, count) ?? 0;
        if (read > 0) Interlocked.Exchange(ref firstRead, null)?.Invoke();
        return read;
    }
}

internal sealed class AudioDeviceMonitor : IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator enumerator = new();
    private readonly Action changed;
    public AudioDeviceMonitor(Action changed) { this.changed = changed; enumerator.RegisterEndpointNotificationCallback(this); }
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) { if (flow == DataFlow.Render && role == Role.Multimedia) changed(); }
    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { if (newState != DeviceState.Active) changed(); }
    public void OnDeviceRemoved(string deviceId) => changed();
    public void OnDeviceAdded(string deviceId) { }
    public void OnPropertyValueChanged(string deviceId, PropertyKey key) { }
    public void Dispose() { enumerator.UnregisterEndpointNotificationCallback(this); enumerator.Dispose(); }
}
