using System.Windows;
using System.Windows.Media;
using TabForge.Views.Band;

namespace TabForge.Views.Video;

// Tells each vertical Band lane which of its systems are in view: a lane engraves every system of the song and slides the strip, and
// replaying the systems far above and below the lane costs raster time for nothing. The others are skipped under an empty clip
// (TabEditorControl.ExportSystemRange), so the pixels are unchanged.
// Owns: the range handed to each lane's editor.
// Does not own: the lane's layout, slide or playhead.
// Tests: TestVideoFrameSourceGolden.
internal static class VideoBandCull
{
    public static void Update(DependencyObject node)
    {
        if (node is BandLane lane) { Apply(lane); return; }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Update(VisualTreeHelper.GetChild(node, i));
    }

    private static void Apply(BandLane lane)
    {
        var editor = lane.Editor;
        if (!lane.Vertical || editor.Track is not { IsAudio: false, Measures.Count: > 0 } track || lane.ActualHeight < 1) return;
        var layout = editor.Layout.GetLayout(track);
        var top = -lane.SlideY;
        var bottom = top + lane.ActualHeight;
        int first = int.MaxValue, last = -1;
        for (var s = 0; s < layout.SystemCount; s++)
        {
            var from = editor.SystemTopForMeasure(layout.Systems[s].FirstMeasure);
            var to = s + 1 < layout.SystemCount ? editor.SystemTopForMeasure(layout.Systems[s + 1].FirstMeasure) : from + editor.SystemHeightNow;
            if (to < top - 1 || from > bottom + 1) continue;
            first = Math.Min(first, s); last = s;
        }
        if (last < 0) return;
        var range = (Math.Max(0, first - 1), Math.Min(layout.SystemCount - 1, last + 1));
        if (editor.ExportSystemRange != range) editor.ExportSystemRange = range;
    }
}
