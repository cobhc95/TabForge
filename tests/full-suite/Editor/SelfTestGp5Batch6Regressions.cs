using System.Linq;
using System.Windows.Input;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// Owns: workflow stories of GP5 batch 6 (regressions of batches 4 and 5): a fret past the end of an overfull triplet bar stays there,
//     a triplet that does not fit the rest of the bar is still written in it, End on the empty spot stays, Shift+Right goes onto the
//     empty spot and from there into the next bar, Undo of a pending dot keeps the earlier Shorter, and Undo after a triplet-off leaves
//     no overfull mark or stray tuplet number. Every expectation was confirmed in one quiet GP5 (single instance, a save after every
//     action, a typed fret at the end): work/gp5diff/verify3/micro6 (x07, x09-x13), %TEMP%/tf-k6gp/k6 (k01-k03), work/gp5diff/b5runs (b01, b03, b08),
//     work/gp5diff/i4micro3 (i19-i21); session 04 step 23 (work/sessions/verify/trace4).
// Does not own: the cursor rules (Views/Score/CursorPositions.cs), the selection record (EditorSelectionState), the writing marks
//     (ScoreEditCommands.Effects), the input helpers (WorkflowKit.cs).
// Tests: TestGp5Batch6RegressionStories (--areas workflow).
public static partial class SelfTest
{
    private static void TestGp5Batch6RegressionStories()
    {
        var w = SmNewWindow();
        var previous = DialogHost.Capture;
        DialogHost.Capture = d => true;   // a dialog (paste or cut question) takes its defaults
        try
        {
            foreach (var (name, story) in new (string, Action<MainWindow>)[]
            {
                ("B1 a fret past the end of an overfull triplet bar stays there (x13, x14)", G6OverfullTripletFret),
                ("B2 a triplet that does not fit the rest of the bar is written in it (x12)", G6TripletKeepsBar),
                ("B3 End on the empty spot stays there (x09, x11)", G6EndOnEmptySpot),
                ("B4 Shift+Right goes onto the empty spot, then into the next bar (x10, b01, b03, b08, i20, i21)", G6ShiftRightEmptySpot),
                ("B5 Undo of a pending dot keeps the earlier Shorter (x07)", G6UndoDotKeepsShorter),
                ("B6 Undo after a triplet-off: no overfull mark, no stray tuplet number (session 04 step 23)", G6UndoTripletOff),
            })
                SmStep($"workflow: {name} runs", () => story(w));
        }
        finally { DialogHost.Capture = previous; SmCloseWindow(w); }
    }

    private static List<int> G6Frets(Wf s) => Enumerable.Range(0, s.Track.Measures.Count).SelectMany(b => GbFrets(s, b)).ToList();

    private static void G6OverfullTripletFret(MainWindow w)
    {
        var s = GnOpenNew(w, "B1 overfull triplet fret");
        s.Begin("Triplet, 1 Right Longer 2 Right 3 Right 4 Right 12");   // GP5 (x13): 1 2 3 4 12
        s.Ed.Effects.ToggleTriplet(); s.Fret(1); s.Key(Key.Right); s.Key(Key.Subtract);
        foreach (var f in new[] { 2, 3, 4 }) { s.Fret(f); s.Key(Key.Right); }
        s.Fret(12);
        s.Expect("1 2 3 4 12", G6Frets(s).SequenceEqual(new[] { 1, 2, 3, 4, 12 }), "1 2 3 4 12", GbAt(s));
        s = GnOpenNew(w, "B1b overfull triplet, two more");
        s.Begin("Triplet, 1 Right Longer 2 Right 3 Right 4 Right 1 Right 12");   // GP5 (x14): 1 2 3 4 1 12
        s.Ed.Effects.ToggleTriplet(); s.Fret(1); s.Key(Key.Right); s.Key(Key.Subtract);
        foreach (var f in new[] { 2, 3, 4, 1 }) { s.Fret(f); s.Key(Key.Right); }
        s.Fret(12);
        s.Expect("1 2 3 4 1 12", G6Frets(s).SequenceEqual(new[] { 1, 2, 3, 4, 1, 12 }), "1 2 3 4 1 12", GbAt(s));
    }

    private static void G6TripletKeepsBar(MainWindow w)
    {
        var s = GnOpenNew(w, "B2 triplet keeps bar");
        s.Begin("0 Right Triplet 15 Right 7 Right 3 Right 14 Right 5");   // GP5 (x12): every beat in bar 1
        s.Fret(0); s.Key(Key.Right); s.Ed.Effects.ToggleTriplet();
        foreach (var f in new[] { 15, 7, 3, 14 }) { s.Fret(f); s.Key(Key.Right); }
        s.Fret(5);
        s.Expect("0 15 7 3 14 5 in bar 1", GbFrets(s, 0).SequenceEqual(new[] { 0, 15, 7, 3, 14, 5 }), "0 15t 7t 3t 14t 5t", GbAt(s));
    }

