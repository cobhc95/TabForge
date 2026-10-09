using System.Windows.Media;
using TabForge.Models;
using TabForge.Views;
using TabForge.Views.Score;

namespace TabForge;

// Score look against the reference: a lone hammer-on slurs to the next note, an incomplete tuplet shows its number
// without bracket fragments, and a tied continuation shows no P.M.
public static partial class SelfTest
{
    private static void TestScoreLookBatch2()
    {
        var renderer = new StaffNotationRenderer();

        // Hammer-on from 3 to 5 on the low E string: the H mark sits on the 3 only.
        var hammer = NewMeasure();
        hammer.Cells[0] = NotationCell(8, 43);
        hammer.Cells[0].Notes[0].StringIndex = 5;
        hammer.Cells[0].Notes[0].Techniques.Add("HOPO");
        hammer.Cells[2] = NotationCell(8, 45);
        hammer.Cells[2].Notes[0].StringIndex = 5;
        Check("a hammer-on marked on its first note only draws one staff slur to the next note",
            Layout(renderer, hammer, kind: TrackKind.Other).HopoSlurs.Count == 1);

        // Two beats of a triplet group (the third not entered yet): a number under each, no bracket lines (GP5, session 04 step 23).
        var partial = NewMeasure();
        partial.Cells[0] = NotationCell(8, 72, triplet: true);
        partial.Cells[1] = NotationCell(8, 74, triplet: true);
        partial.Cells[4] = NotationCell(4, 72);
        var layout = Layout(renderer, partial, kind: TrackKind.Other);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) StaffNotationDrawing.DrawTuplets(dc, layout, Brushes.Black);
        var lines = CountLines(visual.Drawing);
        Check("an incomplete triplet shows a number per beat without bracket fragments",
            layout.TupletGroups.Count == 2 && layout.TupletGroups.All(g => g.Beats.Count == 1) && lines == 0, $"groups={layout.TupletGroups.Count} lines={lines}");

        // P.M. on a tied continuation: not shown, as in the reference.
        var project = new SongProject();
        var track = new TrackModel { Kind = TrackKind.Guitar };
        project.Tracks.Add(track);
        var bar = NewMeasure();
        track.Measures.Add(bar);
        bar.Cells[0] = NotationCell(4, 40);
        bar.Cells[4] = NotationCell(4, 40);
        bar.Cells[4].Notes[0].Tied = true;
        bar.Cells[4].Notes[0].Techniques.Add("PalmMute");
        Check("palm mute on a tied note shows no P.M.",
            ScorePassages.BuildPalmMutePassages(track, project).Count == 0 &&
            !ScoreMarkText.ShowsPalmMute(bar.Cells[4].Notes[0]));
        bar.Cells[4].Notes[0].Tied = false;
        Check("palm mute on a plain note shows P.M.", ScorePassages.BuildPalmMutePassages(track, project).Count == 1);
    }

    private static int CountLines(Drawing? drawing) => drawing switch
    {
        DrawingGroup group => group.Children.Sum(CountLines),
        GeometryDrawing { Geometry: LineGeometry } => 1,
        _ => 0
    };
}
