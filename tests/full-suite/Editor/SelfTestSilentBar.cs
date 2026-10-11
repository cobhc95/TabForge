using System.Linq;
using System.Windows.Input;
using TabForge.Models;
using TabForge.Playback;

namespace TabForge;

// Owns: a seek back inside the bar the transport started in keeps that bar's earlier beats (a timeline compiled from a later cell has none of them).
// Does not own: the schedule reuse itself (PlaybackScheduleReuse), the input helpers (WorkflowKit.cs).
// Tests: TestSilentBarAfterSeekBack (--areas workflow).
public static partial class SelfTest
{
    private static void TestSilentBarAfterSeekBack()
    {
        var w = SmNewWindow();
        try
        {
            var s = GnOpenNew(w, "seek back in the start bar");
            var field = typeof(PlaybackEngine).GetField("_timeline", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            int Bar0Notes() => ((ScoreTimeline?)field.GetValue(s.Doc.Playback.Engine))?.Notes.Count(n => n.Bar == 0) ?? -1;
            s.Tool("duration:eighth");
            foreach (var f in new[] { 0, 0, 0, 2, 0, 0, 0, 2 }) { s.Fret(f); s.Key(Key.Right); }
            s.Click(0, 8, 0);
            s.Begin("Play from cell 8, Space pause, Left to cell 0, Space");
            s.Key(Key.Space);
            s.Key(Key.Space);
            s.Repeat(Key.Left, 4);
            s.Expect("the cursor is on cell 0 of bar 1", s.Ed.SelectedMeasure == 0 && s.Ed.SelectedCell == 0, "cursor on beat 1", s.Cursor);
            s.Key(Key.Space);
            s.Expect("bar 1 still has all 8 notes to play", Bar0Notes() == 8, "plays the whole bar from beat 1", $"{Bar0Notes()} notes of bar 1 in the playing timeline");
            SmCall(w, "StopPlayback");
        }
        finally { SmCloseWindow(w); }
    }
}
