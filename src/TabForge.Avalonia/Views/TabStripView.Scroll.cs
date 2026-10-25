using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using TabForge.Platform.Theme;
using TabForge.Presenters;

// Owns: the strip's live fit to its own width and the scrolling of tabs that do not fit: the scroller, its two arrows, the wheel, and the
//   active tab brought into view.
// Does not own: the width and scroll maths (TabStripPresenter) or the tabs themselves (TabStripView).
// Tests: TabForge.Avalonia.Tests.TabStripScrollTests.

namespace TabForge.Avalonia.Views;

public sealed partial class TabStripView
{
    private const double ScrollArrowWidth = ChromeMetrics.TabScrollArrowWidth;
    // Built by BuildScroller in the constructor.
    private ScrollViewer _scroll = null!;
    private Button _left = null!, _right = null!;
    // The width Show was given, used until the strip has a laid-out width of its own; the active tab to bring into view at the next layout.
    private double _initialWidth;
    private int _activeIndex;
    private bool _revealPending;

    private void BuildScroller()
    {
        // The tabs sit in a scroller capped to the strip: while they fit, the (+) follows the last tab; past that they keep their minimum width and the arrows scroll them.
        _scroll = new ScrollViewer { Name = "TabScroll", HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalAlignment = VerticalAlignment.Top, Content = _tabs };
        _left = ScrollArrow("TabScrollLeft", "Scroll tabs left", IconGeometry.ChevronLeft, forward: false);
        _right = ScrollArrow("TabScrollRight", "Scroll tabs right", IconGeometry.ChevronRight, forward: true);
        _scroll.ScrollChanged += (_, _) => UpdateArrows();
        _scroll.PointerWheelChanged += OnScrollWheel;
        LayoutUpdated += (_, _) => RevealActiveTab();
    }

    // The strip's own laid-out width, so a resize or maximise re-fits; the width Show was given only until the first layout.
    private double StripWidth => Bounds.Width > 0 ? Bounds.Width : _initialWidth;

    // The tab widths for the strip's width now (kept as the natural widths a close freeze returns to), and the scroller capped to what the strip leaves it.
    private (double Min, double Max) FitWidths(int count)
    {
        var (min, max) = TabStripPresenter.TabWidths(Settings, StripWidth, NewTabButtonWidth, count);
        _naturalMinWidth = min;
        _naturalMaxWidth = max;
        _scroll.MaxWidth = TabStripPresenter.Viewport(StripWidth, NewTabButtonWidth, ScrollArrowWidth, min, count);
        return (min, max);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (!e.WidthChanged || _tabs.Children.Count == 0) return;
        var (min, max) = FitWidths(_tabs.Children.Count);
        if (_frozenWidths is null)
            foreach (var tab in _tabs.Children)
            {
                tab.MinWidth = min;
                tab.MaxWidth = max;
            }
        _revealPending = true;
    }

    private Button ScrollArrow(string name, string tip, string geometry, bool forward)
    {
        var arrow = GlyphButton(name, "TabBarPlusButton", ScrollArrowWidth, TabStripPresenter.CornerRadius(Settings), Glyph(geometry, 12));
        arrow.IsVisible = false;
        AutomationProperties.SetName(arrow, tip);
        arrow.Click += (_, _) => ScrollBy(forward);
        return arrow;
    }

    // One tab further, by an arrow or the wheel.
    private void ScrollBy(bool forward)
    {
        var tabs = _tabs.Children.Select(tab => (tab.Bounds.X, tab.Bounds.Width)).ToList();
        _scroll.Offset = new Vector(TabStripPresenter.StepOffset(tabs, _scroll.Offset.X, _scroll.Viewport.Width, forward), 0);
    }

    private void OnScrollWheel(object? sender, PointerWheelEventArgs e)
    {
        var delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        if (delta == 0) return;
        e.Handled = true;
        ScrollBy(forward: delta < 0);
    }

    // The arrows show while the tabs overflow, each enabled only while there is more in its direction.
    private void UpdateArrows()
    {
        var overflow = _scroll.Extent.Width > _scroll.Viewport.Width + 0.5;
        var (canLeft, canRight) = TabStripPresenter.CanScroll(_scroll.Offset.X, _scroll.Extent.Width, _scroll.Viewport.Width);
        _left.IsVisible = _right.IsVisible = overflow;
        _left.IsEnabled = canLeft;
        _right.IsEnabled = canRight;
    }

    // After a layout that follows Show or a resize, the active tab is scrolled into view.
    private void RevealActiveTab()
    {
        if (!_revealPending || _activeIndex < 0 || _activeIndex >= _tabs.Children.Count) return;
        var tab = _tabs.Children[_activeIndex];
        if (tab.Bounds.Width <= 0) return;
        _revealPending = false;
        _scroll.Offset = new Vector(TabStripPresenter.RevealOffset(tab.Bounds.X, tab.Bounds.Width, _scroll.Offset.X, _scroll.Viewport.Width), 0);
    }

    /// <summary>The visible tab area, which clips the tabs scrolled out of it.</summary>
    public Control TabViewport => _scroll;
}
