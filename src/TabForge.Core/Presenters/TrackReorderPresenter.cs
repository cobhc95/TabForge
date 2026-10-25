// Owns: the track drag preview maths without UI types: where each row sits while one is dragged, which track a drawn lane belongs to,
//       the per-frame easing of the passed rows and its smoothing factor.
// Does not own: the drag state fields, the frame callback, the lane visuals, drawing or the reorder edit (DocumentEdits.Run).
// Tests: no dedicated test class.
namespace TabForge.Presenters;

/// <summary>Pure track-drag preview rules. Static and allocation-free so the per-frame caller stays light. Thread: UI thread.</summary>
public static class TrackReorderPresenter
{
    /// <summary>Time constant of the passed rows' easing, in seconds.</summary>
    private const double EaseSeconds = 0.025;

    /// <summary>A frame is never treated as longer than this, so a stall does not make rows jump.</summary>
    private const double MaxFrameSeconds = 0.05;

    /// <summary>Distance in pixels under which an easing row snaps to its target.</summary>
    private const double SnapDistance = 0.25;

    /// <summary>The dragged row follows the pointer; rows between it and the drop slot make room by one row height.</summary>
    public static double TargetOffset(int index, int from, int to, double pointerDeltaY, double movingRowHeight)
    {
        if (index == from) return pointerDeltaY;
        if (from < to && index > from && index <= to) return -movingRowHeight;
        if (from > to && index >= to && index < from) return movingRowHeight;
        return 0;
    }

    /// <summary>The track drawn at <paramref name="drawIndex"/> while the lanes are redrawn in preview order (the dragged track is drawn last).</summary>
    public static int TrackAtDrawIndex(int drawIndex, int from, int trackCount)
        => from < trackCount && drawIndex == trackCount - 1 ? from
         : from < trackCount && drawIndex >= from ? drawIndex + 1 : drawIndex;

    /// <summary>The smoothing factor of a frame that took <paramref name="elapsedSeconds"/>.</summary>
    public static double Smoothing(double elapsedSeconds) => 1 - Math.Exp(-Math.Clamp(elapsedSeconds, 0, MaxFrameSeconds) / EaseSeconds);

    /// <summary>
    /// Moves every row's offset one frame toward its target. Returns whether any offset changed and, in <paramref name="settled"/>,
    /// whether every passed row has reached its target.
    /// </summary>
    public static bool Step(double[] offsets, int from, int to, double pointerDeltaY, double movingRowHeight, double smoothing, out bool settled)
    {
        var moved = false;
        settled = true;
        for (var i = 0; i < offsets.Length; i++)
        {
            var target = TargetOffset(i, from, to, pointerDeltaY, movingRowHeight);
            if (i == from) { offsets[i] = target; continue; }
            var difference = target - offsets[i];
            if (Math.Abs(difference) > SnapDistance)
            {
                offsets[i] += difference * smoothing;
                moved = true;
                settled = false;
            }
            else
            {
                if (Math.Abs(difference) > 0.001) moved = true;
                offsets[i] = target;
            }
        }
        return moved;
    }
}
