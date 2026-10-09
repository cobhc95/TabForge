using System.Linq;
using System.Windows.Input;
using TabForge.Views;

namespace TabForge;

// Owns: the workflow stories of note and rest ENTRY durations against GP5 with the rest fill on: a note on an empty bar takes the writing
//     duration, R writes a rest of it, a note typed on a written rest keeps the rest's length, Shorter on a selection closes up, an undone
//     dot does not leak into new notes, a two-digit fret is one undo step, Delete on a rest keeps the rests normalised.
// Does not own: the commands (Views/Score/ScoreEditCommands*.cs, WritingDuration.cs), the input helpers (WorkflowKit.cs).
// Tests: TestEntryDurationWorkflow (--areas workflow).
public static partial class SelfTest
{
    private static void TestEntryDurationWorkflow()
    {
        var w = SmNewWindow();
        try
        {
            foreach (var (name, story) in new (string, Action<MainWindow>)[]
            {
                ("E1 a note on an empty bar takes the writing duration", EdEmptyBarNote),
                ("E2 R writes rests of the writing duration", EdRestKey),
                ("E3 a fret on a written rest keeps the rest's length", EdFretOnRest),
                ("E4 Shorter on a selection closes up", EdShorterSelection),
                ("E5 an undone dot does not reach new notes", EdUndoneDot),
                ("E6 a two-digit fret is one undo step", EdTwoDigitUndo),
                ("E7 Delete on a rest keeps the rests normalised", EdDeleteRest),
            })
                SmStep($"workflow: {name} runs", () => story(w));
        }
        finally { SmCloseWindow(w); }
    }

    private static void EdEmptyBarNote(MainWindow w)
    {
        var s = WfOpen(w, "E1 empty bar");
        s.Begin("bar 2 (a whole-bar rest), + (shorter), type 7");
        var cells = s.Bar(1).Cells; cells.Clear(); for (var i = 0; i < 16; i++) cells.Add(new Models.TabCell()); cells[0].IsRest = true; cells[0].DurationDenominator = 1;   // a new bar: one whole-bar rest
        s.Click(1, 0, 0);
        s.Expect("the empty bar shows one whole-bar rest", s.Cell(1, 0).IsRest && s.Cell(1, 0).DurationDenominator == 1, "an empty bar", s.Dump(1));
        s.Key(Key.Add);
        s.Expect("+ on the empty bar changes the writing duration, not the placeholder rest", s.Ed.CurrentDurationDenominator == 8 && s.Cell(1, 0).IsRest && s.Cell(1, 0).DurationDenominator == 1,
            "an empty beat has no length of its own", $"writing 1/{s.Ed.CurrentDurationDenominator}, {s.Dump(1)}");
        s.Fret(7);
        s.Expect("the note is an eighth and rests refill the bar", s.Cell(1, 0).Notes.Count == 1 && s.Cell(1, 0).DurationDenominator == 8 && s.State(1).Complete && !s.State(1).Marked,
            "the toolbar duration", s.Dump(1));
    }

    private static void EdRestKey(MainWindow w)
    {
        var s = WfOpen(w, "E2 rest key");
        s.Begin("R on an empty bar with a quarter selected");
        s.Key(Key.R);
        s.Expect("a quarter rest, then the fill", s.Cell(0, 0).IsRest && s.Cell(0, 0).DurationDenominator == 4 && s.State(0).Complete, "a rest of the toolbar duration", s.Dump(0));
        s.Begin("Right, eighth, R on the next empty slot");
        s.Key(Key.Right); s.Tool("duration:eighth"); s.Key(Key.R);
        s.Expect("an eighth rest on beat 2", s.Ed.SelectedCell == 4 && s.Cell(0, 4).IsRest && s.Cell(0, 4).DurationDenominator == 8 && s.State(0).Complete && !s.State(0).Marked,
            "R works on every empty beat", s.Dump(0));
    }

    private static void EdFretOnRest(MainWindow w)
    {
        var s = WfOpen(w, "E3 fret on rest");
        s.Begin("quarter rest, eighth note after it, back on the rest, type 3");
        s.Key(Key.R); s.Key(Key.Right); s.Tool("duration:eighth"); s.Fret(5);
        s.Click(0, 0, 0); s.Fret(3);
        s.Expect("the note keeps the rest's quarter length", s.Cell(0, 0).Notes.Count == 1 && s.Cell(0, 0).DurationDenominator == 4 && s.NoteAt(0, 4, 0)?.Fret == 5 && !s.State(0).Marked,
            "the rest is overwritten by a note of the same length", s.Dump(0));
    }

    private static void EdShorterSelection(MainWindow w)
    {
        var s = WfOpen(w, "E4 shorter selection");
        s.Begin("four eighths, select them, + (shorter)");
        s.Tool("duration:eighth");
        for (var i = 0; i < 4; i++) { s.Click(0, i * 2, 0); s.Fret(5 + i); }
        s.Click(0, 0, 0); s.Repeat(Key.Right, 3, ModifierKeys.Shift);
        s.Key(Key.Add);
        var beats = s.Beats(0);
        s.Expect("four sixteenths end to end, rests only after them", beats.Take(4).SequenceEqual(new[] { 0, 1, 2, 3 }) && beats.Take(4).All(c => s.Cell(0, c).Notes.Count == 1 && s.Cell(0, c).DurationDenominator == 16)
            && beats.Skip(4).All(c => s.Cell(0, c).IsRest) && s.State(0).Complete && !s.State(0).Marked, "the beats close up, no rests between notes", s.Dump(0));
    }

    private static void EdUndoneDot(MainWindow w)
    {
        var s = WfOpen(w, "E5 undone dot");
        s.Begin("type 5, dot, undo, undo, redo, type 7 on beat 2");
        s.Fret(5); s.Key(Key.OemPeriod);
        s.Ctrl(Key.Z); s.Ctrl(Key.Z); s.Ctrl(Key.Y);
        s.Click(0, 4, 0); s.Fret(7);
        s.Expect("the new note is a plain quarter", s.Cell(0, 4).Notes.Count == 1 && s.Cell(0, 4).Dots == 0 && s.Cell(0, 4).DurationDenominator == 4 && s.Cell(0, 0).Dots == 0,
            "the toolbar follows the restored beat", s.Dump(0));
    }

    private static void EdTwoDigitUndo(MainWindow w)
    {
        var s = WfOpen(w, "E6 two-digit undo");
        s.Begin("type 24, Ctrl+Z, Ctrl+Y");
        s.Fret(24);
        s.Expect("fret 24 typed", s.NoteAt(0, 0, 0)?.Fret == 24, "two digits make one fret", s.Dump(0));
        s.Ctrl(Key.Z);
        s.Expect("undo removes the whole number", s.NoteAt(0, 0, 0) is null, "one undo step", s.Dump(0));
        s.Ctrl(Key.Y);
        s.Expect("redo brings 24 back", s.NoteAt(0, 0, 0)?.Fret == 24, "redo repeats the entry", s.Dump(0));
    }

    private static void EdDeleteRest(MainWindow w)
    {
        var s = WfOpen(w, "E7 delete rest");
        s.Begin("3, rest, 5 as quarters, Delete on the rest");
        s.Fret(3); s.Click(0, 4, 0); s.Key(Key.R); s.Click(0, 8, 0); s.Fret(5);
        s.Click(0, 4, 0); s.Key(Key.Delete);
        s.Expect("the 5 moves left, one half rest ends the bar", s.Dump(0) == "0:3@0/4 4:5@0/4 8:r/2 [ok]", "the rest beat goes, later beats move left", s.Dump(0));
    }
}
