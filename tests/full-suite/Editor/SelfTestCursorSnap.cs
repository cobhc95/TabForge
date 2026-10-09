using System.Linq;
using System.Windows;
using System.Windows.Input;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;
using TabForge.Views.Score;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>Feature 6b: the cursor sits only on real beat starts and one append slot after the last beat; clicks and arrows follow the same positions.</summary>
    private static void TestCursorSnap()
    {
        static void Beat(MeasureModel bar, int cell, int denominator)
        {
            bar.Cells[cell].DurationDenominator = denominator;
            bar.Cells[cell].Notes.Add(new TabNote { StringIndex = 0, Fret = 3, MidiValue = 67 });
        }
        var editor = NewEditor(out _, out var track);

        // Owner's case: 4/4 bar with two half notes (cells 0 and 8). Clicks between them land on one of them; no append slot (bar full).
        var halves = track.Measures[0];
        Beat(halves, 0, 2); Beat(halves, 8, 2);
        var inside = TabEditorControl.ResolveBeatHitCell(halves, 5.0, 5);
        Check("a click between two half notes lands on one of them", inside is 0 or 8, inside.ToString());
        Check("a full bar has no append slot", CursorPositions.Allowed(halves.Cells).SequenceEqual(new[] { 0, 8 }));
        Check("a click past the end of a full bar snaps to the last beat", TabEditorControl.ResolveBeatHitCell(halves, 15.5, 15) == 8);

        // Gap between quarters, past-the-end click -> the single append slot.
        var quarters = track.Measures[1];
        Beat(quarters, 0, 4); Beat(quarters, 8, 4);
        Check("a click in the gap between quarters snaps to a beat or the append slot, never the gap",
            TabEditorControl.ResolveBeatHitCell(quarters, 6.5, 6) is 8 && TabEditorControl.ResolveBeatHitCell(quarters, 2.2, 2) is 0 or 4);
        Check("the first slot after the last beat is the append slot", CursorPositions.Allowed(quarters.Cells).SequenceEqual(new[] { 0, 8, 12 }));
        Check("a click past the append slot snaps to it", TabEditorControl.ResolveBeatHitCell(quarters, 15.5, 15) == 12);

        // The cursor box is drawn around the beat head (same x as the glyph), not over its duration span or a stale empty slot.
        {
            var bar = track.Measures[3];
            Beat(bar, 0, 4); Beat(bar, 8, 4);
            bar.Cells[0].Notes[0].Fret = 5; bar.Cells[8].Notes[0].Fret = 7;
            var warp = MeasureWarp.Build(new double[] { 0, 8 }, 16);
            foreach (var zoom in new[] { 1.0, 1.5 })
            {
                var width = 400.0 * zoom;
                foreach (var cell in new[] { 0, 1, 8 })
                {
                    var rect = ScoreRenderer.CursorRect(bar, bar.Cells, cell, 100, warp, width, 10, 16);
                    var beat = cell == 8 ? 8 : 0;
                    var head = 100 + warp.CenterFraction(beat) * width;
                    Check($"cursor box centred on the beat head (cell {cell}, zoom {zoom})", Math.Abs(rect.X + rect.Width / 2 - head) < 0.01 && rect.Width < 30, $"{rect} head {head}");
                }
            }
        }

        // Empty bar: first slot.
        Check("an empty bar has only its first slot", TabEditorControl.ResolveBeatHitCell(track.Measures[2], 9.3, 9) == 0);

        // Arrows visit exactly the allowed positions.
        editor.SetPosition(1, 0, 0);
        editor.MoveBeat(1); var a = editor.SelectedCell;
        editor.MoveBeat(1); var b = editor.SelectedCell;
        Check("the right arrow steps beat, beat, append slot", a == 8 && b == 12, $"{a},{b}");
        editor.MoveBeat(-1);
        Check("the left arrow steps back", editor.SelectedCell == 8, editor.SelectedCell.ToString());

        track.Measures.AddRange(Presets.TemplateFactory.Measures(5));

        var tied = track.Measures[4];
        for (var i = 0; i < tied.Cells.Count; i++) tied.Cells[i] = new TabCell();
        Beat(tied, 0, 8); Beat(tied, 2, 8); tied.Cells[0].Notes[0].Tied = true; tied.Cells[2].IsTied = true;
        editor.SetPosition(4, 0, 0);
        editor.MoveBeat(1); var tie = editor.SelectedCell; editor.MoveBeat(1); var tiedAppend = editor.SelectedCell;
        editor.MoveBeat(1); var empty = (editor.SelectedMeasure, editor.SelectedCell); editor.MoveBeat(-1);
        Check("Right visits a tied continuation then the append slot", tie == 2 && tiedAppend == 4, $"{tie},{tiedAppend}");
        Check("right and left cross the tied bar and empty bar boundary", empty == (5, 0) && editor.SelectedMeasure == 4 && editor.SelectedCell == 4, $"{empty} -> {editor.SelectedMeasure}:{editor.SelectedCell}");

        var triplets = track.Measures[6];
        for (var i = 0; i < triplets.Cells.Count; i++) triplets.Cells[i] = new TabCell();
        for (var i = 0; i < 3; i++) { Beat(triplets, i, 8); triplets.Cells[i].TupletNumerator = 3; triplets.Cells[i].TupletDenominator = 2; triplets.Cells[i].RhythmicPosition = i * MusicTime.CellSlots(triplets.Cells[i]); }
        Check("fractional triplet onsets are the cursor stops plus one append slot",
            CursorPositions.Allowed(triplets.Cells).SequenceEqual(new[] { 0, 1, 2, 4 }));
        editor.SetPosition(6, 0, 0);
        editor.MoveBeat(1); var tripletSecond = editor.SelectedCell; editor.MoveBeat(1); var tripletThird = editor.SelectedCell;
        editor.MoveBeat(1); var tripletAppend = editor.SelectedCell;
        Check("Right steps across all triplet starts and then the append slot",
            tripletSecond == 1 && tripletThird == 2 && tripletAppend == 4,
            $"{tripletSecond},{tripletThird},{tripletAppend}");
        Check("a click on a fractional triplet onset resolves to its beat cell",
            TabEditorControl.ResolveBeatHitCell(triplets, triplets.Cells[1].RhythmicPosition!.Value, 1) == 1);

        var fullTriplets = track.Measures[7];
        for (var i = 0; i < fullTriplets.Cells.Count; i++) fullTriplets.Cells[i] = new TabCell();
        for (var i = 0; i < 12; i++) { Beat(fullTriplets, i, 8); fullTriplets.Cells[i].TupletNumerator = 3; fullTriplets.Cells[i].TupletDenominator = 2; fullTriplets.Cells[i].RhythmicPosition = i * MusicTime.CellSlots(fullTriplets.Cells[i]); }
        Check("a full bar of eighth-triplets has no append slot",
            CursorPositions.Allowed(fullTriplets.Cells).SequenceEqual(Enumerable.Range(0, 12)));
        editor.SetPosition(7, 11, 0);
        editor.MoveBeat(1);
        Check("Right at a full tuplet bar's last beat crosses to the next bar", editor.SelectedMeasure == 8 && editor.SelectedCell == 0, $"{editor.SelectedMeasure}:{editor.SelectedCell}");
        editor.MoveBeat(-1); Check("Left returns from the empty bar to the full triplet bar's last beat", editor.SelectedMeasure == 7 && editor.SelectedCell == 11, $"{editor.SelectedMeasure}:{editor.SelectedCell}");
    }

    /// <summary>Note-editing audit: fret entry, durations, marks, string moves, cut and keyboard navigation keep bars complete, keep notes and use one undo step.</summary>
    private static void TestNoteEditAudit()
    {
        static TabCell Rest(int d) => new() { DurationDenominator = d, IsRest = true };
        static TabCell Note(int d, int s, int fret) => new() { DurationDenominator = d, Notes = { new TabNote { StringIndex = s, Fret = fret, MidiValue = 40 + fret } } };
        static string Dump(List<TabCell> bar) => string.Join(" ", bar.Select((c, i) => (c, i)).Where(x => x.c.Notes.Count > 0 || x.c.IsRest).Select(x => $"{x.i}:{(x.c.IsRest ? "r" : string.Join("+", x.c.Notes.Select(n => n.StringIndex + "/" + n.Fret)))}/{x.c.DurationDenominator}{new string('.', x.c.Dots)}{(x.c.IsTriplet ? "t" : "")}"));
        (TabEditorControl E, SongProject P, List<TabCell> Bar, Func<int> Steps) Make(Action<List<TabCell>>? fill = null)
        {
            var project = Presets.TemplateFactory.Create("Rock Band");
            var e = new TabEditorControl { Project = project, SelectedTrackIndex = 0, FillBarsWithRests = true, AutoAdvanceAfterEntry = false };
            e.Measure(new Size(1200, 800)); e.Arrange(new Rect(0, 0, 1200, 800));
            var steps = 0; e.EditStarting += (_, _) => steps++;
            var bar = project.Tracks[0].Measures[0].Cells;
            for (var i = 0; i < 16; i++) bar[i] = new TabCell();
            if (fill is null) { for (var i = 0; i < 4; i++) bar[i * 4] = Note(4, 1, 3 + i); } else fill(bar);
            return (e, project, bar, () => steps);
        }

        // Fret entry on every string, two digits.
        var (e1, p1, b1, _) = Make(bar => bar[0] = Rest(1));
        for (var s = 0; s < 6; s++) { e1.SetPosition(0, 0, s, false); e1.Effects.EnterFret(7, false); }
        Check("audit: a chord typed on six strings keeps six notes and a complete bar", b1[0].Notes.Count == 6 && !b1[0].IsRest && !MusicTime.AnalyzeBar(p1, 0).Marked, Dump(b1));
        e1.SetPosition(0, 0, 2, false);
        e1.Effects.EnterFret(1, false); e1.Effects.EnterFret(2, false);
        Check("audit: typing 1 then 2 gives fret 12", b1[0].Notes.First(n => n.StringIndex == 2).Fret == 12, Dump(b1));
        Check("audit: a typed note's pitch follows the string tuning", b1[0].Notes.First(n => n.StringIndex == 2).MidiValue == EditCommands.NoteMidi(p1.Tracks[0], 2, 12));

        // Duration changes.
        foreach (var (name, act) in new (string, Action<TabEditorControl>)[] {
            ("eighth", e => e.Effects.SetDuration(8)), ("half", e => e.Effects.SetDuration(2)), ("whole", e => e.Effects.SetDuration(1)),
            ("dot", e => e.Effects.ToggleDot()), ("triplet", e => e.Effects.ToggleTriplet()), ("longer", e => e.Effects.Longer()), ("shorter", e => e.Effects.Shorter()) })
        {
            var (e, p, b, st) = Make();
            e.SetPosition(0, 4, 1, false);
            act(e);
            Check($"audit: {name} on the second quarter note drops no note", b.Sum(c => c.Notes.Count) == 4, Dump(b));
            Check($"audit: {name} on the second quarter note is one undo step", st() == 1, $"{st()}");
            Check($"audit: {name} on the second quarter note never leaves the bar silently short", !MusicTime.AnalyzeBar(p, 0).Marked || MusicTime.AnalyzeBar(p, 0).Complete is false && name is "whole" or "half" or "dot" or "longer" or "triplet", Dump(b));
        }
        var (e2, p2, b2, _) = Make();
        e2.SetPosition(0, 4, 1, false); e2.Effects.SetDuration(8);
        Check("audit: shortening a quarter with the rest fill on keeps the bar complete", !MusicTime.AnalyzeBar(p2, 0).Marked && b2.Count(c => c.IsRest) >= 1, Dump(b2));

        // Insert / delete beat.
        var (e3, _, b3, st3) = Make();
        e3.SetPosition(0, 4, 1, false); e3.Effects.InsertBeat();
        Check("audit: Insert beat drops no note, one undo step", b3.Sum(c => c.Notes.Count) >= 3 && st3() == 1, Dump(b3));
        var (e4, p4, b4, st4) = Make();
        e4.SetPosition(0, 4, 1, false); e4.Effects.DeleteBeats();
        Check("audit: Delete beats keeps the other notes, bar complete, one undo step", b4.Sum(c => c.Notes.Count) == 3 && st4() == 1 && !MusicTime.AnalyzeBar(p4, 0).Marked, Dump(b4));

        // Marks on a single note and on a selection.
        foreach (var tech in new[] { TechniqueNames.Hopo, TechniqueNames.LegatoSlide, TechniqueNames.Bend, TechniqueNames.Vibrato, TechniqueNames.PalmMute, TechniqueNames.LetRing, TechniqueNames.Harmonic })
        {
            var (e, _, b, st) = Make();
            e.SetPosition(0, 4, 1, false);
            e.Effects.ToggleTechnique(tech);
            var on = b[4].Notes[0].Techniques.Contains(tech) && b[0].Notes[0].Techniques.Count == 0;
            e.Effects.ToggleTechnique(tech);
            Check($"audit: {tech} toggles on and off on a single note, one step each", on && b[4].Notes[0].Techniques.Count == 0 && st() == 2, $"{st()} {on}");
            e.SelectRange(0, 0, 0, 8);
            e.Effects.ToggleTechnique(tech);
            var all = b[0].Notes[0].Techniques.Contains(tech) && b[4].Notes[0].Techniques.Contains(tech);
            e.Effects.ToggleTechnique(tech);
            Check($"audit: {tech} toggles on and off over a selection, one step each", all && b[0].Notes[0].Techniques.Count == 0 && st() == 4, $"{st()} {all}");
        }
        var (e5, _, b5, st5) = Make();
        e5.SetPosition(0, 4, 1, false); e5.Effects.ToggleDead();
        var dead = b5[4].Notes[0].Dead; e5.Effects.ToggleDead();
        Check("audit: dead note toggles on a single note", dead && !b5[4].Notes[0].Dead && st5() == 2);
        e5.SetPosition(0, 4, 1, false); e5.Effects.ToggleTie();
        var tied = b5[4].IsTied || b5[4].Notes[0].Tied;
        e5.Effects.ToggleTie();
        // As GP5, L on a tied note removes that note (the beat empties when it was the only one).
        Check("audit: tie toggles on and off on a single beat", tied && !b5[4].IsTied && b5[4].Notes.All(n => !n.Tied), $"{tied} {b5[4].IsTied} notes {b5[4].Notes.Count}");

        // Move to the adjacent string.
        var (e6, p6, b6, st6) = Make();
        e6.SetPosition(0, 4, 1, false);
        var moved = e6.Effects.MoveNotesToAdjacentString(1);
        var n6 = b6[4].Notes[0];
        Check("audit: moving a note to the lower string keeps the pitch, one step", moved && n6.StringIndex == 2 && st6() == 1 && EditCommands.NoteMidi(p6.Tracks[0], n6.StringIndex, n6.Fret) == n6.MidiValue, $"{n6.StringIndex}/{n6.Fret} midi {n6.MidiValue}");

        // Delete a note: a rest of its length, bar complete.
        var (e7, p7, b7, st7) = Make();
        e7.SetPosition(0, 4, 1, false); e7.Effects.DeleteNote();
        Check("audit: Backspace on the only note leaves a rest of its length, bar complete", b7[4].IsRest && b7[4].DurationDenominator == 4 && !MusicTime.AnalyzeBar(p7, 0).Marked && st7() == 1, Dump(b7));

        // Cut a range.
        var (e8, p8, b8, st8) = Make();
        e8.SelectRange(0, 4, 0, 8);
        var clip = e8.CaptureClip(out _);
        var cut = clip is not null && e8.CutSelection(clip);
        Check("audit: cutting two beats takes them out (as GP5), the last note moves up, bar complete, one step", cut && b8[0].Notes.Count == 1 && b8[4].Notes.Count == 1 && b8.Sum(c => c.Notes.Count) == 2 && st8() == 1 && !MusicTime.AnalyzeBar(p8, 0).Marked, Dump(b8));

        // Navigation.
        var (e9, p9, _, _) = Make();
        var tr = p9.Tracks[0];
        var bar1 = tr.Measures[1].Cells; for (var i = 0; i < 16; i++) bar1[i] = new TabCell(); bar1[0] = Note(8, 1, 1); bar1[2] = Note(8, 1, 2);
        e9.SetPosition(0, 12, 1, false);
        e9.TryHandleKey(Key.Right, ModifierKeys.None);
        Check("audit: Right from the last beat goes to the first beat of the next bar", e9.SelectedMeasure == 1 && e9.SelectedCell == 0, $"{e9.SelectedMeasure}:{e9.SelectedCell}");
        e9.TryHandleKey(Key.End, ModifierKeys.None);
        // As GP5 (CursorPositions.EndTarget): End goes to the bar's last written beat, not the empty spot after it.
        Check("audit: End in a half-empty bar goes to the last written beat", e9.SelectedCell == 2, $"{e9.SelectedCell}");
        e9.SetPosition(0, 8, 1, false);
        e9.TryHandleKey(Key.Right, ModifierKeys.Control);
        Check("audit: Ctrl+Right lands on an allowed cursor cell of the next bar", e9.SelectedMeasure == 1 && Views.Score.CursorPositions.Allowed(tr.Measures[1].Cells).Contains(e9.SelectedCell), $"{e9.SelectedMeasure}:{e9.SelectedCell}");
        e9.SetPosition(1, 0, 1, false);
        e9.TryHandleKey(Key.Left, ModifierKeys.None);
        Check("audit: Left from the first beat goes to the last cursor cell of the previous bar", e9.SelectedMeasure == 0 && e9.SelectedCell == 12, $"{e9.SelectedMeasure}:{e9.SelectedCell}");
        e9.SetPosition(0, 0, 1, false);
        e9.TryHandleKey(Key.Right, ModifierKeys.Shift);
        var rg = e9.SelectionCellRange;
        Check("audit: Shift+Right extends the selection by one whole beat", e9.HasSelection && rg.StartCell == 0 && rg.EndCell == 4 && rg.EndMeasure == 0, $"{rg}");

        // A bar with a gap before its first beat: bar-jumping keys land on a beat, never inside the gap.
        var (eg, pg, _, _) = Make();
        var gap = pg.Tracks[0].Measures[1].Cells; for (var i = 0; i < 16; i++) gap[i] = new TabCell(); gap[8] = Note(2, 1, 4);
        eg.SetPosition(0, 4, 1, false); eg.MoveBar(1);
        Check("audit: Ctrl+Right into a bar whose first beat starts later lands on that beat", eg.SelectedMeasure == 1 && eg.SelectedCell == 8, $"{eg.SelectedMeasure}:{eg.SelectedCell}");
        eg.MoveBar(-1); eg.MoveToBarStart();
        Check("audit: Home goes to the first beat of the bar", eg.SelectedCell == 0, $"{eg.SelectedCell}");
        eg.SetPosition(1, 0, 1, false); eg.MoveToBarStart();
        Check("audit: Home in a bar with a leading gap goes to the first beat", eg.SelectedCell == 8, $"{eg.SelectedCell}");

        // Empty bars in the middle: Right steps through them one cursor cell each.
        var (ee, pe, _, _) = Make();
        var empty = pe.Tracks[0].Measures[1].Cells; for (var i = 0; i < 16; i++) empty[i] = new TabCell();
        ee.SetPosition(0, 12, 1, false); ee.MoveBeat(1);
        var at1 = (ee.SelectedMeasure, ee.SelectedCell); ee.MoveBeat(1); var at2 = (ee.SelectedMeasure, ee.SelectedCell); ee.MoveBeat(-1);
        Check("audit: Right/Left step into and across an empty bar", at1 == (1, 0) && at2.SelectedMeasure == 2 && ee.SelectedMeasure == 1 && ee.SelectedCell == 0, $"{at1} {at2}");

        // Selection-wide duration and triplet: notes kept, bar complete, one step.
        var (ew, pw, bw, stw) = Make();
        ew.SelectRange(0, 0, 0, 4); ew.Effects.SetDuration(8);
        Check("audit: eighth over a two-beat selection keeps the notes, bar complete, one step", bw.Sum(c => c.Notes.Count) == 4 && !MusicTime.AnalyzeBar(pw, 0).Marked && stw() == 1, Dump(bw));
        var (ev, pv, bv, stv) = Make();
        ev.SelectRange(0, 0, 0, 4); ev.Effects.ToggleTriplet();
        Check("audit: triplet over a two-beat selection keeps the notes and takes one step", bv.Sum(c => c.Notes.Count) == 4 && stv() == 1 && bv[0].IsTriplet && bv[4].IsTriplet, Dump(bv));

        // Drum and bass tracks.
        var tc = new Controllers.TrackController();
        foreach (var kind in new[] { TrackKind.Bass, TrackKind.Drums })
        {
            var pr = new SongProject(); var tk = tc.CreateTrack(pr, kind); pr.Tracks.Add(tk);
            var ed = new TabEditorControl { Project = pr, SelectedTrackIndex = 0, FillBarsWithRests = true, AutoAdvanceAfterEntry = false };
            ed.Measure(new Size(1200, 800)); ed.Arrange(new Rect(0, 0, 1200, 800));
            ed.SetPosition(0, 0, 1, false);
            ed.Effects.EnterFret(5, false);
            var cl = tk.Measures[0].Cells;
            Check($"audit: {kind} track: a typed number writes a note and the bar stays complete", cl[0].Notes.Count == 1 && !MusicTime.AnalyzeBar(pr, 0).Marked, Dump(cl));
            ed.Effects.SetDuration(8);
            Check($"audit: {kind} track: eighth keeps the note and the bar complete", cl[0].Notes.Count == 1 && !MusicTime.AnalyzeBar(pr, 0).Marked, Dump(cl));
        }
    }
}
