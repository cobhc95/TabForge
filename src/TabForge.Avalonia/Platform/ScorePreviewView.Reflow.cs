using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace TabForge.Avalonia.Platform;

// Owns: the score preview's page extent and reflow to the viewer: the page width rules, the measure, the queued reflow and the UI scale reaction, split from ScorePreviewView.cs.
// Does not own: the layout itself (ScoreGeometryProbe) or the view's public surface (ScorePreviewView).
public sealed partial class ScorePreviewView : Control
{
    /// <summary>The logical page width of the reflowed continuous page for a viewer <paramref name="viewport"/> wide: its width less the border,
    /// and never under <see cref="ContinuousMinPageWidth"/> (the zoom is not applied twice: the page is drawn scaled by it).</summary>
    internal double ReflowPageWidth(double viewport) => Math.Max(ContinuousMinPageWidth, (viewport - ViewerBorder) / Zoom);

    /// <summary>
    /// The horizontal bar the enclosing viewer shows: none in the continuous view while the systems wrap to the viewer's width, otherwise Auto,
    /// which shows the bar only when the content is wider than the viewer (a page zoomed past the pane, the one-line view, or the continuous
    /// view when its narrowest reflow width is wider than the pane).
    /// </summary>
    private ScrollBarVisibility HorizontalBarFor(bool reflow, double viewport) =>
        reflow && ContinuousMinPageWidth * Zoom + ViewerBorder <= viewport ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;

    /// <summary>Viewer width not available to the page (its border), as the WPF page width reserves.</summary>
    private const double ViewerBorder = 2;
    /// <summary>
    /// Reflow deadband (DIPs): a viewport change smaller than this never re-lays the page. The viewport tracks the
    /// page width 1:1 through the scroll viewer, so any deadband finer than the per-pass rounding residue (~1.7 DIPs
    /// on the owner's theme/DPI) chases its own tail and never converges; sustained resizes accumulate past this and
    /// still reflow, so a drag-resize only lags by this many DIPs.
    /// </summary>
    private const double ReflowSettleDips = 2.5;
    /// <summary>Set when a Band card draws this view: the card's own viewer keeps its scroll bar settings.</summary>
    private bool _drawsInPanel;

    /// <summary>Repaints after the song changed under the view (an edit made elsewhere) without laying it out again: the bars keep the widths they had.</summary>
    internal void RepaintSongChanged() => InvalidateVisual();

