using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using TabForge.Audio;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>
/// A song passed on the command line (Explorer double-click) opens exactly as App.OnStartup opens it: a fresh main window is shown and the
/// background open starts at once, before the window has settled. A song handed over by a second launch opens in the running window.
/// Covers .gp, .gp5 and .tforge; each opened song must show its tracks, bars and timeline. Open failures are captured, never shown.
/// </summary>
public static partial class SelfTest
{
    private static void TestStartupFileOpen() => RunInWindowFixture((a, context) =>
    {
        var root = FindRepositoryRoot();
        var gp5 = root is null ? null : Path.Combine(root, "samples", "TabForge Demo - Ashen Meridian.gp5");
        var gp = root is null ? null : Path.Combine(root, "samples", "TabForge Demo - Ashen Meridian.gp");
        Check("startup open: the sample songs are present", gp5 is not null && File.Exists(gp5) && gp is not null && File.Exists(gp));
        if (gp5 is null || gp is null || !File.Exists(gp5) || !File.Exists(gp)) return;
        var folder = Path.Combine(Path.GetTempPath(), "tf-startup-open-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var tforge = Path.Combine(folder, "startup.tforge");
        ProjectService.Save(tforge, GuitarProImporter.Import(gp5));
        var errors = new List<string>();
        var previousMessage = DialogHost.MessageCapture;
        DialogHost.MessageCapture = (caption, text) => errors.Add(caption + ": " + text);
        try
        {
            foreach (var path in new[] { gp, gp5, tforge })
            {
                var name = Path.GetExtension(path);
                // Startup: exactly App.OnStartup's order (construct, Show, open at once in the background).
                errors.Clear();
                var w = new MainWindow(AudioEngineClient.Instance, new Shell.AppOptions());
                w.WindowState = WindowState.Normal; w.Width = 1000; w.Height = 700;
                ShowTestWindow(w);
                w.OpenStartupFile(path);
                StartupSongShows(w, $"startup {name}", errors);

                // Hand-over: a second launch passes another song to this running window.
                errors.Clear();
                w.OpenFromAnotherLaunch(path == gp ? gp5 : gp);
                StartupSongShows(w, $"hand-over into a window opened with {name}", errors, expectedTabs: 2);
                foreach (var session in w.OpenDocuments.ToList()) session.MarkClean();
                w.Close();
            }
            // Hand-over after the first window closed while another stays open (its tabs moved there): the song reaches the open window.
            errors.Clear();
            var first = new MainWindow(AudioEngineClient.Instance, new Shell.AppOptions());
            ShowTestWindow(first);
            var second = new MainWindow(AudioEngineClient.Instance, new Shell.AppOptions());
            ShowTestWindow(second);
            first.Close();
            var target = App.HandOverTarget();
            Check("startup open: hand-over goes to an open window, never the closed first one", target is not null && !ReferenceEquals(target, first));
            target?.OpenFromAnotherLaunch(gp5);
            if (ReferenceEquals(target, second)) StartupSongShows(second, "hand-over after the first window closed", errors, expectedTabs: 2);
            foreach (var session in second.OpenDocuments.ToList()) session.MarkClean();
            second.Close();
        }
        finally
        {
            DialogHost.MessageCapture = previousMessage;
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    });

    private static void StartupSongShows(MainWindow w, string label, List<string> errors, int expectedTabs = 1)
    {
        StartupPump(() => errors.Count > 0 || (w.OpenDocuments.Count == expectedTabs && w.StatusText.Text.StartsWith("Opened", StringComparison.Ordinal)), 30000);
        w.UpdateLayout();
        StartupPump(() => false, 50);
        var song = w.OpenDocuments.Last().Project;
        Check($"startup open: {label}: no open error", errors.Count == 0, string.Join(" | ", errors));
        Check($"startup open: {label}: the song opened in tab {expectedTabs}", w.OpenDocuments.Count == expectedTabs && w.StatusText.Text.StartsWith("Opened", StringComparison.Ordinal),
            $"tabs {w.OpenDocuments.Count}, status '{w.StatusText.Text}'");
        Check($"startup open: {label}: tracks, bars and timeline rows show",
            song.Tracks.Count > 0 && song.Tracks.FirstOrDefault()?.Measures.Count > 1 && w.Arrangement.TrackRowActualHeights.Count == song.Tracks.Count && w.Arrangement.TrackRowActualHeights.All(h => h > 0),
            $"tracks {song.Tracks.Count}, bars {song.Tracks.FirstOrDefault()?.Measures.Count}, rows [{string.Join(",", w.Arrangement.TrackRowActualHeights)}]");
    }

    private static void StartupPump(Func<bool> done, int timeoutMs)
    {
        var watch = Stopwatch.StartNew();
        do
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(5), DispatcherPriority.Background,
                (sender, _) => { ((DispatcherTimer)sender!).Stop(); frame.Continue = false; }, Dispatcher.CurrentDispatcher);
            timer.Start();
            Dispatcher.PushFrame(frame);
        } while (!done() && watch.ElapsedMilliseconds < timeoutMs);
    }
}
