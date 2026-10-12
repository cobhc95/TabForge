using System.Windows.Input;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// Owns: the check that Cut and Delete on the same selected beats leave the same bar (beats, lengths, onsets, cell count, overfull mark).
// Does not own: the commands (EditCommands.CutClear / DeleteBeatsAt / TidyAfterRemoval, ScoreEditCommands.DeleteBeat), the input helpers (WorkflowKit.cs).
public static partial class SelfTest
{
    /// <summary>Rest fill on, keys through the window: in an overfull bar (a half and three quarters) Cut and Delete on the same beats leave the same
    /// tidy bar of sixteen cells, and Delete across a barline leaves the next bar, now without notes, as one whole-bar rest.</summary>
    private static void TestCutTidiesLikeDelete()
    {
        var w = SmNewWindow();
        var previous = DialogHost.Capture;
        DialogHost.Capture = d => d is PasteOptionsDialog;
        try
        {
            static string Bar(Wf s, int bar)
            {
                var cells = s.Bar(bar).Cells; var on = BarGrid.Onsets(cells);
                return string.Join(" ", cells.Select((c, i) => (c, i)).Where(x => x.c.Notes.Count > 0 || x.c.IsRest).Select(x => $"{(x.c.Notes.Count > 0 ? "n" : "r")}/{x.c.DurationDenominator}@{on[x.i]:0.##}"))
                    + $" [{cells.Count} cells{(s.State(bar).Error ? ", overfull" : "")}]";
            }
            Wf Overfull(string name)
            {
                var s = GnOpenNew(w, name); s.Ed.FillBarsWithRests = true; s.Click(0, 0, 0);
                foreach (var f in new[] { 5, 6, 7, 8 }) { s.Fret(f); s.Key(Key.Right); }
                s.Click(0, 0, 0); SmCall(s.Window, "RunHotkey", "Note.Longer"); s.Settle();   // a half and three quarters: five beats of time
                return s;
            }
            foreach (var (kind, expect) in new[] { ("the first two beats", "n/4@0 n/4@4 r/2@8 [16 cells]"), ("the last two beats", "n/2@0 n/4@8 r/4@12 [16 cells]") })
            {
                var got = new Dictionary<string, string>();
                foreach (var op in new[] { "Delete", "Cut" })
                {
                    var s = Overfull($"{op} {kind} of an overfull bar");
                    s.Begin($"half + three quarters, select {kind}, {op}");
                    s.Expect("the bar starts overfull", s.State(0).Error, "an overfull bar is marked", Bar(s, 0));
                    if (kind == "the last two beats") { s.Key(Key.Right); s.Key(Key.Right); }
                    s.Key(Key.Right, ModifierKeys.Shift);
                    if (op == "Cut") s.Ctrl(Key.X); else s.Key(Key.Delete);
                    got[op] = Bar(s, 0);
                    s.Expect("the bar is tidy: the beats left, the fill rests, sixteen cells, no mark", got[op] == expect, "the bar closes up", $"{got[op]}, expected {expect}");
                }
                Check($"workflow: Cut and Delete on {kind} of an overfull bar leave the same bar", got["Cut"] == got["Delete"], $"Cut {got["Cut"]} | Delete {got["Delete"]}");
            }

            // Five quarters (the fifth in bar 2), all selected, Delete: bar 2 holds no note any more, so it is one whole-bar rest.
            var d = GnOpenNew(w, "Delete across the barline"); d.Ed.FillBarsWithRests = true; d.Click(0, 0, 0);
            foreach (var f in new[] { 5, 6, 7, 8, 9 }) { d.Fret(f); d.Key(Key.Right); }
            d.Click(0, 0, 0); d.Repeat(Key.Right, 4, ModifierKeys.Shift);
            d.Begin("five quarters, select all five, Delete");
            d.Key(Key.Delete);
            d.Expect("both bars are one whole-bar rest", Bar(d, 0) == "r/1@0 [16 cells]" && Bar(d, 1) == "r/1@0 [16 cells]", "a bar without notes is empty", $"bar 1 {Bar(d, 0)} | bar 2 {Bar(d, 1)}");
        }
        finally { DialogHost.Capture = previous; SmCloseWindow(w); }
    }
}
