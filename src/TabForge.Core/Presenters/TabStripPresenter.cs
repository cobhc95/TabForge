// Owns: the document tab strip's rules: the tab width range (shared strip or content sized), which close-button mode a setting names,
//       the corner radius, and where a dragged tab lands among the tab centres.
// Does not own: the tab names and tooltips (TabLabelPresenter), the tab order itself (DocumentManager), drawing or mouse input
//       (BrowserTabBar and its Avalonia twin).
// Tests: TabForge.Avalonia.Tests.PanelBarsTests.
using TabForge.Services;

namespace TabForge.Presenters;

/// <summary>The close-button flags for one tab: shown on hover (and when active), always, or never.</summary>
public readonly record struct TabCloseButton(bool OnHover, bool Always, bool Never, bool ActiveOnly = false);

/// <summary>The document tab strip's width, close-button, corner and drop-position rules.</summary>
public static class TabStripPresenter
{
    /// <summary>The (min, max) width of every tab. Sharing the strip, the room left by the new-tab button, the 3 px gaps and a
    /// 14 px inset (the strip's 8 px inset and the (+) button's 6 px margin) is split evenly within the min and max; content sized, tabs grow up to the max.</summary>
    public static (double Min, double Max) TabWidths(TabSettings settings, double stripWidth, double newTabButtonWidth, int count)
    {
        if (!settings.ShrinkToFit || count <= 0) return (0, settings.MaxTabWidth);
        var available = Math.Max(120, stripWidth - newTabButtonWidth - 14 - Math.Max(0, count - 1) * 3);
        var floor = Math.Min(settings.MinTabWidth, settings.MaxTabWidth);
        var width = Math.Clamp(Math.Floor(available / count), floor, Math.Max(floor, settings.MaxTabWidth));
        return (width, width);
    }

    /// <summary>The width the scrolling tab area is capped to: all the strip leaves after its 14 px inset and the (+) button while <paramref name="count"/> tabs
    /// of <paramref name="tabWidth"/> (0: not known) fit, else also less the two scroll arrows that then show.</summary>
    public static double Viewport(double stripWidth, double newTabButtonWidth, double arrowWidth, double tabWidth, int count)
    {
        var room = Math.Max(0, stripWidth - 14 - newTabButtonWidth);
        var fits = tabWidth > 0 && count * tabWidth + Math.Max(0, count - 1) * 3 <= room + 0.5;
        return fits ? room : Math.Max(0, room - 2 * arrowWidth);
    }

    /// <summary>Whether there is more to scroll to on the left and on the right of the tabs' <paramref name="extent"/>.</summary>
    public static (bool Left, bool Right) CanScroll(double offset, double extent, double viewport) =>
        (offset > 0.5, offset + viewport < extent - 0.5);

    /// <summary>The scroll offset that brings a tab (its left edge and width in the tabs' own coordinates) fully into view, moving as little as it can.</summary>
    public static double RevealOffset(double left, double width, double offset, double viewport)
    {
        if (left < offset) return left;
        return left + width > offset + viewport ? left + width - viewport : offset;
    }

    /// <summary>The offset one tab further in a direction: the first tab cut off on the right (or the last cut off on the left) is brought into view; unchanged when none is.
    /// The offset is first clamped into [0, the tabs' extent less the viewport].</summary>
    public static double StepOffset(IReadOnlyList<(double Left, double Width)> tabs, double offset, double viewport, bool forward)
    {
        var extent = tabs.Count == 0 ? 0 : tabs.Max(tab => tab.Left + tab.Width);
        offset = Math.Clamp(offset, 0, Math.Max(0, extent - viewport));
        for (var i = 0; i < tabs.Count; i++)
        {
            var (left, width) = tabs[forward ? i : tabs.Count - 1 - i];
            if (forward ? left + width > offset + viewport + 0.5 : left < offset - 0.5) return RevealOffset(left, width, offset, viewport);
        }
        return offset;
    }

    public static TabCloseButton CloseButton(TabSettings settings) => new(
        settings.CloseButton == CloseButtonModes.ActiveAndHover,
        settings.CloseButton == CloseButtonModes.Always,
        settings.CloseButton == CloseButtonModes.Never,
        settings.CloseButton == CloseButtonModes.ActiveOnly);

    /// <summary>A uniform 8 px pill radius when rounded, else square.</summary>
    public static double CornerRadius(TabSettings settings) => settings.IsRounded ? 8 : 0;

    /// <summary>The index a tab dropped at <paramref name="x"/> takes: before the first tab whose known centre (NaN: not laid out) lies right of it, else the end.</summary>
    public static int InsertionIndex(IReadOnlyList<double> centres, double x)
    {
        for (var i = 0; i < centres.Count; i++)
            if (!double.IsNaN(centres[i]) && x < centres[i]) return i;
        return centres.Count;
    }
}
