using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Samitivity.Core;
using Samitivity.Windows;

namespace Samitivity.Windows.Tests;

public sealed class GlassRenderingTests
{
    [Fact]
    public void WallpaperCompositionIsCachedAndRegeneratedOnResize()
    {
        using var surface = new RoseSurface { Size = new Size(920, 660) };
        var first = surface.Backdrop;
        Assert.Same(first, surface.Backdrop);
        Assert.Equal(1, surface.CompositionCount);
        // The panel preserves spatial colour variation instead of flattening into an opaque fill.
        Assert.NotEqual(first.GetPixel(90, 200), first.GetPixel(800, 200));
        surface.Size = new Size(1000, 720);
        Assert.NotSame(first, surface.Backdrop);
        Assert.Equal(2, surface.CompositionCount);
    }

    [Fact]
    public void ManagementFormRendersWithNativeSelectionAndSharpControls()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "SamitivityGlass-" + Guid.NewGuid().ToString("N"));
            try
            {
                Application.EnableVisualStyles();
                Directory.CreateDirectory(root);
                var config = AppConfig.Defaults();
                new AtomicJsonStore<AppConfig>(Path.Combine(root, "config.json"), AppConfig.Defaults, c => c.Validate()).Save(config);
                var folder = Path.Combine(root, "VoiceNotes", config.Categories[0].Id.ToString("N"));
                Directory.CreateDirectory(folder);
                for (var i = 1; i <= 2; i++)
                {
                    using var wav = new NAudio.Wave.WaveFileWriter(Path.Combine(folder, $"New Recording {i}.wav"), new NAudio.Wave.WaveFormat(44100, 16, 1));
                    var samples = new byte[44100 * 4]; samples[0] = (byte)i;
                    wav.Write(samples, 0, samples.Length);
                }
                using var app = new TrayApplication(false, root);
                try
                {
                    PumpUntil(() => app.Status == "Ready · startup is silent");
                    using var form = new ManagementForm(app);
                    form.Show(); Application.DoEvents();
                    var category = Descendants(form).OfType<GlassCategoryList>().Single();
                    Assert.Equal(42, category.GetItemRectangle(0).Height);
                    category.SelectedIndex = 1; Assert.Equal(1, category.SelectedIndex);
                    category.SelectedIndex = 0;
                    var table = Descendants(form).OfType<GlassRecordingList>().Single();
                    Assert.Equal(2, table.Items.Count);
                    table.Items[0].Selected = true; Assert.Single(table.SelectedItems.Cast<ListViewItem>());
                    table.Items[0].Selected = false;
                    var shortcut = Descendants(form).OfType<ShortcutEditor>().First();
                    Assert.Equal(config.Categories[0].Shortcut, shortcut.Value);
                    SaveCapture(form, "Samitivity-0.1.3-100.png");
                    form.Scale(new SizeF(1.5f, 1.5f)); Application.DoEvents();
                    table.RefreshBackdrop();
                    SaveCapture(form, "Samitivity-0.1.3-scaled150.png");
                    form.Close();
                }
                finally
                {
                    var shutdown = app.ShutdownAsync();
                    PumpUntil(() => shutdown.IsCompleted); shutdown.GetAwaiter().GetResult();
                }
            }
            catch (Exception e) { failure = e; }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "UI render test timed out.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static void PumpUntil(Func<bool> done)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Windows UI operation did not complete.");
            Application.DoEvents(); Thread.Sleep(10);
        }
    }
    private static void SaveCapture(Form form, string filename)
    {
        var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestResults"));
        Directory.CreateDirectory(output);
        using var capture = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(capture, new Rectangle(Point.Empty, form.Size));
        capture.Save(Path.Combine(output, filename), ImageFormat.Png);
    }
}
