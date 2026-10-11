using TabForge.Models;
using TabForge.Services;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>Rest fill on, cursor on a note (no selection): a changed length moves the notes after it; automatic rests sit only at the bar end; a written rest between notes stays.</summary>
    private static void TestGapRestCloseUp()
    {
        static TabCell Note(int fret) => new() { DurationDenominator = 4, Notes = { new TabNote { StringIndex = 1, Fret = fret, MidiValue = 50 + fret } } };
        static bool IsBeat(TabCell c) => c.Notes.Count > 0 || c.IsRest;
        (Views.TabEditorControl Editor, SongProject Project) Make(params (int At, TabCell Cell)[] beats)
        {
            var project = Presets.TemplateFactory.Create("Rock Band");
            var filled = project.Tracks[0].Measures[0].Cells;
            foreach (var (at, cell) in beats) filled[at] = cell;
            BarFill.FillCells(filled, 16, always: true);
            var e = new Views.TabEditorControl { Project = project, SelectedTrackIndex = 0, FillBarsWithRests = true, MergeRestsOnDelete = false, AutoAdvanceAfterEntry = false };
            e.SetPosition(0, 0, 1, false);
            return (e, project);
        }

        var (ed, song) = Make((0, Note(1)), (4, Note(2)));
        ed.Effects.SetDuration(8);
        var cells = song.Tracks[0].Measures[0].Cells;
        Check("gap rest: shortening the first note moves the second next to it, with no rest between",
            cells[0].DurationDenominator == 8 && cells[2].Notes.Count == 1 && cells[2].Notes[0].Fret == 2 && !IsBeat(cells[1]) && cells.Sum(c => c.Notes.Count) == 2);
        Check("gap rest: the freed time is rest at the end and the bar is full",
            cells.Skip(3).Any(c => c.IsRest) && cells.Skip(3).All(c => c.Notes.Count == 0) && !MusicTime.AnalyzeBar(song, 0).Marked);
        ed.Effects.SetDuration(4);
        cells = song.Tracks[0].Measures[0].Cells;
        Check("gap rest: lengthening it again moves the second note back and the end rest shrinks",
            cells[4].Notes.Count == 1 && !cells.Take(4).Any(c => c.IsRest) && !MusicTime.AnalyzeBar(song, 0).Marked);

        var (ed2, song2) = Make((0, Note(1)), (4, new TabCell { IsRest = true, WrittenRest = true, DurationDenominator = 4 }), (8, Note(3)));
        ed2.Effects.SetDuration(8);
        var cells2 = song2.Tracks[0].Measures[0].Cells;
        Check("gap rest: a written rest between notes stays and moves with them",
            cells2[2].IsRest && cells2[2].WrittenRest && cells2[2].DurationDenominator == 4 && cells2[6].Notes.Count == 1 && !MusicTime.AnalyzeBar(song2, 0).Marked);
    }
}
