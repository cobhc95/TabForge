using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>
/// Selection scope: a score selection covers its track, a timeline selection every track. The timeline highlight, the selection menu's
/// Copy / Cut / Paste and the Delete prompt's default follow it; the loop area is the selected bars either way. Also the timeline's bar
/// cells never show a stale summary (edits in place, track cut / paste / reorder / duplicate without a rebind).
/// </summary>
public static partial class SelfTest
{
    private static void TestSelectionScope() => RunInWindowFixture((window, context) =>
    {
        var settings = LtField<AppSettingsStore>(window, "_settingsStore")!.Settings;
        var arrangement = LtField<ArrangementPanel>(window, "Arrangement")!;
        var editor = LtField<TabEditorControl>(window, "Editor")!;
        var grid = LtField<DataGrid>(window, "TrackMixerGrid")!;
        var selection = LtField<SelectionModel>(window, "_selection")!;
        var timeline = arrangement.TimelineForTest;
        var previous = DialogHost.Capture;
        var scopes = new List<int>();
        DialogHost.Capture = w =>
        {
            if (w is ThemedConfirmDialog d) { scopes.Add(d.SelectedScope); d.AnswerForTest(MessageBoxResult.No); }
            return true;
        };
        try
        {
            settings.Editing.BarRangeDelete = "Ask";
            var doc = DoOpen(window, BrwSong());
            var hash = DoHash(doc);
            int Counts() => doc.Project.Tracks.Select(t => t.Measures.Count).Distinct().Count() == 1 ? doc.Project.Tracks[0].Measures.Count : -1;
            void Drag(int track, int from, int to) { timeline.SimulateRangeDrag(track, from, to, SettleLifetimeDispatcher); SettleLifetimeDispatcher(); }
            void Score(int track, int from, int to) { grid.SelectedIndex = track; SettleLifetimeDispatcher(); editor.SelectRange(from, 0, to, -1); SettleLifetimeDispatcher(); }
            void Menu(string header)
            {
                var item = ((ContextMenu)LtCall(window, "BuildSelectionMenu")!).Items.OfType<MenuItem>().First(m => m.Header as string == header);
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                SettleLifetimeDispatcher();
            }
            void Undo() { DocumentEdits.Undo(doc); SettleLifetimeDispatcher(); }
            (int, int) Loop() => (doc.LoopStartBar, doc.LoopEndBar);

            // Scope from each origin, the highlight rows and the loop range.
            Drag(1, 4, 5);
            Check("scope: a timeline drag selects every track; the highlight covers every row; the loop area is the bars",
                selection.Scope == SelectionScope.AllTracks && timeline.SelectionRows.Count == 5 && Loop() == (4, 5), $"{selection.Scope} rows {timeline.SelectionRows.Count} loop {Loop()}");
            Score(1, 4, 5);
            Check("scope: a score selection covers its track; the highlight is that row only; the loop area is its bars",
                selection.Scope == SelectionScope.ThisTrack && selection.TrackIndex == 1 && timeline.SelectionRows.SequenceEqual(new[] { 1 }) && Loop() == (4, 5),
                $"{selection.Scope} track {selection.TrackIndex} rows {string.Join(",", timeline.SelectionRows)} loop {Loop()}");
            grid.SelectedIndex = 2; SettleLifetimeDispatcher();
            Check("scope: switching track keeps a one-track selection and moves it to the new track",
                selection.Scope == SelectionScope.ThisTrack && selection.BarRange is { Start: 4, End: 5 } && timeline.SelectionRows.SequenceEqual(new[] { 2 }),
                $"{selection.Scope} {selection.BarRange} rows {string.Join(",", timeline.SelectionRows)}");
            selection.SetRange(-1, 3, 3, SelectionOrigin.Command);
            Check("scope: a command keeps the current scope", selection.Scope == SelectionScope.ThisTrack);

            // Delete prompt default: This track for a score selection, All tracks for a timeline selection.
            Score(1, 4, 5);
            Menu("Delete…");
            Drag(1, 4, 4);
            Menu("Delete…");
            Check("scope: the Delete prompt preselects This track for a score selection, All tracks for a timeline one",
                scopes.SequenceEqual(new[] { 1, 0 }) && DoHash(doc) == hash, string.Join(",", scopes));

            // One track: copy, paste onto the selected track, cut clears that track only.
            Score(1, 4, 5);
            Menu("Copy");
            Check("scope: Copy on a score selection copies that track's bars only", ClipboardService.Shared.TryGetClip(out _) is { Tracks.Count: 1 });
            Score(1, 2, 3);
            Menu("Paste");
            Check("scope: one track's bars paste onto the selected track in place", BrwNoteAt(doc.Project, 1, 2) && Enumerable.Range(0, 4).Where(t => t != 1).All(t => !BrwNoteAt(doc.Project, t, 2))
                && Counts() == 8 && doc.Undo.UndoCount == 1, $"counts {Counts()} undo {doc.Undo.UndoCount}");
            Undo();
            Score(1, 4, 5);
            Menu("Cut");
            Check("scope: Cut on a score selection clears that track's bars only; bar counts stay", !BrwNoteAt(doc.Project, 1, 4) && BrwNoteAt(doc.Project, 0, 4) && BrwNoteAt(doc.Project, 2, 4)
                && Counts() == 8 && doc.Undo.UndoCount == 1, $"counts {Counts()} undo {doc.Undo.UndoCount}");
            Undo();
            Check("scope: undo restores the one-track edits", DoHash(doc) == hash);

            // Every track: copy, paste inserts on every track, cut removes on every track.
            Drag(0, 4, 4);
            Menu("Copy");
            Check("scope: Copy on a timeline selection copies every track", ClipboardService.Shared.TryGetClip(out _) is { Tracks.Count: > 1 });
            Drag(0, 1, 1);
            Menu("Paste");
            Check("scope: every track's bars insert on every track at the target bar", Counts() == 9 && Enumerable.Range(0, 4).All(t => BrwNoteAt(doc.Project, t, 1)), $"counts {Counts()}");
            Undo();
            Drag(0, 5, 6);   // bars without clips (a clip warning would ask first)
            Menu("Cut");
            Check("scope: Cut on a timeline selection removes the bars on every track", Counts() == 6 && Enumerable.Range(0, 4).All(t => BrwNoteAt(doc.Project, t, 5)), $"counts {Counts()}");
            Undo();
            Check("scope: undo restores the all-track edits", DoHash(doc) == hash);

            // The loop only loops the selected bars: it never changes the scope or the selected track.
            void LoopOn(bool on) { LtCall(window, "SetLoopActive", on); SettleLifetimeDispatcher(); }
            string State() => $"{selection.Scope} track {selection.TrackIndex} range {selection.BarRange} rows {string.Join(",", timeline.SelectionRows)} loop {Loop()}";
            Score(2, 4, 5);
            LoopOn(true);
            Check("loop: a score selection keeps its scope, track and one-row highlight; the loop is its bars",
                selection.Scope == SelectionScope.ThisTrack && selection.TrackIndex == 2 && timeline.SelectionRows.SequenceEqual(new[] { 2 }) && Loop() == (4, 5), State());
            LoopOn(false); LoopOn(true);
            Check("loop: toggling the loop off and on keeps the scope", selection.Scope == SelectionScope.ThisTrack && selection.TrackIndex == 2 && Loop() == (4, 5), State());
            Drag(0, 1, 2);
            Check("loop: a new timeline selection while looping is all tracks and the loop follows it",
                selection.Scope == SelectionScope.AllTracks && timeline.SelectionRows.Count == 5 && Loop() == (1, 2), State());
            LoopOn(false); LoopOn(true);
            Check("loop: a timeline selection stays all tracks with the loop toggled", selection.Scope == SelectionScope.AllTracks && Loop() == (1, 2), State());
            Score(3, 6, 7);
            Check("loop: a score selection made while looping is that track only and the loop follows it",
                selection.Scope == SelectionScope.ThisTrack && selection.TrackIndex == 3 && timeline.SelectionRows.SequenceEqual(new[] { 3 }) && Loop() == (6, 7), State());
            // The model was left cleared with the all-tracks scope while the score holds a range: turning the loop on loops that range without widening it.
            LoopOn(false);
            Drag(0, 1, 2);
            selection.Clear(SelectionOrigin.Command); SettleLifetimeDispatcher();
            var syncApplying = typeof(SelectionSync).GetField("_applyingToEditor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var sync = LtField<SelectionSync>(window, "_selectionSync")!;
            grid.SelectedIndex = 1; SettleLifetimeDispatcher();
            syncApplying.SetValue(sync, true); editor.SelectRange(4, 0, 5, -1); syncApplying.SetValue(sync, false);
            LoopOn(true);
            Check("loop: turning the loop on over a score range with a stale all-tracks scope keeps it one track",
                selection.Scope == SelectionScope.ThisTrack && selection.TrackIndex == 1 && timeline.SelectionRows.SequenceEqual(new[] { 1 }) && Loop() == (4, 5), State());
            // A score drag with the loop on: the press clears, the drag grows the range step by step (and Shift+arrow extends it).
            grid.SelectedIndex = 1; SettleLifetimeDispatcher();
            editor.ClearSelection(); SettleLifetimeDispatcher();
            editor.SelectRange(4, 0, 4, 0); SettleLifetimeDispatcher();
            editor.SelectRange(4, 0, 5, -1); SettleLifetimeDispatcher();
            Check("loop: a score drag while looping selects that track only; the loop follows it",
                selection.Scope == SelectionScope.ThisTrack && selection.TrackIndex == 1 && timeline.SelectionRows.SequenceEqual(new[] { 1 }) && Loop() == (4, 5), State());
            editor.ClearSelection(); SettleLifetimeDispatcher();
            editor.SetPosition(3, 0, 0, false); editor.ExtendSelection(1); editor.ExtendSelection(1); SettleLifetimeDispatcher();
            Check("loop: Shift+arrow extension while looping keeps this track", selection.HasRange && selection.Scope == SelectionScope.ThisTrack && timeline.SelectionRows.SequenceEqual(new[] { 1 }), State());
            LoopOn(false);
        }
        catch (Exception ex) { Check("selection scope scenarios ran", false, ex.ToString()); }
        finally { DialogHost.Capture = previous; settings.Editing.BarRangeDelete = "Ask"; }
    });

    /// <summary>The timeline's bar cells always show the bars' notes: in-place edits and track list changes with no rebind of the panel.</summary>
    private static void TestTimelineCellsCurrent()
    {
        var document = DocumentSession.FromProject(DoSong(3), null);
        var project = document.Project;
        var panel = new ArrangementPanel();
        panel.Bind(project, Array.Empty<Playback.MidiOutputDeviceInfo>());
        var timeline = panel.TimelineForTest;
        string Stale() => string.Join(" ", timeline.StaleCells().Select(c => $"{c.Track}:{c.Bar}"));
        Check("timeline cells: current after bind", Stale() == "", Stale());

        project.Tracks[1].Measures[2].Cells[0].Notes.Add(new TabNote { StringIndex = 1, Fret = 5, MidiValue = project.Tracks[1].PitchOf(1, 5) });
        project.IsDirty = true;   // an edit path that did not name the changed bars
        Check("timeline cells: an in-place note edit shows without a range invalidation", Stale() == "", Stale());

        var flow = new TrackClipboardFlow(new TrackFlowHost(document), new ClipboardService(null), new TrackController());
        flow.Cut(1);
        Check("timeline cells: current after cutting a track", Stale() == "", Stale());
        flow.Paste(0);
        Check("timeline cells: the pasted track shows its notes", Stale() == "" && timeline.StaleCells().Count == 0 && project.Tracks[1].Measures[2].Cells[0].Notes.Count == 1, Stale());
        project.Tracks.Reverse();
        project.IsDirty = true;
        Check("timeline cells: current after reordering tracks", Stale() == "", Stale());
        flow.Duplicate(1);
        Check("timeline cells: current after duplicating a track", Stale() == "", Stale());
        DocumentEdits.Undo(document);
        Check("timeline cells: current after undo", Stale() == "", Stale());
    }
}
