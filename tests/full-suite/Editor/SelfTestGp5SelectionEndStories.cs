using System.Linq;
using System.Windows.Input;
using TabForge.Services;
using TabForge.Views;
using TabForge.Views.Score;

namespace TabForge;

// Owns: workflow stories of the GP5 owner-style sessions 01, 05 and 12 and the masked set's Backspace cluster (work/gp5diff VERIFY_REPORT 3, 4):
//     the cursor box sits on the beat in an overfull bar, Undo / Redo end the selection with the cursor on its last beat, Redo of a paste
//     lands on the first pasted beat, Delete on a selection removes its beats, Backspace and Delete end the selection.
// Does not own: the commands (Views/Score/ScoreEditCommands.SelectionEnd.cs, ScoreEditCommands.Marks.cs), the cursor geometry
//     (CursorPositions.DrawnStart), the input helpers (WorkflowKit.cs).
// Tests: TestGp5SelectionEndStories (--areas workflow).
public static partial class SelfTest
{
    private static void TestGp5SelectionEndStories()
    {
        var w = SmNewWindow();
        var previous = DialogHost.Capture;
        DialogHost.Capture = d => d is PasteOptionsDialog;   // a paste question takes its defaults
        try
        {
            foreach (var (name, story) in new (string, Action<MainWindow>)[]
            {
                ("S1 the cursor box sits on the beat in an overfull bar (session 01)", GsOverfullCursor),
                ("S2 Undo / Redo end the selection on its last beat (session 12)", GsUndoRedoEndsSelection),
                ("S3 Redo of a paste lands on the first pasted beat; Delete removes the selected beats (session 05)", GsRedoPasteThenDelete),
                ("S4 Backspace on a selection ends it, then L writes a tied 0 (masked set)", GsBackspaceEndsSelection),
            })
                SmStep($"workflow: {name} runs", () => story(w));
        }
        finally { DialogHost.Capture = previous; SmCloseWindow(w); }
    }

    private static string GsAt(Wf s) => $"cur {s.Ed.SelectedMeasure}:{s.Ed.SelectedCell} sel {s.Ed.HasSelection}";

    private static void GsOverfullCursor(MainWindow w)
    {
        var s = GnOpenNew(w, "S1 overfull cursor");
        s.Begin("eighths 0 3 5 0 3 5 7 5, Left x5, 2, Longer, Right, 3");
        s.Tool("duration:eighth");
        foreach (var fret in new[] { 0, 3, 5, 0, 3, 5, 7 }) { s.Fret(fret); s.Key(Key.Right); }
        s.Fret(5);
        s.Repeat(Key.Left, 5); s.Fret(2); s.Key(Key.Subtract); s.Key(Key.Right); s.Fret(3);
        var cells = s.Bar(0).Cells;
        var beats = s.Beats(0);
        var ordered = beats.Zip(beats.Skip(1)).All(p => CursorPositions.DrawnStart(cells, p.Second) >= CursorPositions.DrawnStart(cells, p.First) + MusicTime.CellSlots(cells[p.First]) - 0.001);
        s.Expect("no beat is drawn inside the one before", ordered, "beats one after another", s.Dump(0));
        var cursor = ScoreRenderer.CellStartSlots(s.Bar(0), s.Ed.SelectedCell, cells);
        var previousBeat = beats.Where(c => c < s.Ed.SelectedCell).DefaultIfEmpty(-1).Last();
        var after = previousBeat < 0 || cursor >= CursorPositions.DrawnStart(cells, previousBeat) + MusicTime.CellSlots(cells[previousBeat]) - 0.001;
        s.Expect("the cursor box is on the typed 3, not in the gap", s.CursorCell?.Notes.FirstOrDefault()?.Fret == 3 && after, "box on the 3", $"{GsAt(s)} x {cursor} | {s.Dump(0)}");
    }

