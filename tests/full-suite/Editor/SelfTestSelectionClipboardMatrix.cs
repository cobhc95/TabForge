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
/// Copy / cut / paste of continuous selections, every origin (score, timeline) and scope (this track, all tracks), whole bars and a partial-beat
/// score range, pasted into the same track, another track, another bar and over existing content. Each case asserts the notes, equal bar counts,
/// fresh timeline cells, the selection afterwards and that undo restores the song by hash. Track-row, clip and section clipboards are
/// covered by TestTrackRowMenu and TestClipAndSectionEdits.
/// </summary>
public static partial class SelfTest
{
    private static void TestSelectionClipboardMatrix() => RunInWindowFixture((window, context) =>
    {
        var settings = LtField<AppSettingsStore>(window, "_settingsStore")!.Settings;
        var arrangement = LtField<ArrangementPanel>(window, "Arrangement")!;
        var editor = LtField<TabEditorControl>(window, "Editor")!;
        var grid = LtField<DataGrid>(window, "TrackMixerGrid")!;
        var selection = LtField<SelectionModel>(window, "_selection")!;
        var sections = LtField<SectionEditFlow>(window, "_sections")!;
        var timeline = arrangement.TimelineForTest;
        var shared = typeof(ClipboardService).GetField("_shared", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var previousClipboard = shared.GetValue(null);
        shared.SetValue(null, new ClipboardService(null));   // in-memory score clipboard: never the Windows one
        var previous = DialogHost.Capture;
        DialogHost.Capture = w => { if (w is ThemedConfirmDialog d) d.AnswerForTest(MessageBoxResult.Yes); return true; };
        var barDelete = settings.Editing.BarRangeDelete;
        try
        {
            settings.Editing.BarRangeDelete = "Ask";
            DocumentSession? doc = null;
            var hash = "";
            // Tracks 1..3 get a second note in cell 1 of bar 5 (the partial-beat range); track 2 also one in bar 7 (a paste-over target).
            void Open()
            {
                var song = BrwSong();
                foreach (var (t, bar) in new[] { (1, 4), (2, 4), (3, 4), (2, 6) })
                {
                    var tr = song.Tracks[t];
                    tr.Measures[bar].Cells[4].Notes.Add(new TabNote { StringIndex = 1, Fret = 7, MidiValue = tr.PitchOf(1, 7) });
                }
                doc = DoOpen(window, song);
                hash = DoHash(doc);
            }
            int Counts() => doc!.Project.Tracks.Select(t => t.Measures.Count).Distinct().Count() == 1 ? doc.Project.Tracks[0].Measures.Count : -1;
            // Note counts per cell of a bar (pitch is converted between track kinds on paste; the shape is not).
            string Pat(int t, int bar, int cell = -1) => bar >= doc!.Project.Tracks[t].Measures.Count ? "-" : string.Join(",",
                doc.Project.Tracks[t].Measures[bar].Cells.Where((c, i) => cell < 0 || i == cell).Select(c => c.Notes.Count));
            string Pitches(int t, int bar) => string.Join(";", doc!.Project.Tracks[t].Measures[bar].Cells.Select(c => string.Join(",", c.Notes.Select(n => n.MidiValue))));
            void Settle() => SettleLifetimeDispatcher();
            void Drag(int track, int from, int to) { timeline.SimulateRangeDrag(track, from, to, SettleLifetimeDispatcher); Settle(); }
            void Score(int track, int from, int c0, int to, int c1) { grid.SelectedIndex = track; Settle(); editor.SelectRange(from, c0, to, c1); Settle(); }
            void Caret(int track, int bar, int cell) { grid.SelectedIndex = track; Settle(); editor.ClearSelection(); Settle(); editor.SetPosition(bar, cell, 0, false); Settle(); }
            void Key(string method) { LtCall(window, method, window, new RoutedEventArgs()); Settle(); }
            void Menu(string header)
            {
                var item = ((ContextMenu)LtCall(window, "BuildSelectionMenu")!).Items.OfType<MenuItem>().First(m => m.Header as string == header);
                item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Settle();
            }
            string Invariants(string when) =>
                (Counts() == -1 ? $"{when}: bar counts differ; " : "") + (timeline.StaleCells().Count > 0 ? $"{when}: {timeline.StaleCells().Count} stale cells; " : "")
                + (selection.HasRange && selection.EndBar >= Counts() ? $"{when}: selection past the end; " : "")
                + (selection.HasRange && selection.Scope == SelectionScope.ThisTrack && !timeline.SelectionRows.SequenceEqual(new[] { selection.TrackIndex }) ? $"{when}: highlight rows {string.Join(",", timeline.SelectionRows)}; " : "")
                + (selection.HasRange && selection.Scope == SelectionScope.AllTracks && timeline.SelectionRows.Count != doc!.Project.Tracks.Count ? $"{when}: highlight rows {timeline.SelectionRows.Count}; " : "");
            void Case(string name, Action act, Func<bool> expect, int maxSteps = 1)
            {
                var undoBefore = doc!.Undo.UndoCount;
                var problem = "";
                try { act(); problem = Invariants("after"); if (!expect()) problem += $"unexpected result (sel {Sel()} counts {Counts()} bar5 {string.Join("|", Enumerable.Range(0, 4).Select(t => Pat(t, 4)))} bar3 {string.Join("|", Enumerable.Range(0, 4).Select(t => Pat(t, 2)))} bar7 {string.Join("|", Enumerable.Range(0, 4).Select(t => Pat(t, 6)))}); "; }
                catch (Exception ex) { problem = $"{ex.GetType().Name} {ex.Message}; "; }
                var steps = doc.Undo.UndoCount - undoBefore;
                if (steps > maxSteps) problem += $"{steps} undo steps; ";
                for (var i = 0; i < steps; i++) DocumentEdits.Undo(doc);
                Settle();
                if (DoHash(doc) != hash) problem += "undo did not restore; ";
                problem += Invariants("after undo");
                Check($"clipboard matrix: {name}", problem.Length == 0, $"{problem}(undo steps {steps}, status '{LtField<TextBlock>(window, "StatusText")!.Text}')");
            }

            // ---- score origin, this track, whole bars (track 1, bar 5) ----
            Open();
            var barPat = Pat(1, 4);
            Case("score/this track/bars: copy changes nothing and keeps the selection", () => { Score(1, 4, 0, 4, -1); Key("Copy_Click"); },
                () => DoHash(doc!) == hash && doc!.Undo.UndoCount == 0 && selection is { Scope: SelectionScope.ThisTrack, TrackIndex: 1 } && selection.BarRange == (4, 4)
                    && ClipboardService.Shared.TryGetClip(out _) is { Tracks.Count: 1 });
            Case("score/this track/bars: paste into the same track at another bar", () => { Caret(1, 2, 0); Key("Paste_Click"); },
                () => Pat(1, 2) == barPat && Pitches(1, 2) == Pitches(1, 4) && Pat(0, 2) == Pat(0, 3) && Pat(2, 2) == Pat(2, 3) && Counts() == 8);
            Case("score/this track/bars: paste into another track", () => { Caret(2, 2, 0); Key("Paste_Click"); },
                () => Pat(2, 2) == barPat && Pat(1, 2) == Pat(1, 3) && Counts() == 8);
            // As GP5 (quiet runs l6, l6b): beats pasted onto written notes insert before the cursor beat in the same bar, with no question.
            var bar7Before = "";
            Case("score/this track/bars: paste onto written notes inserts in the bar (track 3, bar 7; GP5)", () => { bar7Before = Pat(2, 6); Caret(2, 6, 0); Key("Paste_Click"); },
                () => Pat(2, 6) != bar7Before && Counts() == 8 && LtField<TextBlock>(window, "StatusText")!.Text.Contains("inserted"));
            Case("score/this track/bars: cut clears that track's bar only", () => { Score(1, 4, 0, 4, -1); Key("Cut_Click"); },
                () => !BrwNoteAt(doc!.Project, 1, 4) && BrwNoteAt(doc.Project, 0, 4) && BrwNoteAt(doc.Project, 2, 4) && Counts() == 8
                    && ClipboardService.Shared.TryGetClip(out _) is { Tracks.Count: 1 });

            // ---- score origin, partial-beat range (track 1, bar 5, cell 2 only) ----
            Open();
            Case("score/this track/beats: copy of one beat changes nothing", () => { Score(1, 4, 4, 4, 4); Key("Copy_Click"); },
                () => DoHash(doc!) == hash && doc!.Undo.UndoCount == 0 && ClipboardService.Shared.TryGetClip(out _) is { Kind: ScoreClipKind.Beats });
            Case("score/this track/beats: paste at another bar in the same track", () => { Caret(1, 2, 4); Key("Paste_Click"); },
                () => Pat(1, 2, 4) == "1" && Pat(1, 2, 0) == "0" && Pitches(1, 2).Split(';')[4] == Pitches(1, 4).Split(';')[4] && Counts() == 8);
            Case("score/this track/beats: paste into another track", () => { Caret(0, 5, 4); Key("Paste_Click"); },
                () => Pat(0, 5, 4) == "1" && Pat(1, 5, 4) == "0" && Counts() == 8);
            Case("score/this track/beats: paste over an existing beat (track 3, bar 7)", () => { Caret(2, 6, 4); Key("Paste_Click"); },
                () => Pat(2, 6, 4) == "1" && Counts() == 8);
            Case("score/this track/beats: cut rests the beat and keeps the bar's other beats", () => { Score(1, 4, 4, 4, 4); Key("Cut_Click"); },
                () => Pat(1, 4, 4) == "0" && Pat(1, 4, 0) == "1" && Pat(2, 4, 4) == "1" && Counts() == 8);

            // ---- timeline origin, all tracks, whole bars (bar 5) ----
            Open();
            var all = Enumerable.Range(0, 4).Select(t => Pitches(t, 4)).ToArray();
            Case("timeline/all tracks/bars: copy changes nothing and keeps the selection", () => { Drag(0, 4, 4); Menu("Copy"); },
                () => DoHash(doc!) == hash && doc!.Undo.UndoCount == 0 && selection is { Scope: SelectionScope.AllTracks } && selection.BarRange == (4, 4)
                    && ClipboardService.Shared.TryGetClip(out _) is { Tracks.Count: 5 });
            Case("timeline/all tracks/bars: paste inserts on every track at the target bar", () => { Drag(0, 1, 1); Menu("Paste"); },
                () => Counts() == 9 && Enumerable.Range(0, 4).All(t => Pitches(t, 1) == all[t]));
            Case("timeline/all tracks/bars: paste at another bar (the last)", () => { Drag(0, 7, 7); Menu("Paste"); },
                () => Counts() == 9 && Enumerable.Range(0, 4).All(t => Pitches(t, 7) == all[t]));
            Case("timeline/all tracks/bars: paste over existing bars on every track", () => sections.PasteBar(doc!, 7, null, allTracks: true),
                () => Counts() == 8 && Enumerable.Range(0, 4).All(t => Pitches(t, 7) == all[t]));
            Case("timeline/all tracks/bars: cut removes the bars on every track", () => { Drag(0, 5, 6); Menu("Cut"); },
                () => Counts() == 6 && ClipboardService.Shared.TryGetClip(out _) is { Tracks.Count: 5 } && !selection.HasRange);

            // ---- the other two origin/scope pairs: the clip and the cut follow the scope, not the origin ----
            Open();
            foreach (var (origin, scope) in new[] { (SelectionOrigin.Timeline, SelectionScope.ThisTrack), (SelectionOrigin.Editor, SelectionScope.AllTracks) })
            {
                var tracksInClip = scope == SelectionScope.ThisTrack ? 1 : 5;
                void Select() { grid.SelectedIndex = 1; Settle(); selection.SetRange(1, 4, 4, origin, 0, -1, scope); Settle(); }
                Case($"{origin}/{scope}/bars: copy takes {tracksInClip} track(s), selection stays", () => { Select(); Menu("Copy"); },
                    () => DoHash(doc!) == hash && selection.Scope == scope && ClipboardService.Shared.TryGetClip(out _) is { } c && c.Tracks.Count == tracksInClip);
                Case($"{origin}/{scope}/bars: cut {(scope == SelectionScope.ThisTrack ? "clears one track" : "removes the bar everywhere")}", () => { Select(); Menu("Cut"); },
                    () => scope == SelectionScope.ThisTrack ? Counts() == 8 && !BrwNoteAt(doc!.Project, 1, 4) && BrwNoteAt(doc.Project, 0, 4) : Counts() == 7);
            }

            // ---- paste of the two crossed pairs: score origin / all tracks inserts everywhere, timeline origin / this track pastes in place ----
            Open();
            var every = Enumerable.Range(0, 4).Select(t => Pitches(t, 4)).ToArray();
            void Pick(int track, int bar, SelectionOrigin origin, SelectionScope scope) { grid.SelectedIndex = track; Settle(); selection.SetRange(track, bar, bar, origin, 0, -1, scope); Settle(); }
            string Sel() => selection.HasRange ? $"{selection.Scope} {selection.StartBar}-{selection.EndBar}" : "none";
            Pick(1, 4, SelectionOrigin.Editor, SelectionScope.AllTracks); Menu("Copy");
            var allClip = ClipboardService.Shared.TryGetClip(out _);
            Check("clipboard matrix: score origin / all tracks: the copy holds every track", allClip is { Tracks.Count: 5 });
            Case("score/all tracks/bars: paste at another bar (selection moves to the pasted bar)", () => { Pick(1, 1, SelectionOrigin.Editor, SelectionScope.AllTracks); Menu("Paste"); },
                () => Counts() == 9 && Enumerable.Range(0, 4).All(t => Pitches(t, 1) == every[t]) && Sel() == "AllTracks 1-1");
            Case("score/all tracks/bars: paste with another track selected inserts on every track", () => { Pick(2, 6, SelectionOrigin.Editor, SelectionScope.AllTracks); Menu("Paste"); },
                () => Counts() == 9 && Enumerable.Range(0, 4).All(t => Pitches(t, 6) == every[t]) && Sel() == "AllTracks 6-6");
            Case("score/all tracks/bars: paste over existing bars on every track", () => sections.PasteBar(doc!, 6, null, allTracks: true),
                () => Counts() == 8 && Enumerable.Range(0, 4).All(t => Pitches(t, 6) == every[t]));
            Pick(1, 4, SelectionOrigin.Timeline, SelectionScope.ThisTrack); Menu("Copy");
            Check("clipboard matrix: timeline origin / this track: the copy holds one track", ClipboardService.Shared.TryGetClip(out _) is { Tracks.Count: 1 });
            Case("timeline/this track/bars: paste into the same track at another bar", () => { Pick(1, 2, SelectionOrigin.Timeline, SelectionScope.ThisTrack); Menu("Paste"); },
                () => Counts() == 8 && Pat(1, 2) == barPat && Pat(0, 2) == "0" + string.Concat(Enumerable.Repeat(",0", 15)) && Pat(2, 2) == Pat(0, 2));
            Case("timeline/this track/bars: paste into another track", () => { Pick(2, 2, SelectionOrigin.Timeline, SelectionScope.ThisTrack); Menu("Paste"); },
                () => Counts() == 8 && Pat(2, 2) == barPat && Pat(1, 2) == Pat(0, 2));
            Case("timeline/this track/bars: paste at another bar (the last)", () => { Pick(1, 3, SelectionOrigin.Timeline, SelectionScope.ThisTrack); Menu("Paste"); },
                () => Counts() == 8 && Pat(1, 3) == barPat);
            Case("timeline/this track/bars: paste over existing content (track 3, bar 7)", () => { Pick(2, 6, SelectionOrigin.Timeline, SelectionScope.ThisTrack); Menu("Paste"); },
                () => Counts() == 8 && Pat(2, 6) == barPat);

            // ---- track rows: copy, cut, paste, duplicate ----
            Open();
            var flow = (TrackClipboardFlow)LtCall(window, "get_TrackFlow")!;
            var rowPat = Pat(1, 4);
            Case("track row: copy changes nothing", () => flow.Copy(1), () => DoHash(doc!) == hash && doc!.Undo.UndoCount == 0 && flow.CanPaste);
            Case("track row: paste adds a copy after the track with its notes", () => { flow.Copy(1); flow.Paste(1); },
                () => doc!.Project.Tracks.Count == 6 && Pat(2, 4) == rowPat && Pitches(2, 4) == Pitches(1, 4) && Counts() == 8);
            Case("track row: cut removes the track and keeps the bar counts", () => flow.Cut(1), () => doc!.Project.Tracks.Count == 4 && Counts() == 8 && flow.CanPaste);
            Case("track row: cut then paste moves the track", () => { flow.Cut(1); flow.Paste(2); }, () => doc!.Project.Tracks.Count == 5 && Pat(3, 4) == rowPat && Counts() == 8, 2);
            Case("track row: duplicate of an audio track keeps its clips", () => flow.Duplicate(4),
                () => doc!.Project.Tracks.Count == 6 && doc.Project.Tracks[5].AudioClips.Count == doc.Project.Tracks[4].AudioClips.Count && doc.Project.Tracks[4].AudioClips.Count == 2);
            Case("track row: duplicate of a note track copies the notes", () => flow.Duplicate(0), () => doc!.Project.Tracks.Count == 6 && Pitches(1, 4) == Pitches(0, 4) && Counts() == 8);

            // ---- clips: an audio clip and a MIDI clip, copy then paste at another time and over an existing clip ----
            Open();
            var clips = LtField<ClipEditController>(window, "_clips")!;
            doc!.Project.Tracks[1].AudioClips.Add(new AudioClip { Name = "midi", StartSec = 1, SourceLengthSec = 1, FileLengthSec = 1, Notes = new List<ClipNote> { new(0, 0.5, 60, 90) } });
            hash = DoHash(doc);
            TrackModel Tr(int i) => doc!.Project.Tracks[i];   // undo rebuilds the song: tracks are looked up again each time
            void ClipPaste(int fromTrack, int clip, int toTrack, double sec)
            {
                arrangement.SelectedClip = Tr(fromTrack).AudioClips[clip]; clips.RunHotkey(doc!, "Clip.Copy");
                clips.LaneCursor = new LaneCursor(Tr(toTrack), 0, sec);
                clips.RunHotkey(doc!, "Clip.Paste");
                Settle();
            }
            Case("clip: copying an audio clip changes nothing", () => { arrangement.SelectedClip = Tr(4).AudioClips[0]; clips.RunHotkey(doc!, "Clip.Copy"); }, () => DoHash(doc!) == hash && doc!.Undo.UndoCount == 0);
            Case("clip: an audio clip pastes at another time", () => ClipPaste(4, 0, 4, 12),
                () => Tr(4).AudioClips.Count == 3 && Tr(4).AudioClips.Count(c => Math.Abs(c.StartSec - 12) < 1e-9) == 1 && Counts() == 8);
            Case("clip: an audio clip pasted over an existing clip takes a free lane", () => ClipPaste(4, 1, 4, Tr(4).AudioClips[0].StartSec),
                () => Tr(4).AudioClips.Count == 3 && Tr(4).AudioClips.Select(c => (c.Lane, c.StartSec)).Distinct().Count() == 3 && Counts() == 8);
            Case("clip: a MIDI clip pastes at another time on its track", () => ClipPaste(1, 0, 1, 12),
                () => Tr(1).AudioClips.Count == 2 && Tr(1).AudioClips.Count(c => c.IsMidi && c.Notes!.Count == 1) == 2 && Counts() == 8);
            Case("clip: a MIDI clip pastes onto another instrument track", () => ClipPaste(1, 0, 0, 3),
                () => Tr(0).AudioClips.Any(c => c.IsMidi && c.Notes!.Count == 1) && Tr(1).AudioClips.Count == 1 && Counts() == 8);

            // ---- sections: copy, paste after, paste at a bar, cut ----
            var confirmSection = settings.General.ConfirmDeleteSection;
            settings.General.ConfirmDeleteSection = false;
            try
            {
                Open();
                var sectionBars = Enumerable.Range(0, 4).Select(t => Pitches(t, 4)).ToArray();
                Case("section: copy changes nothing", () => sections.CopySection(doc!, doc.Project.Markers[1]), () => DoHash(doc!) == hash && doc.Undo.UndoCount == 0 && ClipboardService.Shared.TryGetClip(out _) is { Tracks.Count: 5 });
                Case("section: paste after another section inserts its bars on every track", () => { sections.CopySection(doc!, doc.Project.Markers[1]); sections.PasteSectionAfter(doc, doc.Project.Markers[0]); },
                    () => Counts() == 12 && Enumerable.Range(0, 4).All(t => Pitches(t, 4) == sectionBars[t] && Pitches(t, 8) == sectionBars[t]) && doc!.Project.Markers.Count == 3);
                Case("section: paste at a bar", () => { sections.CopySection(doc!, doc.Project.Markers[1]); sections.PasteSectionAt(doc, 2); },
                    () => Counts() == 12 && Enumerable.Range(0, 4).All(t => Pitches(t, 2) == sectionBars[t]) && doc!.Project.Markers.Count == 3);
                Case("section: cut removes its bars everywhere and keeps them on the clipboard", () => sections.CutSection(doc!, doc.Project.Markers[1]),
                    () => Counts() == 4 && doc!.Project.Markers.Count == 1 && ClipboardService.Shared.TryGetClip(out _) is { Tracks.Count: 5 });
                Case("section: cut then paste at the end restores the bars", () => { sections.CutSection(doc!, doc.Project.Markers[0]); sections.PasteSectionAt(doc, 4); },
                    () => Counts() == 8 && Enumerable.Range(0, 4).All(t => Pitches(t, 0) == sectionBars[t] && Pat(t, 4) == Pat(t, 5)) && doc!.Project.Markers.Count == 2, 2);
            }
            finally { settings.General.ConfirmDeleteSection = confirmSection; }
        }
        catch (Exception ex) { Check("clipboard matrix scenarios ran", false, ex.ToString()); }
        finally { DialogHost.Capture = previous; shared.SetValue(null, previousClipboard); settings.Editing.BarRangeDelete = barDelete; }
    });
}
