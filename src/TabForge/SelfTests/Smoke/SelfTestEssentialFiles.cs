using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>Essential-action smoke checks of the file side: start with and without a file, a second file, tabs, close with unsaved changes, save and reopen, autosave.</summary>
public static partial class SelfTest
{
    private static void TestEssentialStartNoFile()
    {
        var w = SmNewWindow();
        try
        {
            SmStep("start with no file: the window opens with an empty song ready to edit", () =>
            {
                var doc = SmActive(w);
                Check("start with no file: one tab, one track, the editor shows it", w.OpenDocuments.Count == 1 && doc.Project.Tracks.Count >= 1 && !doc.HasUnsavedChanges);
            });
        }
        finally { SmCloseWindow(w); }
    }

    private static void TestEssentialStartupFiles()
    {
        var dir = SmScratch();
        try
        {
            var song = SmDemoSong();
            var tf = Path.Combine(dir, "start.tforge"); ProjectService.Save(tf, song);
            var gp = Path.Combine(dir, "start.gp"); GuitarProExporter.Save(song, gp);
            var gp5 = SmSample("TabForge Demo - Ashen Meridian.gp5");
            foreach (var path in new[] { tf, gp, gp5 })
            {
                var kind = Path.GetExtension(path);
                if (!File.Exists(path)) { Skip($"startup file {kind}", "sample not found"); continue; }
                var w = SmNewWindow();
                try
                {
                    // The entry point of a file named on the command line (App.OpenStartupFile, with the arguments as the shell splits an unquoted path): the background open of the first tab.
                    var args = path.Split(' ');
                    SmStep($"startup file argument {kind}: opens without an exception", () =>
                        typeof(App).GetMethod("OpenStartupFile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, new object[] { w, args }));
                    var opened = SmUntil(() => w.OpenDocuments.Any(d => d.Project.Tracks.Count > 1), 30000);
                    Check($"startup file argument {kind}: the song is open in the first tab with all its tracks", opened && w.OpenDocuments.Count == 1,
                        $"tabs {w.OpenDocuments.Count}, tracks {string.Join("/", w.OpenDocuments.Select(d => d.Project.Tracks.Count))} of {song.Tracks.Count}");
                }
                finally { SmCloseWindow(w); }
            }
        }
        finally { SmClean(dir); }
    }

    private static void TestEssentialSecondFileAndTabs()
    {
        var dir = SmScratch();
        var w = SmNewWindow();
        try
        {
            var song = SmDemoSong();
            var path = Path.Combine(dir, "second.tforge"); ProjectService.Save(path, song);
            var first = SmOpenSong(w, SmDemoSong(200));
            var before = w.OpenDocuments.Count;
            SmStep("open a second file while running", () => SmAwait((Task)SmCall(w, "OpenDocumentFromPath", path, false, false)!));
            Check("open a second file: it becomes a new tab", SmUntil(() => w.OpenDocuments.Count == before + 1) && w.OpenDocuments.Any(d => d.Path == path));
            SmStep("tab switch", () =>
            {
                SmCall(w, "ActivateTabAt", w.OpenDocuments.ToList().IndexOf(first)); SmSettle();
                var firstActive = ReferenceEquals(SmActive(w), first);
                SmCall(w, "ActivateTabAt", w.OpenDocuments.Count - 1); SmSettle();
                Check("tab switch: each tab shows its own song", firstActive && SmActive(w).Path == path && SmActive(w).Project.Tracks.Count == song.Tracks.Count);
            });
        }
        finally { SmCloseWindow(w); SmClean(dir); }
    }

    private static void TestEssentialCloseTabUnsaved()
    {
        var w = SmNewWindow();
        try
        {
            var doc = SmOpenSong(w, SmDemoSong());
            var count = w.OpenDocuments.Count;
            DocumentEdits.Run(doc, p => { p.Tempo += 1; return true; });
            Check("close tab with unsaved changes: the edit marks the song unsaved", doc.HasUnsavedChanges);
            var seen = new List<string>();
            SmWithDialogs(SmAnswerConfirm(MessageBoxResult.Cancel, seen), () => { SmCall(w, "CloseDocument", w.OpenDocuments.ToList().IndexOf(doc)); SmSettle(); return 0; });
            Check("close tab with unsaved changes: the prompt is shown and Cancel keeps the tab", seen.Count == 1 && w.OpenDocuments.Count == count);
            SmWithDialogs(SmAnswerConfirm(MessageBoxResult.No), () => { SmCall(w, "CloseDocument", w.OpenDocuments.ToList().IndexOf(doc)); return SmUntil(() => w.OpenDocuments.Count == count - 1); });
            Check("close tab with unsaved changes: Discard closes the tab", w.OpenDocuments.Count == count - 1 && !w.OpenDocuments.Contains(doc));
        }
        finally { SmCloseWindow(w); }
    }

    private static Func<Window, bool?> SmSaveAnswers() => dialog =>
    {
        // .gp saves ask how to keep audio settings and what a clean file leaves out: take the clean, compatible choice.
        foreach (var b in SmButtons(dialog))
            if (b.Content is StackPanel p && p.Children.OfType<TextBlock>().FirstOrDefault()?.Text.StartsWith("Clean .gp file", StringComparison.Ordinal) == true
                || System.Windows.Automation.AutomationProperties.GetAutomationId(b) == GpExportPreflightDialog.CompatibleId)
            {
                try { b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); } catch (InvalidOperationException) { } // Not logged: test driver: a dialog that is already closed is expected.
                return true;
            }
        return false;
    };

    private static IEnumerable<Button> SmButtons(DependencyObject root)
    {
        if (root is Button hit) yield return hit;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var found in SmButtons(child)) yield return found;
    }

