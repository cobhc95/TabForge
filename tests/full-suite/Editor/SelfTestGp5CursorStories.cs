using System.Linq;
using System.Windows.Input;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// Owns: workflow stories of the GP5 harness's navigation and undo-cursor clusters (work/gp5diff VERIFY2_REPORT: Right at the song end,
//     the cursor after Undo / Redo, paste after a selection, triplets and overfull bars, sessions 05 and 10). Every expectation is
//     confirmed in one quiet GP5 (one instance, nothing else in it) with a typed fret at the end so GP5's saved file shows where its
//     cursor was: work/gp5diff/verify2/micro5 (w02, w04) and work/gp5diff/i4micro, i4micro2, i4micro3, i4micro4 (i01-i25).
// Does not own: the navigation (Views/Score/CursorPositions.cs, TabEditorControl.Navigation.cs), the cursor after Undo / Redo
//     (ScoreEditCommands.SelectionEnd.cs), the input helpers (WorkflowKit.cs).
// Tests: TestGp5CursorStories (--areas workflow).
public static partial class SelfTest
{
    private static void TestGp5CursorStories()
    {
        var w = SmNewWindow();
        var previous = DialogHost.Capture;
        DialogHost.Capture = d => d is PasteOptionsDialog;   // a paste question takes its defaults
        try
        {
            foreach (var (name, story) in new (string, Action<MainWindow>)[]
            {
                ("C1 Right in an empty last bar makes the next bar (micro w02, w04)", GcRightMakesBar),
                ("C2 Redo puts the cursor back where Undo found it (i01, i03)", GcRedoCursor),
                ("C3 Undo / Redo of the bar Ctrl+Right made keeps the cursor in it (i05)", GcRedoNextBar),
                ("C4 Right after a one-beat selection pastes into the same bar (i14)", GcPasteAfterSelection),
                ("C5 End goes to a typed trailing rest after Undo / Redo (session 10)", GcEndAfterRestore),
                ("C6 Redo of a paste puts the cursor on the first pasted beat (i15, i16)", GcRedoPasteFirstBeat),
                ("C7 a bar keeps its notes while it has room or is overfull (i11, i12)", GcBarKeepsAdding),
                ("C8 Undo past a writing mark: Redo takes the song step back first (monkey seed 1)", GcRedoAfterMark),
            })
                SmStep($"workflow: {name} runs", () => story(w));
        }
        finally { DialogHost.Capture = previous; SmCloseWindow(w); }
    }

    private static List<int> GcFrets(Wf s, int bar) => s.Beats(bar).Select(c => s.Cell(bar, c).Notes.FirstOrDefault()?.Fret ?? -1).ToList();

    private static void GcRightMakesBar(MainWindow w)
    {
        var s = GnOpenNew(w, "C1 right makes bar");
        s.Begin("Right, 3");   // GP5 (w02): the 3 in bar 2
        s.Key(Key.Right); s.Fret(3);
        s.Expect("the 3 in bar 2", s.Track.Measures.Count == 2 && s.NoteAt(1, 0, 0)?.Fret == 3, "| 3", GnCursor(s));
        s = GnOpenNew(w, "C1b ctrl right right");
        s.Begin("5, Ctrl+Right, Right, 7");   // GP5 (w04): 5 | empty | 7
        s.Fret(5); s.Ctrl(Key.Right); s.Key(Key.Right); s.Fret(7);
        s.Expect("5 | empty | 7", s.Track.Measures.Count == 3 && s.NoteAt(2, 0, 0)?.Fret == 7 && s.Bar(1).Cells.All(c => c.Notes.Count == 0), "5 | | 7", GnCursor(s));
    }

