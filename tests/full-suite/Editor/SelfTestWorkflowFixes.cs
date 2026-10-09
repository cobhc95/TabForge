using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using TabForge.Models;
using TabForge.Views;
using TabForge.Views.Score;

namespace TabForge;

// Owns: one narrow check per editing-workflow fix: Delete on an empty string of a chord, the "." key toggling a single dot,
// keyboard moves scrolling the cursor into view, the no-note status after a successful Backspace, no mark for the default dynamic.
// Does not own: the commands (Views/Score/ScoreEditCommands*.cs), follow scrolling (Views/ScoreFollowCoordinator.cs).
// Tests: TestDeleteOnEmptyStringKeepsChord, TestDotKeyTogglesSingleDot, TestKeyMoveScrollsCursorIntoView,
//     TestBackspaceClearsNoNoteStatus, TestDefaultDynamicUnmarked.
public static partial class SelfTest
{
    private static void WfWithEditor(int bars, Action<MainWindow, Documents.DocumentSession, TabEditorControl> body)
    {
        var w = SmNewWindow();
        try
        {
            var doc = SmOpenSong(w, SmBlankSong(bars));
            var ed = SmField<TabEditorControl>(w, "Editor")!;
            ed.SelectedTrackIndex = 0;
            body(w, doc, ed);
        }
        finally { SmCloseWindow(w); }
    }

    private static void WfChord(TabEditorControl ed, int cell, params int[] strings)
    {
        ed.Effects.SetDuration(4);
        foreach (var s in strings) { ed.SetPosition(0, cell, s); ed.Effects.EnterFret(5, autoAdvance: false); }
    }

    private static void TestDeleteOnEmptyStringKeepsChord() => WfWithEditor(4, (w, doc, ed) =>
    {
        WfChord(ed, 0, 0, 1);
        ed.SetPosition(0, 0, 2);
        ed.Effects.DeleteBeat();
        var cell = SmCell(doc, 0, 0);
        Check("Delete on an empty string of a chord keeps the chord", cell.Notes.Count == 2 && !cell.IsRest, $"{cell.Notes.Count} notes, rest {cell.IsRest}");
        Eq("Delete on an empty string says there is no note", ScoreEditCommands.NoNoteMessage, SmStatus(w));
    });

    private static void TestDotKeyTogglesSingleDot() => WfWithEditor(4, (w, doc, ed) =>
    {
        WfChord(ed, 0, 0);
        ed.SetPosition(0, 0, 0);
        ed.Effects.ToggleDot();
        Eq("\".\" dots a plain beat", 1, SmCell(doc, 0, 0).Dots);
        ed.Effects.ToggleDot();
        Eq("\".\" again removes the dot (no double dot)", 0, SmCell(doc, 0, 0).Dots);
        ed.Effects.SetDots(2);
        Eq("double dot has its own command", 2, SmCell(doc, 0, 0).Dots);
    });

    private static void TestKeyMoveScrollsCursorIntoView() => WfWithEditor(48, (w, doc, ed) =>
    {
        var scroll = SmField<ScrollViewer>(w, "ScoreScroll")!;
        bool InView()
        {
            var top = ed.SystemTopForMeasure(ed.SelectedMeasure);
            return top >= scroll.VerticalOffset - 0.5 && top + ed.SystemHeightNow <= scroll.VerticalOffset + scroll.ViewportHeight + 0.5;
        }
        ed.SetPosition(0, 0, 0);
        for (var i = 0; i < 25; i++) ed.TryHandleKey(Key.Right, ModifierKeys.Control);
        SmSettle();
        Check("Ctrl+Right x25 scrolls bar 26 into view", ed.SelectedMeasure == 25 && InView(),
            $"bar {ed.SelectedMeasure + 1}, system {ed.SystemTopForMeasure(ed.SelectedMeasure):0}-{ed.SystemTopForMeasure(ed.SelectedMeasure) + ed.SystemHeightNow:0}, view {scroll.VerticalOffset:0}-{scroll.VerticalOffset + scroll.ViewportHeight:0}");
        var offset = scroll.VerticalOffset;
        ed.TryHandleKey(Key.Right, ModifierKeys.None);
        SmSettle();
        Check("a move inside the visible system does not scroll", Math.Abs(scroll.VerticalOffset - offset) < 0.5, $"{offset:0} -> {scroll.VerticalOffset:0}");
        ed.TryHandleKey(Key.Home, ModifierKeys.Control);
        SmSettle();
        Check("Ctrl+Home scrolls back to bar 1", ed.SelectedMeasure == 0 && InView(), $"view {scroll.VerticalOffset:0}");
    });

    private static void TestBackspaceClearsNoNoteStatus() => WfWithEditor(4, (w, doc, ed) =>
    {
        WfChord(ed, 0, 0);
        ed.SetPosition(0, 0, 3);
        ed.TryHandleKey(Key.Back, ModifierKeys.None);
        Eq("Backspace on an empty string says there is no note", ScoreEditCommands.NoNoteMessage, SmStatus(w));
        ed.SetPosition(0, 0, 0);
        ed.TryHandleKey(Key.Back, ModifierKeys.None);
        Check("Backspace that deletes a note shows no no-note message", SmCell(doc, 0, 0).Notes.Count == 0 && SmStatus(w) != ScoreEditCommands.NoNoteMessage, $"status \"{SmStatus(w)}\"");
    });

    private static void TestDefaultDynamicUnmarked() => WfWithEditor(4, (w, doc, ed) =>
    {
        WfChord(ed, 0, 0);
        WfChord(ed, 4, 0);
        WfChord(ed, 8, 0);
        var track = doc.Project.Tracks[0];
        Check("the default dynamic is not marked on a new song", ScorePassages.BuildDynamicMarks(track).Count == 0,
            string.Join(",", ScorePassages.BuildDynamicMarks(track).Values));
        SmCell(doc, 0, 4).Notes[0].Velocity = Dynamics.VelocityFor("p");
        var marks = ScorePassages.BuildDynamicMarks(track);
        Check("a change is marked, and the return to forte too", marks.Count == 2 && marks[SmCell(doc, 0, 4)] == "p" && marks[SmCell(doc, 0, 8)] == "f",
            string.Join(",", marks.Values));
    });
}
