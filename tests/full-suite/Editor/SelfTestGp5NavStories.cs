using System.Linq;
using System.Windows.Input;

namespace TabForge;

// Owns: workflow stories of the GP5 harness's navigation clusters (work/gp5diff VERIFY_REPORT.md 2.2-2.8, session 10): Right at the
//     song end and after a written rest, Alt+Down on an empty spot, an overfull bar keeps its next note, paste after moving past the end,
//     Ctrl+End and End targets.
// Does not own: the navigation (Views/Score/CursorPositions.cs, TabEditorControl.Navigation.cs), the input helpers (WorkflowKit.cs).
// Tests: TestGp5NavStories (--areas workflow).
public static partial class SelfTest
{
    private static void TestGp5NavStories()
    {
        var w = SmNewWindow();
        try
        {
            foreach (var (name, story) in new (string, Action<MainWindow>)[]
            {
                ("N1 Right in an empty last bar makes the next bar (micro5 w04)", GnRightInEmptyLastBar),
                ("N2 after a written rest Right stays in the bar (u09)", GnRightAfterWrittenRest),
                ("N3 Alt+Down / Alt+Up on an empty spot move the cursor string (u05)", GnAltDownOnEmpty),
                ("N4 an overfull bar keeps the next note (u06)", GnOverfullKeepsNote),
                ("N5 paste after moving past the end lands in the bar Right made (w04, w12)", GnPastePastEnd),
                ("N6 Ctrl+End goes to the last beat (u10)", GnCtrlEnd),
                ("N7 End lands on a typed trailing rest (session 10)", GnEndOnTypedRest),
            })
                SmStep($"workflow: {name} runs", () => story(w));
        }
        finally { SmCloseWindow(w); }
    }

    /// <summary>A story on File > New's song (one bar with no cells), as the harness replays it.</summary>
    private static Wf GnOpenNew(MainWindow w, string scenario)
    {
        var s = new Wf { Window = w, Doc = SmOpenSong(w, GdSong()), Scenario = scenario };
        s.Ed.SelectedTrackIndex = 0; s.Ed.AutoAdvanceAfterEntry = false;
        s.Click(0, 0, 0); s.Tool("duration:quarter");
        return s;
    }

    private static string GnCursor(Wf s) => $"cur {s.Ed.SelectedMeasure}:{s.Ed.SelectedCell}:{s.Ed.SelectedString}, {s.Track.Measures.Count} bars, {s.Dump(0)}";

    private static void GnRightInEmptyLastBar(MainWindow w)
    {
        var s = GnOpenNew(w, "N1 right in empty last bar");
        s.Begin("5, Ctrl+Right, Right");
        s.Fret(5); s.Ctrl(Key.Right); s.Key(Key.Right);
        // A quiet GP5 makes bar 3 (work/gp5diff/verify2/micro5 w03, w04: the next typed fret lands in bar 3); c06 read a file GP5 had not saved after the move.
        s.Expect("three bars, the cursor in bar 3", s.Track.Measures.Count == 3 && s.Ed.SelectedMeasure == 2, "5 | (empty) | (empty), cursor in bar 3", GnCursor(s));
    }

    private static void GnRightAfterWrittenRest(MainWindow w)
    {
        var s = GnOpenNew(w, "N2 right after written rest");
        s.Begin("5, Right, R, Right, 7");
        s.Fret(5); s.Key(Key.Right); s.Key(Key.R); s.Key(Key.Right); s.Fret(7);
        s.Expect("5 r 7 in bar 1", s.NoteAt(0, 0, 0)?.Fret == 5 && s.Cell(0, 4).IsRest && s.NoteAt(0, 8, 0)?.Fret == 7, "5 r 7", GnCursor(s));
    }

    private static void GnAltDownOnEmpty(MainWindow w)
    {
        var s = GnOpenNew(w, "N3 alt down on empty");
        s.Begin("Alt+Down, 3");
        s.Key(Key.Down, ModifierKeys.Alt); s.Fret(3);
        s.Expect("the 3 on string 2", s.NoteAt(0, 0, 1)?.Fret == 3 && s.NoteAt(0, 0, 0) is null, "3 on string 2", GnCursor(s));
        s.Begin("Right, Alt+Down, Alt+Down, Alt+Up, 4");
        s.Key(Key.Right); s.Key(Key.Down, ModifierKeys.Alt); s.Key(Key.Down, ModifierKeys.Alt); s.Key(Key.Up, ModifierKeys.Alt); s.Fret(4);
        s.Expect("the 4 on string 3", s.NoteAt(0, 4, 2)?.Fret == 4, "4 on string 3", GnCursor(s));
    }

    private static void GnOverfullKeepsNote(MainWindow w)
    {
        var s = GnOpenNew(w, "N4 overfull keeps note");
        s.Begin("1, Right, Longer, 2, Right, 3, Right, 5");
        s.Fret(1); s.Key(Key.Right); s.Key(Key.Subtract); s.Fret(2); s.Key(Key.Right); s.Fret(3); s.Key(Key.Right); s.Fret(5);
        var frets = s.Beats(0).Select(c => s.Cell(0, c).Notes.FirstOrDefault()?.Fret ?? -1).Where(f => f >= 0).ToList();
        s.Expect("1 2 3 5 in bar 1", frets.SequenceEqual(new[] { 1, 2, 3, 5 }), "1 2 3 5 (bar exceeded)", GnCursor(s) + " | " + (s.Track.Measures.Count > 1 ? s.Dump(1) : ""));
    }

    private static void GnPastePastEnd(MainWindow w)
    {
        var s = GnOpenNew(w, "N5 paste past end");
        s.Begin("5, Home, Shift+Right, Ctrl+C, Ctrl+Right, Right, End, Ctrl+V");
        s.Fret(5); s.Key(Key.Home); s.Key(Key.Right, ModifierKeys.Shift); s.Ctrl(Key.C); s.Ctrl(Key.Right); s.Key(Key.Right); s.Key(Key.End); s.Ctrl(Key.V);
        // Right in the empty bar 2 makes bar 3 (quiet GP5, work/gp5diff/verify2/micro5 w04 and w12), so the paste lands there.
        s.Expect("the 5 pasted into bar 3", s.Track.Measures.Count == 3 && s.NoteAt(2, 0, 0)?.Fret == 5 && s.Bar(1).Cells.All(c => c.Notes.Count == 0),
            "5 | | 5", GnCursor(s) + " | " + s.Dump(2));
    }

    private static void GnCtrlEnd(MainWindow w)
    {
        var s = GnOpenNew(w, "N6 ctrl end");
        s.Begin("5, Right, 7, Right, 8, Right, 9, Ctrl+End, 3");
        s.Fret(5); s.Key(Key.Right); s.Fret(7); s.Key(Key.Right); s.Fret(8); s.Key(Key.Right); s.Fret(9); s.Key(Key.Home); s.Key(Key.End, ModifierKeys.Control); s.Fret(3);
        s.Expect("5 7 8 3", s.NoteAt(0, 0, 0)?.Fret == 5 && s.NoteAt(0, 12, 0)?.Fret == 3, "5 7 8 3", GnCursor(s));
    }

    private static void GnEndOnTypedRest(MainWindow w)
    {
        var s = GnOpenNew(w, "N7 end on typed rest");
        s.Begin("5, Right, R, Home, End");
        s.Fret(5); s.Key(Key.Right); s.Key(Key.R); s.Key(Key.Home); s.Key(Key.End);
        s.Expect("the cursor on the typed rest", s.Ed.SelectedCell == 4, "cursor on r", GnCursor(s));
    }
}
