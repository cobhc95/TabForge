using TabForge.Platform;

namespace TabForge.Docking;

// Owns: which drop zone a point inside a dock target falls in, whether a split fits, the split fraction and the preview rectangle.
// Does not own: hit-testing real views, screen conversion, the ghost window or the preview drawing (src/TabForge.Avalonia/Views/Dock/DockPaneDragFlow.cs).
// Tests: no dedicated test class.
/// <summary>The drop-zone maths of a dock tab drag, in DIPs, with no UI types.</summary>
public static class DockDropRules
{
    /// <summary>The width of the splitter a split adds between the two halves.</summary>
    public const double SplitterSize = 5;

    public static bool IsHorizontal(DockDropZone zone) => zone is DockDropZone.Left or DockDropZone.Right;

    /// <summary>True when the point is inside the rectangle, edges included.</summary>
    public static bool Contains(UiRect rect, UiPoint point) =>
        point.X >= rect.X && point.X <= rect.Right && point.Y >= rect.Y && point.Y <= rect.Bottom;

    /// <summary>The zone of a target of the given size that the local point is in: the nearest edge band, else the centre.</summary>
    public static DockDropZone ClassifyZone(UiPoint point, double width, double height)
    {
        if (width <= 0 || height <= 0) return DockDropZone.Center;
        var edge = DockLayoutTree.EdgeFraction;
        var x = point.X / width;
        var y = point.Y / height;
        var candidates = new List<(DockDropZone Zone, double Distance)>();
        if (x <= edge) candidates.Add((DockDropZone.Left, x / edge));
        if (x >= 1 - edge) candidates.Add((DockDropZone.Right, (1 - x) / edge));
        if (y <= edge) candidates.Add((DockDropZone.Top, y / edge));
        if (y >= 1 - edge) candidates.Add((DockDropZone.Bottom, (1 - y) / edge));
        return candidates.Count == 0 ? DockDropZone.Center : candidates.MinBy(c => c.Distance).Zone;
    }

    /// <summary>The width a dock column keeps so its tab headers stay readable; a split that would make a column narrower is not offered.</summary>
    public const double MinPaneWidth = 120;

    /// <summary>The zone of a tab group of the given size: anywhere in the header row (height <paramref name="stripHeight"/>) is a tab drop (Center);
    /// below it the body's edge bands split and its middle merges.</summary>
    public static DockDropZone ClassifyTabGroupZone(UiPoint point, double width, double height, double stripHeight)
    {
        if (point.Y <= stripHeight) return DockDropZone.Center;
        return ClassifyZone(new UiPoint(point.X, point.Y - stripHeight), width, height - stripHeight);
    }

    /// <summary>True when the point is in the header row, so the drop is a tab merge with an insertion position.</summary>
    public static bool IsInStrip(UiPoint point, double stripHeight) => point.Y <= stripHeight;

    /// <summary>The position a dropped tab takes among tabs given as (left, width) in header coordinates: after every tab whose middle lies left of the pointer.</summary>
    public static int TabInsertIndex(double pointerX, IReadOnlyList<(double Left, double Width)> tabs)
    {
        var index = 0;
        foreach (var tab in tabs)
            if (pointerX > tab.Left + tab.Width / 2) index++;
        return index;
    }

    /// <summary>The x of the insertion marker for a tab position: the left edge of the tab there, else the right edge of the last tab.</summary>
    public static double TabInsertX(int index, IReadOnlyList<(double Left, double Width)> tabs) =>
        tabs.Count == 0 ? 0 : index < tabs.Count ? tabs[index].Left : tabs[^1].Left + tabs[^1].Width;

    /// <summary>True when a target extent along the split axis holds the new panel's minimum and the existing content's minimum.</summary>
    public static bool SplitFits(double extent, double newMinimum, double existingMinimum) =>
        extent - SplitterSize >= newMinimum + existingMinimum;

    /// <summary>The share of the target the new panel takes: the edge fraction, held inside what both minimums allow.</summary>
    public static double SplitFraction(double extent, double newMinimum, double existingMinimum)
    {
        var edge = DockLayoutTree.EdgeFraction;
        var available = extent - SplitterSize;
        if (available <= 0 || newMinimum + existingMinimum <= 0) return edge;
        var minFraction = newMinimum / available;
        var maxFraction = 1 - existingMinimum / available;
        return minFraction <= maxFraction ? Math.Clamp(edge, minFraction, maxFraction) : edge;
    }

    /// <summary>The rectangle the dropped panel would take: the whole target for the centre, else the fraction of it at that edge.</summary>
    public static UiRect PreviewRect(UiRect target, DockDropZone zone, double fraction)
    {
        if (zone == DockDropZone.Center) return target;
        if (IsHorizontal(zone))
        {
            var newWidth = Math.Max(0, (target.Width - SplitterSize) * fraction);
            return zone == DockDropZone.Left
                ? new UiRect(target.X, target.Y, newWidth, target.Height)
                : new UiRect(target.X + target.Width - newWidth, target.Y, newWidth, target.Height);
        }
        var newHeight = Math.Max(0, (target.Height - SplitterSize) * fraction);
        return zone == DockDropZone.Top
            ? new UiRect(target.X, target.Y, target.Width, newHeight)
            : new UiRect(target.X, target.Y + target.Height - newHeight, target.Width, newHeight);
    }
}

// Owns: the size and place a floating dock window starts with and how it is fitted into a monitor's work area.
// Does not own: reading or moving the real window (src/TabForge.Avalonia/Views/Dock/DockWorkspaceController.cs).
// Tests: no dedicated test class.
/// <summary>The floating-window placement maths, with no UI types.</summary>
public static class FloatingPlacementRules
{
    private const double FallbackLeft = 180;
    private const double FallbackTop = 140;
    private const double FrameAllowance = 20;

    /// <summary>The saved bounds made finite and usable: a non-finite value falls back, a size never drops below the smaller of its floor and the window minimum.</summary>
    public static UiRect FiniteBounds(double left, double top, double width, double height, double minWidth, double minHeight) =>
        new(double.IsFinite(left) ? left : FallbackLeft,
            double.IsFinite(top) ? top : FallbackTop,
            Math.Max(Math.Min(240, minWidth), double.IsFinite(width) ? width : Math.Max(320, minWidth)),
            Math.Max(Math.Min(150, minHeight), double.IsFinite(height) ? height : Math.Max(220, minHeight)));

    /// <summary>The largest window size in DIPs that leaves room for the frame inside a work area of the given size.</summary>
    public static (double Width, double Height) MaxSize(double workWidth, double workHeight) =>
        (Math.Max(180, workWidth - FrameAllowance), Math.Max(120, workHeight - FrameAllowance));

    /// <summary>The window bounds moved and shrunk to lie inside the work area (all in device pixels).</summary>
    public static UiRect FitToWorkArea(UiRect bounds, UiRect work)
    {
        var workWidth = Math.Max(1, work.Width);
        var workHeight = Math.Max(1, work.Height);
        var width = Math.Min(bounds.Width, workWidth);
        var height = Math.Min(bounds.Height, workHeight);
        return new UiRect(Math.Clamp(bounds.X, work.X, work.Right - width), Math.Clamp(bounds.Y, work.Y, work.Bottom - height), width, height);
    }
}
