using Avalonia;
using TabForge.Avalonia.Platform;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the Add track strip's part in a clip drag: while a clip is dragged below the last track the strip takes the drop shade and the words (move or copy to a new track), and when
//   the timeline has no room for the new track slot under its last row the strip shows the clip at its time as the landing.
// Does not own: choosing the landing (TimelineClipGestureController), the slot the timeline draws itself (TimelineAudioClips.DragPreview) or the file drag's use of the strip (MainWindowAudioDrop).
// Tests: TabForge.Avalonia.Tests.ClipVerticalDragTests.
public sealed partial class ArrangementView
{
    private bool _stripShowsClipDrag;

    /// <summary>Follows the timeline's clip drag preview: shows or removes the strip's new-track landing. Touches the strip only when the clip drag set it, so a file drag keeps its own.</summary>
    private void ShowClipDragOnStrip()
    {
        var preview = Timeline.ClipDragPreview;
        var over = preview is { NewTrack: true } && AddTrackBar.IsVisible;
        if (!over && !_stripShowsClipDrag) return;
        _stripShowsClipDrag = over;
        AddTrackBar.DropWords = over ? (preview!.Copy ? AddTrackLane.ClipCopyLabel : AddTrackLane.ClipMoveLabel) : null;
        AddTrackBar.DropActive = over;
        AddTrackBar.DropGhost = over && !Timeline.NewTrackSlotFits ? ClipGhostOnStrip(preview!) : null;
    }

    /// <summary>The first dragged clip as a block on the strip, at its time (the strip is outside the timeline scroll, so the x is translated); null when the strip is not laid out.</summary>
    private (double X1, double X2, string Label, string Readout)? ClipGhostOnStrip(ClipDragPreview preview)
    {
        if (preview.Ghosts.Count == 0) return null;
        var clip = preview.Ghosts[0].Shown;
        var (x1, x2) = (Timeline.XOfSeconds(clip.StartSec), Timeline.XOfSeconds(clip.EndSec));
        if (Timeline.TranslatePoint(new Point(x1, 0), AddTrackBar) is not { } left) return null;
        return (left.X, left.X + System.Math.Max(3, x2 - x1), clip.Name, "");
    }
}
