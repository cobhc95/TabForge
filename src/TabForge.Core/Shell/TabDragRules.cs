// Owns: the browser-style tab drag decisions that need no window: the drag threshold, the insertion slot among captured tab
//       midpoints, the reorder destination and its neighbour slide offsets, the tear-off action for a tab count, the tear-off band
//       around the strip, the window whose tab strip a drop lands on, and whether a drop is accepted across windows.
// Does not own: pointer capture, window moves and the live tear-off (src/TabForge.Avalonia/Shell/DocumentTabDragFlow.cs).
// Tests: TabForge.Avalonia.Tests.TabDragRulesTests (fake rectangles), TestChromeHitRules, TestBrowserTabShell.
using System.Collections.Generic;
using System.Linq;
using TabForge.Platform;

namespace TabForge.Shell;

/// <summary>What dragging a tab out of the strip does.</summary>
public enum TabTearOffAction { NewWindow, MoveWindow }

public static class TabDragRules
{
    public const double DragThreshold = 4;
    /// <summary>How far the pointer may leave the tab strip band (above or below it) before the tab tears off, in DIPs. Shared with the
    /// the old shell's tear-off rule, so it keeps the value its own checks use.</summary>
    public const double TearOffTolerance = 22;

    public static bool AcceptsDrop(bool allowCrossWindowMerge, bool isLocalTab) =>
        allowCrossWindowMerge || isLocalTab;

    public static bool CrossedThreshold(double dx, double dy, double threshold = DragThreshold) =>
        !double.IsNaN(dx) && !double.IsNaN(dy) && !double.IsNaN(threshold) && threshold >= 0 &&
        (Math.Abs(dx) >= threshold || Math.Abs(dy) >= threshold);

    /// <summary>Returns the slot before the first tab midpoint to the right of x.</summary>
    public static int InsertionIndex(IReadOnlyList<double> lefts, IReadOnlyList<double> widths, double x, int excludedIndex = -1)
    {
        ArgumentNullException.ThrowIfNull(lefts);
        ArgumentNullException.ThrowIfNull(widths);
        if (lefts.Count != widths.Count) throw new ArgumentException("Tab geometry arrays must have the same length.");
        for (var i = 0; i < lefts.Count; i++)
        {
            if (i == excludedIndex) continue;
            if (x < lefts[i] + widths[i] / 2) return i;
        }
        return lefts.Count;
    }

    public static int ReorderDestination(int fromIndex, int insertionIndex, int count)
    {
        if (count <= 0) return -1;
        if (fromIndex < 0 || fromIndex >= count) return -1;
        var insert = Math.Clamp(insertionIndex, 0, count);
        return Math.Clamp(insert > fromIndex ? insert - 1 : insert, 0, count - 1);
    }

    /// <summary>A window's only tab (main or secondary) moves the window itself (no new window, no blank replacement tab
    /// left behind); with 2+ tabs the tab is torn off into a new window.</summary>
    public static TabTearOffAction TearOffAction(int tabCount) =>
        tabCount == 1 ? TabTearOffAction.MoveWindow : TabTearOffAction.NewWindow;

    /// <summary>True when the pointer has left the window horizontally, or has left the strip band by more than the tolerance.</summary>
    public static bool IsBeyondTearOff(double pointerX, double pointerY, double windowWidth,
        double stripTop, double stripHeight, double tolerance = TearOffTolerance) =>
        pointerX < 0 || pointerX >= windowWidth || pointerY < stripTop - tolerance ||
        pointerY > stripTop + stripHeight + tolerance;

    /// <summary>The first window, in the caller's topmost-first order, whose tab strip rectangle (empty space included) contains
    /// the screen point; -1 when none does. <paramref name="excludedIndex"/> skips the dragged window.</summary>
    public static int ResolveDropTarget(double x, double y, IReadOnlyList<UiRect> stripRectsTopmostFirst, int excludedIndex = -1)
    {
        ArgumentNullException.ThrowIfNull(stripRectsTopmostFirst);
        for (var i = 0; i < stripRectsTopmostFirst.Count; i++)
        {
            if (i == excludedIndex) continue;
            if (ContainsPoint(stripRectsTopmostFirst[i], x, y)) return i;
        }
        return -1;
    }

    /// <summary>The slot a dropped tab takes at screen x among the tab rectangles, by comparing x with each tab's centre.</summary>
    public static int InsertIndex(double x, IReadOnlyList<UiRect> tabRects, int excludedIndex = -1)
    {
        ArgumentNullException.ThrowIfNull(tabRects);
        return InsertionIndex(tabRects.Select(r => r.X).ToList(), tabRects.Select(r => r.Width).ToList(), x, excludedIndex);
    }

    /// <summary>Horizontal offsets that open the gap for a tab dragged from <paramref name="fromIndex"/> into the slot
    /// <paramref name="insertionIndex"/>: the tabs it passes slide by the dragged tab's width plus the spacing. The dragged tab's offset is 0.</summary>
    public static double[] ReorderSlideOffsets(int fromIndex, int insertionIndex, IReadOnlyList<double> widths, double spacing)
    {
        ArgumentNullException.ThrowIfNull(widths);
        var offsets = new double[widths.Count];
        var destination = ReorderDestination(fromIndex, insertionIndex, widths.Count);
        if (destination < 0 || destination == fromIndex) return offsets;
        var shift = widths[fromIndex] + spacing;
        var passed = destination > fromIndex ? -shift : shift;
        for (var i = Math.Min(fromIndex, destination); i <= Math.Max(fromIndex, destination); i++)
            if (i != fromIndex) offsets[i] = passed;
        return offsets;
    }

    /// <summary>How far the dragged tab's left edge moves when it is reordered from <paramref name="fromIndex"/> to <paramref name="destination"/>:
    /// the widths and spacing of the tabs it passes (negative when it moves left). Zero when the order does not change.</summary>
    public static double ReorderDisplacement(int fromIndex, int destination, IReadOnlyList<double> widths, double spacing)
    {
        ArgumentNullException.ThrowIfNull(widths);
        if (destination == fromIndex || destination < 0 || destination >= widths.Count) return 0;
        var total = 0.0;
        if (destination > fromIndex)
            for (var i = fromIndex + 1; i <= destination; i++) total += widths[i] + spacing;
        else
            for (var i = destination; i < fromIndex; i++) total -= widths[i] + spacing;
        return total;
    }

    private static bool ContainsPoint(UiRect rect, double x, double y) =>
        x >= rect.X && x < rect.Right && y >= rect.Y && y < rect.Bottom;
}
