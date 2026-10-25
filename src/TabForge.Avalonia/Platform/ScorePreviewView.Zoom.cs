using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Reactive;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TabForge.Menus;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views.Score;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The score view's zoom: the zoom factor and its steps, Fit width, the first open at normal size, the page-layout refit, and page coordinates.
/// Owns: the zoom value and how it is applied. Does not own: the zoom range and fit rules (Core ScoreZoomRules), the stored song zoom
/// (SongDocument.ZoomFactor) or the surface (ScorePreviewView).
/// Tests: TabForge.Avalonia.Tests.ScoreGeometryProofTests.
/// </summary>
public sealed partial class ScorePreviewView
{
    /// <summary>The zoom factor of the score (1 = 100%), set by the score's zoom rows and Fit width.</summary>
    public double Zoom { get; private set; } = 1;

    /// <summary>The smallest zoom this view takes; the main score keeps the Core minimum, a Band card sets a lower one so several cards fit.</summary>
    public double MinZoom { get; set; } = ScoreZoomRules.MinFactor;

    /// <summary>Raised on the UI thread after every zoom change, so the View and tempo readout shows the new percentage.</summary>
    public event Action? ZoomChanged;

    /// <summary>Sets the zoom (1 = 100%) within the Core's zoom range: the page is drawn scaled and the layout size follows, so the scroll viewer reflows. Leaves Fit width.</summary>
    public void SetZoom(double factor)
    {
        _zoomedByHand = true;
        _fitWidthOn = false;
        ApplyZoom(factor);
    }

    /// <summary>
    /// One zoom step in (+1) or out (-1) through Core's <see cref="ScoreZoomRules.Step"/>, about <paramref name="anchor"/> (viewer coordinates; the
    /// viewport's middle when null): the score point under the anchor keeps its place on screen. A step is a hand zoom and leaves Fit width.
    /// </summary>
    public void ZoomStep(int direction, Point? anchor = null)
    {
        var next = ScoreZoomRules.Step(Zoom, direction);
        if (Math.Abs(next - Zoom) < ZoomEpsilon) return;   // already at the limit
        var viewer = this.FindAncestorOfType<ScrollViewer>();
        var point = anchor ?? new Point((viewer?.Viewport.Width ?? 0) / 2, (viewer?.Viewport.Height ?? 0) / 2);
        var offset = viewer?.Offset ?? default;
        var leftMargin = viewer is null ? 0 : PageLeftMargin(viewer);   // the page's left edge inside the viewer, taken out before the zoom
        var pageX = (offset.X + point.X - leftMargin) / Zoom;
        var pageY = (offset.Y + point.Y) / Zoom;
        SetZoom(next);
        if (viewer is null) return;
        // The viewer's extent follows the new size only after a layout pass, and the offset is clamped to the extent.
        viewer.UpdateLayout();
        EnsurePageOrigin(viewer, new Vector(PageLeftMargin(viewer) + pageX * Zoom - point.X, Math.Max(0, pageY * Zoom - point.Y)));
    }

    private const double ZoomEpsilon = 1e-9;

    /// <summary>
    /// The page layout changed (page or continuous, from the score's rows): entering page mode fits the width again unless the zoom was set by
    /// hand; continuous mode keeps the zoom it has.
    /// </summary>
    internal void PageLayoutChanged()
    {
        if (!_probe.Appearance.CenterSystems && !_zoomedByHand)
        {
            FitWidth();
            return;
        }
        Refresh();
    }

    /// <summary>Applies a zoom value and repaints; the zoom rows and Fit width both come through here.</summary>
    private void ApplyZoom(double factor)
    {
        Zoom = Math.Clamp(factor, MinZoom, ScoreZoomRules.MaxFactor);
        Reflow();
        ZoomChanged?.Invoke();
    }

    /// <summary>The zoom a song without a zoom of its own opens at in the continuous view.</summary>
    public const double NormalZoom = 1;

    /// <summary>Shows the score at <see cref="NormalZoom"/> without counting it as a hand zoom, so entering the page view still fits its width.</summary>
    private void OpenAtNormalSize()
    {
        _fitWidthOn = false;
        _zoomedByHand = false;
        ApplyZoom(NormalZoom);
    }

    /// <summary>
    /// The one-line view sits in the middle of the viewer's height when its line is shorter than the viewer, as the WPF one-line view does
    /// (its page takes a top margin); the page and the continuous view start at the top, with no margin.
    /// </summary>
    internal void CentreOneLineVertically()
    {
        var viewer = this.FindAncestorOfType<ScrollViewer>();
        var top = !_probe.HorizontalScroll || viewer is null || viewer.Viewport.Height <= 1 ? 0
            : Math.Max(0, Math.Floor((viewer.Viewport.Height - _probe.ContentSize().Height * Zoom) / 2));
        if (Math.Abs(Margin.Top - top) > 0.5) Margin = new Thickness(0, top, 0, 0);
    }

    /// <summary>A pointer position in page coordinates (the control's own position divided by the zoom).</summary>
    internal Point PagePoint(Point position) => new(position.X / Zoom, position.Y / Zoom);

    /// <summary>"Fit width": the continuous view fits the viewport through the Core rule, the page view fits the page to the viewport's width.</summary>
    public void FitWidth()
    {
        _fitWidthOn = true;
        _zoomedByHand = false;
        var viewport = this.FindAncestorOfType<ScrollViewer>()?.Viewport.Width ?? 0;   // the viewer's width less its bars, the width the page can fill
        if (viewport <= 1) return;
        ApplyZoom(_probe.Appearance.CenterSystems ? ScoreZoomRules.FitContinuous(viewport) : Math.Max(1, viewport - 2) / ScoreGeometryProbe.PageWidth);
    }
}
