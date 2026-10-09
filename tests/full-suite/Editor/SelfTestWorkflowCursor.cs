using System.Linq;
using System.Windows.Input;
using TabForge.Views;

namespace TabForge;

// Owns: the workflow stories about the cursor and the selection: plain navigation and typing end a selection, the cursor
//     on the beats of an overfull bar, Up / Down wrapping within the beat, deleting a bar while a selection is up (GP5 is the reference in each check).
// Does not own: the input helpers (WorkflowKit.cs), the other stories (SelfTestWorkflow.cs).
// Tests: TestWorkflow (W16-W19).
public static partial class SelfTest
{
    // ---------- W16 ----------
    // Type 3 5 7 5, select three beats and copy, go to the end, type 12, Backspace, paste: everything acts on the cursor.
    private static void WfStaleSelection(MainWindow w)
    {
        var s = WfOpen(w, "W16 stale selection");
        s.Begin("type 3 5 7 5 in bar 1");
        for (var i = 0; i < 4; i++) { if (i > 0) s.Key(Key.Right); s.Fret(new[] { 3, 5, 7, 5 }[i]); }
        var bar1 = s.Dump(0);
        s.Begin("Home, Shift+Right x3, Ctrl+C");
        s.Key(Key.Home); s.Repeat(Key.Right, 3, ModifierKeys.Shift); s.Ctrl(Key.C);
        s.Expect("Shift+Right x3 selects", s.Ed.HasSelection, "Shift+arrows select beats", s.Cursor);
        s.Begin("Ctrl+End, End, Right");
        s.Ctrl(Key.End);
        s.Expect("Ctrl+End ends the selection", !s.Ed.HasSelection, "plain navigation drops the highlight", s.Cursor);
        s.Key(Key.End); s.Key(Key.Right);
        var bar = s.Ed.SelectedMeasure;
        s.Begin("type 12, Backspace");
        s.Fret(12);
        s.Expect("12 lands at the cursor", s.CursorCell?.Notes.Any(n => n.Fret == 12) == true, "typing goes to the cursor", s.Cursor + " " + s.Dump(bar));
        s.Key(Key.Back);
        s.Expect("Backspace removes the 12 and leaves bar 1 alone", s.Dump(0) == bar1 && s.Bar(bar).Cells.All(c => c.Notes.All(n => n.Fret != 12)),
            "Backspace acts on the cursor, not an old highlight", s.Dump(0) + " | " + s.Dump(bar));
        s.Expect("the cursor stays where the 12 was", s.Ed.SelectedMeasure == bar, "the cursor does not jump", s.Cursor);
        s.Begin("Ctrl+V");
        s.Ctrl(Key.V);
        s.Expect("the paste goes to the cursor bar, bar 1 unchanged", s.Dump(0) == bar1 && s.Bar(bar).Cells.Sum(c => c.Notes.Count) >= 3,
            "paste goes to the cursor", s.Dump(0) + " | " + s.Dump(bar));
        s.Begin("select, then type a fret");
        s.Click(0, 0, 0); s.Repeat(Key.Right, 2, ModifierKeys.Shift);
        s.Fret(9);
        s.Expect("typing a fret ends the selection", !s.Ed.HasSelection, "typing drops the highlight", s.Cursor);
    }

    // ---------- W17 ----------
    // Five quarters in a 4/4 bar; type 10 on the fifth, then Ctrl+Right.
    private static void WfOverfullBar(MainWindow w)
    {
        var s = WfOpen(w, "W17 overfull bar");
        s.Begin("3 5 7 8, insert a beat before the 5, type 6");
        for (var i = 0; i < 4; i++) { if (i > 0) s.Key(Key.Right); s.Fret(new[] { 3, 5, 7, 8 }[i]); }
        s.Key(Key.Home); s.Key(Key.Right); s.Key(Key.Insert); s.Fret(6);
        s.Expect("bar 1 holds five quarters", s.Beats(0).Count == 5, "Insert pushes the beats on", s.Dump(0));
        s.Begin("Right x3 to beat 5, type 10");
        s.Repeat(Key.Right, 3);
        var fifth = s.Beats(0).ElementAtOrDefault(4);
        s.Expect("Right reaches the fifth beat", s.Ed.SelectedMeasure == 0 && s.Ed.SelectedCell == fifth, "the cursor sits on real beats", s.Cursor);
        s.Fret(10);
        s.Expect("10 replaces the 8 on one beat", s.Beats(0).Count == 5 && s.NoteAt(0, fifth, 0)?.Fret == 10, "two digits make one fret", s.Dump(0));
        s.Expect("the cursor stays on the fifth beat", s.Ed.SelectedCell == fifth, "the cursor box is on the note", s.Cursor);
        s.Begin("Ctrl+Right");
        s.Ctrl(Key.Right);
        s.Expect("Ctrl+Right goes to bar 2", s.Ed.SelectedMeasure == 1, "next bar", s.Cursor);
        s.Begin("Ctrl+End, Ctrl+Right");
        var bars = s.Track.Measures.Count;
        s.Ctrl(Key.End); s.Ctrl(Key.Right);
        s.Expect("Ctrl+Right on the last bar goes on to a new bar", s.Track.Measures.Count == bars + 1 && s.Ed.SelectedMeasure == bars, "next bar", s.Cursor);
    }

