using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>
/// The bar-range Delete prompt in a real window, the way the mouse and keyboard reach it: a five-track song (guitar, bass, drums, keys and an audio
/// track with clips), a range dragged on a track lane of the timeline, then Delete through the window's key routing, the prompt answered through
/// DialogHost.Capture. "All tracks" (the default) changes every track; "This track" only the selected one with equal bar counts; Ctrl+Delete /
/// Ctrl+Shift+Space run directly; "Remember my answer" skips the prompt; the menu's "Delete…" opens the same prompt; Undo restores the hash.
/// </summary>
public static partial class SelfTest
{
    private static SongProject BrwSong()
    {
        var project = TemplateFactory.Blank();
        var controller = new TrackController();
        project.Tracks.Clear();
        var kinds = new[] { TrackKind.Guitar, TrackKind.Bass, TrackKind.Drums, TrackKind.Keys, TrackKind.Audio };
        for (var i = 0; i < kinds.Length; i++)
        {
            var track = controller.CreateTrack(project, kinds[i]);
            track.Name = $"T{i + 1}";
            track.Measures = TemplateFactory.Measures(8);
            project.Tracks.Add(track);
            if (kinds[i] == TrackKind.Audio) continue;
            foreach (var bar in new[] { 4, 7 })   // a note in bar 5 and in the last bar of every note track
                track.Measures[bar].Cells[0].Notes.Add(new TabNote { StringIndex = 0, Fret = 3 + i, MidiValue = kinds[i] == TrackKind.Drums ? 38 : 50 + i });
        }
        project.Markers.Clear();
        project.Markers.Add(new MarkerModel { MeasureIndex = 0, Title = "A" });
        project.Markers.Add(new MarkerModel { MeasureIndex = 4, Title = "B" });
        var span = BarRangeGaps.Span(project, 2, 3)!.Value;
        project.Tracks[4].AudioClips.Add(BrClip(span.To + 0.5, 1));        // after the range
        project.Tracks[4].AudioClips.Add(BrClip(span.From - 0.5, 1));      // across the left edge
        project.IsDirty = false;
        return project;
    }

    private static bool BrwNoteAt(SongProject p, int track, int bar) => bar < p.Tracks[track].Measures.Count && p.Tracks[track].Measures[bar].Cells.Any(c => c.Notes.Count > 0);

