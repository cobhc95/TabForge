using System.Linq;
using System.Windows.Input;
using TabForge.Models;
using TabForge.Views;

namespace TabForge;

// Owns: workflow stories of the GP5 verification 2 clusters 2.3, R10 and session 15 (work/gp5diff/VERIFY2_REPORT.md): a cut that leaves only
//     rests empties the bar, the cursor at the cut's start, so a later paste gets no leading rests; a paste past the end of an overfull bar
//     goes on in that bar; Undo of Longer on an empty spot restores the writing duration; palm mute on a tied note adds nothing.
//     Every expectation was confirmed in one quiet GP5 (single instance, a save after every action, a typed note after each move):
//     work/gp5diff/j4 (j01 = s004869, j03 = s000199, j05 / j06 = session 15 step 47, j07 = session 15 step 56).
// Does not own: the commands (EditCommands.CutClear / PasteBeats, ScoreEditCommands.SetDuration / ToggleTechnique,
//     TabEditorControl.CutSelection), the input helpers (WorkflowKit.cs).
// Tests: TestGp5CutPasteStories (--areas workflow).
public static partial class SelfTest
{
    private static void TestGp5CutPasteStories()
    {
        var w = SmNewWindow();
        var previous = DialogHost.Capture;
        DialogHost.Capture = d => d is PasteOptionsDialog;   // a paste question takes its defaults
        try
        {
            foreach (var (name, story) in new (string, Action<MainWindow>)[]
            {
                ("C1 cut inside a bar empties it, the cursor at its start, a paste gets no leading rests (j01, s004869)", GcCutThenPaste),
                ("C2 paste past the end of an overfull bar goes on in that bar (j07, session 15 step 56)", GcPasteOverfull),
                ("C4 Undo of Longer on an empty spot restores the writing duration (j03, s000199)", GcUndoLonger),
                ("C6 beats pasted onto written beats insert in the bar, the cursor on the last pasted beat (l6 p01, p04, p05)", GcPasteInBar),
                ("C5 palm mute on a tied note adds nothing (j05, session 15 step 47)", GcPalmMuteOnTie),
            })
                SmStep($"workflow: {name} runs", () => story(w));
        }
        finally { DialogHost.Capture = previous; SmCloseWindow(w); }
    }

    private static string GcAt(Wf s) => $"cur {s.Ed.SelectedMeasure}:{s.Ed.SelectedCell} sel {s.Ed.HasSelection}, {s.Track.Measures.Count} bars, {s.Dump(0)}";

    private static void GcCutThenPaste(MainWindow w)
    {
        var s = GnOpenNew(w, "C1 cut then paste");
        s.Begin("8 Right 12 Right Shorter 2 Right, Home, Shift+Right x2, Ctrl+X, Ctrl+V");
        s.Fret(8); s.Key(Key.Right); s.Fret(12); s.Key(Key.Right); s.Key(Key.Add); s.Fret(2); s.Key(Key.Right);
        s.Key(Key.Home); s.Repeat(Key.Right, 2, ModifierKeys.Shift); s.Ctrl(Key.X);
        s.Expect("the bar is empty and the cursor at its start", s.Track.Measures.Count == 1 && s.Bar(0).Cells.All(c => c.Notes.Count == 0)
            && s.Beats(0).Count <= 1 && s.Ed.SelectedMeasure == 0 && s.Ed.SelectedCell == 0 && !s.Ed.HasSelection, "empty bar, cursor on beat 1", GcAt(s));
        s.Ctrl(Key.V);
        s.Expect("the paste starts the bar", s.NoteAt(0, 0, 0)?.Fret == 8 && s.Cell(0, 0).DurationDenominator == 4, "8 12 2/8", GcAt(s));
    }

    private static void GcPasteOverfull(MainWindow w)
    {
        var s = GnOpenNew(w, "C2 paste overfull");
        s.Begin("Longer 5 Right Longer 6 Right 7, Home, Shift+Right, Ctrl+C, End, Right, Ctrl+V");
        s.Key(Key.Subtract); s.Fret(5); s.Key(Key.Right); s.Key(Key.Subtract); s.Fret(6); s.Key(Key.Right); s.Fret(7);
        s.Key(Key.Home); s.Key(Key.Right, ModifierKeys.Shift); s.Ctrl(Key.C);
        s.Key(Key.End); s.Key(Key.Right); s.Ctrl(Key.V);
        var frets = s.Beats(0).Select(i => s.Cell(0, i)).Where(c => c.Notes.Count > 0).Select(c => c.Notes[0].Fret).ToList();
        s.Expect("one bar holding 5 6 7 5 6, no rests", s.Track.Measures.Count == 1 && frets.SequenceEqual(new[] { 5, 6, 7, 5, 6 })
            && s.Beats(0).All(i => !s.Cell(0, i).IsRest), "5/2 6/1 7/1 5/2 6/1", GcAt(s));
    }