    private static void GcRedoCursor(MainWindow w)
    {
        var s = GnOpenNew(w, "C2 redo cursor");
        s.Begin("5, Right, 14, Right, Undo, Redo, Shorter, 3");   // GP5 (i01): 5 14 3/8
        s.Fret(5); s.Key(Key.Right); s.Fret(14); s.Key(Key.Right); s.Ctrl(Key.Z); s.Ctrl(Key.Y); s.Key(Key.Add); s.Fret(3);
        s.Expect("5 14 3 in bar 1", GcFrets(s, 0).Take(3).SequenceEqual(new[] { 5, 14, 3 }) && s.NoteAt(0, 8, 0)?.Fret == 3 && s.Cell(0, 8).DurationDenominator == 8,
            "5 14 3/8", GnCursor(s));
        s = GnOpenNew(w, "C2b undo redo of a fret");
        s.Begin("5, Right, 14, Undo, Redo, 3");   // GP5 (i03): 5 3, the cursor stays on the restored beat
        s.Fret(5); s.Key(Key.Right); s.Fret(14); s.Ctrl(Key.Z); s.Ctrl(Key.Y); s.Fret(3);
        s.Expect("5 3", GcFrets(s, 0).Take(2).SequenceEqual(new[] { 5, 3 }), "5 3", GnCursor(s));
    }

    private static void GcRedoNextBar(MainWindow w)
    {
        var s = GnOpenNew(w, "C3 redo next bar");
        s.Begin("Ctrl+Right, Undo, Redo, Up, 5");   // GP5 (i05): the 5 in bar 2 on string 6
        s.Ctrl(Key.Right); s.Ctrl(Key.Z); s.Ctrl(Key.Y); s.Key(Key.Up); s.Fret(5);
        s.Expect("the 5 in bar 2", s.Track.Measures.Count == 2 && s.NoteAt(1, 0, 5)?.Fret == 5, "| 5 (string 6)", GnCursor(s));
    }

    private static void GcPasteAfterSelection(MainWindow w)
    {
        var s = GnOpenNew(w, "C4 paste after selection");
        s.Begin("R, Left, Shift+Right, Ctrl+C, Right, Ctrl+V, 9");   // GP5 (i14): r 9 in bar 1, one bar
        s.Key(Key.R); s.Key(Key.Left); s.Key(Key.Right, ModifierKeys.Shift); s.Ctrl(Key.C); s.Key(Key.Right); s.Ctrl(Key.V); s.Fret(9);
        s.Expect("r 9 in bar 1", s.Track.Measures.Count == 1 && s.Cell(0, 0).IsRest && s.NoteAt(0, 4, 0)?.Fret == 9, "r 9", GnCursor(s));
        s = GnOpenNew(w, "C4b right after selection");
        s.Begin("5 R 7 R 8, Home, Shift+Right, Right, 0");   // GP5 (i4micro3 i19): 5 7 0, Right goes on from the selection's last beat
        s.Fret(5); s.Key(Key.Right); s.Fret(7); s.Key(Key.Right); s.Fret(8); s.Key(Key.Home); s.Key(Key.Right, ModifierKeys.Shift); s.Key(Key.Right); s.Fret(0);
        s.Expect("5 7 0", GcFrets(s, 0).Take(3).SequenceEqual(new[] { 5, 7, 0 }), "5 7 0", GnCursor(s));
    }

    private static void GcEndAfterRestore(MainWindow w)
    {
        var s = GnOpenNew(w, "C5 end after restore");
        s.Begin("session 10: R Right R Shorter Right R Right 5 Home 3 Right 5 Right Del Left Del Right Right R R Home R Undo Redo End");
        s.Key(Key.R); s.Key(Key.Right); s.Key(Key.R); s.Key(Key.Add); s.Key(Key.Right); s.Key(Key.R); s.Key(Key.Right); s.Fret(5);
        s.Key(Key.Home); s.Fret(3); s.Key(Key.Right); s.Fret(5); s.Key(Key.Right); s.Key(Key.Delete); s.Key(Key.Left); s.Key(Key.Delete);
        s.Key(Key.Right); s.Key(Key.Right); s.Key(Key.R); s.Key(Key.R); s.Key(Key.Home); s.Key(Key.R); s.Ctrl(Key.Z); s.Ctrl(Key.Y); s.Key(Key.End);
        s.Expect("End on the typed trailing rest (cell 8)", s.Ed.SelectedCell == 8 && s.Cell(0, 8).IsRest, "cursor on the last r/8", GnCursor(s));
    }

