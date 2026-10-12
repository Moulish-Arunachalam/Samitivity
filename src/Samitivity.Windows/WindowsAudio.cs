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
        using var reader = new MediaFoundationReader(path);
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

public sealed class WindowsAudioBackend : IAudioBackend
{
    public event Action<double>? StartMeasured;
    public async Task PlayAsync(Recording recording, CancellationToken cancellationToken)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        cancellationToken.ThrowIfCancellationRequested();
        using var reader = new MediaFoundationReader(recording.FullPath);
        reader.WaveFormatChanged += (_, _) => throw new InvalidDataException("Mid-stream audio format changes are unsupported.");
        var prefix = new PrefetchedWaveProvider(reader, cancellationToken);
        var fade = new FadeInOutSampleProvider(prefix.ToSampleProvider(), true);
        fade.BeginFadeIn(5);
        using var enumerator = new MMDeviceEnumerator();
        using var endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        using var output = new WasapiOut(endpoint, AudioClientShareMode.Shared, true, 50);
        var ended = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, e) => ended.TrySetResult(e.Exception);
        output.Init(fade.ToWaveProvider());
        cancellationToken.ThrowIfCancellationRequested();
        output.Play();
        StartMeasured?.Invoke(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        try
        {
            var error = await ended.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (error is not null) throw new IOException("Windows audio output failed.", error);
        }
        finally
        {
            if (output.PlaybackState == NAudio.Wave.PlaybackState.Playing)
            {
                // Best effort de-click. Buffered device/Bluetooth latency can outlast this short ramp.
                fade.BeginFadeOut(8);
                await Task.Delay(12).ConfigureAwait(false);
            }
            // Stop joins NAudio's render thread. Resources are never reused by a newer request.
            output.Stop();
        }
    }
}

internal sealed class PrefetchedWaveProvider : IWaveProvider
{
    private readonly IWaveProvider source;
    private readonly byte[] prefix;
    private int position;
    private readonly int length;
    public WaveFormat WaveFormat => source.WaveFormat;
    public PrefetchedWaveProvider(IWaveProvider source, CancellationToken token)
    {
        this.source = source;
        var bytes = Math.Min(128 * 1024, source.WaveFormat.AverageBytesPerSecond / 10);
        prefix = new byte[Math.Max(source.WaveFormat.BlockAlign, bytes / source.WaveFormat.BlockAlign * source.WaveFormat.BlockAlign)];
        while (length < prefix.Length)
        { token.ThrowIfCancellationRequested(); var n = source.Read(prefix, length, prefix.Length - length); if (n == 0) break; length += n; }
    }
    public int Read(byte[] buffer, int offset, int count)
    {
        var take = Math.Min(count, length - position);
        Array.Copy(prefix, position, buffer, offset, take); position += take;
        return take + (take < count ? source.Read(buffer, offset + take, count - take) : 0);
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
