using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;

namespace Samitivity.Windows;

internal static class StartupManager
{
    public static string UserId => WindowsIdentity.GetCurrent().User!.Value;
    private static string TaskName => "Samitivity-" + UserId;
    private static string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Samitivity.lnk");
    public static string Install()
    {
        try
        {
            WithScheduler(folder => { folder.RegisterTask(TaskName, BuildTaskXml(Environment.ProcessPath!, UserId), 6, null, null, 3, null); });
            if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
            return "Scheduled Task: sign-in startup; restart-on-failure requested (requires validation on this PC).";
        }
        catch
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            try
            {
                dynamic link = shell.CreateShortcut(ShortcutPath);
                try { link.TargetPath = Environment.ProcessPath; link.Arguments = "--background"; link.WorkingDirectory = AppContext.BaseDirectory; link.Save(); }
                finally { Marshal.FinalReleaseComObject(link); }
            }
            finally { Marshal.FinalReleaseComObject(shell); }
            return "Startup folder fallback: starts at sign-in; automatic crash restart unavailable.";
        }
    }
    public static void Launch()
    {
        try { WithScheduler(folder => { dynamic task = folder.GetTask(TaskName); try { task.Run(null); } finally { Marshal.FinalReleaseComObject(task); } }); }
        catch { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--background") { UseShellExecute = false }); }
    }
    public static void Remove()
    {
        if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
        try { WithScheduler(folder => folder.DeleteTask(TaskName, 0)); }
        catch (COMException e) when (e.HResult == unchecked((int)0x80070002)) { } // Already absent.

    }
    private static void WithScheduler(Action<dynamic> action)
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        try { service.Connect(); dynamic folder = service.GetFolder("\\"); try { action(folder); } finally { Marshal.FinalReleaseComObject(folder); } }
        finally { Marshal.FinalReleaseComObject(service); }
    }
    internal static string BuildTaskXml(string executable, string sid)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        XElement E(string n, object? value) => new(ns + n, value);
        return new XDocument(new XElement(ns + "Task", new XAttribute("version", "1.2"),
            E("RegistrationInfo", E("Description", "Samitivity private voice notes. Starts silently at sign-in; uninstall removes this task.")),
            E("Triggers", E("LogonTrigger", new object[] { E("Enabled", "true"), E("UserId", sid) })),
            E("Principals", new XElement(ns + "Principal", new XAttribute("id", "Author"), E("UserId", sid), E("LogonType", "InteractiveToken"), E("RunLevel", "LeastPrivilege"))),
            E("Settings", new object[] { E("MultipleInstancesPolicy", "IgnoreNew"), E("DisallowStartIfOnBatteries", "false"), E("StopIfGoingOnBatteries", "false"),
                E("AllowHardTerminate", "true"), E("StartWhenAvailable", "true"), E("RunOnlyIfNetworkAvailable", "false"),
                E("IdleSettings", new object[] { E("StopOnIdleEnd", "false"), E("RestartOnIdle", "false") }),
                E("AllowStartOnDemand", "true"), E("Enabled", "true"), E("Hidden", "false"), E("RunOnlyIfIdle", "false"),
                E("WakeToRun", "false"), E("ExecutionTimeLimit", "PT0S"), E("Priority", "7"),
                E("RestartOnFailure", new object[] { E("Interval", "PT1M"), E("Count", "3") }) }),
            new XElement(ns + "Actions", new XAttribute("Context", "Author"), E("Exec", new object[] { E("Command", executable), E("Arguments", "--background"), E("WorkingDirectory", Path.GetDirectoryName(executable)) })))).ToString();
    }
}
