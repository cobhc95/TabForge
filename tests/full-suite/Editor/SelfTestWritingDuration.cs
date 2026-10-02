using TabForge.Models;
using TabForge.Services;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>The remembered writing duration: typed notes on a rest use it (consecutive notes, cursor advances), a note keeps its own length, the tools show what typing will do.</summary>
    private static void TestWritingDuration()
    {
        foreach (var fill in new[] { true, false })
        {
            var tag = fill ? "fill on" : "fill off";
            var project = Presets.TemplateFactory.Create("Rock Band");
            var ed = new Views.TabEditorControl { Project = project, SelectedTrackIndex = 0, FillBarsWithRests = fill, AutoAdvanceAfterEntry = true };
            var steps = 0;
            ed.EditStarting += (_, _) => steps++;
            if (fill) ed.EmptyBar();                       // one whole-bar rest
            var bar = project.Tracks[0].Measures[0].Cells;
            ed.CurrentDurationDenominator = 16;
            ed.SetPosition(0, 0, 1, false);
            Check($"writing duration ({tag}): on a rest the tools show the writing duration", ed.GetToolState("duration:sixteenth") == true);
            var before = steps;
            foreach (var fret in new[] { 5, 7, 5, 8 }) ed.EnterFret(fret);
            Check($"writing duration ({tag}): typing on a rest writes consecutive 16th notes, one undo step each",
                new[] { 0, 1, 2, 3 }.All(i => bar[i].Notes.Count == 1 && bar[i].DurationDenominator == 16) && steps - before == 4 && ed.SelectedCell == 4);
            if (fill) Check("writing duration (fill on): the rest remainder is refilled, the bar adds up", bar[4].IsRest && !MusicTime.AnalyzeBar(project, 0).Marked);

            // A quarter note elsewhere: the tools show its length, typing keeps it, back on a rest the writing duration returns.
            var quarter = bar[8];
            quarter.IsRest = false; quarter.DurationDenominator = 4; quarter.Notes.Clear();
            quarter.Notes.Add(new TabNote { StringIndex = 1, Fret = 3, MidiValue = 53 });
            ed.SetPosition(0, 8, 1, false);
            Check($"writing duration ({tag}): on a note the tools show its length", ed.GetToolState("duration:quarter") == true && ed.GetToolState("duration:sixteenth") == false);
            ed.EnterFret(9);
            Check($"writing duration ({tag}): typing on a note keeps its length", quarter.Notes[0].Fret == 9 && quarter.DurationDenominator == 4 && ed.CurrentDurationDenominator == 16);
            ed.SetPosition(0, 4, 1, false);
            Check($"writing duration ({tag}): back on a rest the tools show the writing duration again", ed.GetToolState("duration:sixteenth") == true);

            // Picking an eighth on a rest sets the writing duration; the next notes are eighths.
            ed.SetPosition(0, 12, 1, false);
            ed.SetDuration(8);
            Check($"writing duration ({tag}): picking 8th on a rest sets the writing duration", ed.CurrentDurationDenominator == 8 && ed.GetToolState("duration:eighth") == true);
            ed.SetPosition(0, 12, 1, false);
            ed.EnterFret(2);
            Check($"writing duration ({tag}): the next note is an eighth", bar[12].Notes.Count == 1 && bar[12].DurationDenominator == 8);
        }
    }

    /// <summary>Selections are made of whole beats: a range that starts or ends inside a beat covers that whole beat.</summary>
    private static void TestSelectionWholeBeats()
    {
        var project = Presets.TemplateFactory.Create("Rock Band");
        var ed = new Views.TabEditorControl { Project = project, SelectedTrackIndex = 0, FillBarsWithRests = true, AutoAdvanceAfterEntry = true };
        var measures = project.Tracks[0].Measures;
        foreach (var bar in new[] { measures[0], measures[^1] }) { bar.Cells[0] = new TabCell { IsRest = true, DurationDenominator = 1 }; for (var i = 1; i < bar.Cells.Count; i++) bar.Cells[i] = new TabCell(); }   // whole-bar rests
        ed.CurrentDurationDenominator = 4;
        ed.SetPosition(1, 0, 1, false);
        foreach (var fret in new[] { 1, 2, 3, 4 }) ed.EnterFret(fret);   // bar 1: four quarter notes
        ed.SelectRange(0, 5, 1, 6);
        Check("selection: a range from the middle of a whole-bar rest to the middle of a quarter covers whole beats", ed.SelectionCellRange == (0, 0, 1, 4), ed.SelectionCellRange.ToString());
        ed.SelectRange(1, 5, 1, 10);
        Check("selection: a range inside quarters starts and ends on beat starts", ed.SelectionCellRange == (1, 4, 1, 8));
        ed.SelectAll();
        var all = ed.SelectionCellRange;
        var last = project.Tracks[0].Measures.Count - 1;
        var lastCells = project.Tracks[0].Measures[last].Cells;
        Check("selection: select all starts on the first beat and ends on the last beat of the last bar",
            all.StartCell == 0 && (lastCells[all.EndCell].IsRest || lastCells[all.EndCell].Notes.Count > 0)
            && all.EndCell + MusicTime.CellSlotsRounded(lastCells[all.EndCell]) >= MusicTime.BarSlots(project, last));
        ed.SetPosition(1, 0, 1, false);
        ed.BeginSelection();
        ed.ExtendSelection(1);
        Check("selection: shift-arrow ends on a beat start", ed.SelectionCellRange.EndCell == 4);
    }
}
