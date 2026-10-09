using System.Linq;
using System.Windows.Input;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// Owns: workflow stories of GP5 batch 5: a cut of beats inside one bar keeps the bar (even every beat of a full bar), a selection
//     that crosses a barline cuts the bars it touches with the cursor on the selection's end bar; the triplet taken off the middle note
//     of a triplet group moves the later notes up; a dead note takes no harmonic, a harmonic note keeps it when made dead.
//     Every expectation was confirmed in one quiet GP5 (single instance, a save after every action, a typed fret at the end):
//     %TEMP%/tf-b5gp b5 (b01 = j04, b02, b05, b06) and b5b (b07, b08, b09); work/gp5diff/j4b (k01, k02).
// Does not own: the commands (EditCommands.CutTakesBars, ScoreEditCommands.ToggleTriplet, OrnamentEdits.ApplyHarmonic), the selection
//     record (EditorSelectionState.IsWholeBars), the input helpers (WorkflowKit.cs).
// Tests: TestGp5Batch5Stories (--areas workflow).
public static partial class SelfTest
{
    private static void TestGp5Batch5Stories()
    {
        var w = SmNewWindow();
        var previous = DialogHost.Capture;
        DialogHost.Capture = d => true;   // a dialog (cut question, harmonic editor) takes its defaults
        try
        {
            foreach (var (name, story) in new (string, Action<MainWindow>)[]
            {
                ("K1 a cut of beats inside a bar with room left keeps the bar (b01, j04)", GbCutBeatsKeepsBar),
                ("K2 a cut of every beat of a full bar by Shift+Right keeps the bar (b07)", GbCutFullBarBeats),
                ("K3 a selection across a barline cuts its bars, the cursor on its end bar (b02, b09)", GbCutAcrossBarline),
                ("K4 a bar selection still cuts the bar (TestEssentialSelectionCopyPaste)", GbCutBarSelection),
                ("K5 triplet off on the middle note moves the later notes up (b05, b06, session 04 step 20)", GbTripletOffMiddle),
                ("K6 a dead note takes no harmonic, a harmonic note made dead keeps both (k01, k02)", GbDeadHarmonic),
            })
                SmStep($"workflow: {name} runs", () => story(w));
        }
        finally { DialogHost.Capture = previous; SmCloseWindow(w); }
    }

    private static string GbAt(Wf s) => $"cur {s.Ed.SelectedMeasure}:{s.Ed.SelectedCell}, {s.Track.Measures.Count} bars, "
        + string.Join(" | ", Enumerable.Range(0, s.Track.Measures.Count).Select(s.Dump));

    private static List<int> GbFrets(Wf s, int bar) => s.Beats(bar).Select(i => s.Cell(bar, i)).Where(c => c.Notes.Count > 0).Select(c => c.Notes[0].Fret).ToList();

    /// <summary>1 2 3 4 | 9 | - | - | 10 (with <paramref name="eight"/>: 1 2 3 4 | 9 8 | - | 10), the cursor on bar 1's first beat.</summary>
    private static Wf GbFiveBars(MainWindow w, string scenario, bool eight = false)
    {
        var s = GnOpenNew(w, scenario);
        foreach (var f in new[] { 1, 2, 3, 4, 9 }) { s.Fret(f); s.Key(Key.Right); }
        if (eight) { s.Fret(8); s.Repeat(Key.Right, 3); } else s.Repeat(Key.Right, 4);
        s.Fret(10); s.Key(Key.Home, ModifierKeys.Control); s.Key(Key.Home);
        return s;
    }

    private static void GbCutBeatsKeepsBar(MainWindow w)
    {
        var s = GnOpenNew(w, "K1 cut beats");
        s.Begin("Ctrl+Ins, 7 Right 12 Right, Home, Shift+Right x2, Ctrl+X, 5");
        s.Key(Key.Insert, ModifierKeys.Control); s.Fret(7); s.Key(Key.Right); s.Fret(12); s.Key(Key.Right);
        s.Key(Key.Home); s.Repeat(Key.Right, 2, ModifierKeys.Shift); s.Ctrl(Key.X); s.Fret(5);
        s.Expect("two bars, bar 1 holds the 5 alone", s.Track.Measures.Count == 2 && GbFrets(s, 0).SequenceEqual(new[] { 5 }) && GbFrets(s, 1).Count == 0,
            "5 | -", GbAt(s));
    }

    private static void GbCutFullBarBeats(MainWindow w)
    {
        var s = GbFiveBars(w, "K2 cut a full bar's beats");
        s.Begin("1 2 3 4 | 9 | - | - | 10, Shift+Right x3, Ctrl+X, 5");
        var bars = s.Track.Measures.Count; s.Repeat(Key.Right, 3, ModifierKeys.Shift); s.Ctrl(Key.X); s.Fret(5);
        s.Expect("every bar stays, bar 1 holds the 5", s.Track.Measures.Count == bars && GbFrets(s, 0).SequenceEqual(new[] { 5 }) && GbFrets(s, 1).SequenceEqual(new[] { 9 }),
            "5 | 9 | - | - | 10", GbAt(s));
    }

