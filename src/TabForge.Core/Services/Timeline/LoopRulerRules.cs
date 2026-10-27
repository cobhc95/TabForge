namespace TabForge.Services;

// Owns: the pure rules of editing the loop region on the bar-number ruler: which part a press grabs (an edge, the inside or nothing) and the bars a drag
//   from it gives (a new loop, an edge resized, the whole loop moved), snapped to bars and kept inside the song.
// Does not own: the loop state (SelectionModel), the undo step of each drag (DocumentEdits.EditLoop) or the pointer handling (TimelineInputController.Loop).
// Tests: LoopRegionTests.

/// <summary>What a press on the ruler grabs of the loop region.</summary>
public enum LoopGrip { None, StartEdge, EndEdge, Inside }

/// <summary>The ruler's loop editing rules.</summary>
public static class LoopRulerRules
{
    /// <summary>How close (px) to an edge a press grabs it.</summary>
    public const double EdgeTolerance = 5;

    /// <summary>What a press at <paramref name="x"/> grabs of a loop drawn from <paramref name="left"/> to <paramref name="right"/> (null: no loop).</summary>
    public static LoopGrip GripAt(double x, double? left, double? right)
    {
        if (left is not { } l || right is not { } r) return LoopGrip.None;
        if (Math.Abs(x - l) <= EdgeTolerance) return LoopGrip.StartEdge;
        if (Math.Abs(x - r) <= EdgeTolerance) return LoopGrip.EndEdge;
        return x > l && x < r ? LoopGrip.Inside : LoopGrip.None;
    }

    /// <summary>
    /// The loop bars a drag gives: <see cref="LoopGrip.None"/> makes a loop from the press bar to the pointer bar (with <paramref name="extend"/>, from the
    /// existing loop's far end), an edge moves that end (crossing the other end flips them), and the inside moves the whole loop by whole bars.
    /// </summary>
    public static (int Start, int End) Drag(LoopGrip grip, int pressBar, int bar, (int Start, int End)? loop, int barCount, bool extend = false)
    {
        var last = Math.Max(0, barCount - 1);
        bar = Math.Clamp(bar, 0, last);
        pressBar = Math.Clamp(pressBar, 0, last);
        switch (grip)
        {
            case LoopGrip.StartEdge when loop is { } l: return Order(bar, l.End);
            case LoopGrip.EndEdge when loop is { } l: return Order(l.Start, bar);
            case LoopGrip.Inside when loop is { } l:
                var delta = Math.Clamp(bar - pressBar, -l.Start, last - l.End);
                return (l.Start + delta, l.End + delta);
            default:
                if (extend && loop is { } e) return Order(Math.Abs(pressBar - e.Start) <= Math.Abs(pressBar - e.End) ? e.End : e.Start, bar);
                return Order(pressBar, bar);
        }
    }

    private static (int, int) Order(int a, int b) => a <= b ? (a, b) : (b, a);
}
