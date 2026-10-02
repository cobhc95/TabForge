using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;

namespace TabForge;

public static partial class SelfTest
{
    /// <summary>Feature 6a: a bar with less music than its time signature is marked like an error bar; an empty bar, a full bar and a pickup bar are not.</summary>
    private static void TestBarIncompleteMarking()
    {
        static void Quarter(MeasureModel bar, int cell) { bar.Cells[cell].DurationDenominator = 4; bar.Cells[cell].Notes.Add(new TabNote { StringIndex = 0, Fret = 3, MidiValue = 67 }); }
        var p = TemplateFactory.Blank();
        var bar = p.Tracks[0].Measures[0];

        Check("empty bar is not marked", !MusicTime.AnalyzeBar(p, 0).Marked);
        Quarter(bar, 0); Quarter(bar, 4);
        var half = MusicTime.AnalyzeBar(p, 0);
        Check("half-empty bar (2 of 4 quarters) is marked, not an error", half.Marked && half.Short && !half.Error && !half.Complete, half.ToString());
        Quarter(bar, 8); Quarter(bar, 12);
        Check("full bar is not marked", !MusicTime.AnalyzeBar(p, 0).Marked);
        bar.Cells[12] = new TabCell { DurationDenominator = 2, Notes = { new TabNote { StringIndex = 0, Fret = 5 } } };
        var over = MusicTime.AnalyzeBar(p, 0);
        Check("overfull bar is still an error and marked", over.Error && over.Marked && !over.Short, over.ToString());

        bar.Cells[12] = new TabCell(); bar.Cells[8] = new TabCell();
        bar.Anacrusis = true;
        Check("pickup (anacrusis) bar with short content is not marked", !MusicTime.AnalyzeBar(p, 0).Marked);
        bar.Anacrusis = false;
        bar.Cells[8] = new TabCell { IsRest = true, DurationDenominator = 2 };
        Check("a half rest fills the bar: not marked", !MusicTime.AnalyzeBar(p, 0).Marked);
    }
}
