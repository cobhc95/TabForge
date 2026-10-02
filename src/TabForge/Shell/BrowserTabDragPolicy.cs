using System.Windows;

namespace TabForge.Shell;

public enum TabTearOffAction { NewWindow, MoveWindow }

public enum BrowserChromeHit { Client, Tab, TabAction, CaptionButton, DraggableCaption }

/// <summary>
/// Pointer and insertion rules for browser-style tab dragging. Kept independent of WPF so threshold,
/// target-index and ownership decisions can be verified without opening a window.
/// </summary>
public static class BrowserTabDragPolicy
{
    public const double DragThreshold = 4;
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

    /// <summary>What dragging a tab out of the strip does. A window's only tab (main or secondary) moves the window itself
    /// (no new window, no blank replacement tab left behind); with 2+ tabs the tab is torn off into a new window.</summary>
    public static TabTearOffAction TearOffAction(int tabCount) =>
        tabCount == 1 ? TabTearOffAction.MoveWindow : TabTearOffAction.NewWindow;

    public static bool IsBeyondTearOff(double pointerX, double pointerY, double windowWidth,
        double stripTop, double stripHeight, double tolerance = TearOffTolerance) =>
        pointerX < 0 || pointerX >= windowWidth || pointerY < stripTop - tolerance ||
        pointerY > stripTop + stripHeight + tolerance;
}

/// <summary>Pure chrome-region classification used by the shell's title-bar gesture routing.</summary>
public static class BrowserChromeHitTest
{
    public static BrowserChromeHit Classify(Point point, IReadOnlyList<Rect> tabBounds,
        IReadOnlyList<Rect> actionBounds, IReadOnlyList<Rect> captionButtonBounds, Rect captionBounds)
    {
        ArgumentNullException.ThrowIfNull(tabBounds);
        ArgumentNullException.ThrowIfNull(actionBounds);
        ArgumentNullException.ThrowIfNull(captionButtonBounds);
        if (captionButtonBounds.Any(r => r.Contains(point))) return BrowserChromeHit.CaptionButton;
        if (actionBounds.Any(r => r.Contains(point))) return BrowserChromeHit.TabAction;
        if (tabBounds.Any(r => r.Contains(point))) return BrowserChromeHit.Tab;
        return captionBounds.Contains(point) ? BrowserChromeHit.DraggableCaption : BrowserChromeHit.Client;
    }
}

/// <summary>Tracks tab ownership transitions during detach/attach operations.</summary>
public sealed class TabOwnershipState
{
    private readonly Dictionary<Guid, Guid> _owners = new();

    public int Count => _owners.Count;
    public bool TryGetOwner(Guid tabId, out Guid windowId) => _owners.TryGetValue(tabId, out windowId);
    public void Register(Guid tabId, Guid windowId) => _owners[tabId] = windowId;
    public bool Transfer(Guid tabId, Guid sourceWindowId, Guid destinationWindowId)
    {
        if (!_owners.TryGetValue(tabId, out var owner) || owner != sourceWindowId) return false;
        _owners[tabId] = destinationWindowId;
        return true;
    }
    public bool Close(Guid tabId, Guid windowId) =>
        _owners.TryGetValue(tabId, out var owner) && owner == windowId && _owners.Remove(tabId);
}