    private static void GcRedoPasteFirstBeat(MainWindow w)
    {
        var s = GnOpenNew(w, "C6 redo paste");
        s.Begin("5 7 8 9 | 1 2 3 4, Ctrl+Home, Home, Shift+Right x7, Ctrl+C, Ctrl+End, End, Right, Ctrl+V, Undo, Redo, 0");   // GP5 (i15): 0 on bar 3 beat 1
        foreach (var fret in new[] { 5, 7, 8, 9, 1, 2, 3 }) { s.Fret(fret); s.Key(Key.Right); }
        s.Fret(4);
        s.Ctrl(Key.Home); s.Key(Key.Home); s.Repeat(Key.Right, 7, ModifierKeys.Shift); s.Ctrl(Key.C);
        s.Ctrl(Key.End); s.Key(Key.End); s.Key(Key.Right); s.Ctrl(Key.V); s.Ctrl(Key.Z); s.Ctrl(Key.Y); s.Fret(0);
        s.Expect("0 7 8 9 in bar 3", s.Track.Measures.Count == 4 && GcFrets(s, 2).SequenceEqual(new[] { 0, 7, 8, 9 }), "0 7 8 9 | 1 2 3 4", GnCursor(s));
    }

    private static void GcRedoAfterMark(MainWindow w)
    {
        var s = GnOpenNew(w, "C8 redo after mark");
        s.Begin("5, Right, 7, '.', Undo, Undo, Redo");
        s.Fret(5); s.Key(Key.Right); s.Fret(7); s.Key(Key.Right); s.Key(Key.Left); s.Key(Key.Right); s.Key(Key.OemPeriod); s.Ctrl(Key.Z);
        var before = ProjectService.ContentHash(s.Song);
        s.Ctrl(Key.Z); s.Ctrl(Key.Y);
        s.Expect("Undo then Redo gives the same song", before.AsSpan().SequenceEqual(ProjectService.ContentHash(s.Song)), "the 7 back", GnCursor(s));
    }

    private static void GcBarKeepsAdding(MainWindow w)
    {
        var s = GnOpenNew(w, "C7 triplets");
        s.Begin("Triplet, 2 R 3 R 3 R 15 R, Shorter, 5 R 10 R 9");   // GP5 (i11): all seven in bar 1 (11/12 of the bar)
        s.Ed.Effects.ToggleTriplet();
        foreach (var fret in new[] { 2, 3, 3 }) { s.Fret(fret); s.Key(Key.Right); }
        s.Fret(15); s.Key(Key.Right); s.Key(Key.Add); s.Fret(5); s.Key(Key.Right); s.Fret(10); s.Key(Key.Right); s.Fret(9);
        s.Expect("2 3 3 15 5 10 9 in bar 1", s.Track.Measures.Count == 1 && GcFrets(s, 0).Where(f => f >= 0).SequenceEqual(new[] { 2, 3, 3, 15, 5, 10, 9 }), "one bar", GnCursor(s));
        s = GnOpenNew(w, "C7b overfull");
        s.Begin("5 R 6 R 7 R 8, Left, Longer, End, Right, 9");   // GP5 (i12): 5 6 7/2 8 9 in bar 1 (bar exceeded)
        foreach (var fret in new[] { 5, 6, 7 }) { s.Fret(fret); s.Key(Key.Right); }
        s.Fret(8); s.Key(Key.Left); s.Key(Key.Subtract); s.Key(Key.End); s.Key(Key.Right); s.Fret(9);
        s.Expect("5 6 7 8 9 in bar 1", GcFrets(s, 0).Where(f => f >= 0).SequenceEqual(new[] { 5, 6, 7, 8, 9 }), "5 6 7/2 8 9", GnCursor(s) + " | " + (s.Track.Measures.Count > 1 ? s.Dump(1) : ""));
    }
}
