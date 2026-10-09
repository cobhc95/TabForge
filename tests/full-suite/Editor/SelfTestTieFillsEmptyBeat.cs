using System.Linq;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// Owns: checks that the tie command on an empty beat after a chord writes a real tied note (the cursor string's note, at the
// beat's duration) that fills the bar, and that a second press removes it (as GP5: ties are per note).
// Does not own: the command (Views/Score/ScoreEditCommands.Ties.cs), bar length (MusicTime.AnalyzeBar).
// Tests: TestTieFillsEmptyBeat.
public static partial class SelfTest
{
    private static void TestTieFillsEmptyBeat()
    {
        var w = SmNewWindow();
        try
        {
            var doc = SmOpenSong(w, SmBlankSong(4));
            var ed = SmField<TabEditorControl>(w, "Editor")!;
            ed.SelectedTrackIndex = 0;
            void Enter(int cell, int s, int fret) { ed.SetPosition(0, cell, s); ed.Effects.SetDuration(8); ed.Effects.EnterFret(fret, autoAdvance: false); }
            for (var cell = 0; cell < 12; cell += 2) Enter(cell, 0, 5);
            for (var s = 0; s < 3; s++) Enter(12, s, 6);   // chord 6/6/6 on the seventh eighth
            SmCell(doc, 0, 14).IsRest = false; SmCell(doc, 0, 15).IsRest = false;   // the owner's case: an empty beat, no rest
            var shortBefore = MusicTime.AnalyzeBar(doc.Project, 0).Short;
            ed.SetPosition(0, 14, 0); ed.Effects.SetDuration(8); ed.Effects.ToggleTie();
            var tied = SmCell(doc, 0, 14);
            var bar = MusicTime.AnalyzeBar(doc.Project, 0);
            Check("tie on an empty beat copies the cursor string's note as a tied note",
                tied.Notes.Count == 1 && tied.Notes.All(n => n.Tied && n.Fret == 6 && n.StringIndex == 0) && tied.DurationDenominator == 8 && !tied.IsRest,
                $"{tied.Notes.Count} notes, frets {string.Join(",", tied.Notes.Select(n => $"s{n.StringIndex}f{n.Fret}{(n.Tied ? "t" : "")}"))}, dur {tied.DurationDenominator}");
            Check("a tied beat fills the bar (not short)", shortBefore && !bar.Short && bar.Complete, $"before short {shortBefore}, after {bar}, cells " + string.Join(" ", doc.Project.Tracks[0].Measures[0].Cells.Select((x, i) => $"{i}:{(x.IsRest ? "r" : x.Notes.Count.ToString())}/{x.DurationDenominator}{(x.IsTied ? "T" : "")}")));
            Check("the tie tool is lit on the tied beat", ed.Effects.GetToolState("duration:tie") == true);
            ed.Effects.ToggleTie();
            Check("a second press removes the tied note", tied.Notes.Count == 0 && !tied.IsTied, $"{tied.Notes.Count} notes, beat tied {tied.IsTied}");
            tied.Notes.Clear();

            tied.IsRest = true; ed.Effects.ToggleTie();
            Check("tie on the rest after a chord turns it into the tied cursor-string note", !tied.IsRest && tied.Notes.Count == 1 && tied.Notes.All(n => n.Tied));
            tied.Notes.Clear(); tied.IsRest = false;
            var held = SmCell(doc, 0, 14); held.IsTied = true;   // an empty beat carrying only the beat tie still holds its time
            Check("a beat-tie-only beat counts in the bar", !MusicTime.AnalyzeBar(doc.Project, 0).Short);
        }
        finally { SmCloseWindow(w); }
    }
}
