// Owns: the timeline's section hit-testing and section-drag maths without UI types: which section is under a point, the slot a dragged
//       section drops before, the packed preview layout, and the per-frame easing step.
// Does not own: the section rectangles' drawing, the drag state fields, the frame callback, the overlay or the drop edit.
// Tests: no dedicated test class.
namespace TabForge.Presenters;

/// <summary>One section block on the timeline's section lane, in lane order.</summary>
public interface ISectionSlot
{
    int MarkerIndex { get; }
    int FirstBar { get; }
    double Left { get; }
    double Top { get; }
    double Width { get; }
    double Height { get; }
}

/// <summary>Pure section drag rules. Static and allocation-free so the per-frame callers stay light. Thread: UI thread.</summary>
public static class SectionDragPresenter
{
    /// <summary>Gap in pixels between packed section blocks in the drag preview.</summary>
    public const double PackedGap = 2;

    /// <summary>Time constant of the section easing, in seconds.</summary>
    private const double EaseSeconds = 0.035;

    /// <summary>A frame is never treated as longer than this, so a stall does not make sections jump.</summary>
    private const double MaxFrameSeconds = 0.05;

    /// <summary>Distance in pixels under which an easing section snaps to its target.</summary>
    public const double SnapDistance = 0.25;

    /// <summary>Lane position of the section whose block contains the point (edges included), or -1.</summary>
    public static int HitIndex<T>(IReadOnlyList<T> slots, double x, double y) where T : ISectionSlot
    {
        for (var i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            if (x >= s.Left && x <= s.Left + s.Width && y >= s.Top && y <= s.Top + s.Height) return i;
        }
        return -1;
    }

    /// <summary>The marker index a dragged section drops before: the first section whose middle is right of the pointer, else the section count.</summary>
    public static int InsertBefore<T>(IReadOnlyList<T> slots, double pointerX) where T : ISectionSlot
    {
        for (var i = 0; i < slots.Count; i++)
            if (pointerX < slots[i].Left + slots[i].Width / 2) return slots[i].MarkerIndex;
        return slots.Count;
    }

    /// <summary>
    /// Fills <paramref name="xBySlot"/> (one entry per slot, indexed by lane position) with each section's packed preview x
    /// after the section at <paramref name="sourceIndex"/> moves before <paramref name="insertBefore"/>.
    /// </summary>
    public static void FillPreviewLayout<T>(IReadOnlyList<T> slots, int sourceIndex, int insertBefore, double[] xBySlot) where T : ISectionSlot
    {
        var count = slots.Count;
        var target = Math.Clamp(insertBefore > sourceIndex ? insertBefore - 1 : insertBefore, 0, count - 1);
        var x = slots[0].Left;
        for (var destination = 0; destination < count; destination++)
        {
            var source = destination == target
                ? sourceIndex
                : sourceIndex < target && destination >= sourceIndex && destination < target
                    ? destination + 1
                    : sourceIndex > target && destination > target && destination <= sourceIndex
                        ? destination - 1
                        : destination;
            xBySlot[source] = x;
            x += slots[source].Width + PackedGap;
        }
    }

    /// <summary>Seconds since the last frame, clamped to 50 ms; 1/60 on the first frame (<paramref name="last"/> is zero).</summary>
    public static double FrameSeconds(TimeSpan now, TimeSpan last)
        => last == TimeSpan.Zero ? 1.0 / 60.0 : Math.Clamp((now - last).TotalSeconds, 0, MaxFrameSeconds);

    /// <summary>Share of the remaining distance covered in a frame of <paramref name="elapsedSeconds"/>.</summary>
    public static double EaseShare(double elapsedSeconds) => 1 - Math.Exp(-elapsedSeconds / EaseSeconds);

    /// <summary>One easing step toward the target; within a quarter pixel it lands on the target. <paramref name="arrived"/> is true when it did.</summary>
    public static double Step(double current, double target, double share, out bool arrived)
    {
        var difference = target - current;
        arrived = Math.Abs(difference) <= SnapDistance;
        return arrived ? target : current + difference * share;
    }
}