    private static void G6EndOnEmptySpot(MainWindow w)
    {
        var s = GnOpenNew(w, "B3 end on empty spot");
        s.Begin("2 Home Shift+Right Ctrl+C Right End End Right Ctrl+V 9");   // GP5 (x09): 2 | 9
        s.Fret(2); s.Key(Key.Home); s.Key(Key.Right, ModifierKeys.Shift); s.Ctrl(Key.C); s.Key(Key.Right); s.Key(Key.End); s.Key(Key.End);
        s.Key(Key.Right); s.Ctrl(Key.V); s.Fret(9);
        s.Expect("2 | 9", s.Track.Measures.Count == 2 && GbFrets(s, 0).SequenceEqual(new[] { 2 }) && GbFrets(s, 1).SequenceEqual(new[] { 9 }), "2 | 9", GbAt(s));
        s = GnOpenNew(w, "B3b end then next bar");
        s.Begin("5 Right 3 Right 3 Right Left Shift+Right Ctrl+C Right End Right Ctrl+Right Ctrl+V 9");   // GP5 (x11): 5 3 3 | - | 9
        foreach (var f in new[] { 5, 3, 3 }) { s.Fret(f); s.Key(Key.Right); }
        s.Key(Key.Left); s.Key(Key.Right, ModifierKeys.Shift); s.Ctrl(Key.C); s.Key(Key.Right); s.Key(Key.End); s.Key(Key.Right); s.Ctrl(Key.Right);
        s.Ctrl(Key.V); s.Fret(9);
        s.Expect("5 3 3 | - | 9", s.Track.Measures.Count == 3 && GbFrets(s, 0).SequenceEqual(new[] { 5, 3, 3 }) && GbFrets(s, 1).Count == 0
            && GbFrets(s, 2).FirstOrDefault(-1) == 9, "5 3 3 | - | 9", GbAt(s));
    }

    private static void G6ShiftRightEmptySpot(MainWindow w)
    {
        var s = GnOpenNew(w, "B4 shift right into empty bar");
        s.Begin("Ctrl+Ins 2 Home Shift+Right Shift+Right Ctrl+X 9");   // GP5 (x10): one bar, 9
        s.Key(Key.Insert, ModifierKeys.Control); s.Fret(2); s.Key(Key.Home); s.Repeat(Key.Right, 2, ModifierKeys.Shift); s.Ctrl(Key.X); s.Fret(9);
        s.Expect("one bar: 9", s.Track.Measures.Count == 1 && GbFrets(s, 0).SequenceEqual(new[] { 9 }), "9", GbAt(s));
        foreach (var (id, back) in new[] { ("k01", false), ("k02", true) })
        {
            s = GnOpenNew(w, $"B4 {id} onto the empty spot keeps the bar");
            s.Begin(back ? "Ctrl+Ins 2 Home Shift+Right Shift+Right Shift+Left Ctrl+X 9" : "Ctrl+Ins 5 Home Shift+Right Ctrl+X 9");   // GP5 (k6 k01, k02): 9 | -
            s.Key(Key.Insert, ModifierKeys.Control); s.Fret(back ? 2 : 5); s.Key(Key.Home); s.Repeat(Key.Right, back ? 2 : 1, ModifierKeys.Shift);
            if (back) s.Key(Key.Left, ModifierKeys.Shift);
            s.Ctrl(Key.X); s.Fret(9);
            s.Expect("9 | -", s.Track.Measures.Count == 2 && GbFrets(s, 0).SequenceEqual(new[] { 9 }) && GbFrets(s, 1).Count == 0, "9 | -", GbAt(s));
        }
        s = GnOpenNew(w, "B4b two empty bars");
        s.Begin("Ctrl+Ins Ctrl+Ins 7 Right 12 Home Shift+Right x3 Ctrl+X 5");   // GP5 (b08): one bar, 5
        s.Key(Key.Insert, ModifierKeys.Control); s.Key(Key.Insert, ModifierKeys.Control); s.Fret(7); s.Key(Key.Right); s.Fret(12); s.Key(Key.Home);
        s.Repeat(Key.Right, 3, ModifierKeys.Shift); s.Ctrl(Key.X); s.Fret(5);
        s.Expect("one bar: 5", s.Track.Measures.Count == 1 && GbFrets(s, 0).SequenceEqual(new[] { 5 }), "5", GbAt(s));
        s = GbFiveBars(w, "B4c onto the empty spot after a lone note");
        s.Begin("1 2 3 4 | 9 | - | - | 10, Shift+Right x5, Ctrl+X, 5");   // GP5 (b03): - | 5 | 10
        var bars = s.Track.Measures.Count; s.Repeat(Key.Right, 5, ModifierKeys.Shift);
        var range = $"selection {s.Ed.SelectionCellRange}, whole bars {s.Ed.SelectionIsWholeBars}, before the cut {GbAt(s)}";
        s.Ctrl(Key.X); s.Fret(5);
        s.Expect("bars 1 and 2 go, the 5 in the new bar 2", s.Track.Measures.Count == bars - 2 && GbFrets(s, 0).Count == 0 && GbFrets(s, 1).SequenceEqual(new[] { 5 }),
            "- | 5 | 10", GbAt(s) + "; " + range);
        s = GnOpenNew(w, "B4d right after a selection onto the empty spot");
        s.Begin("R Left Shift+Right Right 9");   // GP5 (i21): r 9
        s.Key(Key.R); s.Key(Key.Left); s.Key(Key.Right, ModifierKeys.Shift); s.Key(Key.Right); s.Fret(9);
        s.Expect("r 9 in bar 1", s.Track.Measures.Count == 1 && s.Cell(0, 0).IsRest && GbFrets(s, 0).SequenceEqual(new[] { 9 }), "r 9", GbAt(s));
        s = GnOpenNew(w, "B4e right after a two-beat selection");
        s.Begin("5 R 7 R 8 Home Shift+Right Shift+Right Right 0");   // GP5 (i20): 5 7 8 0
        foreach (var f in new[] { 5, 7 }) { s.Fret(f); s.Key(Key.Right); }
        s.Fret(8); s.Key(Key.Home); s.Repeat(Key.Right, 2, ModifierKeys.Shift); s.Key(Key.Right); s.Fret(0);
        s.Expect("5 7 8 0", GbFrets(s, 0).SequenceEqual(new[] { 5, 7, 8, 0 }), "5 7 8 0", GbAt(s));
    }

