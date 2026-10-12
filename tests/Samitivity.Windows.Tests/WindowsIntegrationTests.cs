using System.Runtime.InteropServices;
using System.Xml.Linq;
using NAudio.Wave;
using Samitivity.Core;
using Samitivity.Windows;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace Samitivity.Windows.Tests;

public sealed class WindowsIntegrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "SamitivityWindowsTests-" + Guid.NewGuid().ToString("N"));
    public WindowsIntegrationTests() => Directory.CreateDirectory(root);
    private string SyntheticWave(int rate = 44100, int channels = 1)
    {
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
        var path = Path.Combine(root, "corrupt." + extension); File.WriteAllText(path, "not an audio stream");
        Assert.ThrowsAny<Exception>(() => new WindowsAudioValidator().Validate(path, default));
        var library = new LibraryService(Path.Combine(root, "data"), new WindowsAudioValidator());
        Assert.ThrowsAny<Exception>(() => library.Stage(path)); Assert.Empty(Directory.GetFiles(Path.Combine(root, "data", ".staging")));
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
    public void Dispose() => Directory.Delete(root, true);
}
