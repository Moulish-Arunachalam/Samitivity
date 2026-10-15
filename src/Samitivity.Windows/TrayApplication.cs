using Microsoft.Win32;
using Samitivity.Core;

namespace Samitivity.Windows;

internal sealed class TrayApplication : ApplicationContext
{
    private readonly Control dispatch = new();
    private readonly NotifyIcon tray;
    private readonly HotkeyWindow hotkeys = new();
    private readonly EventWaitHandle show = new(false, EventResetMode.AutoReset, Program.InstanceName + "-show");
    private readonly EventWaitHandle quit = new(false, EventResetMode.AutoReset, Program.InstanceName + "-quit");
    private readonly RegisteredWaitHandle showWait;
    private readonly RegisteredWaitHandle quitWait;
    private readonly AtomicJsonStore<AppConfig> configStore;
    private readonly FileSystemWatcher watcher;
    private readonly System.Threading.Timer refreshTimer;
    private readonly SemaphoreSlim maintenance = new(1, 1);
    private AudioDeviceMonitor? devices;
    private readonly List<string> notices = [];
    private List<string> hotkeyErrors = [];
    private ManagementForm? form;
    private readonly CancellationTokenSource lifetime = new();
    private Task initialization = Task.CompletedTask;
    private readonly WindowsAudioBackend audio = new();
    private CancellationTokenSource? warming;
    private readonly long started = Diagnostics.Now;
    private bool exiting;
    private bool ready;
    private bool saving;
    private bool refreshRequested;
    private bool forceRefreshRequested;
    private int refreshRetries;
    private long lastStatusGeneration;
    public bool Ready => ready;
    public AppConfig Config { get; private set; }
    public LibraryService Library { get; }
    public PlaybackController Player { get; }
    public string Status { get; private set; } = "Loading local library…";
    public string Details => string.Join(Environment.NewLine, notices.Concat(hotkeyErrors).Concat(Library.Warnings).Distinct());
    public event Action? Updated;
    public string DataDirectory { get; }
    public TrayApplication(bool configure, string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Program.DataDirectory;
        _ = dispatch.Handle;
        configStore = new(Path.Combine(DataDirectory, "config.json"), AppConfig.Defaults, c => c.Validate());
        var migration = Path.Combine(DataDirectory, DataMigration.Receipt);
        if (Directory.Exists(Path.Combine(DataDirectory, "RecoveredData")) && File.Exists(migration)) notices.Add(File.ReadAllText(migration));
        var loaded = configStore.Load(); Config = loaded.Value;
        if (loaded.Warning is not null) notices.Add(loaded.Warning);
        if (!File.Exists(Path.Combine(DataDirectory, "config.json"))) configStore.Save(Config);
        var shuffle = new ShuffleEngine(new(Path.Combine(DataDirectory, "state.json"), () => new(), x => x.Validate()));
        if (shuffle.RecoveryWarning is not null) notices.Add(shuffle.RecoveryWarning);
        Library = new(DataDirectory, new WindowsAudioValidator());
        Player = new(shuffle, Library, audio);
        Player.StatusChanged += s => Post(() => { if (s.Generation < lastStatusGeneration) return; lastStatusGeneration = s.Generation; Status = s.Message; Updated?.Invoke(); });
        var menu = new ContextMenuStrip();
        menu.Items.Add("Configure", null, (_, _) => ShowConfiguration());
        menu.Items.Add("Stop", null, (_, _) => _ = Player.Stop());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit Samitivity", null, async (_, _) => await ShutdownAsync());
        tray = new NotifyIcon { Icon = SystemIcons.Information, Text = "Samitivity · private voice notes", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowConfiguration();
        hotkeys.Activated += id => { Diagnostics.Mark("shortcut_received"); warming?.Cancel(); if (exiting) return; if (id is null) _ = Player.Stop(); else if (ready && !saving) _ = Player.Play(id.Value); };
        hotkeys.ExplorerRestarted += () => { tray.Visible = false; tray.Visible = true; };
        hotkeyErrors = hotkeys.Apply(Config);
        Diagnostics.Mark("hotkeys_registered", started);
        showWait = ThreadPool.RegisterWaitForSingleObject(show, (_, _) => Post(ShowConfiguration), null, Timeout.Infinite, false);
        quitWait = ThreadPool.RegisterWaitForSingleObject(quit, (_, _) => Post(async () => await ShutdownAsync()), null, Timeout.Infinite, false);
        SystemEvents.PowerModeChanged += PowerChanged;
        var startup = Path.Combine(DataDirectory, "startup-status.txt");
        notices.Add(File.Exists(startup) ? File.ReadAllText(startup) : "Startup not configured. Use the installer to enable sign-in startup.");
        refreshTimer = new(_ => Post(async () => await RefreshAsync()), null, Timeout.Infinite, Timeout.Infinite);
        watcher = new FileSystemWatcher(Path.Combine(DataDirectory, "VoiceNotes")) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
        watcher.Changed += LibraryChanged; watcher.Created += LibraryChanged; watcher.Deleted += LibraryChanged; watcher.Renamed += LibraryChanged;
        watcher.Error += (_, _) => { try { refreshTimer.Change(750, Timeout.Infinite); } catch (ObjectDisposedException) { } };
        watcher.EnableRaisingEvents = true;
        Post(async () => { initialization = InitializeAsync(); await initialization; if (configure && !exiting) ShowConfiguration(); });
    }
    private void Post(Action action) { if (!exiting && !dispatch.IsDisposed) { try { dispatch.BeginInvoke(action); } catch (InvalidOperationException) { } } }
    private async Task InitializeAsync()
    {
        ready = true; Diagnostics.Mark("startup_ready", started); Updated?.Invoke(); // Registered shortcuts use the persisted index during reconciliation.
        try { devices = await Task.Run(() => new AudioDeviceMonitor(() => Post(() => { if (!exiting) { audio.Invalidate(); _ = Player.Stop(); Status = "Audio device changed · press a shortcut to use the current default."; Updated?.Invoke(); } }))); }
        catch { notices.Add("Audio device notifications unavailable. Playback errors reopen the Windows default device."); }
        await RefreshAsync();
        var bundle = Path.Combine(AppContext.BaseDirectory, "BundledVoiceNotes");
        var receipt = Path.Combine(DataDirectory, "bundle-imported-v1.txt");
        if (Directory.Exists(bundle) && !File.Exists(receipt))
        {
            try
            {
                var categories = Config.Categories.ToArray();
                await Task.Run(() =>
                {
                    foreach (var c in categories)
                    {
                        var folder = Path.Combine(bundle, c.Id.ToString("N"));
                        if (!Directory.Exists(folder)) continue;
                        foreach (var path in Directory.EnumerateFiles(folder))
                        { using var staged = Library.Stage(path, lifetime.Token); Library.Commit(c.Id, staged); }
                    }
                    File.WriteAllText(receipt, "Bundled recordings imported. No playback was started.");
                });
            }
            catch (Exception e) { notices.Add($"Bundled import incomplete ({e.GetType().Name}). Import the affected recordings through Configure."); }
        }
        if (exiting) return;
        ready = true; Status = "Ready · startup is silent"; Diagnostics.Mark("startup_library_reconciled", started); Updated?.Invoke();
        WarmAudio();
        if (hotkeyErrors.Count > 0 || notices.Any(x => (x.Contains("fallback", StringComparison.OrdinalIgnoreCase) || x.Contains("unavailable", StringComparison.OrdinalIgnoreCase))))
        { tray.BalloonTipTitle = "Samitivity needs a quick check"; tray.BalloonTipText = "Open Configure from the tray to review shortcuts or startup status."; tray.ShowBalloonTip(5000); }
    }
    private void WarmAudio()
    {
        warming?.Cancel();
        warming = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var cancellation = warming;
        var token = cancellation.Token;
        var notes = Config.Categories.SelectMany(c => Library.GetRecordings(c.Id)).ToArray();
        _ = Task.Run(async () =>
        {
            try { await audio.WarmAsync(notes, token); }
            finally { Post(() => { if (ReferenceEquals(warming, cancellation)) warming = null; cancellation.Dispose(); }); }
        });
    }
    private void LibraryChanged(object sender, FileSystemEventArgs e)
    { Post(() => refreshRetries = 0); try { refreshTimer.Change(750, Timeout.Infinite); } catch (ObjectDisposedException) { } }
    public async Task RefreshAsync(bool revalidate = false)
    {
        if (exiting) return;
        forceRefreshRequested |= revalidate;
        if (!await maintenance.WaitAsync(0)) { refreshRequested = true; return; }
        revalidate = forceRefreshRequested; forceRefreshRequested = false;
        try
        {
            var ids = Config.Categories.Select(x => x.Id).ToArray();
            await Task.Run(() => Library.Refresh(ids, lifetime.Token, revalidate));
            if (Library.Warnings.Count > 0 && refreshRetries++ < 3) refreshTimer.Change(2000, Timeout.Infinite);
            else if (Library.Warnings.Count == 0) refreshRetries = 0;
            Status = Library.Warnings.Count == 0 ? "Library up to date" : "Some files could not be read · finish copying, then Refresh";
            Updated?.Invoke();
        }
        catch (Exception e) { Status = $"Library refresh failed ({e.GetType().Name}). Use Refresh to retry."; Updated?.Invoke(); }
        finally { ReleaseMaintenance(); }
    }
    public async Task SaveAsync(AppConfig config, IReadOnlyList<(Guid Category, StagedImport Import)> imports, IReadOnlyList<(Guid Category, string Id)> deletions)
    {
        var started = Diagnostics.Now;
        config.Validate(); saving = true;
        warming?.Cancel();
        var mediaChanged = imports.Count > 0 || deletions.Count > 0;
        if (mediaChanged) { await Player.Stop(); await maintenance.WaitAsync(); }
        try
        {
            // Independent atomic operations; interrupted Save is recoverable, not an all-or-nothing multi-file transaction.
            await Task.Run(() =>
            {
                configStore.Save(config);
                foreach (var (category, import) in imports) Library.Commit(category, import);
                foreach (var (category, id) in deletions) Library.Delete(category, id);
            });
        }
        finally
        {
            Config = configStore.Load().Value; hotkeyErrors = hotkeys.Apply(Config);
            if (mediaChanged) ReleaseMaintenance(); saving = false; Updated?.Invoke();
        }
        Diagnostics.Mark("configuration_save", started);
        await RefreshAsync();
        if (mediaChanged) WarmAudio();
    }
    private void ReleaseMaintenance()
    {
        maintenance.Release();
        if (refreshRequested && !exiting)
        {
            refreshRequested = false;
            refreshTimer.Change(750, Timeout.Infinite);
        }
    }
    private void PowerChanged(object sender, PowerModeChangedEventArgs e) => Post(() =>
    {
        if (exiting) return;
        audio.Invalidate();
        _ = Player.Stop();
        Status = e.Mode == PowerModes.Resume ? "Resumed · ready for the next shortcut" : "Stopped for power transition";
        Updated?.Invoke();
    });
    private void ShowConfiguration()
    {
        if (exiting) return;
        if (form is null || form.IsDisposed) form = new(this);
        _ = RefreshAsync();
        form.Show(); form.WindowState = FormWindowState.Normal; form.Activate();
    }
    internal async Task ShutdownAsync()
    {
        if (exiting) return;
        if (form is { IsDisposed: false } && !form.CanDiscard()) return;
        exiting = true; lifetime.Cancel(); watcher.EnableRaisingEvents = false;
        tray.Visible = false; form?.Hide();
        await Player.DisposeAsync();
        audio.Dispose();
        await initialization;
        await maintenance.WaitAsync(); maintenance.Release();
        ExitThread();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            exiting = true; SystemEvents.PowerModeChanged -= PowerChanged;
            showWait.Unregister(null); quitWait.Unregister(null); show.Dispose(); quit.Dispose();
            watcher.Dispose(); refreshTimer.Dispose(); lifetime.Dispose(); devices?.Dispose(); hotkeys.Dispose(); tray.ContextMenuStrip?.Dispose(); tray.Dispose();
            form?.Dispose(); dispatch.Dispose();
        }
        base.Dispose(disposing);
    }
}
