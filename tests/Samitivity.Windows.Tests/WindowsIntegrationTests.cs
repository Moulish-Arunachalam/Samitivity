using System.Runtime.InteropServices;
using System.Xml.Linq;
using NAudio.Wave;
using Samitivity.Core;
using Samitivity.Windows;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace Samitivity.Windows.Tests;

public sealed class WindowsIntegrationTests(Xunit.Abstractions.ITestOutputHelper log) : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "SamitivityWindowsTests-" + Guid.NewGuid().ToString("N"));

    private string SyntheticWave(int rate = 44100, int channels = 1)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, Guid.NewGuid() + ".wav");
        using var writer = new WaveFileWriter(path, new WaveFormat(rate, 16, channels));
        for (var n = 0; n < rate / 4; n++)
            for (var c = 0; c < channels; c++) writer.WriteSample((float)(0.03 * Math.Sin(2 * Math.PI * 440 * n / rate)));
        return path;
    }
    [Theory] [InlineData(22050, 1)] [InlineData(44100, 2)] [InlineData(48000, 1)]
    public void NativeDecoderReadsSyntheticWav(int rate, int channels)
    {
        var duration = new WindowsAudioValidator().Validate(SyntheticWave(rate, channels), default);
        Assert.InRange(duration.TotalSeconds, 0.24, 0.3);
    }
    [Theory] [InlineData("m4a")] [InlineData("mp3")]
    public void NativeDecoderReadsSyntheticEncodedAudio(string extension)
    {
        var path = Path.Combine(root, "synthetic." + extension);
        using (var reader = new WaveFileReader(SyntheticWave()))
        {
            if (extension == "m4a") MediaFoundationEncoder.EncodeToAac(reader, path);
            else MediaFoundationEncoder.EncodeToMp3(reader, path);
        }
        var duration = new WindowsAudioValidator().Validate(path, default); Assert.True(duration > TimeSpan.Zero);
        var library = new LibraryService(Path.Combine(root, "data"), new WindowsAudioValidator());
        using var staged = library.Stage(path); Assert.True(library.Commit(Guid.NewGuid(), staged));
    }
    [Theory] [InlineData("m4a")] [InlineData("mp3")] [InlineData("wav")]
    public void CorruptAudioIsRejectedByNativeDecoder(string extension)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "corrupt." + extension); File.WriteAllText(path, "not an audio stream");
        Assert.ThrowsAny<Exception>(() => new WindowsAudioValidator().Validate(path, default));
        var library = new LibraryService(Path.Combine(root, "data"), new WindowsAudioValidator());
        Assert.ThrowsAny<Exception>(() => library.Stage(path)); Assert.Empty(Directory.GetFiles(Path.Combine(root, "data", ".staging")));
    }
    [Fact] public async Task DirectCopyIsDecodedIndexedAndReadyForPlayback()
    {
        var path = SyntheticWave(); var category = Guid.NewGuid();
        var library = new LibraryService(Path.Combine(root, "external"), new WindowsAudioValidator());
        File.Copy(path, Path.Combine(library.CategoryFolder(category), "copied.WAV"));
        await Task.Run(() => library.Refresh([category], revalidate: true));
        var note = Assert.Single(library.GetRecordings(category));
        using var backend = new WindowsAudioBackend();
        Assert.NotNull(await backend.PrepareAsync(note, default));
    }
    [Fact] public void RoseSurfaceLoadsEmbeddedAssetAndPaints()
    {
        using var surface = new RoseSurface { Size = new System.Drawing.Size(860, 640) };
        using var bitmap = new System.Drawing.Bitmap(860, 640);
        surface.DrawToBitmap(bitmap, surface.ClientRectangle);
        Assert.NotEqual(bitmap.GetPixel(10, 320), bitmap.GetPixel(430, 320));
    }
    [Fact] public void StartupTaskIsInteractiveUnelevatedSilentAndBounded()
    {
        var xml = XDocument.Parse(StartupManager.BuildTaskXml(@"C:\Users\A&B\Samitivity.exe", "S-1-5-21-123"));
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        string Value(string key) => xml.Descendants(ns + key).Single().Value;
        Assert.Equal("InteractiveToken", Value("LogonType")); Assert.Equal("LeastPrivilege", Value("RunLevel"));
        Assert.Equal("--background", Value("Arguments")); Assert.Equal("PT0S", Value("ExecutionTimeLimit"));
        Assert.Equal("3", Value("Count")); Assert.Equal("false", Value("RunOnlyIfNetworkAvailable"));
        Assert.Equal(@"C:\Users\A&B\Samitivity.exe", Value("Command"));
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [Fact] public void EmergencyStopRegistrationConflictIsVisibleAndReassignmentWorks()
    {
        // Run registration and cleanup on the same thread. This tests registration, not physical keyboard delivery.
        const uint flags = 0x4000 | 1 | 2 | 4;
        Assert.True(RegisterHotKey(IntPtr.Zero, 29001, flags, 0x79));
        try
        {
            using var window = new HotkeyWindow();
            var config = new AppConfig { StopShortcut = new(Modifiers.Control | Modifiers.Alt | Modifiers.Shift, 0x79) };
            Assert.Single(window.Apply(config));
            config = config with { StopShortcut = new(Modifiers.Control | Modifiers.Alt | Modifiers.Shift, 0x78) };
            Assert.Empty(window.Apply(config));
        }
        finally { UnregisterHotKey(IntPtr.Zero, 29001); }
    }
    [Theory] [InlineData("m4a")] [InlineData("mp3")] [InlineData("wav")]
    public async Task PreparationReusesPcmAndInvalidatesChangedFile(string extension)
    {
        var wav = SyntheticWave(); var path = wav;
        if (extension != "wav")
        {
            path = Path.Combine(root, "prepared." + extension);
            using var reader = new WaveFileReader(wav);
            if (extension == "m4a") MediaFoundationEncoder.EncodeToAac(reader, path);
            else MediaFoundationEncoder.EncodeToMp3(reader, path);
        }
        using var backend = new WindowsAudioBackend();
        var note = new Recording("synthetic", Guid.NewGuid(), "synthetic", path, TimeSpan.FromSeconds(0.25));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var first = await backend.PrepareAsync(note, default); var cold = clock.Elapsed.TotalMilliseconds;
        clock.Restart(); var second = await backend.PrepareAsync(note, default); var warm = clock.Elapsed.TotalMilliseconds;
        Assert.NotNull(first); Assert.Same(first, second); Assert.NotEmpty(first.Bytes);
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(2));
        Assert.NotSame(first, await backend.PrepareAsync(note, default));
        log.WriteLine($"Synthetic {extension} preparation only: cold={cold:F2}ms warm={warm:F2}ms. No audio device or audible timing measured.");
    }
    [Theory]
    [InlineData("m4a", 44100, 1)] [InlineData("m4a", 48000, 2)]
    [InlineData("mp3", 44100, 2)] [InlineData("wav", 22050, 1)]
    public async Task ProductionImportSurvivesCommitAndReopenWithoutChangingOriginalBytes(string extension, int rate, int channels)
    {
        var wav = SyntheticWave(rate, channels);
        var path = Path.Combine(root, "synthetic recording #1." + extension);
        if (extension == "wav") File.Copy(wav, path);
        else
        {
            using var reader = new WaveFileReader(wav);
            if (extension == "m4a") MediaFoundationEncoder.EncodeToAac(reader, path);
            else MediaFoundationEncoder.EncodeToMp3(reader, path);
        }
        var original = File.ReadAllBytes(path); var category = Guid.NewGuid(); var data = Path.Combine(root, "managed");
        var library = new LibraryService(data, new WindowsAudioValidator());
        // Same Task.Run -> Stage -> WindowsAudioValidator path as ManagementForm, then Save's Commit.
        using var staged = await Task.Run(() => library.Stage(path));
        Assert.True(library.Commit(category, staged));
        var reopened = new LibraryService(data, new WindowsAudioValidator());
        var recording = Assert.Single(reopened.GetRecordings(category));
        Assert.Equal(original, File.ReadAllBytes(recording.FullPath)); Assert.Equal(original, File.ReadAllBytes(path));
        using var backend = new WindowsAudioBackend();
        Assert.NotNull(await backend.PrepareAsync(recording, default));
    }
    [Fact] public void SmallImportAndMetadataSaveReportIndependentTimings()
    {
        var path = SyntheticWave(); var library = new LibraryService(Path.Combine(root, "data"), new WindowsAudioValidator());
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var staged = library.Stage(path); var import = clock.Elapsed.TotalMilliseconds;
        clock.Restart(); Assert.True(library.Commit(Guid.NewGuid(), staged)); var commit = clock.Elapsed.TotalMilliseconds;
        var store = new AtomicJsonStore<AppConfig>(Path.Combine(root, "config.json"), AppConfig.Defaults, c => c.Validate());
        store.Save(AppConfig.Defaults()); clock.Restart(); store.Save(AppConfig.Defaults());
        log.WriteLine($"Synthetic WAV import={import:F2}ms commit={commit:F2}ms metadata persistence={clock.Elapsed.TotalMilliseconds:F2}ms. CI disk only; no UI or physical audio assertion.");
    }
    [Fact] public async Task CancelledPreparationDoesNotOpenMissingFile()
    {
        using var backend = new WindowsAudioBackend(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var note = new Recording("missing", Guid.NewGuid(), "missing", Path.Combine(root, "missing.wav"), TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => backend.PrepareAsync(note, cancellation.Token));
    }
    [Fact] public void SwitchingProviderReportsFirstDataOnceAndDropsPreviousSource()
    {
        var format = new WaveFormat(44100, 16, 1); var provider = new SwitchingProvider(format); var reads = 0;
        using var first = new RawSourceWaveStream(new MemoryStream(new byte[128]), format);
        provider.Set(first, () => reads++); var buffer = new byte[32];
        Assert.Equal(32, provider.Read(buffer, 0, 32)); provider.Read(buffer, 0, 32); Assert.Equal(1, reads);
        provider.Set(null); Assert.Equal(0, provider.Read(buffer, 0, 32));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
