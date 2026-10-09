using System.Linq;
using System.Windows.Input;
using TabForge.Views;

namespace TabForge;

// Owns: workflow stories of the GP5 harness's entry-state, duration and undo clusters (work/gp5diff VERIFY_REPORT 2.1, 2.7, 2.12,
//     sessions 04 / 15, the masked set): dot and triplet on an empty spot set the writing duration, a new beat after Right takes the
//     previous beat's length, an inserted bar writes the last beat's length (a quarter in a new song), undo / redo of a long phrase stays
//     valid, semitone down on fret 0, undo after Backspace in an empty bar.
// Does not own: the commands (Views/Score/ScoreEditCommands*.cs), the input helpers (WorkflowKit.cs).
// Tests: TestGp5EntryStories (--areas workflow).
public static partial class SelfTest
{
    private static void TestGp5EntryStories()
    {
        var w = SmNewWindow();
        try
        {
            foreach (var (name, story) in new (string, Action<MainWindow>)[]
            {
                ("E1 dot / triplet on an empty spot set the writing duration (2.1)", GeDotTripletOnEmpty),
                ("E3 undo / redo of a long phrase with Longer and Dead stays valid (2.12, s004837)", GeUndoRedoLongPhrase),
                ("E4 a note in an inserted bar takes the last beat before it, else a quarter (2.7, s001075)", GeInsertedBarQuarter),
                ("E5 a new beat after Right takes the previous beat's length: a triplet stays a triplet (session 04)", GeTripletCarries),
                ("E7 semitone down on fret 0 leaves the note (q020888)", GeSemitoneDownOnZero),
                ("E9 undo after Backspace in an empty bar 2 keeps the bar (q020459)", GeUndoBackspaceKeepsBar),
            })
                SmStep($"workflow: {name} runs", () => story(w));
        }
        finally { SmCloseWindow(w); }
    }

    private static string GeBeat(Wf s, int bar, int cell)
    {
        var c = s.Cell(bar, cell);
        return $"{(c.IsRest ? "r" : string.Join(",", c.Notes.Select(n => n.Fret)))}/{c.DurationDenominator}{new string('.', c.Dots)}{(c.IsTriplet || c.TupletNumerator > 0 ? "t" : "")}";
    }

    private static void GeDotTripletOnEmpty(MainWindow w)
    {
        var s = WfOpen(w, "E1 dot on empty");
        s.Begin(". 9");
        s.Key(Key.OemPeriod); s.Fret(9);
        s.Expect("a dotted quarter 9", s.Cell(0, 0).Dots == 1 && s.Cell(0, 0).DurationDenominator == 4, "9/4.", GeBeat(s, 0, 0));
        var t = WfOpen(w, "E1b triplet on empty");
        t.Begin("triplet 5");
        t.Ed.Effects.ToggleTriplet(); t.Fret(5);
        t.Expect("a triplet quarter 5", t.Cell(0, 0).IsTriplet && t.Cell(0, 0).DurationDenominator == 4, "5/4t", GeBeat(t, 0, 0));
        var u = WfOpen(w, "E1c dot twice");
        u.Begin(". . 9");
        u.Key(Key.OemPeriod); u.Key(Key.OemPeriod); u.Fret(9);
        u.Expect("a plain quarter 9", u.Cell(0, 0).Dots == 0, "9/4", GeBeat(u, 0, 0));
        var v = WfOpen(w, "E1d dot undo");
        v.Begin(". Ctrl+Z 1");
        v.Key(Key.OemPeriod); v.Ctrl(Key.Z); v.Fret(1);
        v.Expect("a plain quarter 1 (no stale dot)", v.Cell(0, 0).Dots == 0 && v.Cell(0, 0).DurationDenominator == 4, "1/4", GeBeat(v, 0, 0));
        var a = WfOpen(w, "E1e accent on empty");
        a.Begin("accent 5");
        a.Ed.Effects.CycleAccent(); a.Fret(5);
        a.Expect("a plain 5", a.Cell(0, 0).Accent == 0, "5", GeBeat(a, 0, 0));
    }

