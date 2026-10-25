using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Visualization;

namespace TabForge.Avalonia.Platform;

// Owns: the panel's pointer gestures and the fretboard's drag to a new horizontal place (Left, Centre, Right). A press on a fretboard arms a gesture:
//   a horizontal move past DragThreshold drags the board with the pointer and shows where it would land (LandingPlace, drawn as a preview); a
//   release then raises PlacementCommitted. A release without a drag picks the note under the pointer (NotePicked), unless the pointer moved
//   vertically past the threshold. Keyboards and drum maps pick on press. The hover cell under the pointer is the ghost dot.
// Does not own: the place rule (Core FretboardGeometryRules.NearestPlacement), the setting the place is saved to (the strip's menu), the board's
//   drawing (InstrumentPanelView.cs), the note entry (the strip).
// Tests: TabForge.Avalonia.Tests.FretboardPlacementDragTests, TabForge.Avalonia.Tests.FretboardHoverTests.
// Threads: UI thread only.
public sealed partial class InstrumentPanelView
{
    /// <summary>How far the pointer moves before a press on the fretboard counts as a drag, in DIPs.</summary>
    private const double DragThreshold = 6;
    /// <summary>The landing preview: its inset from the pane's edges, its corner radius, and its fill opacity.</summary>
    private const double LandingInset = 2, LandingRadius = 9, LandingFillOpacity = 0.07;
    /// <summary>The landing preview's halo rings, outermost first: the stroke width and opacity of each.</summary>
    private static readonly IReadOnlyList<(double Width, double Opacity)> LandingHalos = new (double Width, double Opacity)[] { (14, 0.10), (6, 0.24), (1.8, 0.95) };
    /// <summary>The place badge under the landing preview: its size, its distance up from the preview's bottom, and its text size.</summary>
    private const double BadgeWidth = 48, BadgeHeight = 16, BadgeLift = 20, BadgeFontSize = 8, BadgeOpacity = 0.92;

    private bool _pressArmed;
    private bool _dragging;
    private bool _moved;
    private Point _pressAt;
    /// <summary>How far the fretboard is dragged from its place, in pane DIPs (0 at rest).</summary>
    private double _dragOffset;
    /// <summary>The place a release would commit while the fretboard is dragged; null otherwise.</summary>
    private FretboardHorizontalPosition? _landing;

    /// <summary>Raised when a fretboard drag is released: the place the board landed at.</summary>
    public event Action<FretboardHorizontalPosition>? PlacementCommitted;

    /// <summary>The place a release would commit while the fretboard is dragged (the preview shows it), or null.</summary>
    internal FretboardHorizontalPosition? LandingPlace => _landing;

    private bool CanDragFretboard => _kind == InstrumentPanelKind.Fretboard && _track is not null;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var at = e.GetPosition(this);
        e.Handled = true;
        if (CanDragFretboard)
        {
            _pressArmed = true;
            _moved = false;
            _dragging = false;
            _pressAt = at;
            e.Pointer.Capture(this);
            return;
        }
        if (Pick(new UiPoint(at.X, at.Y)) is { } pick) { BeginKeyPress(pick); NotePicked?.Invoke(pick); }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var at = e.GetPosition(this);
        if (_pressArmed)
        {
            FollowGesture(at);
            return;
        }
        (int String, int Fret)? hover = null;
        if (_kind == InstrumentPanelKind.Fretboard && Pick(new UiPoint(at.X, at.Y)) is { StringIndex: { } s, Fret: { } f }) hover = (s, f);
        SetHover(hover);
        UpdateFeedback(at);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        EndKeyPress();
        if (!_pressArmed) return;
        var at = e.GetPosition(this);
        var wasDragging = _dragging;
        var moved = _moved;
        var landing = PlaceForOffset(at.X - _pressAt.X);
        EndGesture();
        e.Pointer.Capture(null);
        e.Handled = true;
        if (wasDragging) PlacementCommitted?.Invoke(landing);
        else if (!moved && Pick(new UiPoint(at.X, at.Y)) is { } pick) NotePicked?.Invoke(pick);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        SetHover(null);
        EndKeyPress();   // a release outside the panel never reaches it
        ClearKeyHover();
    }

    private void SetHover((int String, int Fret)? hover)
    {
        if (_hover == hover) return;
        _hover = hover;
        InvalidateVisual();
    }

    /// <summary>Moves the armed gesture with the pointer: past the threshold a horizontal move starts the drag, and the board follows the pointer.</summary>
    private void FollowGesture(Point at)
    {
        var dx = at.X - _pressAt.X;
        var dy = at.Y - _pressAt.Y;
        if (Math.Abs(dx) >= DragThreshold || Math.Abs(dy) >= DragThreshold) _moved = true;
        if (!_dragging)
        {
            if (Math.Abs(dx) < DragThreshold || Math.Abs(dx) <= Math.Abs(dy)) return;
            _dragging = true;
            SetHover(null);
        }
        _dragOffset = dx;
        _landing = PlaceForOffset(dx);
        InvalidateVisual();
    }

    /// <summary>The place the fretboard lands at when it is dragged <paramref name="dx"/> DIPs from its place (the Core rule, in the panel's drawing scale).</summary>
    private FretboardHorizontalPosition PlaceForOffset(double dx) =>
        FretboardGeometryRules.NearestPlacement(FretInput, FretboardArea(), _look.FretboardPosition, dx / FretboardScale(), BoardLimits);

    private void EndGesture()
    {
        _pressArmed = false;
        _dragging = false;
        _moved = false;
        _dragOffset = 0;
        _landing = null;
        InvalidateVisual();
    }

    /// <summary>The landing preview of a fretboard drag: the place the board would land in, under a halo, with its place name as a badge.</summary>
    private void DrawLandingPreview(IDrawContext d, Func<string, UiColor> ink)
    {
        if (_landing is not { } target) return;
        var scale = FretboardScale();
        var paneWidth = Bounds.Width / scale;
        var targetBoard = FretboardGeometryRules.Compute(FretInput, FretboardArea(), target, limits: BoardLimits).Board;
        var left = Math.Max(0, targetBoard.X - FretboardGeometryRules.LeftGutter);
        var right = Math.Min(paneWidth, targetBoard.Right + FretboardGeometryRules.LegendWidth);
        if (right <= left) return;

        var accent = ink("InstrumentAccent");
        var x = left + LandingInset;
        var y = LandingInset;
        var width = right - left - 2 * LandingInset;
        var height = Math.Max(1, Bounds.Height / scale - 2 * LandingInset);
        d.Rect(accent.WithOpacity(LandingFillOpacity), null, x, y, width, height, LandingRadius);
        foreach (var (haloWidth, opacity) in LandingHalos)
            d.Rect(null, new UiStroke(accent.WithOpacity(opacity), haloWidth), x, y, width, height, LandingRadius);

        var badgeY = y + height - BadgeLift;
        d.Rect(ink("InstrumentBackground").WithOpacity(BadgeOpacity), new UiStroke(accent.WithOpacity(0.9), 1), left + 3, badgeY, BadgeWidth, BadgeHeight, 4);
        var label = d.Text(target.ToString().ToUpperInvariant(), Font, BadgeFontSize, ink("InstrumentText"), d.PixelsPerDip, weight: 700);
        d.DrawText(label, left + 3 + (BadgeWidth - label.Width) / 2, badgeY + (BadgeHeight - label.Height) / 2);
    }
}
