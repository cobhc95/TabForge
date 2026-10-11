using System.Linq;
using System.Windows.Input;
using TabForge.Models;
using TabForge.Views;

namespace TabForge;

// Owns: a paused transport's green playhead line follows the score's re-layout after every edit (paste, cut, delete, bar insert and delete, undo, redo):
//     the overlay's drawn position equals the position the score computes now, and sits in the bar the editor cursor is in.
// Does not own: the overlay (PlayheadOverlay), the tick (PlaybackViewController), the input helpers (WorkflowKit.cs).
// Tests: TestPlayheadAfterEdit (--areas workflow).
public static partial class SelfTest
{
    private static void TestPlayheadAfterEdit()
    {
        var w = SmNewWindow();
        var previous = DialogHost.Capture;
        DialogHost.Capture = d => d is PasteOptionsDialog;
        try
        {
            var steps = new (string Name, Action<Wf> Do)[]
            {
                ("Ctrl+V (12 beats at bar 3)", s => s.Ctrl(Key.V)),
                ("Ctrl+Z", s => s.Ctrl(Key.Z)),
                ("Ctrl+Y", s => s.Ctrl(Key.Y)),
                ("Ctrl+Insert (insert bar)", s => s.Key(Key.Insert, ModifierKeys.Control)),
                ("Ctrl+Delete (delete bar)", s => s.Key(Key.Delete, ModifierKeys.Control)),
                ("Delete", s => s.Key(Key.Delete)),
            };
            foreach (var (name, act) in steps)
            {
                var s = GnOpenNew(w, "playhead after " + name);
                s.Tool("duration:eighth");
                for (var b = 0; b < 2; b++) foreach (var f in new[] { 0, 0, 0, 2, 0, 0, 0, 2 }) { s.Fret(f); s.Key(Key.Right); }
                s.Click(0, 0, 0); s.Repeat(Key.Right, 12, ModifierKeys.Shift); s.Ctrl(Key.C);
                s.Click(2, 2, 0);
                s.Key(Key.Space); s.Key(Key.Space);   // play, pause
                s.Begin("paused at bar 3, " + name);
                s.Ctrl(Key.V);   // a first paste for undo to take back, and the layout to change
                act(s);
                var drawn = (ValueTuple<double, double, double>?)typeof(PlayheadOverlay).GetField("_geometry", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(SmField<PlayheadOverlay>(w, "Playhead"));
                var now = s.Ed.Playback.PlayheadGeometry();
                s.Expect("the green line is where the score puts it now", drawn is { } d && now is { } n && Math.Abs(d.Item1 - n.X) < 0.6 && Math.Abs(d.Item2 - n.Top) < 0.6,
                    "line follows the layout", $"drawn {drawn?.Item1:0.#} computed {now?.X:0.#}");
                SmCall(w, "StopPlayback");
            }
        }
        finally { DialogHost.Capture = previous; SmCloseWindow(w); }
    }
}
