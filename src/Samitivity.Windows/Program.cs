namespace Samitivity.Windows;

internal static class Program
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LittleNotes");
    internal static string InstanceName => @"Global\Samitivity-" + StartupManager.UserId;
    private static void DiagnosticsStart() { Samitivity.Core.Diagnostics.Start(Path.Combine(DataDirectory, "logs")); }
    [STAThread]
    private static int Main(string[] args)
    {
        Samitivity.Core.Diagnostics.Mark("process_entry");
        ApplicationConfiguration.Initialize();
        Directory.CreateDirectory(DataDirectory);
        if (args.Contains("--install-startup"))
        {
            try { File.WriteAllText(Path.Combine(DataDirectory, "startup-status.txt"), StartupManager.Install()); return 0; }
            catch { File.WriteAllText(Path.Combine(DataDirectory, "startup-status.txt"), "Startup unavailable: Windows policy blocked both Scheduled Task and Startup folder setup. Open Samitivity from Start after sign-in."); return 1; }
        }
        if (args.Contains("--uninstall-startup"))
        {
            try { StartupManager.Remove(); return 0; }
            catch { MessageBox.Show("Windows could not remove Samitivity startup. Remove the Samitivity task in Task Scheduler and try uninstall again.", "Samitivity"); return 1; }
        }
        if (args.Contains("--launch")) { StartupManager.Launch(); return 0; }
        if (args.Contains("--diagnostic-exit-23")) return 23; // Opt-in Task Scheduler probe; never used during normal startup.
        if (args.Contains("--shutdown"))
        {
            if (EventWaitHandle.TryOpenExisting(InstanceName + "-quit", out var ev)) { using (ev) ev.Set(); }
            using var waiting = new Mutex(false, InstanceName);
            try { if (!waiting.WaitOne(TimeSpan.FromSeconds(15))) return 1; waiting.ReleaseMutex(); } catch (AbandonedMutexException) { waiting.ReleaseMutex(); }
            return 0;
        }
        using var mutex = new Mutex(true, InstanceName, out var first);
        if (!first)
        {
            if (!args.Contains("--background") && EventWaitHandle.TryOpenExisting(InstanceName + "-show", out var show)) { using (show) show.Set(); }
            return 0;
        }
        DiagnosticsStart();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        try { using var context = new TrayApplication(args.Contains("--configure")); Application.Run(context); return 0; }
        catch (Exception e)
        {
            // Fixed, bounded diagnostic; no audio paths, filenames, message contents or configuration.
            Directory.CreateDirectory(Path.Combine(DataDirectory, "logs"));
            File.WriteAllText(Path.Combine(DataDirectory, "logs", "last-error.txt"), $"{DateTimeOffset.UtcNow:O} {e.GetType().Name} 0x{e.HResult:X8}");
            if (args.Contains("--configure")) MessageBox.Show("Samitivity could not start. Your recordings are preserved. Try launching it again; see logs/last-error.txt in your LittleNotes folder.", "Samitivity", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        finally { mutex.ReleaseMutex(); }
    }
}