    private static void GbCutAcrossBarline(MainWindow w)
    {
        var s = GbFiveBars(w, "K3 cut across a barline");
        s.Begin("1 2 3 4 | 9 | - | - | 10, Shift+Right x4, Ctrl+X, 5");
        var bars = s.Track.Measures.Count; s.Repeat(Key.Right, 4, ModifierKeys.Shift); s.Ctrl(Key.X); s.Fret(5);
        s.Expect("bars 1 and 2 go, the 5 lands in the new bar 2", s.Track.Measures.Count == bars - 2 && GbFrets(s, 0).Count == 0
            && GbFrets(s, 1).SequenceEqual(new[] { 5 }), "- | 5 | 10", GbAt(s));
        s = GbFiveBars(w, "K3b cut across a barline, a beat after it", eight: true);
        s.Begin("1 2 3 4 | 9 8 | - | 10, Shift+Right x4, Ctrl+X, 5");
        bars = s.Track.Measures.Count; s.Repeat(Key.Right, 4, ModifierKeys.Shift); s.Ctrl(Key.X); s.Fret(5);
        s.Expect("bars 1 and 2 go (the 8 with them)", s.Track.Measures.Count == bars - 2 && GbFrets(s, 0).Count == 0 && GbFrets(s, 1).SequenceEqual(new[] { 5 }),
            "- | 5", GbAt(s));
    }

    private static void GbCutBarSelection(MainWindow w)
    {
        var s = GbFiveBars(w, "K4 cut a bar selection");
        s.Begin("1 2 3 4 | 9 | - | - | 10, select bar 1, Ctrl+X");
        var bars = s.Track.Measures.Count; s.Ed.SelectMeasureRange(0, 0); s.Settle(); s.Ctrl(Key.X);
        s.Expect("bar 1 goes", s.Track.Measures.Count == bars - 1 && GbFrets(s, 0).SequenceEqual(new[] { 9 }), "9 | - | - | 10", GbAt(s));
    }

    private static void GbTripletOffMiddle(MainWindow w)
    {
        foreach (var (id, frets) in new[] { ("b05", new[] { 5, 7, 8, 2, 5 }), ("b06", new[] { 5, 7, 8, 7, 2 }) })
        {
            var s = GnOpenNew(w, $"K5 triplet off ({id})");
            s.Begin("5 R 7 Shorter Triplet R 8 R 7" + (id == "b05" ? " R 5 L L Triplet R 2" : " L Triplet R R 2"));
            s.Fret(5); s.Key(Key.Right); s.Fret(7); s.Key(Key.Add); s.Ed.Effects.ToggleTriplet(); s.Key(Key.Right); s.Fret(8); s.Key(Key.Right); s.Fret(7);
            if (id == "b05") { s.Key(Key.Right); s.Fret(5); s.Repeat(Key.Left, 2); } else s.Key(Key.Left);
            s.Ed.Effects.ToggleTriplet();
            // Right after the toggle every beat starts where the one before it ends: no gap, no overlap (GP5 has no other timing).
            var cells = s.Bar(0).Cells;
            var onsets = BarGrid.Onsets(cells);
            var beats = s.Beats(0).Where(i => cells[i].Notes.Count > 0).ToList();
            var gapless = beats.Zip(beats.Skip(1)).All(p => Math.Abs(onsets[p.First] + MusicTime.CellSlots(cells[p.First]) - onsets[p.Second]) < 1e-6);
            var detail = s.Dump(0) + " onsets " + string.Join(" ", beats.Select(i => onsets[i].ToString("0.###")));
            if (id == "b05") s.Key(Key.Right); else s.Repeat(Key.Right, 2);
            s.Fret(2);
            cells = s.Bar(0).Cells; beats = s.Beats(0).Where(i => cells[i].Notes.Count > 0).ToList();
            detail = GbAt(s) + ", after the toggle " + detail;
            s.Expect("5/4 7/8t 8/8 then the rest, each beat right after the one before", GbFrets(s, 0).SequenceEqual(frets) && gapless
                && !cells[beats[2]].IsTriplet && cells[beats[1]].IsTriplet, id == "b05" ? "5/4 7/8t 8/8 2/8t 5/8t" : "5/4 7/8t 8/8 7/8t 2/8t", detail);
        }
    }

    private static void GbDeadHarmonic(MainWindow w)
    {
        var s = GnOpenNew(w, "K6 dead then harmonic (k01)");
        s.Begin("7 X Y Right 5");
        s.Fret(7); s.Key(Key.X); s.Key(Key.Y); s.Key(Key.Right); s.Fret(5);
        var n = s.NoteAt(0, 0, 0);
        s.Expect("the 7 is dead without a harmonic", n is { Dead: true } && OrnamentEditsHarmonic(n) < 0, "7[dead] 5", $"{GbAt(s)}, dead {n?.Dead}, {string.Join(",", n?.Techniques ?? new())}");
        s = GnOpenNew(w, "K6b harmonic then dead (k02)");
        s.Begin("7 Y X Right 5");
        s.Fret(7);   // Y then OK in the harmonic editor: a natural harmonic
        TabForge.Views.EffectEditors.OrnamentEdits.ApplyHarmonic(new[] { s.NoteAt(0, 0, 0)! }, 0, 12); s.Key(Key.X); s.Key(Key.Right); s.Fret(5);
        n = s.NoteAt(0, 0, 0);
        s.Expect("the 7 is dead and keeps its harmonic", n is { Dead: true } && OrnamentEditsHarmonic(n) >= 0, "7[dead harm] 5", $"{GbAt(s)}, dead {n?.Dead}, {string.Join(",", n?.Techniques ?? new())}");
    }

    private static int OrnamentEditsHarmonic(TabNote n) => TabForge.Views.EffectEditors.OrnamentEdits.HarmonicTypeOf(n);
}
