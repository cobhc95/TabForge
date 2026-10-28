// Owns: the pure chrome hit rules: which region a pointer is over (caption buttons, tab actions, tabs, caption, client), which
//       window edge or corner a pointer is on for a borderless resize, and the leading room the tab strip keeps per OS.
// Does not own: reading the pointer or the window rectangle (the UI-toolkit adapters), the native WM_NCHITTEST reply,
//       the tab geometry (the tab views).
// Tests: TestChromeHitRules.
using TabForge.Platform;

namespace TabForge.Shell;

/// <summary>A region of a borderless window's chrome, as a pointer press or hover sees it. Resize edges are <see cref="ResizeEdge"/>,
/// reported by <see cref="ChromeHitRules.ClassifyResizeEdge"/>.</summary>
public enum ChromeHit
{
    Client,
    Tab,
    TabAction,
    MinButton,
    MaxButton,
    CloseButton,
    Caption,
}

/// <summary>The operating system whose chrome conventions apply.</summary>
public enum ChromeOs { Windows, MacOs, Linux }

public static class ChromeHitRules
{
    /// <summary>The leading room kept clear on macOS for the window's traffic-light buttons; Windows and Linux keep none.</summary>
    public const double MacOsLeadingInset = 78;

    /// <summary>
    /// True when a point in the window's coordinates is empty caption (the caption, not a tab, tab action or caption
    /// button). The single source of truth for "middle-click opens a tab here": the native hook's hit test and its
    /// middle-click spot both read this, so no pixel answers differently in the two paths.
    /// </summary>
    public static bool IsEmptyCaption(UiPoint point, IReadOnlyList<UiRect> tabBounds, IReadOnlyList<UiRect> actionBounds,
        IReadOnlyList<(UiRect Bounds, ChromeHit Kind)> captionButtons, UiRect captionBounds) =>
        Classify(point, tabBounds, actionBounds, captionButtons, captionBounds) == ChromeHit.Caption;

    /// <summary>
    /// Classifies a point in the window's coordinates. Caption buttons win over tab actions, tab actions over tabs, tabs over the
    /// caption, and anything else is client. Right and bottom edges count as inside, like a rectangle-contains test.
    /// </summary>
    public static ChromeHit Classify(UiPoint point, IReadOnlyList<UiRect> tabBounds, IReadOnlyList<UiRect> actionBounds,
        IReadOnlyList<(UiRect Bounds, ChromeHit Kind)> captionButtons, UiRect captionBounds)
    {
        ArgumentNullException.ThrowIfNull(tabBounds);
        ArgumentNullException.ThrowIfNull(actionBounds);
        ArgumentNullException.ThrowIfNull(captionButtons);
        foreach (var button in captionButtons)
            if (Contains(button.Bounds, point)) return button.Kind;
        if (actionBounds.Any(bounds => Contains(bounds, point))) return ChromeHit.TabAction;
        if (tabBounds.Any(bounds => Contains(bounds, point))) return ChromeHit.Tab;
        return Contains(captionBounds, point) ? ChromeHit.Caption : ChromeHit.Client;
    }

    /// <summary>
    /// The window edge or corner under a point, for a borderless resize, or null when the point is not in a resize border. The
    /// point, the window bounds and the border are in one unit (a toolkit may pass device pixels); the border is at least one unit
    /// thick. A maximised window has no resize edge.
    /// </summary>
    public static ResizeEdge? ClassifyResizeEdge(UiPoint point, UiRect windowBounds, double borderThickness, bool maximised)
    {
        if (maximised) return null;
        var border = Math.Max(1, borderThickness);
        var onLeft = point.X < windowBounds.X + border;
        var onRight = point.X >= windowBounds.Right - border;
        var onTop = point.Y < windowBounds.Y + border;
        var onBottom = point.Y >= windowBounds.Bottom - border;
        return (onTop, onBottom, onLeft, onRight) switch
        {
            (true, _, true, _) => ResizeEdge.TopLeft,
            (true, _, _, true) => ResizeEdge.TopRight,
            (_, true, true, _) => ResizeEdge.BottomLeft,
            (_, true, _, true) => ResizeEdge.BottomRight,
            (true, _, _, _) => ResizeEdge.Top,
            (_, true, _, _) => ResizeEdge.Bottom,
            (_, _, true, _) => ResizeEdge.Left,
            (_, _, _, true) => ResizeEdge.Right,
            _ => null,
        };
    }

    /// <summary>The leading room, in DIPs, that the tab strip keeps clear on the given operating system.</summary>
    public static double LeadingInset(ChromeOs os) => os switch
    {
        ChromeOs.Windows or ChromeOs.Linux => 0,
        ChromeOs.MacOs => MacOsLeadingInset,
        _ => throw new ArgumentOutOfRangeException(nameof(os), os, "Unknown operating system for chrome inset."),
    };

    private static bool Contains(UiRect rect, UiPoint point) =>
        point.X >= rect.X && point.X <= rect.Right && point.Y >= rect.Y && point.Y <= rect.Bottom;
}