    private static void TestEssentialSaveAndReopen()
    {
        var dir = SmScratch();
        var w = SmNewWindow();
        try
        {
            foreach (var ext in new[] { ".tforge", ".gp" })
            {
                var doc = SmOpenSong(w, SmDemoSong());
                var path = Path.Combine(dir, "saved" + ext);
                SmStep($"save as {ext}", () =>
                {
                    var hold = ((DocumentController)SmField<object>(w, "_documentController")!).TryBeginSave();
                    var task = (Task<bool>)SmCall(w, "SaveToAsync", doc, path, hold!)!;
                    var saved = SmWithDialogs(SmSaveAnswers(), () => SmAwait(task) && task.Result);
                    hold!.Dispose();
                    Check($"save as {ext}: the file is written and the song is clean", saved && File.Exists(path) && !doc.HasUnsavedChanges && doc.Path == path);
                });
                DocumentEdits.Run(doc, p => { p.Tempo += 1; return true; });
                SmStep($"save {ext}", () =>
                {
                    var task = (Task<bool>)SmCall(w, "SaveCurrentAsync", doc, null)!;
                    Check($"save {ext}: the changed song is written and clean", SmAwait(task) && task.Result && !doc.HasUnsavedChanges);
                });
                SmStep($"reopen {ext}", () =>
                {
                    var reopened = new DocumentController().Open(path).Project;
                    var diffs = new List<string>();
                    CompareModel(doc.Project, reopened, "song", diffs, new HashSet<object>(ReferenceEqualityComparer.Instance));
                    Check($"reopen {ext}: the reopened song equals the saved one", diffs.Count == 0, string.Join("; ", diffs.Take(3)));
                });
            }
        }
        finally { SmCloseWindow(w); SmClean(dir); }
    }

    private static void TestEssentialAutosaveAndRecovery()
    {
        var dir = SmScratch();
        var w = SmNewWindow();
        try
        {
            var doc = SmOpenSong(w, SmDemoSong());
            DocumentEdits.Run(doc, p => { p.Tempo += 3; return true; });
            SmStep("autosave: a pass writes a recovery copy of the unsaved song", () =>
            {
                var task = new AutosaveRunner().RunPassAsync(new[] { doc }, dir);
                SmAwait(task);
            });
            var found = AutosaveService.FindOrphans(dir, _ => false);
            Check("autosave: the recovery listing finds the copy", found.Count == 1, $"found {found.Count}");
            if (found.Count == 1)
            {
                var back = ProjectService.Load(found[0]);
                Check("autosave: the recovery copy holds the unsaved song", back.Tempo == doc.Project.Tempo && back.Tracks.Count == doc.Project.Tracks.Count);
            }
        }
        finally { SmCloseWindow(w); SmClean(dir); }
    }
}
