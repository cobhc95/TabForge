// Owns: the Band view's row-drag maths without UI types: where a dragged row would land, the top of a slot, the glide target of the
//       other rows, the edge scroll step of a held row, and the clamp of a row's own height.
// Does not own: the drag state, pointer capture, the animations, the auto-scroll timer or the reorder edit (BandLayoutState.MoveShown).
// Tests: no dedicated test class.
namespace TabForge.Presenters;

/// <summary>Pure Band row-drag rules over a list of row pitches (height plus gap). Static and allocation-free. Thread: UI thread.</summary>
public static class BandReorderPresenter
{
    /// <summary>Fewest pixels a row's own height can be dragged to.</summary>
    public const double MinRowHeight = 80;

    /// <summary>Height of the strip at each edge of the viewport where a held row scrolls the list.</summary>
    public const double EdgeZone = 40;

    /// <summary>Most the list scrolls per tick.</summary>
    public const double MaxScrollStep = 18;

    /// <summary>A row's own height: never below the minimum and never taller than the panel's viewport (at least the minimum).</summary>
    public static double ClampHeight(double height, double viewport) => Math.Clamp(height, MinRowHeight, Math.Max(MinRowHeight, viewport));

    /// <summary>
    /// Where a dragged row would land: its index among the rows once it is dropped. A row moving down passes a neighbour when its bottom edge
    /// crosses the neighbour's middle, moving up when its top edge does.
    /// </summary>
    public static int DropIndex(IReadOnlyList<double> pitches, int from, double draggedTop)
    {
        var bottom = draggedTop + pitches[from];
        var target = from;
        var y = 0.0;
        for (var i = 0; i < pitches.Count; i++)
        {
            var mid = y + pitches[i] / 2;
            y += pitches[i];
            if (i > from && bottom > mid) target = i;
            if (i < from && draggedTop < mid) { target = i; break; }
        }
        return target;
    }

    /// <summary>The top of slot <paramref name="index"/> once row <paramref name="from"/> is moved there.</summary>
    public static double SlotTop(IReadOnlyList<double> pitches, int from, int index)
    {
        var y = 0.0;
        var count = 0;
        for (var i = 0; i < pitches.Count && count < index; i++)
        {
            if (i == from) continue;
            y += pitches[i];
            count++;
        }
        return y;
    }

    /// <summary>Where row <paramref name="index"/> glides to while row <paramref name="from"/> is held over slot <paramref name="target"/>: aside by the held row's pitch, or 0.</summary>
    public static double GlideOffset(int index, int from, int target, double heldPitch)
        => index > from && index <= target ? -heldPitch : index < from && index >= target ? heldPitch : 0;

    /// <summary>How far the list scrolls (pixels, negative = up) with the pointer at <paramref name="pointerY"/> in a viewport of <paramref name="height"/>: faster the closer to the edge.</summary>
    public static double AutoScrollStep(double pointerY, double height)
    {
        if (pointerY < EdgeZone) return -MaxScrollStep * Math.Min(1, (EdgeZone - pointerY) / EdgeZone);
        if (pointerY > height - EdgeZone) return MaxScrollStep * Math.Min(1, (pointerY - (height - EdgeZone)) / EdgeZone);
        return 0;
    }
}
