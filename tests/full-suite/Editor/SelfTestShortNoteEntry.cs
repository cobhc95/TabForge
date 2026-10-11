using TabForge.Models;
using TabForge.Services;
using TabForge.Views;
using TabForge.Views.Score;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>Rest fill on, guitar track: notes shorter than a sixteenth typed one after another follow each other without a gap (one beamed
    /// group per beat, the fill rest holds the real remainder, the cursor walks every note), and a bar of typed sixteenths stays a plain bar.</summary>
    private static void TestShortNoteEntry()
    {
        var project = Presets.TemplateFactory.Create("Rock Band");
        var trackIndex = project.Tracks.FindIndex(t => t.Kind != TrackKind.Drums);
        var track = project.Tracks[trackIndex];
        while (track.Measures.Count < 9) BarGrid.AppendBars(project, 1);
        foreach (var m in track.Measures) { m.Cells.Clear(); BarFill.FillCells(m.Cells, 16, always: true); }
        var ed = new Views.TabEditorControl { Project = project, SelectedTrackIndex = trackIndex, FillBarsWithRests = true, AutoAdvanceAfterEntry = false };

        List<TabCell> Cells(int bar) => track.Measures[bar].Cells;
        string Dump(int bar) => string.Join(" ", Cells(bar).Select((c, i) => $"{i}:{(c.Notes.Count > 0 ? "n" : c.IsRest ? "r" : "-")}/{c.DurationDenominator}{(c.Dots > 0 ? "." : "")}{(c.RhythmicPosition is { } p ? "@" + p : "")}"));
        List<double> NoteOnsets(int bar) { var o = BarGrid.Onsets(Cells(bar)); return Cells(bar).Select((c, i) => (c, i)).Where(x => x.c.Notes.Count > 0).Select(x => o[x.i]).ToList(); }
        double RestTime(int bar) => Cells(bar).Where(c => c.IsRest && c.Notes.Count == 0).Sum(MusicTime.CellSlots);
        void Type(int bar, int count, int denominator)
        {
            if (ed.SelectedMeasure != bar) ed.SetPosition(bar, 0, 0, false);
            for (var i = 0; i < count; i++)
            {
                ed.Effects.SetDuration(denominator);
                ed.Effects.EnterFret(5);
                ed.MoveBeat(1);
            }
        }
        List<double> Steps(int count) => Enumerable.Range(0, count).Select(i => (double)i).ToList();

        // Eight 32nds: the first beat of the bar.
        ed.SetPosition(0, 0, 0, false);
        Type(0, 8, 32);
        Check("short notes: eight typed 32nds sit on half sixteenths 0..3.5", NoteOnsets(0).SequenceEqual(Steps(8).Select(i => i * 0.5)), Dump(0));
        Check("short notes: the fill rests hold the remaining three beats", Math.Abs(RestTime(0) - 12) < 1e-6 && !MusicTime.AnalyzeBar(project, 0, track).Marked, Dump(0));
        Check("short notes: after the eighth 32nd the cursor is on the first free position of the same bar",
            ed.SelectedMeasure == 0 && ed.SelectedCell == CursorPositions.EmptySpot(Cells(0)) && Math.Abs(BarGrid.Onsets(Cells(0))[ed.SelectedCell] - 4) < 1e-6, $"bar {ed.SelectedMeasure} cell {ed.SelectedCell} | {Dump(0)}");
        var layout = Layout(new StaffNotationRenderer(), track.Measures[0], track: track);
        Check("short notes: eight typed 32nds are one beamed group with three beams",
            layout.BeamGroups.Count == 1 && layout.BeamGroups[0].Beats.Count == 8 && layout.BeamSegments.Any(s => s.Level == 3), $"groups={layout.BeamGroups.Count} | {Dump(0)}");
        var walked = new List<double>();
        ed.SetPosition(0, 0, 0, false);
        for (var i = 0; i < 9 && ed.SelectedMeasure == 0; i++) { walked.Add(BarGrid.Onsets(Cells(0))[ed.SelectedCell]); ed.MoveBeat(1); }
        Check("short notes: Right visits each 32nd, then the free position, then the next bar",
            walked.SequenceEqual(Steps(9).Select(i => i * 0.5)) && ed.SelectedMeasure == 1, string.Join(",", walked));
        ed.MoveBeat(-1);
        var back = new List<double>();
        for (var i = 0; i < 9; i++) { back.Add(BarGrid.Onsets(Cells(0))[ed.SelectedCell]); ed.MoveBeat(-1); }
        Check("short notes: Left walks the same positions back", ed.SelectedMeasure == 0 && back.SequenceEqual(Steps(9).Select(i => 4 - i * 0.5)), string.Join(",", back));

        // A whole bar of 32nds and one of 64ths.
        Type(1, 32, 32);
        Check("short notes: a 4/4 bar holds 32 typed 32nds and the cursor then leaves it",
            NoteOnsets(1).SequenceEqual(Steps(32).Select(i => i * 0.5)) && RestTime(1) == 0 && ed.SelectedMeasure == 2 && !MusicTime.AnalyzeBar(project, 1, track).Marked, $"bar {ed.SelectedMeasure} | {Dump(1)}");
        Type(2, 16, 64);
        Check("short notes: sixteen typed 64ths fill one beat", NoteOnsets(2).SequenceEqual(Steps(16).Select(i => i * 0.25)) && Math.Abs(RestTime(2) - 12) < 1e-6 && ed.SelectedMeasure == 2, Dump(2));
        Type(2, 47, 64);
        Check("short notes: the cursor stays in the bar before the last 64th", ed.SelectedMeasure == 2, $"bar {ed.SelectedMeasure}");
        Type(2, 1, 64);
        Check("short notes: a 4/4 bar holds 64 typed 64ths and the cursor then leaves it",
            NoteOnsets(2).SequenceEqual(Steps(64).Select(i => i * 0.25)) && ed.SelectedMeasure == 3 && !MusicTime.AnalyzeBar(project, 2, track).Marked, $"bar {ed.SelectedMeasure} | {Dump(2)}");

        // Mixed: two eighths, two sixteenths, then 32nds to the end of the second beat.
        Type(3, 2, 8); Type(3, 2, 16); Type(3, 4, 32);
        Check("short notes: eighths, sixteenths, then 32nds follow each other", NoteOnsets(3).SequenceEqual(new[] { 0, 2, 4, 5, 6, 6.5, 7, 7.5 }) && Math.Abs(RestTime(3) - 8) < 1e-6 && ed.SelectedMeasure == 3, Dump(3));

        // Sixteenths only: the plain grid, one cell per sixteenth.
        Type(4, 16, 16);
        Check("short notes: a bar of typed sixteenths stays a plain bar of sixteen cells",
            Cells(4).Count == 16 && BarFill.IsPlain(Cells(4)) && Cells(4).All(c => c.Notes.Count == 1 && c.DurationDenominator == 16) && ed.SelectedMeasure == 5, Dump(4));

        // A whole bar of 32nds selected, then a new length: every note takes it, they stay back to back and the selection keeps them all.
        bool AllNotes(int bar, int denominator) => Cells(bar).Count(c => c.Notes.Count > 0) == 32 && Cells(bar).Where(c => c.Notes.Count > 0).All(c => c.DurationDenominator == denominator);
        string Sel() { var r = ed.SelectionCellRange; return $"sel {r.StartMeasure}:{r.StartCell}-{r.EndMeasure}:{r.EndCell}"; }
        bool SpansNotes(int bar) { var r = ed.SelectionCellRange; return ed.HasSelection && r.StartMeasure == bar && r.EndMeasure == bar && r.StartCell == Cells(bar).FindIndex(c => c.Notes.Count > 0) && r.EndCell == Cells(bar).FindLastIndex(c => c.Notes.Count > 0); }
        Type(5, 32, 32);
        var warp = MeasureWarp.Build(BarGrid.Onsets(Cells(5)), 16);
        var outline = ScoreRenderer.HoverRect(track.Measures[5], Cells(5), 4, 100, warp, 1600, 10, 16);
        var afterNext = 100 + warp.Fraction(3) * 1600;
        Check("short notes: the hover outline of one 32nd ends before the note after next", outline.Right < afterNext - 1, $"right {outline.Right:0.#} note after next {afterNext:0.#}");
        ed.SelectRange(5, 0, 5, -1);
        Check("short notes: selecting the bar takes all 32 notes", SpansNotes(5) && ed.Effects.ToolCells().Count(c => c.Notes.Count > 0) == 32, Sel());
        ed.Effects.SetDuration(64);
        layout = Layout(new StaffNotationRenderer(), track.Measures[5], track: track);
        Check("short notes: 32 selected 32nds set to 64ths are all 64ths, back to back over half the bar",
            AllNotes(5, 64) && NoteOnsets(5).SequenceEqual(Steps(32).Select(i => i * 0.25)) && Math.Abs(RestTime(5) - 8) < 1e-6 && !MusicTime.AnalyzeBar(project, 5, track).Marked, Dump(5));
        Check("short notes: the 64ths are beamed, none stands alone", layout.BeamGroups.Sum(g => g.Beats.Count) == 32, $"groups={layout.BeamGroups.Count} beats={layout.BeamGroups.Sum(g => g.Beats.Count)}");
        Check("short notes: the selection still covers the 32 notes after the change", SpansNotes(5), Sel() + " | " + Dump(5));
        ed.ClearSelection();
        Type(6, 32, 32);
        ed.SelectRange(6, 0, 6, -1);
        ed.Effects.SetDuration(16);
        Check("short notes: 32 selected 32nds set to sixteenths stay in the bar back to back, the bar is overfull",
            AllNotes(6, 16) && NoteOnsets(6).SequenceEqual(Steps(32)) && MusicTime.AnalyzeBar(project, 6, track).Marked && Cells(7).All(c => c.Notes.Count == 0), Dump(6));
        Check("short notes: the selection still covers the 32 sixteenths", SpansNotes(6), Sel());
        ed.ClearSelection();
        ed.SetPosition(6, 0, 0, false);
        for (var i = 0; i < 31; i++) ed.ExtendSelection(1);
        Check("short notes: Shift+Right reaches the last note of an overfull bar", ed.SelectionCellRange == (6, 0, 6, 31), Sel());
        ed.ClearSelection();
        ed.SelectRange(6, 0, 6, -1);
        Check("short notes: selecting an overfull bar takes every note", SpansNotes(6) && ed.SelectionCellRange.EndCell == 31, Sel());
        ed.ClearSelection();

        // Delete over an overfull bar: every cell of each selected bar goes, and a partial delete leaves the other beats one after another.
        void Overfull(int bar)
        {
            Cells(bar).Clear();
            for (var i = 0; i < 20; i++) Cells(bar).Add(new TabCell { DurationDenominator = 16, Notes = { new TabNote { StringIndex = 0, Fret = 5 } } });
            Cells(bar)[3].Notes.Add(new TabNote { StringIndex = 1, Fret = 7 });
            for (var i = 16; i < 19; i++) { Cells(bar)[i].IsTriplet = true; Cells(bar)[i].RhythmicPosition = 16 + (i - 16) * 2.0 / 3; }
            Cells(bar)[19].RhythmicPosition = 18;
        }
        bool Spaced(int bar)
        {
            var o = BarGrid.Onsets(Cells(bar)); var beats = Enumerable.Range(0, Cells(bar).Count).Where(i => CursorPositions.IsBeatCell(Cells(bar)[i])).ToList();
            return beats.Zip(beats.Skip(1), (a, b) => o[b] - o[a] >= Math.Min(MusicTime.CellSlots(Cells(bar)[a]), MusicTime.CellSlots(Cells(bar)[b])) - 1e-6).All(ok => ok);
        }
        Overfull(7); Cells(8).Clear(); BarFill.FillCells(Cells(8), 16, always: true); Cells(8)[0] = new TabCell { DurationDenominator = 16, Notes = { new TabNote { StringIndex = 0, Fret = 2 } } };
        ed.SelectRange(7, 0, 8, 0);
        ed.Effects.DeleteBeat();
        Check("short notes: Delete over a whole overfull bar and the next bar's first beat leaves no note in either",
            Cells(7).All(c => c.Notes.Count == 0) && Cells(8).All(c => c.Notes.Count == 0), Dump(7) + " | " + Dump(8));
        Check("short notes: the emptied overfull bar is a fresh bar (plain cells, no positions or tuplets, not flagged)",
            Cells(7).Count == 16 && Cells(7).All(c => c.RhythmicPosition is null && !c.IsTriplet && c.Tuplet.Item1 == 0) && !MusicTime.AnalyzeBar(project, 7, track).Marked, Dump(7));
        Overfull(7);
        ed.SelectRange(7, 4, 7, 11);
        ed.Effects.DeleteBeat();
        Check("short notes: a partial delete in an overfull bar keeps the other twelve beats spaced by their lengths",
            NoteOnsets(7).Count == 12 && Spaced(7) && NoteOnsets(7).Zip(NoteOnsets(7).Skip(1), (a, b) => b > a + 1e-6).All(ok => ok), Dump(7));
        Overfull(7);
        ed.SelectRange(7, 0, 7, 16);
        ed.Effects.DeleteBeat();
        Check("short notes: a delete ending inside a triplet leaves its other members apart", NoteOnsets(7).Count == 3 && Spaced(7), Dump(7));
        ed.ClearSelection();

        // Drum track: 32nds typed as one digit, then each retyped as two digits, then more typed directly: all back to back.
        var drumIndex = project.Tracks.FindIndex(t => t.Kind == TrackKind.Drums);
        var drums = project.Tracks[drumIndex];
        foreach (var m in drums.Measures) { m.Cells.Clear(); BarFill.FillCells(m.Cells, 16, always: true); }
        var de = new Views.TabEditorControl { Project = project, SelectedTrackIndex = drumIndex, FillBarsWithRests = true, AutoAdvanceAfterEntry = false };
        var line = drums.StringTunings.Count - 1;
        string DrumDump(int bar) => string.Join(" ", drums.Measures[bar].Cells.Select((c, i) => $"{i}:{(c.Notes.Count > 0 ? "n" + c.Notes[0].Fret : c.IsRest ? "r" : "-")}/{c.DurationDenominator}{(c.RhythmicPosition is { } p ? "@" + p : "")}"));
        List<double> DrumOnsets(int bar) { var o = BarGrid.Onsets(drums.Measures[bar].Cells); return drums.Measures[bar].Cells.Select((c, i) => (c, i)).Where(x => x.c.Notes.Count > 0).Select(x => o[x.i]).ToList(); }
        de.SetPosition(0, 0, line, false);
        for (var i = 0; i < 7; i++) { de.Effects.SetDuration(32); de.Effects.EnterFret(3); de.MoveBeat(1); }   // Right inside the two-digit window
        Check("short notes: seven 32nds typed as one digit sit back to back", DrumOnsets(0).SequenceEqual(Steps(7).Select(i => i * 0.5)), DrumDump(0));
        de.SetPosition(0, 0, line, false);
        for (var i = 0; i < 7; i++) { de.Effects.EnterFret(3); de.Effects.EnterFret(6); de.MoveBeat(1); }
        for (var i = 0; i < 5; i++) { de.Effects.SetDuration(32); de.Effects.EnterFret(3); de.Effects.EnterFret(6); de.MoveBeat(1); }
        Check("short notes: retyped and newly typed two-digit 32nds are twelve notes back to back",
            DrumOnsets(0).SequenceEqual(Steps(12).Select(i => i * 0.5)) && drums.Measures[0].Cells.Where(c => c.Notes.Count > 0).All(c => c.Notes[0].Fret == 36 && c.DurationDenominator == 32), DrumDump(0));

        // Sixteenths typed with auto-advance, each then shortened to a 32nd under the cursor and retyped: the later notes close up.
        de.AutoAdvanceAfterEntry = true;
        de.SetPosition(1, 0, line, false);
        de.Effects.SetDuration(16);
        for (var i = 0; i < 7; i++) de.Effects.EnterFret(3);
        de.SetPosition(1, 0, line, false);
        for (var i = 0; i < 7; i++) { de.Effects.SetDuration(32); de.Effects.EnterFret(3); de.Effects.EnterFret(6); de.MoveBeat(1); }
        for (var i = 0; i < 5; i++) { de.Effects.SetDuration(32); de.Effects.EnterFret(3); }
        Check("short notes: sixteenths shortened to 32nds one by one close up, later typed 32nds follow",
            DrumOnsets(1).SequenceEqual(Steps(12).Select(i => i * 0.5)), DrumDump(1));

        var sixteenthMs = 60000.0 / project.Tempo / 4;
        var played = Playback.MidiTimelineBuilder.Build(project, new Playback.PlaybackOptions()).Notes.Where(n => n.TrackIndex == trackIndex && n.Bar == 0)
            .Select(n => Math.Round(n.OnsetMs / sixteenthMs * 2) / 2).OrderBy(x => x).ToList();
        Check("short notes: the eight 32nds play one after another", played.SequenceEqual(Steps(8).Select(i => i * 0.5)), string.Join(",", played));
    }
}
