using System;

namespace TabForge.Avalonia.Platform;

// Owns: the view state of a clip drag that moves clips to other lanes and tracks: the landing preview the drag publishes, the change notice the Add track strip listens to,
//   and where the "new track" slot starts (the first y below the last track row).
// Does not own: choosing the landing (TimelineClipGestureController), drawing it (TimelineAudioClips.DragPreview) or the strip (AddTrackBar).
// Tests: TabForge.Avalonia.Tests.ClipVerticalDragTests.
public sealed partial class TimelineView
{
    private ClipDragPreview? _clipDragPreview;

    /// <summary>Where a clip drag would land its clip or clips on release (the clips drawn on the target lanes, or the copies for a Ctrl drag, and whether the target tracks can take them); null when the drag stays on its own lane or none runs.</summary>
    internal ClipDragPreview? ClipDragPreview
    {
        get => _clipDragPreview;
        set
        {
            if (_clipDragPreview is null && value is null) return;
            _clipDragPreview = value;
            ClipDragPreviewChanged?.Invoke();
        }
    }

    /// <summary>Raised when <see cref="ClipDragPreview"/> is set: the Add track strip shows or hides its new-track landing.</summary>
    internal event Action? ClipDragPreviewChanged;

    /// <summary>True while the arrangement shows the Add track strip: a clip dragged below the last track then lands on a track of its own. Off in a bare timeline.</summary>
    internal bool NewTrackDropAvailable { get; set; }

    /// <summary>The y from which a dragged clip pointer is over the new track slot: the bottom of the last track row, or the bottom of the visible rows when they end first.</summary>
    internal double NewTrackSlotTop
    {
        get
        {
            var rows = Song.Tracks.Count;
            var lastBottom = rows == 0 ? TrackRowsTop : RowTop(rows - 1) + RowHeightOf(rows - 1);
            return Math.Min(lastBottom, TrackRowsBottom);
        }
    }

    /// <summary>True when the new track slot (one lane tall, under the last row) lies inside the timeline, so the timeline draws it; otherwise only the strip below shows the landing.</summary>
    internal bool NewTrackSlotFits => NewTrackSlotTop + TabForge.Models.ClipLanes.AudioLaneHeight <= Bounds.Height;
}
