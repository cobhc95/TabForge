using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using TabForge.Views.Score;

namespace TabForge.Avalonia.Platform;

// Owns: keeping the cursor in view after it moves (a key, an edit, a menu move, a Go To): the enclosing viewer scrolls to the cursor's system
//   when less than a quarter of it shows, and in the one-line view sideways to the cursor's bar. It does nothing while playback runs, because the
//   playhead follow owns the scroll then.
// Does not own: the offsets (Core ScoreCursorSight), the cursor (Core ScorePreviewEditor), the playhead follow (ScorePlayheadFollower).
// Tests: TabForge.Avalonia.Tests.ScoreCursorSightTests.
public sealed partial class ScorePreviewView
{
    /// <summary>Scrolls the viewer so the cursor's system (or, in the one-line view, its bar) is in view; runs on every cursor move.</summary>
    private void KeepCursorInSight()
    {
        // A Band card shows a window of systems the card's owner slides to the playing one (BandTrackPanel.ShowSystemsAround): scrolling it
        // here after a click or an edit would jump the tab under the reader's hand, so a card keeps its window.
        if (_drawsInPanel) return;
        if (_probe.Play.Active || this.FindAncestorOfType<ScrollViewer>() is not { } viewer || viewer.Viewport.Height <= 1) return;
        if (_probe.Cursor is not { } cursor || _probe.Track is not { } track || cursor.Measure < 0 || cursor.Measure >= track.Measures.Count) return;
        var measure = cursor.Measure;
        viewer.UpdateLayout();   // the cursor's bar may have just been added; the viewer's extent must cover it before it is scrolled to
        var position = _probe.Layout.GetLayout(track).Measure(measure);
        if (_probe.HorizontalScroll)
        {
            if (ScoreCursorSight.HorizontalOffset(position.X * Zoom, viewer.Offset.X, viewer.Viewport.Width) is { } x)
                EnsurePageOrigin(viewer, new Vector(x, viewer.Offset.Y));
            return;
        }
        var top = ScoreSectionLinks.BarTop(_probe, measure) * Zoom;
        var height = _probe.SystemBandHeight * Zoom;
        if (ScoreCursorSight.VerticalOffset(top, height, viewer.Offset.Y, viewer.Viewport.Height, Follow.MarginPercent) is { } y)
            EnsurePageOrigin(viewer, new Vector(viewer.Offset.X, y));
    }
}