    // GP5 (quiet runs l6, l6b, p01..p05): pasting beats onto a written beat inserts them before the cursor beat in the same bar (the bar may
    // overfill, nothing moves on), asks nothing, and leaves the cursor on the last pasted beat, so the next typed fret replaces it.
    private static void GcPasteInBar(MainWindow w)
    {
        List<int> Frets(Wf s) => s.Beats(0).Select(i => s.Cell(0, i)).Where(c => c.Notes.Count > 0).Select(c => c.Notes[0].Fret).ToList();
        var s = GnOpenNew(w, "C6 paste in bar");
        s.Begin("12 Left Shift+Right Ctrl+C End Ctrl+V 9");
        s.Fret(12); s.Key(Key.Left); s.Key(Key.Right, ModifierKeys.Shift); s.Ctrl(Key.C); s.Key(Key.End); s.Ctrl(Key.V); s.Fret(9);
        s.Expect("9 12", s.Track.Measures.Count == 1 && Frets(s).SequenceEqual(new[] { 9, 12 }), "9 12", GcAt(s));

        s = GnOpenNew(w, "C6 paste in bar 2");
        s.Begin("1 2 3, Home, Shift+Right x2, Ctrl+C, Home, Right, Ctrl+V, 9");
        s.Fret(1); s.Key(Key.Right); s.Fret(2); s.Key(Key.Right); s.Fret(3);
        s.Key(Key.Home); s.Repeat(Key.Right, 2, ModifierKeys.Shift); s.Ctrl(Key.C); s.Key(Key.Home); s.Key(Key.Right); s.Ctrl(Key.V); s.Fret(9);
        s.Expect("1 1 2 9 2 3 in one bar", s.Track.Measures.Count == 1 && Frets(s).SequenceEqual(new[] { 1, 1, 2, 9, 2, 3 }), "1 1 2 9 2 3", GcAt(s));

        s = GnOpenNew(w, "C6 paste in bar 3");
        s.Begin("1 2 3 4, Home, Shift+Right, Ctrl+C, Home, Right x2, Ctrl+V, 9");
        s.Fret(1); s.Key(Key.Right); s.Fret(2); s.Key(Key.Right); s.Fret(3); s.Key(Key.Right); s.Fret(4);
        s.Key(Key.Home); s.Key(Key.Right, ModifierKeys.Shift); s.Ctrl(Key.C); s.Key(Key.Home); s.Repeat(Key.Right, 2); s.Ctrl(Key.V); s.Fret(9);
        s.Expect("1 2 1 9 3 4 in one bar", s.Track.Measures.Count == 1 && Frets(s).SequenceEqual(new[] { 1, 2, 1, 9, 3, 4 }), "1 2 1 9 3 4", GcAt(s));
    }

    private static void GcUndoLonger(MainWindow w)
    {
        var s = GnOpenNew(w, "C4 undo longer");
        s.Begin("Longer, Undo, 14");
        s.Key(Key.Subtract); s.Ctrl(Key.Z); s.Fret(14);
        s.Expect("a quarter note", s.NoteAt(0, 0, 0)?.Fret == 14 && s.Cell(0, 0).DurationDenominator == 4, "14/4", GcAt(s));
    }

    private static void GcPalmMuteOnTie(MainWindow w)
    {
        var s = GnOpenNew(w, "C5 palm mute on tie");
        s.Begin("7 Right L P");
        s.Fret(7); s.Key(Key.Right); s.Key(Key.L); s.Key(Key.P);
        var tied = s.CursorCell?.Notes.FirstOrDefault();
        s.Expect("the tied note has no palm mute", tied is { Tied: true } && !tied.Techniques.Any(TechniqueNames.IsPalmMute), "7 7[tie]",
            $"{GcAt(s)}, {string.Join(",", tied?.Techniques ?? new())}");
        s.Key(Key.Left); s.Key(Key.P);
        s.Expect("the origin still takes it", s.NoteAt(0, 0, 0)?.Techniques.Any(TechniqueNames.IsPalmMute) == true, "7[pm]", GcAt(s));
    }
}
