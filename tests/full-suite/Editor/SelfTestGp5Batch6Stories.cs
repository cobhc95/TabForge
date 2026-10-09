using System.Linq;
using System.Windows.Input;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// Owns: workflow stories of GP5 batch 6: Alt+Up / Alt+Down on an empty spot wrap past the top / bottom string; Dot on selected beats
//     shrinks the fill rests; a deleted beat takes the length it was given with it; Insert bar with a selection does nothing.
//     Every expectation was confirmed in one quiet GP5 (single instance, a save after every action, a typed fret at the end):
//     work/gp5diff/l6 (a01, a02, f01, i01, i03, i04, d02, d03) and l6b (t03, t05).
// Does not own: the commands (ScoreEditCommands.MoveNotesToAdjacentString, ToggleDot, DeleteBeats; MainWindow InsertBar_Click), the input
//     helpers (WorkflowKit.cs).
// Tests: TestGp5Batch6Stories (--areas workflow).
public static partial class SelfTest
{
    private static void TestGp5Batch6Stories()
    {
        var w = SmNewWindow();
        var previous = DialogHost.Capture;
        DialogHost.Capture = d => true;
        try
        {
            foreach (var (name, story) in new (string, Action<MainWindow>)[]
            {
                ("L1 Alt+Up on the top string's empty spot wraps to string 6, Alt+Down on string 6 to string 1 (a01, a02)", GlAltWraps),
                ("L2 Dot on two selected eighths shrinks the fill rests (f01, session 12 step 20)", GlDotRefills),
                ("L3 a deleted beat takes its length with it; Longer on an empty spot survives Delete beat (t03, t05)", GlDeleteBeatLength),
                ("L4 Insert bar with a selection does nothing (i01, i03, i04)", GlInsertBarWithSelection),
            })
                SmStep($"workflow: {name} runs", () => story(w));
        }
        finally { DialogHost.Capture = previous; SmCloseWindow(w); }
    }

    private static string GlAt(Wf s) => $"cur {s.Ed.SelectedMeasure}:{s.Ed.SelectedCell}:{s.Ed.SelectedString}, {s.Track.Measures.Count} bars, "
        + string.Join(" | ", Enumerable.Range(0, s.Track.Measures.Count).Select(s.Dump));

    private static void GlAltWraps(MainWindow w)
    {
        var s = GnOpenNew(w, "L1 Alt+Up wraps");
        s.Begin("Alt+Up 17");
        s.Key(Key.Up, ModifierKeys.Alt); s.Fret(17);
        s.Expect("the 17 is on string 6", s.NoteAt(0, 0, 5)?.Fret == 17, "17@6", GlAt(s));
        s = GnOpenNew(w, "L1b Alt+Down wraps");
        s.Begin("Down x5, Alt+Down 3");
        s.Repeat(Key.Down, 5); s.Key(Key.Down, ModifierKeys.Alt); s.Fret(3);
        s.Expect("the 3 is on string 1", s.NoteAt(0, 0, 0)?.Fret == 3, "3@1", GlAt(s));
    }

    private static void GlDotRefills(MainWindow w)
    {
        var s = GnOpenNew(w, "L2 dot a selection");
        s.Begin("Shorter 3 Right 5 Home Shift+Right Dot");
        s.Key(Key.Add); s.Fret(3); s.Key(Key.Right); s.Fret(5); s.Key(Key.Home); s.Key(Key.Right, ModifierKeys.Shift); s.Ed.Effects.ToggleDot();
        var cells = s.Bar(0).Cells;
        var notes = s.Beats(0).Select(i => cells[i]).Where(c => c.Notes.Count > 0).ToList();
        var total = s.Beats(0).Sum(i => MusicTime.CellSlots(cells[i]));
        s.Expect("3. 5. and fill rests that make the bar exactly full", notes.Count == 2 && notes.All(c => c.Dots == 1 && c.DurationDenominator == 8)
            && Math.Abs(total - MusicTime.BarSlots(s.Song, 0)) < 1e-6, "3/8. 5/8. (incomplete bar 3/8)", $"{GlAt(s)}, slots {total}");
    }

    private static void GlDeleteBeatLength(MainWindow w)
    {
        var s = GnOpenNew(w, "L3 tie longer delete beat");
        s.Begin("Tie Longer Delete-beat 3");
        s.Key(Key.L); s.Key(Key.Subtract); s.Ed.Effects.DeleteBeats(); s.Settle(); s.Fret(3);
        s.Expect("the 3 is a quarter", s.NoteAt(0, 0, 0) is not null && s.Cell(0, 0).DurationDenominator == 4, "3/4", GlAt(s));
        s = GnOpenNew(w, "L3b longer delete beat");
        s.Begin("Longer Delete-beat 3");
        s.Key(Key.Subtract); s.Ed.Effects.DeleteBeats(); s.Settle(); s.Fret(3);
        s.Expect("the 3 is a half (nothing was deleted)", s.NoteAt(0, 0, 0) is not null && s.Cell(0, 0).DurationDenominator == 2, "3/2", GlAt(s));
    }

    private static void GlInsertBarWithSelection(MainWindow w)
    {
        var s = GnOpenNew(w, "L4 insert bar with a selection");
        s.Begin("Shift+Left Ctrl+Ins 5");
        s.Key(Key.Left, ModifierKeys.Shift); s.Key(Key.Insert, ModifierKeys.Control); s.Fret(5);
        s.Expect("one bar holding the 5", s.Track.Measures.Count == 1 && s.NoteAt(0, 0, 0)?.Fret == 5, "5", GlAt(s));
        s = GnOpenNew(w, "L4b insert bar with a two-beat selection");
        s.Begin("5 Right 7 Shift+Left Ctrl+Ins 9");
        s.Fret(5); s.Key(Key.Right); s.Fret(7); s.Key(Key.Left, ModifierKeys.Shift); s.Key(Key.Insert, ModifierKeys.Control); s.Fret(9);
        var frets = s.Beats(0).Select(i => s.Cell(0, i)).Where(c => c.Notes.Count > 0).Select(c => c.Notes[0].Fret).ToList();
        s.Expect("one bar, 9 7", s.Track.Measures.Count == 1 && frets.SequenceEqual(new[] { 9, 7 }), "9 7", GlAt(s));
        s = GnOpenNew(w, "L4c insert bar without a selection");
        s.Begin("Ctrl+Ins 5");
        s.Key(Key.Insert, ModifierKeys.Control); s.Fret(5);
        s.Expect("two bars, the 5 in bar 1", s.Track.Measures.Count == 2 && s.NoteAt(0, 0, 0)?.Fret == 5, "5 | -", GlAt(s));
    }
}
