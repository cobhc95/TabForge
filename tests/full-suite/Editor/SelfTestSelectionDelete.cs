using System.Linq;
using System.Windows.Input;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// Owns: Delete, Backspace, Cut and Copy on each kind of selection (cursor, one selected beat, a chord, several beats, a whole bar, several bars) touch exactly
//     the selected thing: one selected beat becomes a rest where it stands and nothing after it moves, other bars stay as they are.
// Does not own: the commands (ScoreEditCommands.DeleteBeat / DeleteNote, EditCommands.CutClear), the input helpers (WorkflowKit.cs).
// Tests: TestSelectionDelete (--areas workflow).
public static partial class SelfTest
{
    private static void TestSelectionDelete()
    {
        var w = SmNewWindow();
        var previous = DialogHost.Capture;
        DialogHost.Capture = d => d is PasteOptionsDialog;
        try
        {
            // Frets at cells 0, 4, 8, 12 of a bar: -1 for a rest or nothing.
            static string Frets(Wf s, int bar) => bar >= s.Track.Measures.Count || s.Track.Measures[bar].Cells.Count < 13 ? "(no such bar)" : string.Join(" ", new[] { 0, 4, 8, 12 }.Select(c => s.Cell(bar, c).Notes.Count == 0 ? "-" : string.Join("+", s.Cell(bar, c).Notes.OrderBy(n => n.StringIndex).Select(n => n.Fret))));
            Wf Fresh(string name)
            {
                var s = GnOpenNew(w, name);
                foreach (var bar in new[] { 0, 1 })
                {
                    s.Click(bar, 0, 0);
                    foreach (var f in new[] { 4, 5, 6, 7 }) { s.Fret(f); s.Key(Key.Right); }
                }
                s.Click(0, 4, 1); s.Fret(9);   // a second note: the beat at cell 4 is a chord (strings 1 and 2)
                s.Click(0, 4, 0);
                return s;
            }
            void Select(Wf s, string kind)
            {
                switch (kind)
                {
                    case "one beat": s.Click(0, 4, 0); s.Ed.ShiftClickExtend(0, 4, 0); SmSettle(); break;
                    case "two beats": s.Click(0, 4, 0); s.Key(Key.Right, ModifierKeys.Shift); break;
                    case "whole bar": s.Click(0, 0, 0); s.Repeat(Key.Right, 3, ModifierKeys.Shift); break;
                    case "bars 1 to 2": s.Click(0, 0, 0); s.Ed.ShiftClickExtend(1, 12, 0); SmSettle(); break;
                    default: s.Click(0, 4, 0); break;
                }
            }
            // Expected frets of bar 1 after Delete / Backspace / Cut, the other bar, and how many beats a copy holds. A cut (and Delete on several
            // beats) removes the beats and the following ones move left, as GP5; one selected beat under Delete or Backspace becomes a rest in place.
            foreach (var (kind, delete, back, cut, bar3, copied) in new (string, string, string, string, string, int)[]
            {
                // The cursor sits on string 1 of the chord at cell 4: Delete takes that note, Backspace likewise; Cut takes the beat.
                ("cursor", "4 9 6 7", "4 9 6 7", "4 6 7 -", "4 5 6 7", 1),
                ("one beat", "4 - 6 7", "4 - 6 7", "4 6 7 -", "4 5 6 7", 1),
                ("two beats", "4 7 - -", "4 - - 7", "4 7 - -", "4 5 6 7", 2),
                ("whole bar", "- - - -", "- - - -", "- - - -", "4 5 6 7", 4),
                ("bars 1 to 2", "- - - -", "- - - -", "- - - -", "- - - -", 8),
            })
            {
                foreach (var (op, expect) in new (string, string)[] { ("Delete", delete), ("Backspace", back), ("Cut", cut) })
                {
                    var s = Fresh($"{op} on {kind}");
                    Select(s, kind);
                    s.Begin($"{op} on {kind}");
                    if (op == "Cut") s.Ctrl(Key.X); else s.Key(op == "Delete" ? Key.Delete : Key.Back);
                    var got = Frets(s, 0);
                    s.Expect("the bar holds what is left", got == expect,
                        "only the selected beats go", $"bar 1 {got}, expected {expect}; {GnCursor(s)}");
                    s.Expect("bar 2 is as it should be", Frets(s, 1) == (op == "Cut" && kind == "bars 1 to 2" ? "(no such bar)" : bar3),   // a cut of whole bars takes the bars out
                         "other bars stay", $"bar 2 {Frets(s, 1)}, expected {bar3}");
                }
                var c = Fresh($"Copy on {kind}");
                Select(c, kind);
                c.Begin($"Copy on {kind}");
                var clip = c.Ed.CaptureClip(out _);
                var beats = clip?.Tracks[0].Events.Count(e => e.Cell.Notes.Count > 0 || e.Cell.IsRest) ?? -1;
                c.Expect("the copy holds the selected beats", clip is not null && beats >= 1 && (kind == "bars 1 to 2" || beats <= copied), "copies the selection", $"{beats} beats, expected up to {copied}");
            }
            // Cut on selected empty bars removes them as Delete does; Copy on them changes nothing; Undo brings them back.
            foreach (var op in new[] { "Cut", "Copy" })
            {
                DialogHost.Capture = d => { if (d is ThemedConfirmDialog t) t.AnswerForTest(System.Windows.MessageBoxResult.Yes); return d is PasteOptionsDialog || d is ThemedConfirmDialog; };
                var s = GnOpenNew(w, op + " on empty bars");
                s.Click(0, 0, 0); s.Fret(3);
                s.Ed.SelectRange(0, 0, 0, 0); s.Ed.ClearSelection();
                for (var i = 0; i < 5; i++) s.Key(Key.Insert, ModifierKeys.Control);   // empty bars after bar 1
                var bars = s.Track.Measures.Count; var hash = Convert.ToHexString(ProjectService.ContentHash(s.Song));
                s.Ed.SelectRange(2, 0, bars - 1, -1); SmSettle();
                s.Begin($"{op} on bars 3 to {bars} (empty)");
                if (op == "Delete") s.Key(Key.Delete); else s.Ctrl(op == "Cut" ? Key.X : Key.C);
                var removed = op != "Copy";
                s.Expect(removed ? "the empty bars are gone" : "nothing changes", s.Track.Measures.Count == (removed ? 2 : bars) && (removed || Convert.ToHexString(ProjectService.ContentHash(s.Song)) == hash), "same as Delete", $"{s.Track.Measures.Count} bars of {bars}");
                if (removed) { s.Ctrl(Key.Z); s.Expect("Undo brings the bars back", s.Track.Measures.Count == bars && Convert.ToHexString(ProjectService.ContentHash(s.Song)) == hash, "undo", $"{s.Track.Measures.Count} bars"); }
            }
        }
        finally { DialogHost.Capture = previous; SmCloseWindow(w); }
    }
}