    private static void GsUndoRedoEndsSelection(MainWindow w)
    {
        var s = GnOpenNew(w, "S2 undo redo selection");
        s.Begin("3 R 5 R 7 R 8, Home, Shift+Right x3, Shorter, Shorter, Longer, Undo, Redo, Shift+Left, Longer");
        s.Fret(3); s.Key(Key.Right); s.Fret(5); s.Key(Key.Right); s.Fret(7); s.Key(Key.Right); s.Fret(8);
        s.Key(Key.Home); s.Repeat(Key.Right, 3, ModifierKeys.Shift);
        s.Key(Key.Add); s.Key(Key.Add); s.Key(Key.Subtract);
        s.Ctrl(Key.Z); s.Ctrl(Key.Y);
        s.Expect("Redo ends the selection, the cursor on the 8", !s.Ed.HasSelection && s.CursorCell?.Notes.FirstOrDefault()?.Fret == 8, "8 boxed, nothing selected", GsAt(s));
        s.Key(Key.Left, ModifierKeys.Shift); s.Key(Key.Subtract);
        var durations = s.Beats(0).Where(c => s.Cell(0, c).Notes.Count > 0).Select(c => s.Cell(0, c).DurationDenominator).ToList();
        s.Expect("Longer changes the 7 and the 8", durations.SequenceEqual(new[] { 8, 8, 4, 4 }), "3/8 5/8 7/4 8/4", s.Dump(0));
    }

    private static void GsRedoPasteThenDelete(MainWindow w)
    {
        var s = GnOpenNew(w, "S3 redo paste delete");
        s.Begin("3 5 7 5 | 3 2 0 2, Home of bar 1, Shift+Right x7, Ctrl+C, End, Right, Ctrl+V, Undo, Redo, Home, Shift+Right x3, Delete");
        foreach (var fret in new[] { 3, 5, 7, 5, 3, 2, 0 }) { s.Fret(fret); s.Key(Key.Right); }
        s.Fret(2);
        s.Click(0, 0, 0); s.Repeat(Key.Right, 7, ModifierKeys.Shift); s.Ctrl(Key.C);
        s.Click(1, 0, 0); s.Key(Key.End); s.Key(Key.Right); s.Ctrl(Key.V);
        var bars = s.Track.Measures.Count;
        s.Ctrl(Key.Z); s.Ctrl(Key.Y);
        // Quiet GP5 (work/gp5diff/i4micro2 i15, i16): after the paste and after its Redo the next fret lands on the first pasted beat.
        s.Expect("Redo puts the cursor on the first pasted beat", bars >= 4 && s.Ed.SelectedMeasure == 2 && s.Ed.SelectedCell == 0 && !s.Ed.HasSelection, "bar 3 beat 1", $"{bars} bars, {GsAt(s)}");
        s.Key(Key.Home); s.Repeat(Key.Right, 3, ModifierKeys.Shift); s.Key(Key.Delete);
        s.Expect("Delete removes the four beats of bar 3", s.Bar(2).Cells.All(c => c.Notes.Count == 0 && !c.WrittenRest) && s.NoteAt(3, 0, 0)?.Fret == 3,
            "bar 3 empty (a fill rest in TabForge), bar 4 kept", $"{s.Dump(2)} | {s.Dump(3)}");
        s.Expect("the selection ends", !s.Ed.HasSelection && s.Ed.SelectedMeasure == 2, "cursor in bar 3, nothing selected", GsAt(s));
    }

    private static void GsBackspaceEndsSelection(MainWindow w)
    {
        var s = GnOpenNew(w, "S4 backspace selection");
        s.Begin("5 R 7, Home, Shift+Right, Backspace, L");
        s.Fret(5); s.Key(Key.Right); s.Fret(7);
        s.Key(Key.Home); s.Key(Key.Right, ModifierKeys.Shift); s.Key(Key.Back);
        s.Expect("Backspace ends the selection", !s.Ed.HasSelection, "nothing selected", GsAt(s));
        s.Key(Key.L);
        s.Expect("L writes a tied 0 on the first beat", s.NoteAt(0, 0, 0) is { Fret: 0, Tied: true }, "0[tie]", s.Dump(0));
    }
}
