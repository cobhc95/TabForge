using TabForge.Models;
using TabForge.Views;

namespace TabForge;

// Owns: checks that accent, tie, technique, ghost, slide and fermata tools toggle and never create notes on empty strings.
// Does not own: the commands (Views/Score/ScoreEditCommands*.cs).
// Tests: TestEditToolToggles.
public static partial class SelfTest
{
    private static void TestEditToolToggles()
    {
        var w = SmNewWindow();
        try
        {
            var doc = SmOpenSong(w, SmBlankSong(6));
            var ed = SmField<TabEditorControl>(w, "Editor")!;
            ed.SelectedTrackIndex = 0;
            TabCell Note(int bar, int cell, int s, int fret)
            {
                ed.SetPosition(bar, cell, s); ed.Effects.SetDuration(4); ed.Effects.EnterFret(fret, autoAdvance: false);
                return SmCell(doc, bar, cell);
            }

            SmStep("accent palette toggles off", () =>
            {
                var c = Note(0, 0, 0, 5);
                ed.Effects.SetAccent(1); ed.Effects.SetAccent(1);
                Check("accent: palette Accent pressed twice removes it", c.Accent == 0, $"accent {c.Accent}");
                ed.Effects.SetAccent(2); ed.Effects.SetAccent(2);
                Check("heavy accent: pressed twice removes it", c.Accent == 0, $"accent {c.Accent}");
            });
            SmStep("double dot toggles off", () =>
            {
                var c = Note(0, 4, 0, 5);
                ed.Effects.SetDots(2); ed.Effects.SetDots(2);
                Check("double dot: pressed twice removes it", c.Dots == 0, $"dots {c.Dots}");
            });
            SmStep("tie note toggles off", () =>
            {
                Note(4, 4, 0, 5); var c = Note(4, 8, 0, 5);
                ed.SetPosition(4, 8, 0); var can = ed.Effects.CanTieSelectedNote(); ed.Effects.TieSelectedNote(); var tied = c.Notes[0].Tied;
                ed.Effects.TieSelectedNote();
                Check("tie note: pressed twice unties", tied && !c.Notes[0].Tied, $"can {can}, sel {ed.HasSelection}, midi {SmCell(doc, 4, 4).Notes[0].MidiValue}/{c.Notes[0].MidiValue}, cursor {ed.SelectedMeasure}:{ed.SelectedCell}:{ed.SelectedString}, first {tied}, cells " + string.Join(" ", doc.Project.Tracks[0].Measures[4].Cells.Take(12).Select((x, i) => $"{i}:{(x.IsRest ? "r" : x.Notes.Count.ToString())}/{x.DurationDenominator}@{x.RhythmicPosition}")));
                c.Notes[0].Tied = true;
                ed.Effects.ToggleTie();
                Check("tie (L / palette): one press removes the tied note (GP5: ties are per note)", c.Notes.Count == 0 && ed.Effects.GetToolState("duration:tie") != true,
                    $"notes {c.Notes.Count}, beat tied {c.IsTied}, lit {ed.Effects.GetToolState("duration:tie")}");
            });
            SmStep("technique on an empty string", () =>
            {
                var c = Note(2, 0, 0, 7);
                ed.SetPosition(2, 0, 3); ed.Effects.ToggleTechnique(TechniqueNames.Vibrato);
                Check("vibrato on an empty string of a chord adds no note", c.Notes.Count == 1, $"{c.Notes.Count} notes, frets {string.Join(",", c.Notes.Select(n => n.Fret))}");
                ed.SetPosition(2, 0, 4); ed.Effects.ToggleGhost();
                Check("ghost on an empty string adds no fret-5 note", c.Notes.All(n => n.StringIndex != 4), string.Join(",", c.Notes.Select(n => $"s{n.StringIndex}f{n.Fret}")));
            });
            SmStep("slide lit state matches toggle", () =>
            {
                var c = Note(2, 4, 0, 7);
                c.Notes[0].Techniques.Add("ShiftSlide");
                ed.SetPosition(2, 4, 0);
                ed.Effects.ToggleTechnique(TechniqueNames.LegatoSlide);
                Check("slide: one press on a lit Slide clears every slide kind", ed.Effects.GetToolState("effect:slides") != true,
                    string.Join(",", c.Notes[0].Techniques));
            });
            SmStep("fermata on a selection", () =>
            {
                var a = Note(3, 0, 0, 5); var b = Note(3, 4, 0, 5);
                ed.SelectRange(3, 0, 3, 4); ed.Effects.ToggleFermata();
                Check("fermata acts on every selected beat", a.Fermata && b.Fermata, $"a {a.Fermata}, b {b.Fermata}");
            });
        }
        finally { SmCloseWindow(w); }
    }
}