    private static void CheckBarRangeGapsInWindow() => RunInWindowFixture((window, context) =>
    {
        var settings = LtField<AppSettingsStore>(window, "_settingsStore")!.Settings;
        var arrangement = LtField<ArrangementPanel>(window, "Arrangement")!;
        var previous = DialogHost.Capture;
        var prompts = new List<string>();
        Action<ThemedConfirmDialog> answer = d => d.AnswerForTest(MessageBoxResult.No);
        DialogHost.Capture = w =>
        {
            prompts.Add(string.Join(" ", Logical<TextBlock>(w).Select(t => t.Text)) + " | " + string.Join(" | ", Logical<RadioButton>(w).Select(r => r.Content as string)));
            answer((ThemedConfirmDialog)w);
            return true;
        };
        try
        {
            settings.Editing.BarRangeDelete = "Ask";
            settings.Editing.BarRangeLastChoice = "Clear";
            var doc = DoOpen(window, BrwSong());
            var hash = DoHash(doc);
            var span = BarRangeGaps.Span(doc.Project, 2, 3)!.Value;
            var d = span.To - span.From;
            int Counts() => doc.Project.Tracks.Select(t => t.Measures.Count).Distinct().Count() == 1 ? doc.Project.Tracks[0].Measures.Count : -1;
            void Drag(int track, int from, int to) { arrangement.TimelineForTest.SimulateRangeDrag(track, from, to, SettleLifetimeDispatcher); SettleLifetimeDispatcher(); }
            void Undo() { DocumentEdits.Undo(doc); SettleLifetimeDispatcher(); }

            // A range dragged on the drums lane (where the old scope guess picked one track), then Delete.
            Drag(2, 2, 3);
            Check("window: a drag on a lane selects the bars and focuses the timeline", arrangement.TimelineHasFocus && LtField<SelectionModel>(window, "_selection")!.BarRange is { Start: 2, End: 3 },
                $"focus {arrangement.TimelineHasFocus}, focused {FocusManager.GetFocusedElement(window)?.GetType().Name}");
            answer = dlg => { dlg.PickForTest(1); dlg.AnswerForTest(MessageBoxResult.Yes); };
            var handled = IxKey(window, Key.Delete);
            Check("window: Delete opens one prompt with the four options, live keys and the clip note", handled && prompts.Count == 1 && prompts[0].Contains("Bars 3-4 are selected")
                && prompts[0].Contains("close the gap (Ctrl+Delete)") && prompts[0].Contains("before the selection (Ctrl+Shift+Space)") && prompts[0].Contains("after the selection")
                && prompts[0].Contains("All tracks") && prompts[0].Contains("This track") && prompts[0].Contains("clip"), string.Join(" || ", prompts));
            Check("window: remove + All tracks (default) closes the gap on EVERY track", Counts() == 6 && Enumerable.Range(0, 4).All(t => BrwNoteAt(doc.Project, t, 2) && !BrwNoteAt(doc.Project, t, 4))
                && doc.Project.Markers[1].MeasureIndex == 2 && doc.Undo.UndoCount == 1, $"counts {Counts()} undo {doc.Undo.UndoCount}");
            var clips = doc.Project.Tracks[4].AudioClips;
            Check("window: the audio track's clips follow (later clip earlier, edge clip cut)", clips.Count == 2 && Math.Abs(clips[0].StartSec - (span.To + 0.5 - d)) < 1e-6
                && Math.Abs(clips[1].SourceLengthSec - 0.5) < 1e-6, string.Join(",", clips.Select(c => $"{c.StartSec:0.###}/{c.SourceLengthSec:0.###}")));
            Check("window: the timeline shows the new bar count", arrangement.TimelineForTest.Project == doc.Project);
            Undo();
            Check("window: Undo restores the song exactly", DoHash(doc) == hash);

            // This track: only the selected (drums) track shifts; bar counts stay equal.
            prompts.Clear();
            Drag(2, 2, 3);
            answer = dlg => { dlg.PickForTest(1, 1); dlg.AnswerForTest(MessageBoxResult.Yes); };
            IxKey(window, Key.Delete);
            Check("window: remove + This track shifts only that track, counts equal", prompts.Count == 1 && Counts() == 8 && BrwNoteAt(doc.Project, 2, 2) && !BrwNoteAt(doc.Project, 2, 4)
                && Enumerable.Range(0, 4).Where(t => t != 2).All(t => BrwNoteAt(doc.Project, t, 4) && !BrwNoteAt(doc.Project, t, 2)) && doc.Project.Tracks[4].AudioClips.Count == 2, $"counts {Counts()}");
            Undo();
            Drag(2, 2, 3);
            answer = dlg => { dlg.PickForTest(3, 1); dlg.AnswerForTest(MessageBoxResult.Yes); };
            IxKey(window, Key.Delete);
            Check("window: insert after + This track grows every track to stay equal, only that track moves", Counts() == 10 && BrwNoteAt(doc.Project, 2, 6) && BrwNoteAt(doc.Project, 2, 9)
                && BrwNoteAt(doc.Project, 0, 4) && BrwNoteAt(doc.Project, 0, 7) && doc.Undo.UndoCount == 1, $"counts {Counts()}");
            Undo();
            Check("window: Undo restores after the one-track edits", DoHash(doc) == hash);

            // Clear (leave a gap) and insert before, all tracks.
            Drag(2, 4, 4);
            answer = dlg => { dlg.PickForTest(0); dlg.AnswerForTest(MessageBoxResult.Yes); };
            IxKey(window, Key.Delete);
            Check("window: clear + All tracks empties bar 5 on every track, bar count and clips stay", Counts() == 8 && Enumerable.Range(0, 4).All(t => !BrwNoteAt(doc.Project, t, 4))
                && doc.Project.Tracks[4].AudioClips.Count == 2);
            Undo();
            Drag(2, 2, 3);
            answer = dlg => { dlg.PickForTest(2); dlg.AnswerForTest(MessageBoxResult.Yes); };
            IxKey(window, Key.Delete);
            Check("window: insert before + All tracks moves every track and the later clip", Counts() == 10 && Enumerable.Range(0, 4).All(t => BrwNoteAt(doc.Project, t, 6))
                && Math.Abs(doc.Project.Tracks[4].AudioClips[0].StartSec - (span.To + 0.5 + d)) < 1e-6);
            Undo();

            // Direct commands (the Ctrl+Delete / Ctrl+Shift+Space bindings run these ids through the same route): no prompt, every track.
            prompts.Clear();
            Drag(2, 2, 3);
            answer = dlg => dlg.AnswerForTest(MessageBoxResult.Yes);   // the clip warning of a direct remove
            IxCommand(window, "Range.Remove");
            Check("window: Range.Remove (Ctrl+Delete) removes on every track without the choice prompt", Counts() == 6 && Enumerable.Range(0, 4).All(t => BrwNoteAt(doc.Project, t, 2))
                && prompts.All(p => !p.Contains("What should Delete do")));
            Undo();
            prompts.Clear();
            Drag(2, 2, 3);
            IxCommand(window, "Range.InsertBefore");
            Check("window: Range.InsertBefore (Ctrl+Shift+Space) inserts on every track without a prompt", Counts() == 10 && prompts.Count == 0 && Enumerable.Range(0, 4).All(t => BrwNoteAt(doc.Project, t, 6)));
            Undo();
            Check("window: Undo restores after the direct commands", DoHash(doc) == hash);

            // The range menu: one "Delete…" item with its key opens the same prompt.
            prompts.Clear();
            Drag(2, 2, 3);
            var menu = (ContextMenu)LtCall(window, "BuildSelectionMenu")!;
            var items = menu.Items.OfType<MenuItem>().ToList();
            var delete = items.FirstOrDefault(m => m.Header as string == "Delete…");
            Check("window: the range menu has one Delete… item with its key and no separate gap items", delete is not null && delete.InputGestureText == "Delete"
                && !items.Any(m => (m.Header as string ?? "").Contains("gap", StringComparison.OrdinalIgnoreCase)), string.Join("|", items.Select(m => m.Header)));
            answer = dlg => dlg.AnswerForTest(MessageBoxResult.No);
            delete?.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            SettleLifetimeDispatcher();
            Check("window: menu Delete… opens the same prompt; Cancel changes nothing", prompts.Count == 1 && prompts[0].Contains("What should Delete do") && DoHash(doc) == hash);

            // Remember my answer: the next Delete runs it directly (action and scope).
            prompts.Clear();
            answer = dlg => { dlg.PickForTest(1, 0); dlg.RememberForTest(true); dlg.AnswerForTest(MessageBoxResult.Yes); };
            IxKey(window, Key.Delete);
            Check("window: 'Remember my answer' stores action and scope", settings.Editing.BarRangeDelete == "Remove" && Counts() == 6);
            Drag(2, 0, 0);
            prompts.Clear();
            IxKey(window, Key.Delete);
            Check("window: with a remembered answer Delete removes on every track without the choice prompt", Counts() == 5 && prompts.All(p => !p.Contains("What should Delete do")), $"{Counts()} {prompts.Count}");
            settings.Editing.BarRangeDelete = "Ask";
        }
        catch (Exception ex) { Check("bar range window scenarios ran", false, ex.ToString()); }
        finally { DialogHost.Capture = previous; settings.Editing.BarRangeDelete = "Ask"; }
    });
}
