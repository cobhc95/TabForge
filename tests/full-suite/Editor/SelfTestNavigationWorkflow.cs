using System.Linq;
using System.Windows.Input;
using TabForge.Views;

namespace TabForge;

// Owns: the workflow stories of cursor NAVIGATION against GP5 with the rest fill on: Right skips the fill rests (last note, empty spot,
//     next bar), End goes to the last written beat, an overfull bar keeps taking notes, Insert bar puts the cursor on beat 1 of the new bar,
//     Alt+Up / Alt+Down on an empty spot move the cursor to the next string.
// Does not own: the navigation (Views/TabEditorControl.Navigation.cs, Views/Score/CursorPositions.cs), the input helpers (WorkflowKit.cs).
// Tests: TestNavigationWorkflow (--areas workflow).
public static partial class SelfTest
{
    private static void TestNavigationWorkflow()
    {
        var w = SmNewWindow();
        try
        {
            foreach (var (name, story) in new (string, Action<MainWindow>)[]
            {
                ("N1 Right from the empty spot goes to the next bar", NvRightPastLastNote),
                ("N2 an overfull bar keeps taking notes", NvOverfullBar),
                ("N3 Insert bar puts the cursor on beat 1 of the new bar", NvInsertBar),
                ("N4 Alt+Up / Alt+Down on an empty spot move the cursor string", NvAltStringOnEmpty),
                ("N5 End goes to the last written beat", NvEndThenRight),
            })
                SmStep($"workflow: {name} runs", () => story(w));
        }
        finally { SmCloseWindow(w); }
    }

    private static void NvRightPastLastNote(MainWindow w)
    {
        var s = WfOpen(w, "N1 right past last note", bars: 1);
        s.Begin("5, Right, Right, 7 on a one-bar song");
        s.Fret(5); s.Key(Key.Right);
        s.Expect("the first Right goes to the empty spot after the note", s.Ed.SelectedMeasure == 0 && s.Ed.SelectedCell == 4, "the empty beat after the last note",
            $"cursor {s.Ed.SelectedMeasure}:{s.Ed.SelectedCell}, {s.Dump(0)}");
        s.Key(Key.Right); s.Fret(7);
        s.Expect("the second Right goes to a new bar 2, where the 7 lands", s.Track.Measures.Count == 2 && s.NoteAt(1, 0, 0)?.Fret == 7 && s.Beats(0).Count(c => s.Cell(0, c).Notes.Count > 0) == 1,
            "bar 1 `5`, bar 2 `7`", $"bars {s.Track.Measures.Count}, bar 1 {s.Dump(0)}");
    }

    private static void NvOverfullBar(MainWindow w)
    {
        var s = WfOpen(w, "N2 overfull bar", bars: 1);
        s.Begin("half 5, Right, whole 6, Right, 7");
        s.Tool("duration:half"); s.Fret(5); s.Key(Key.Right);
        s.Tool("duration:whole"); s.Fret(6); s.Key(Key.Right); s.Fret(7);
        var notes = s.Beats(0).Where(c => s.Cell(0, c).Notes.Count > 0).Select(c => s.Cell(0, c).Notes[0].Fret).ToList();
        s.Expect("all three notes stay in bar 1, which is overfull", s.Track.Measures.Count == 1 && notes.SequenceEqual(new[] { 5, 6, 7 }),
            "one bar `5/2 6/1 7/1` (overfull, marked)", $"bars {s.Track.Measures.Count}, bar 1 {s.Dump(0)}");
        s.Key(Key.Right);
        s.Expect("Right from the 7 goes to the empty spot after it, still in bar 1", s.Ed.SelectedMeasure == 0 && s.Ed.SelectedCell == 40, "the empty beat after the last note", $"cursor {s.Ed.SelectedMeasure}:{s.Ed.SelectedCell}");
        s.Key(Key.Right);
        s.Expect("Right from that empty spot goes to the next bar", s.Ed.SelectedMeasure == 1, "the next bar", $"cursor {s.Ed.SelectedMeasure}:{s.Ed.SelectedCell}");
    }

    private static void NvInsertBar(MainWindow w)
    {
        var s = WfOpen(w, "N3 insert bar", bars: 2);
        s.Begin("5, Right, Ctrl+Insert, eighth, 17");
        s.Fret(5); s.Key(Key.Right);
        s.Ctrl(Key.Insert);
        s.Expect("the cursor is on beat 1 of the inserted bar", s.Ed.SelectedMeasure == 0 && s.Ed.SelectedCell == 0, "the new bar's start", $"cursor {s.Ed.SelectedMeasure}:{s.Ed.SelectedCell}");
        s.Tool("duration:eighth"); s.Fret(17);
        s.Expect("the note starts the new bar and the bar is not overfull", s.NoteAt(0, 0, 0)?.Fret == 17 && s.Cell(0, 0).DurationDenominator == 8 && !s.State(0).Marked && s.NoteAt(1, 0, 0)?.Fret == 5,
            "new bar `17/8` at its start", $"{s.Dump(0)} | {s.Dump(1)}");
    }

    private static void NvAltStringOnEmpty(MainWindow w)
    {
        var s = WfOpen(w, "N4 alt string on empty", bars: 1);
        s.Begin("Alt+Down, 3; then Right, Alt+Down twice, Alt+Up");
        s.Key(Key.Down, ModifierKeys.Alt);
        s.Expect("Alt+Down on an empty spot moves the cursor to string 2", s.Ed.SelectedString == 1, "the cursor moves down a string", $"string {s.Ed.SelectedString + 1}");
        s.Fret(3);
        s.Expect("the 3 lands on string 2", s.NoteAt(0, 0, 1)?.Fret == 3 && s.NoteAt(0, 0, 0) is null, "`3` on string 2", s.Dump(0));
        s.Key(Key.Right); s.Key(Key.Down, ModifierKeys.Alt); s.Key(Key.Down, ModifierKeys.Alt); s.Key(Key.Up, ModifierKeys.Alt);
        s.Expect("Alt+Up moves the cursor back up on the empty spot", s.Ed.SelectedString == 2 && s.Ed.SelectedCell == 4, "string 3 on beat 2", $"string {s.Ed.SelectedString + 1}, cell {s.Ed.SelectedCell}");
    }

    private static void NvEndThenRight(MainWindow w)
    {
        var s = WfOpen(w, "N5 end then right", bars: 2);
        s.Begin("5, Right, 3, Home, End, Right");
        s.Fret(5); s.Key(Key.Right); s.Fret(3); s.Key(Key.Home); s.Key(Key.End);
        s.Expect("End goes to the last written beat (the 3)", s.Ed.SelectedCell == 4, "the last note", $"cell {s.Ed.SelectedCell}, {s.Dump(0)}");
        s.Key(Key.Right);
        s.Expect("Right from there goes to the empty spot in the same bar", s.Ed.SelectedMeasure == 0 && s.Ed.SelectedCell == 8, "the empty beat after the 3", $"cursor {s.Ed.SelectedMeasure}:{s.Ed.SelectedCell}");
        s.Key(Key.Right); s.Key(Key.Left);
        s.Expect("Left from the next bar comes back to the empty spot", s.Ed.SelectedMeasure == 0 && s.Ed.SelectedCell == 8, "the empty spot", $"cursor {s.Ed.SelectedMeasure}:{s.Ed.SelectedCell}");
    }
}
