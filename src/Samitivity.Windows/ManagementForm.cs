using System.Diagnostics;
using Samitivity.Core;

namespace Samitivity.Windows;
internal sealed class ManagementForm : Form
{
    private readonly TrayApplication app;
    private AppConfig draft;
    private readonly List<(Guid Category, StagedImport Import)> imports = [];
    private readonly List<(Guid Category, string Id)> deletions = [];
    private readonly ListBox categories = new() { Dock = DockStyle.Fill, DisplayMember = "Name" };
    private readonly TextBox name = new() { Width = 320, MaxLength = 60 };
    private readonly ShortcutEditor shortcut = new();
    private readonly ShortcutEditor stopShortcut = new();
    private readonly ListView recordings = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, Dock = DockStyle.Fill, HideSelection = false };
    private readonly TextBox status = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly Label pendingLabel = new() { AutoSize = true, Text = "All changes saved" };
    private readonly Button save = new() { Text = "Save", AutoSize = true };
    private readonly List<Control> mutationControls = [];
    private bool dirty;
    private bool loading;
    private bool busy;
    private Category? Selected => categories.SelectedItem as Category;
    public ManagementForm(TrayApplication app)
    {
        this.app = app; draft = app.Config.Copy();
        Text = "Samitivity · Configure"; Icon = SystemIcons.Information; MinimumSize = new(880, 690); Size = new(1000, 760); StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 2, RowCount = 6 };
        layout.ColumnStyles.Add(new(SizeType.Absolute, 190)); layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 36)); layout.RowStyles.Add(new(SizeType.Absolute, 112)); layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 74)); layout.RowStyles.Add(new(SizeType.Absolute, 110)); layout.RowStyles.Add(new(SizeType.Absolute, 46));
        Controls.Add(layout);
        var heading = new Label { Text = "Your voice notes · private and offline", Font = new(Font, FontStyle.Bold), AutoSize = true };
        layout.Controls.Add(heading, 0, 0); layout.SetColumnSpan(heading, 2);
        layout.Controls.Add(categories, 0, 1); layout.SetRowSpan(categories, 2);
        var edit = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        edit.Controls.Add(name); edit.Controls.Add(shortcut);
        edit.Controls.Add(new Label { AutoSize = true, Text = "Win combinations may be reserved; Ctrl+Alt may act as AltGr on some layouts." });
        layout.Controls.Add(edit, 1, 1);
        recordings.Columns.Add("Recording", 450); recordings.Columns.Add("Duration", 90); recordings.Columns.Add("Change", 95);
        layout.Controls.Add(recordings, 1, 2);
        var categoryActions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        categoryActions.Controls.Add(Mutate("New category", AddCategory)); categoryActions.Controls.Add(Mutate("Remove category", RemoveCategory)); layout.Controls.Add(categoryActions, 0, 3);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        actions.Controls.Add(Mutate("Import…", async () => await ImportAsync()));
        actions.Controls.Add(Mutate("Preview", () => { if (recordings.SelectedItems.Count > 0 && recordings.SelectedItems[0].Tag is Recording r) _ = app.Player.Preview(r.CategoryId, r.Id); }));
        actions.Controls.Add(Button("Stop", () => _ = app.Player.Stop()));
        actions.Controls.Add(Mutate("Delete", DeleteRecording));
        actions.Controls.Add(Button("Open folder", () => { if (Selected is { } c) Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = false, ArgumentList = { app.Library.CategoryFolder(c.Id) } }); }));
        actions.Controls.Add(Button("Refresh", async () => { await app.RefreshAsync(); RenderRecordings(); }));
        layout.Controls.Add(actions, 1, 3);
        var statusArea = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        statusArea.RowStyles.Add(new(SizeType.Absolute, 36)); statusArea.RowStyles.Add(new(SizeType.Percent, 100));
        var stop = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        stop.Controls.Add(new Label { AutoSize = true, Text = "Emergency Stop" }); stop.Controls.Add(stopShortcut);
        statusArea.Controls.Add(stop, 0, 0); statusArea.Controls.Add(status, 0, 1);
        layout.Controls.Add(statusArea, 0, 4); layout.SetColumnSpan(statusArea, 2);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        save.Click += async (_, _) => await SaveAsync(); footer.Controls.Add(save);
        footer.Controls.Add(Button("Close", Close)); footer.Controls.Add(pendingLabel); layout.Controls.Add(footer, 0, 5); layout.SetColumnSpan(footer, 2);
        mutationControls.AddRange([categories, name, shortcut, stopShortcut, save]);
        categories.SelectedIndexChanged += (_, _) => { if (!loading) LoadCategory(); };
        name.TextChanged += (_, _) => EditCategory(); shortcut.Changed += EditCategory;
        stopShortcut.Changed += () => { if (!loading) { draft = draft with { StopShortcut = stopShortcut.Value }; MarkDirty(); } };
        FormClosing += (_, e) => { if (busy || !CanDiscard()) e.Cancel = true; };
        app.Updated += UpdateStatus;
        LoadDraft(); UpdateStatus();
    }
    private static Button Button(string text, Action action)
    {
        var b = new Button { Text = text, AutoSize = true };
        b.Click += (_, _) => { try { action(); } catch (Exception e) { MessageBox.Show(e.Message, "Samitivity", MessageBoxButtons.OK, MessageBoxIcon.Warning); } };
        return b;
    }
    private Button Mutate(string text, Action action) { var b = Button(text, action); mutationControls.Add(b); return b; }
    private void MarkDirty() { dirty = true; pendingLabel.Text = "Unsaved changes"; }
    private void LoadDraft(Guid? selected = null)
    {
        loading = true; categories.Items.Clear(); foreach (var c in draft.Categories) categories.Items.Add(c);
        stopShortcut.Value = draft.StopShortcut;
        categories.SelectedIndex = selected is { } id ? draft.Categories.FindIndex(x => x.Id == id) : (categories.Items.Count > 0 ? 0 : -1);
        loading = false; LoadCategory();
    }
    private void LoadCategory()
    {
        loading = true;
        name.Enabled = shortcut.Enabled = Selected is not null;
        name.Text = Selected?.Name ?? ""; if (Selected is { } c) shortcut.Value = c.Shortcut;
        loading = false; RenderRecordings();
    }
    private void EditCategory()
    {
        if (loading || Selected is not { } c) return;
        var updated = c with { Name = name.Text, Shortcut = shortcut.Value };
        var index = draft.Categories.FindIndex(x => x.Id == c.Id); draft.Categories[index] = updated;
        loading = true; categories.Items[categories.SelectedIndex] = updated; loading = false; MarkDirty();
    }
    private void RenderRecordings()
    {
        var selectedTag = recordings.SelectedItems.Count > 0 ? recordings.SelectedItems[0].Tag : null;
        recordings.Items.Clear(); if (Selected is not { } c) return;
        foreach (var note in app.Library.GetRecordings(c.Id))
        {
            var removed = deletions.Contains((c.Id, note.Id));
            var row = new ListViewItem([note.FileName, note.Duration.ToString(@"hh\:mm\:ss"), removed ? "Delete on Save" : ""]) { Tag = note };
            recordings.Items.Add(row);
        }
        foreach (var pending in imports.Where(x => x.Category == c.Id))
            recordings.Items.Add(new ListViewItem([pending.Import.OriginalName, pending.Import.Duration.ToString(@"hh\:mm\:ss"), "Import on Save"]) { Tag = pending.Import });
        foreach (ListViewItem row in recordings.Items)
            if (Equals(row.Tag, selectedTag)) row.Selected = true;
    }
    private void AddCategory()
    {
        // Find a valid unused suggestion, still subject to OS conflict checks at Save.
        var used = draft.Categories.Select(x => x.Shortcut).Append(draft.StopShortcut).ToHashSet();
        var proposed = Enumerable.Range(65, 26).Select(k => new Shortcut(Modifiers.Control | Modifiers.Alt | Modifiers.Shift, k)).FirstOrDefault(x => !used.Contains(x));
        if (proposed is null || draft.Categories.Count >= 32) throw new InvalidOperationException("Maximum categories reached.");
        var c = new Category(Guid.NewGuid(), "New category " + (draft.Categories.Count + 1), proposed);
        draft.Categories.Add(c); MarkDirty(); LoadDraft(c.Id); name.Focus(); name.SelectAll();
    }
    private void RemoveCategory()
    {
        if (Selected is not { } c || MessageBox.Show("Remove this category on Save? Its recordings will remain in its local folder for recovery.", "Remove category", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        draft.Categories.RemoveAll(x => x.Id == c.Id);
        foreach (var pending in imports.Where(x => x.Category == c.Id).ToArray()) { pending.Import.Dispose(); imports.Remove(pending); }
        deletions.RemoveAll(x => x.Category == c.Id); MarkDirty(); LoadDraft();
    }
    private void DeleteRecording()
    {
        if (recordings.SelectedItems.Count == 0 || Selected is not { } c) return;
        if (MessageBox.Show("Delete this recording on Save? Saved recordings move to the local Trash folder for recovery.", "Delete recording", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        var tag = recordings.SelectedItems[0].Tag;
        if (tag is Recording r && !deletions.Contains((c.Id, r.Id))) deletions.Add((c.Id, r.Id));
        if (tag is StagedImport staged) { staged.Dispose(); imports.RemoveAll(x => ReferenceEquals(x.Import, staged)); }
        MarkDirty(); RenderRecordings();
    }
    private void SetBusy(bool value)
    {
        busy = value;
        foreach (var control in mutationControls) control.Enabled = !value && app.Ready;
        name.Enabled = shortcut.Enabled = !value && app.Ready && Selected is not null;
    }
    private async Task ImportAsync()
    {
        if (Selected is not { } c) return;
        using var picker = new OpenFileDialog { Filter = "Voice recordings|*.m4a;*.mp3;*.wav", Multiselect = true, CheckFileExists = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        SetBusy(true); var errors = new List<string>();
        try
        {
            foreach (var file in picker.FileNames)
            {
                pendingLabel.Text = "Validating " + Path.GetFileName(file);
                try { var staged = await Task.Run(() => app.Library.Stage(file)); imports.Add((c.Id, staged)); MarkDirty(); }
                catch (Exception e) { errors.Add($"{Path.GetFileName(file)}: unsupported, damaged or inaccessible ({e.GetType().Name})."); }
            }
            RenderRecordings();
            if (errors.Count > 0) MessageBox.Show(string.Join(Environment.NewLine, errors), "Some recordings could not be imported", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { SetBusy(false); pendingLabel.Text = dirty ? "Unsaved changes" : "All changes saved"; }
    }
    private async Task SaveAsync()
    {
        if (!dirty) return;
        try { draft.Validate(); } catch (Exception e) { MessageBox.Show(e.Message, "Check settings"); return; }
        if (MessageBox.Show("Save settings and apply pending imports and deletions? Playback will stop.", "Save changes", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        SetBusy(true);
        try
        {
            await app.SaveAsync(draft.Copy(), imports, deletions);
            foreach (var x in imports) x.Import.Dispose(); imports.Clear(); deletions.Clear();
            draft = app.Config.Copy(); dirty = false; pendingLabel.Text = "Saved"; LoadDraft();
        }
        catch (Exception e)
        {
            // Successful individual imports are committed; keep only retryable staging entries.
            imports.RemoveAll(x => !File.Exists(x.Import.Path));
            MessageBox.Show($"Save was only partially applied ({e.GetType().Name}). Check the list and retry. Original files remain safe.", "Save incomplete", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            MarkDirty(); RenderRecordings();
        }
        finally { SetBusy(false); UpdateStatus(); }
    }
    public bool CanDiscard()
    {
        if (busy) return false;
        if (!dirty) return true;
        return MessageBox.Show("Discard unsaved changes and close? Choose No to return and Save.", "Unsaved changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
    }
    private void UpdateStatus() { if (!IsDisposed) { status.Text = app.Status + Environment.NewLine + app.Details; SetBusy(busy); if (!busy) RenderRecordings(); } }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { app.Updated -= UpdateStatus; foreach (var x in imports) x.Import.Dispose(); }
        base.Dispose(disposing);
    }
}