    private static void GeUndoRedoLongPhrase(MainWindow w)
    {
        var s = WfOpen(w, "E3 undo redo phrase");
        s.Begin("f5 Right f2 Right Left Longer Right f7 ... Dead ... f0 Right f0 Right Undo Redo");
        var over = new List<string>();
        void Watch(string what) { var counts = s.Track.Measures.Select(m => m.Cells.Count).ToList(); if (over.Count == 0 && counts.Any(c => c > TabForge.Services.InputLimits.MaxCellsPerMeasure)) over.Add($"{what}: {string.Join(",", counts)}"); }
        void K(Key key, string what) { s.Key(key); Watch(what); }
        void F(int fret) { s.Fret(fret); Watch($"f{fret}"); K(Key.Right, $"f{fret} Right"); }
        F(5); F(2); K(Key.Left, "Left"); K(Key.Subtract, "Longer"); K(Key.Right, "Right"); F(7); F(2); F(15); K(Key.Subtract, "Longer");
        foreach (var fret in new[] { 17, 15, 3, 2, 15, 17, 3, 12, 7 }) F(fret);
        K(Key.X, "Dead"); K(Key.Right, "Dead Right");
        foreach (var fret in new[] { 7, 9, 0, 0 }) F(fret);
        s.Expect("no bar holds more cells than a bar may", over.Count == 0, "under the limit", string.Join("; ", over));
        string? error = null;
        try { s.Ctrl(Key.Z); s.Ctrl(Key.Y); TabForge.Services.ProjectValidator.Validate(s.Song); }
        catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; }
        s.Expect("undo and redo do not throw and the song stays valid", error is null, "no error", error);
    }

    private static void GeInsertedBarQuarter(MainWindow w)
    {
        var s = WfOpen(w, "E4 inserted bar");
        s.Begin("Longer, Ctrl+Insert, 12");
        s.Key(Key.Subtract); s.Key(Key.Insert, ModifierKeys.Control); s.Fret(12);
        var bar = s.Ed.SelectedMeasure;
        s.Expect("a quarter 12", s.Cell(bar, 0).DurationDenominator == 4 && s.Cell(bar, 0).Dots == 0, "12/4", GeBeat(s, bar, 0));
        var t = WfOpen(w, "E4b inserted bar shorter");
        t.Begin("Shorter, Ctrl+Insert, 2");
        t.Key(Key.Add); t.Key(Key.Insert, ModifierKeys.Control); t.Fret(2);
        t.Expect("a quarter 2", t.Cell(t.Ed.SelectedMeasure, 0).DurationDenominator == 4, "2/4", GeBeat(t, t.Ed.SelectedMeasure, 0));
    }

    private static void GeTripletCarries(MainWindow w)
    {
        var s = WfOpen(w, "E5 triplet carries");
        s.Begin("session 04: 5 . . : : 3 3 Right 7 Shorter 3 Right 8 Right 7 Right 5 Left 3 Left 3 . Undo Undo Redo Right Right Right 3 .");
        var e = s.Ed.Effects;
        s.Fret(5); s.Key(Key.OemPeriod); s.Key(Key.OemPeriod); e.SetDots(2); e.SetDots(2); e.ToggleTriplet(); e.ToggleTriplet();
        s.Key(Key.Right); s.Fret(7); s.Key(Key.Add); e.ToggleTriplet(); s.Key(Key.Right); s.Fret(8); s.Key(Key.Right); s.Fret(7); s.Key(Key.Right); s.Fret(5);
        s.Key(Key.Left); e.ToggleTriplet(); s.Key(Key.Left); e.ToggleTriplet(); s.Key(Key.OemPeriod); s.Ctrl(Key.Z); s.Ctrl(Key.Z); s.Ctrl(Key.Y);
        s.Repeat(Key.Right, 3);
        var at = s.Ed.SelectedCell;
        s.Fret(3);
        s.Expect("a triplet eighth 3 after the triplet 5", s.Cell(0, at).TupletNumerator == 3 && s.Cell(0, at).DurationDenominator == 8, "3/8t", s.Dump(0));
        s.Key(Key.OemPeriod);
        s.Expect("then a dotted eighth triplet", s.Cell(0, at).TupletNumerator == 3 && s.Cell(0, at).Dots == 1, "3/8.t", s.Dump(0));
    }

    private static void GeSemitoneDownOnZero(MainWindow w)
    {
        var s = WfOpen(w, "E7 semitone down on 0");
        s.Begin("0, Shift+Down");
        s.Fret(0); s.Key(Key.Down, ModifierKeys.Shift);
        var notes = s.Cell(0, 0).Notes;
        s.Expect("the 0 stays on its string", notes.Count == 1 && notes[0].Fret == 0 && notes[0].StringIndex == s.Ed.SelectedString, "0", string.Join(",", notes.Select(n => $"{n.Fret}@{n.StringIndex}")));
    }

    private static void GeUndoBackspaceKeepsBar(MainWindow w)
    {
        var s = WfOpen(w, "E9 undo backspace");
        s.Begin("Right, Backspace, Left, Undo");
        s.Key(Key.Right);
        var bars = s.Track.Measures.Count;
        s.Key(Key.Back); s.Key(Key.Left); s.Ctrl(Key.Z);
        s.Expect("the bars the song had stay", s.Track.Measures.Count == bars, $"{bars} bars", $"{s.Track.Measures.Count} bars");
    }
}