    private (double Width, double Height) _measuredPage;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (!_audioPlaceholder) return PageExtent(this.FindAncestorOfType<ScrollViewer>());
        var viewport = this.FindAncestorOfType<ScrollViewer>()?.Viewport ?? default;   // no score to lay out: the note fills the viewer
        return new Size(viewport.Width > 1 ? viewport.Width : 400, viewport.Height > 1 ? viewport.Height : 200);
    }

    /// <summary>
    /// The page's size at the current zoom, worked out from the live layout and viewer (never a cached measure): the whole page, and in the
    /// continuous view never narrower than the viewer, so a page that fills it is not centred into a blank band on its left. The page view
    /// keeps its own width (a narrow page is centred by design), and the one-line view keeps the whole line's width.
    /// </summary>
    private Size PageExtent(ScrollViewer? viewer)
    {
        _measuredPage = _probe.ContentSize();
        return new Size(PageExtentWidth(viewer), _measuredPage.Height * Zoom);
    }

    /// <summary>
    /// The page's width in view pixels from the live layout and viewer: the drawn page, and in the continuous view never narrower than the viewer.
    /// The one rule the measure, the arrange and the page's left margin all derive from, so the origin and the extent cannot disagree.
    /// </summary>
    private double PageExtentWidth(ScrollViewer? viewer)
    {
        var width = _probe.CurrentPageWidth * Zoom;
        if (_probe.Appearance.CenterSystems && !_probe.HorizontalScroll && viewer is { Viewport.Width: > 1 } live)
            width = Math.Max(width, live.Viewport.Width);
        return width;
    }

    // Page rows (top, bottom) drawn by the last Render: the viewer's rows plus one viewport above and below; null while no viewer shows the score.
    private (double Top, double Bottom)? _drawnRows;

    private (double Top, double Bottom)? DrawnRows()
    {
        if (this.FindAncestorOfType<ScrollViewer>() is not { } viewer || viewer.Viewport.Height <= 1) return _drawnRows = null;
        var top = (viewer.Offset.Y - viewer.Viewport.Height) / Zoom;
        var bottom = (viewer.Offset.Y + viewer.Viewport.Height * 2) / Zoom;
        return _drawnRows = (top, bottom);
    }

    /// <summary>A scroll past the drawn rows repaints so the systems that came into view are drawn.</summary>
    private void ViewerScrolled(object? sender, ScrollChangedEventArgs e)
    {
        if (_scrollBarHeld) NoteHandScroll();
        if (sender is not ScrollViewer viewer || _drawnRows is not var (top, bottom)) return;
        if (viewer.Offset.Y / Zoom < top || (viewer.Offset.Y + viewer.Viewport.Height) / Zoom > bottom) InvalidateVisual();
    }

    /// <summary>The viewer's viewport changed (its size, or a scroll bar appearing or going): the continuous view reflows and Fit width fits again.</summary>
    private bool _reflowQueued;   // P0: Viewport fires during Arrange; the reflow must run after it (never inside), or it never converges
    private bool _reflowForced;   // a reflow an attach or a layout change asked for, which the width deadband must not swallow
    private void ViewerResized() => QueueReflow();

    /// <summary>Asks for one reflow after the pass that raised it (one guard, one post, however many triggers arrive).</summary>
    private void QueueReflow(bool force = false)
    {
        _reflowForced |= force;
        if (_reflowQueued) return;
        _reflowQueued = true;
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _reflowQueued = false;
            var forced = _reflowForced;
            _reflowForced = false;
            if (forced) Reflow();
            else ApplyViewportWidth();
        }, global::Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>Reflows to the viewer's current width after the layout pass (never inside it): only a real width (or reflow flag) change refreshes.</summary>
    private void ApplyViewportWidth()
    {
        if (_fitWidthOn) { _scaleChanged = false; FitWidth(); return; }
        // P0: a UI scale change leaves the stored page width matching the new viewport while the measured page still has the old
        // scale's width (the blank band), so it bypasses every early-out below: the deadband and the "want already stored" check
        // would otherwise swallow it. The flag is cleared once this reflow has run.
        if (_scaleChanged)
        {
            _scaleChanged = false;
            Reflow();
            return;
        }
        // P0: the Viewport observable fires for height changes too, and Refresh invalidates the measure; the viewport
        // also tracks the page width 1:1 through the scroll viewer, so a sub-DIP early-out chases per-pass rounding
        // residue (~1.7 DIPs) forever. Skip anything inside the settle deadband: only a real width (or reflow flag) change refreshes.
        var viewer = _scrollViewer ?? this.FindAncestorOfType<ScrollViewer>();
        var viewport = viewer?.Viewport.Width ?? 0;
        var reflow = _probe.Appearance.CenterSystems && !_probe.HorizontalScroll && viewport > 1;
        var want = reflow ? ReflowPageWidth(viewport) : 0;
        if (Math.Abs(want - _probe.PageWidthOverride) < ReflowSettleDips && _probe.Layout.JustifyAllSystems == reflow) return;
        Reflow();
    }

    /// <summary>Set by <see cref="ThemeSettingsBinding.UiScaleChanged"/>: the page may still be laid out at the pre-change scale.</summary>
    private bool _scaleChanged;

    /// <summary>A UI scale change resizes the viewer under this view; posted so the window's transform arranges the new size first, then the page follows it.</summary>
    private void OnUiScaleChanged()
    {
        _scaleChanged = true;
        var viewer = _scrollViewer ?? this.FindAncestorOfType<ScrollViewer>();
        // The window's new scale has to be arranged before the page can reflow to the new viewer width.
        viewer?.UpdateLayout();
        Reflow();
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _scaleChanged = true;
            Reflow();
            if (viewer is not null) EnsurePageOrigin(viewer, viewer.Offset);
            LogScaleState(viewer);
        }, global::Avalonia.Threading.DispatcherPriority.Background);
        // Cheap third pass after this pass's arrange: reflow only while the page still overflows the viewer.
        global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (viewer is null) return;
            if (viewer.Extent.Width > viewer.Viewport.Width + 1)
            {
                _scaleChanged = true;
                Reflow();
            }
            EnsurePageOrigin(viewer, viewer.Offset);
        }, global::Avalonia.Threading.DispatcherPriority.Render);
    }

    /// <summary>
    /// The one write of the viewer's offset: X stays on the page's origin, between 0 and the scrollable width (0 when the page fits the
    /// viewer), so no stale or over-scrolled X can leave the score off the viewer's left edge. Every Offset.X writer goes through here.
    /// </summary>
    internal static void EnsurePageOrigin(ScrollViewer viewer, Vector wanted)
    {
        // Before the first layout the extent says nothing about the page: keep the offset the reader has.
        if (viewer.Extent.Width <= 0) return;
        var maxX = Math.Max(0, viewer.Extent.Width - viewer.Viewport.Width);
        viewer.Offset = new Vector(Math.Clamp(wanted.X, 0, maxX), wanted.Y);
    }

    /// <summary>One diagnostic line per UI scale change: the viewer's width/offset against the laid-out page and the root scale.</summary>
    private void LogScaleState(ScrollViewer? viewer)
    {
        TabForge.Services.Trace.Write(TabForge.Services.Trace.Ui, FormattableString.Invariant(
            $"[ScoreScale] viewport={viewer?.Viewport.Width:0.##} extent={viewer?.Extent.Width:0.##} offsetX={viewer?.Offset.X:0.##} override={_probe.PageWidthOverride:0.##} page={_probe.CurrentPageWidth:0.##} pageZoomed={_probe.CurrentPageWidth * Zoom:0.##} align={HorizontalAlignment}"));
    }
}