    private static void G6UndoDotKeepsShorter(MainWindow w)
    {
        var s = GnOpenNew(w, "B5 undo dot keeps shorter");
        s.Begin("Shorter Dot Undo 3");   // GP5 (x07): 3/8
        s.Key(Key.Add); s.Key(Key.OemPeriod); s.Ctrl(Key.Z); s.Fret(3);
        var c = s.Cell(0, 0);
        s.Expect("an eighth, no dot", c.Notes.FirstOrDefault()?.Fret == 3 && c.DurationDenominator == 8 && c.Dots == 0, "3/8", $"{GbAt(s)}, 1/{c.DurationDenominator} dots {c.Dots}");
    }

    private static void G6UndoTripletOff(MainWindow w)
    {
        var s = GnOpenNew(w, "B6 undo triplet off");
        s.Begin("session 04: 5 Right 7 Shorter Triplet Right 8 Right 7 Right 5 Left Triplet Left Triplet Dot Undo Undo");
        s.Fret(5); s.Key(Key.Right); s.Fret(7); s.Key(Key.Add); s.Ed.Effects.ToggleTriplet(); s.Key(Key.Right); s.Fret(8); s.Key(Key.Right); s.Fret(7);
        s.Key(Key.Right); s.Fret(5); s.Key(Key.Left); s.Ed.Effects.ToggleTriplet(); s.Key(Key.Left);
        var before = G6Bar(s);
        s.Ed.Effects.ToggleTriplet(); s.Key(Key.OemPeriod); s.Ctrl(Key.Z); s.Ctrl(Key.Z);
        var state = s.State(0);
        var cells = s.Bar(0).Cells;
        var beats = s.Beats(0).Where(i => cells[i].Notes.Count > 0).ToList();
        var plainInTuplet = beats.Select(i => cells[i]).Select(c => c.IsTriplet).SequenceEqual(new[] { false, true, true, false, true });
        s.Expect("5/4 7/8t 8/8t 7/8 5/8t, the bar not marked overfull", !state.Error && plainInTuplet && G6Bar(s) == before,
            "5/4 7/8t 8/8t 7/8 5/8t (incomplete 2.5/4)", $"{GbAt(s)}, state {state}, now {G6Bar(s)}, before {before}");
    }

    // The bar's beats with their written positions, to compare a restored bar with the one before the edits.
    private static string G6Bar(Wf s) => string.Join(" ", s.Bar(0).Cells.Select((c, i) => (c, i)).Where(p => CursorPositionsIsBeat(p.c))
        .Select(p => $"{p.i}:{p.c.Notes.FirstOrDefault()?.Fret}/{p.c.DurationDenominator}{(p.c.IsTriplet ? "t" : "")}{(p.c.Dots > 0 ? "." : "")}@{p.c.RhythmicPosition?.ToString("0.###") ?? "-"}"));

    private static bool CursorPositionsIsBeat(TabCell c) => c.Notes.Count > 0 || c.IsRest || c.HasAnnotation;
}