    // ---------- W19 ----------
    // The big-run sequence that crashed: a selection, Ctrl+Left, Ctrl+Delete, Shift+Left, then the score is drawn.
    private static void WfDeleteBarWithSelection(MainWindow w)
    {
        var s = WfOpen(w, "W19 delete bar with a selection", bars: 1);
        Exception? unhandled = null;
        void OnUnhandled(object? o, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) { unhandled ??= e.Exception; e.Handled = true; }
        System.Windows.Threading.Dispatcher.CurrentDispatcher.UnhandledException += OnUnhandled;
        try
        {
            s.Begin("two bars of chords and short notes");
            s.Ctrl(Key.Right);
            s.Fret(5); s.Key(Key.Down); s.Fret(7); s.Key(Key.Right); s.Fret(10); s.Key(Key.Right); s.Fret(0); s.Key(Key.Down); s.Fret(17); s.Key(Key.Right);
            s.Key(Key.OemPlus); s.Fret(1); s.Key(Key.Right); s.Key(Key.OemPlus); s.Fret(15); s.Key(Key.Up); s.Fret(5); s.Key(Key.V); s.Key(Key.Right);
            s.Fret(8); s.Key(Key.Right); s.Key(Key.Down); s.Fret(17); s.Key(Key.Right); s.Fret(5); s.Key(Key.Right); s.Key(Key.OemMinus); s.Fret(3);
            s.Key(Key.Up); s.Fret(0); s.Key(Key.Right); s.Fret(0); s.Key(Key.Up); s.Fret(15); s.Key(Key.Right);
            s.Begin("Shift+Left, Ctrl+Left, Ctrl+Delete, Shift+Left, V V, Ctrl+Z, Ctrl+Insert");
            s.Key(Key.Left, ModifierKeys.Shift); s.Ctrl(Key.Left); s.Key(Key.Delete, ModifierKeys.Control);
            s.Key(Key.Left, ModifierKeys.Shift); s.Key(Key.V); s.Key(Key.V); s.Ctrl(Key.Z); s.Key(Key.Insert, ModifierKeys.Control);
            foreach (var bar in Enumerable.Range(0, s.Track.Measures.Count)) s.Drawn(bar);
            s.Expect("no exception while editing and drawing", unhandled is null, "GP5 never fails", unhandled?.GetType().Name + ": " + unhandled?.Message + " " + unhandled?.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("TabForge")));
            s.Expect("the cursor and any selection stay inside the song", s.Ed.SelectedMeasure < s.Track.Measures.Count && (!s.Ed.HasSelection || s.Ed.SelectionCellRange.EndMeasure < s.Track.Measures.Count),
                "indices stay valid", s.Cursor);
        }
        finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.UnhandledException -= OnUnhandled; }
    }

    // ---------- W18 ----------
    private static void WfStringWrap(MainWindow w)
    {
        var s = WfOpen(w, "W18 string wrap");
        s.Fret(3); s.Key(Key.Right); s.Fret(5);
        var cell = s.Ed.SelectedCell;
        s.Begin("Up on the top string");
        s.Key(Key.Up);
        s.Expect("Up wraps to the bottom string of the same beat", s.Ed.SelectedCell == cell && s.Ed.SelectedString == s.Track.StringTunings.Count - 1, "Up wraps within the beat", s.Cursor);
        s.Begin("Down on the bottom string");
        s.Key(Key.Down);
        s.Expect("Down wraps to the top string of the same beat", s.Ed.SelectedCell == cell && s.Ed.SelectedString == 0, "Down wraps within the beat", s.Cursor);
    }
}
