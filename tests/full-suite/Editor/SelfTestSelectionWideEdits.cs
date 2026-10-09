using TabForge.Models;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>Delete, insert/delete beats, tenuto and pitch shift act on the selected range across bar lines, in one undo step; with no selection they act on the cursor beat.</summary>
    private static void TestSelectionWideEdits()
    {
        static TabCell Beat(int fret) => new() { DurationDenominator = 16, Notes = { new TabNote { StringIndex = 1, Fret = fret, MidiValue = 50 + fret } } };
        (Views.TabEditorControl Editor, SongProject Project, Func<int> Steps) Make()
        {
            var project = Presets.TemplateFactory.Create("Rock Band");
            var e = new Views.TabEditorControl { Project = project, SelectedTrackIndex = 0 };
            var steps = 0;
            e.EditStarting += (_, _) => steps++;
            var track = project.Tracks[0];
            foreach (var bar in new[] { 0, 1 })
                for (var i = 0; i < 4; i++) track.Measures[bar].Cells[i] = Beat(i + 1);
            return (e, project, () => steps);
        }

        // Delete over a range that crosses a bar line: as GP5 (CHANGELOG), every selected beat is removed and the following beats of its bar move left, one undo step.
        var (ed, song, steps) = Make();
        int Fret(List<TabCell> cells, int i) => i < cells.Count && cells[i].Notes.Count == 1 ? cells[i].Notes[0].Fret : -1;
        ed.SelectRange(0, 2, 1, 1);
        ed.Effects.DeleteBeat();
        var m0 = song.Tracks[0].Measures[0].Cells; var m1 = song.Tracks[0].Measures[1].Cells;
        Check("selection delete: beats 2-3 of bar 1 and 0-1 of bar 2 are removed, the others kept and moved left, one undo step",
            Fret(m0, 0) == 1 && Fret(m0, 1) == 2 && !m0.Skip(2).Any(c => c.Notes.Count > 0) &&
            Fret(m1, 0) == 3 && Fret(m1, 1) == 4 && steps() == 1,
            $"bar 1 {string.Join(",", m0.Select(c => c.Notes.Count))}; bar 2 {string.Join(",", m1.Select(c => c.Notes.Count))}; steps {steps()}");

        // Tenuto over a range: on for all, one step; again: off.
        var (ed2, song2, steps2) = Make();
        ed2.SelectRange(0, 3, 1, 0);
        ed2.Effects.ToggleTenuto();
        var a = song2.Tracks[0].Measures[0].Cells; var b = song2.Tracks[0].Measures[1].Cells;
        Check("selection tenuto: both ends marked, neighbours not, one undo step", a[3].Tenuto && b[0].Tenuto && !a[2].Tenuto && !b[1].Tenuto && steps2() == 1);

        // Pitch up over a range: every note moves.
        var (ed3, song3, steps3) = Make();
        ed3.SelectRange(0, 0, 1, 0);
        ed3.Effects.ShiftPitch(1);
        var c = song3.Tracks[0].Measures[0].Cells; var d = song3.Tracks[0].Measures[1].Cells;
        Check("selection pitch shift: every selected note up a semitone, one undo step",
            c[0].Notes[0].Fret == 2 && c[3].Notes[0].Fret == 5 && d[0].Notes[0].Fret == 2 && d[1].Notes[0].Fret == 2 && steps3() == 1);

        // Delete beats: removes the selected slots in each selected bar and closes the gap; bar length stays.
        var (ed4, song4, steps4) = Make();
        var len0 = song4.Tracks[0].Measures[0].Cells.Count;
        ed4.SelectRange(0, 3, 1, 0);
        ed4.Effects.DeleteBeats();
        var e0 = song4.Tracks[0].Measures[0].Cells; var e1 = song4.Tracks[0].Measures[1].Cells;
        Check("selection delete beats: later beats close the gap in each bar, bar length unchanged, one undo step",
            e0.Count == len0 && e0[2].Notes[0].Fret == 3 && e0[3].Notes.Count == 0 &&
            e1.Count == len0 && e1[0].Notes[0].Fret == 2 && e1[2].Notes[0].Fret == 4 && e1[3].Notes.Count == 0 && steps4() == 1);

        // Insert beat: one empty beat at the start of the range in each selected bar.
        var (ed5, song5, steps5) = Make();
        ed5.SelectRange(0, 1, 1, 1);
        ed5.Effects.InsertBeat();
        var f0 = song5.Tracks[0].Measures[0].Cells; var f1 = song5.Tracks[0].Measures[1].Cells;
        Check("selection insert beat: one empty beat at the range start only (the next bar is untouched), the rest of that bar shifted right, one undo step",
            f0[0].Notes[0].Fret == 1 && f0[1].Notes.Count == 0 && f0[2].Notes[0].Fret == 2 &&
            f1[0].Notes[0].Fret == 1 && f1[1].Notes[0].Fret == 2 && steps5() == 1);

        // No selection: the cursor beat only.
        var (ed6, song6, steps6) = Make();
        ed6.SetPosition(0, 1, 1, false);
        ed6.Effects.DeleteBeat();
        var g = song6.Tracks[0].Measures[0].Cells;
        Check("no selection: delete beat clears only the cursor beat", g[0].Notes.Count == 1 && g[1].Notes.Count == 0 && g[2].Notes.Count == 1 && steps6() == 1);
        ed6.SetPosition(0, 2, 1, false);
        ed6.Effects.ToggleTenuto();
        Check("no selection: tenuto marks only the cursor beat", g[2].Tenuto && !g[3].Tenuto);
    }
}
