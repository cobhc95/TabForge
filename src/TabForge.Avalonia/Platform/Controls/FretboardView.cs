using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Platform.Controls;

// Owns: the one shared real-fretboard-with-capo: the board painter both surfaces draw through (the scene, wood,
// inlays, wires, strings, the behind-capo shade and the capo bars with their landing badge, all from the layout,
// the look and the capo state) and the board control the Capo card hosts (hover with a ghost preview on an empty
// fret and a hand cursor with a tooltip on every bar, click to place/select, drag to move with one commit).
// The instrument panel draws its board through FretboardBoardPainter (its markers, legend, scale marks, playback,
// scroll and selection stay in the panel); the Capo card hosts FretboardView, which draws the same board and
// shares the same painter, hit test and drag session. Colours are palette tokens, sizes are DialogMetrics tokens.
// Does not own: the capo values (the window's working copies, the track), the song or the undo step.
// Tests: TrackPropertiesRedesignTests, InstrumentPanelViewTests, FretboardHoverTests.
// Threads: UI thread only.
internal static class FretboardBoardPainter
{
    /// <summary>How much taller than the wood the visible capo bar is drawn: a little, so the clamp wraps over both edges. The artwork's own proportions live in <see cref="DialogMetrics"/> with the other capo tokens.</summary>
    internal const double CapoOverhang = 1.1;

    /// <summary>Dims the neck behind each string's capo: nothing can be played there, and written frets start at the capo.</summary>
    internal static void DrawBehindCapo(IDrawContext d, Func<string, UiColor> ink, in FretboardGeometryRules.Layout layout, UiRect wood)
    {
        if (layout.CapoByString == 0) return;
        var shade = ink("FretboardBehindCapo");
        for (var s = 0; s < layout.Strings; s++)
        {
            var capo = layout.CapoOf(s);
            if (capo <= 0) continue;
            var edge = FretboardGeometryRules.WireX(layout, capo);
            var (left, width) = layout.LeftHanded ? (edge, wood.Right - edge) : (wood.X, edge - wood.X);
            var y = FretboardGeometryRules.StringY(layout, s);
            var top = s == 0 ? wood.Y : y - layout.StringGap / 2;
            var bottom = s == layout.Strings - 1 ? wood.Bottom : y + layout.StringGap / 2;
            if (width > 0 && bottom > top) d.Rect(shade, null, left, top, width, bottom - top);
        }
    }

    /// <summary>The full bar and each partial bar (clipped to the strings it covers, never stretched) with its cast
    /// shadow, plus the drag landing badge on the bar being moved. A fret of 0 draws no bar (off).</summary>
    internal static void DrawCapos(
        IDrawContext d, Func<string, UiColor> ink,
        in FretboardGeometryRules.Layout layout,
        FretboardStyles.Look style, bool bass, FretboardAssets assets,
        int fullFret, IReadOnlyList<PartialCapo>? partials,
        int previewPartialIndex, int? previewPartialFret,
        int? badgeFret, int badgePartialIndex,
        double numberScale, string font, bool showBadge = true)
    {
        var look = style.CapoLook;
        if (assets.Capo(look, shadow: false) is not { } image || FretboardAssets.SizeOf(image) is not { } size) return;
        var shadow = assets.Capo(look, shadow: true);
        var wood = FretboardArtist.Wood(layout, bass);
        var height = wood.Height * CapoOverhang * DialogMetrics.TrackCapoImageHeight / DialogMetrics.TrackCapoImageVisible;
        var width = height * size.Width / size.Height;
        var accent = ink("Accent");
        if (fullFret >= layout.FirstFret && fullFret <= layout.LastFret)
        {
            var bar = DrawCapoBar(d, image, shadow, layout, wood, fullFret, width, height);
            if (showBadge && badgeFret is { } full && badgePartialIndex < 0)
                DrawLandingBadge(d, ink, accent, bar, full, numberScale, font);
        }
        if (partials is null) return;
        for (var i = 0; i < partials.Count; i++)
        {
            var partial = partials[i];
            if (partial is null || partial.Fret <= 0) continue;
            var fret = i == previewPartialIndex && previewPartialFret is { } preview ? preview : partial.Fret;
            if (fret < layout.FirstFret || fret > layout.LastFret) continue;
            if (FretboardCapoRules.PartialBand(layout, partial.StringMask) is not { } band) continue;
            var top = Math.Max(wood.Y, band.Top);
            var bottom = Math.Min(wood.Bottom, band.Bottom);
            if (!(bottom > top)) continue;
            d.PushClip(wood.X, top, wood.Width, bottom - top);
            var bar = DrawCapoBar(d, image, shadow, layout, wood, fret, width, height);
            d.Pop();
            if (showBadge && badgeFret is { } landed && badgePartialIndex == i)
                DrawLandingBadge(d, ink, accent, bar, landed, numberScale, font);
        }
    }

    internal static UiRect DrawCapoBar(IDrawContext d, IUiImage image, IUiImage? shadow, in FretboardGeometryRules.Layout layout, UiRect wood, int fret, double width, double height)
    {
        var bounds = BarBounds(layout, wood, fret);
        // The bounds are the visible bar; the artwork around it (transparent margins, thumbscrew) keeps its share of the width.
        width = bounds.Width / DialogMetrics.TrackCapoBarShare;
        var x = bounds.X + bounds.Width / 2 - width * DialogMetrics.TrackCapoImageCentreX / DialogMetrics.TrackCapoImageWidth;
        var y = wood.Y - (wood.Height * (CapoOverhang - 1) / 2) - height * DialogMetrics.TrackCapoImageTop / DialogMetrics.TrackCapoImageHeight;
        if (shadow is not null) d.Image(shadow, x + 3, y + 2, width, height);
        d.Image(image, x, y, width, height);
        return new UiRect(bounds.X, y, bounds.Width, height);
    }

    internal static double CapoLeft(in FretboardGeometryRules.Layout layout, int fret, double width) =>
        FretboardGeometryRules.FretX(layout, fret) - width * DialogMetrics.TrackCapoImageCentreX / DialogMetrics.TrackCapoImageWidth;

    // The visible bar at the artwork's own proportions, narrowed to fit a late fret. Bar, ghost and ring share this footprint.
    internal static UiRect BarBounds(in FretboardGeometryRules.Layout layout, UiRect wood, int fret)
    {
        var a = FretboardGeometryRules.WireX(layout, fret - 1);
        var b = FretboardGeometryRules.WireX(layout, fret);
        var left = Math.Min(a, b);
        var right = Math.Max(a, b);
        var natural = wood.Height * CapoOverhang * DialogMetrics.TrackCapoImageWidth / DialogMetrics.TrackCapoImageVisible * DialogMetrics.TrackCapoBarShare;
        var width = Math.Min(natural, right - left);
        var x = Math.Clamp(CapoLeft(layout, fret, width), left, right - width);
        return new UiRect(x, wood.Y, width, wood.Height);
    }

    /// <summary>The drag landing badge: a solid accent pill above the bar with the landing fret's real number.</summary>
    internal static void DrawLandingBadge(IDrawContext d, Func<string, UiColor> ink, UiColor accent, UiRect bar, int fret, double numberScale, string font)
    {
        var label = d.Text(fret.ToString(), font, 13 * numberScale, ThemeColours.InkOn(accent), d.PixelsPerDip, weight: 700);
        var w = Math.Max(label.Width + 18, 30);
        var h = label.Height + 8;
        var x = bar.X + bar.Width / 2 - w / 2;
        var y = Math.Max(2, bar.Y - 5 - h - 6);
        d.Rect(accent, null, x, y, w, h, h / 2);
        d.DrawText(label, x + (w - label.Width) / 2, y + (h - label.Height) / 2);
    }

    /// <summary>The drawn capo under the pointer: the partial index, -1 for the full bar, else null.</summary>
    internal static int? CapoAt(in FretboardGeometryRules.Layout layout, double x, double y, int fullFret, IReadOnlyList<PartialCapo>? partials)
    {
        if (partials is not null)
            for (var i = 0; i < partials.Count; i++)
            {
                var partial = partials[i];
                if (partial is null || partial.Fret <= 0) continue;
                if (FretboardCapoRules.HitTestPartial(layout, x, y, partial.Fret, partial.StringMask)) return i;
            }
        return FretboardCapoRules.HitTest(layout, x, y, fullFret) ? -1 : null;
    }
}

/// <summary>
/// The board control the Capo card hosts: the real fretboard the instrument panel draws (wood, inlays, wires,
/// strings, behind-capo shade, the capo bar clipped to its strings), with the same hover and drag feedback:
/// a ghost preview on an empty fret, the hand cursor and "Capo fret N, drag to move" on the bar, click on a
/// fret to jump, drag to move with one commit on release (preview only, nothing written until the event).
/// Owns: the board layout for its strings, the drawing through <see cref="FretboardBoardPainter"/> and the drag.
/// Does not own: the capo value (the window's working copy: <see cref="CommitRequested"/> carries the fret, the
/// window writes it), the song or the undo step.
/// </summary>
internal class FretboardView : Control
{
    internal static readonly FretboardBoardLimits BoardLimits = new(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity, ClearsLegend: false, RealProportions: true);
    private const string Font = InstrumentPanelView.Font;
    private const double NumberScale = 1.0;
    private const int ShownFrets = 12;

    private readonly AvaloniaDrawContext _draw = new();
    private readonly FretboardAssets _assets = new();
    private readonly FretboardInlayPlan _inlays = new();
    private readonly List<PartialCapo> _single = new() { new PartialCapo() };

    private FretboardStyles.Look _look = FretboardStyles.DefaultLook;
    private bool _bass;
    private int _fret;
    private int _mask;
    private IReadOnlyList<bool> _covered = Array.Empty<bool>();
    private bool _isFull = true;
    private int? _preview;
    private FretboardCapoDrag? _drag;
    private int? _hoverCapo;
    private int? _ghostFret;
    private int? _placementMask;

    internal void SetPlacementStrings(int? mask)
    {
        _placementMask = mask;
        InvalidateVisual();
    }
    private bool _pressArmed;
    private Point _pressAt;
    private int? _pressFret;
    private Cursor? _handCursor;
    private IReadOnlyList<PartialCapo>? _partials;
    private int _fullFret;
    private int _selected = -1;
    private bool _multiple;
    internal event Action<int>? SelectionRequested;
    internal event Action<int>? PlacementRequested;
    protected override Type StyleKeyOverride => typeof(Control);

    internal void ShowCapos(int full, IReadOnlyList<PartialCapo> partials, int selected, int strings, bool bass)
    {
        _multiple = true;
        _fullFret = full;
        _partials = partials;
        _selected = selected;
        var covered = new bool[strings];
        for (var s = 0; s < strings; s++) covered[s] = selected < 0 || (partials[selected].StringMask & (1 << s)) != 0;
        Show(selected < 0 ? full : partials[selected].Fret, covered, bass: bass);
    }

    internal FretboardView() => Height = DialogMetrics.TrackCapoBoardHeight;

    /// <summary>Raised once when a changed drag releases; the window writes its working copy, never the song.</summary>
    public event Func<int, bool>? CommitRequested;

    /// <summary>The fret the bar shows now: the drag preview while dragging, else the capo's fret.</summary>
    internal int ShownFret => _preview ?? _fret;

    /// <summary>The strings the bar covers now (one flag per string): all of them for a full capo, the ticked ones for a partial.</summary>
    internal IReadOnlyList<bool> Covered => _covered;

    /// <summary>True while a capo drag is in progress.</summary>
    internal bool Dragging => _drag is not null;
    internal UiRect? GhostBounds
    {
        get
        {
            var layout = Board(Bounds.Width, Bounds.Height).Layout;
            return _ghostFret is { } fret ? PlacementBounds(layout, fret) : null;
        }
    }

    /// <summary>Shows the capo at <paramref name="fret"/> over the strings flagged in <paramref name="covered"/> (one flag per string).</summary>
    internal void Show(int fret, IReadOnlyList<bool> covered, FretboardStyles.Look? look = null, bool bass = false)
    {
        _fret = fret;
        _covered = covered;
        _look = look ?? FretboardStyles.DefaultLook;
        _bass = bass;
        _isFull = covered.Count > 0;
        _mask = 0;
        for (var s = 0; s < covered.Count && s < 31; s++)
            if (covered[s]) _mask |= 1 << s;
            else _isFull = false;
        _preview = null;
        _drag = null;
        _hoverCapo = null;
        _ghostFret = null;
        Cursor = null;
        RefreshSingle();
        ToolTip.SetTip(this, null);
        InvalidateVisual();
    }

    private void RefreshSingle()
    {
        _single[0].Fret = ShownFret;
        _single[0].StringMask = _mask;
    }

    private (FretboardGeometryRules.Input Input, UiRect Content, FretboardGeometryRules.Layout Layout) Board(double width, double height)
    {
        var strings = Math.Max(1, _covered.Count);
        var packed = _multiple
            ? FretboardGeometryRules.PackCapos(_selected < 0 ? ShownFret : _fullFret, PreviewPartials(), strings)
            : _isFull
            ? FretboardGeometryRules.PackCapos(ShownFret, null, strings)
            : FretboardGeometryRules.PackCapos(0, _single, strings);
        var input = new FretboardGeometryRules.Input(strings, ShownFrets, ShownFrets, 1.0, LeftHanded: false, AnchorFret: 0, Bass: _bass, CapoByString: packed);
        var content = new UiRect(0, 0, Math.Max(80, width), Math.Max(40, height));
        return (input, content, FretboardGeometryRules.Compute(input, content, limits: BoardLimits));
    }

    private IReadOnlyList<PartialCapo>? PreviewPartials()
    {
        if (_preview is null || _selected < 0 || _partials is null) return _partials;
        var result = new List<PartialCapo>(_partials);
        result[_selected] = new PartialCapo { Fret = ShownFret, StringMask = _partials[_selected].StringMask };
        return result;
    }

    /// <summary>The bar's centre x (board DIPs) at <paramref name="fret"/>.</summary>
    internal double FretX(int fret) => FretboardGeometryRules.FretX(Board(Bounds.Width, Bounds.Height).Layout, fret);
    internal double StringY(int index) => FretboardGeometryRules.StringY(Board(Bounds.Width, Bounds.Height).Layout, index);

    public override void Render(DrawingContext context)
    {
        var light = ActualThemeVariant == ThemeVariant.Light;
        var d = _draw.Use(context);
        var ink = AvaloniaPalette.InkOf(light);
        var layout = Board(Bounds.Width, Bounds.Height).Layout;
        var scene = new FretboardScene(d, ink, layout, _look, _bass, _assets, _inlays);
        FretboardArtist.DrawBackdrop(scene);
        FretboardArtist.DrawInlays(scene);
        FretboardArtist.DrawWires(scene);
        DrawFretNumbers(d, ink, layout);
        FretboardArtist.DrawStrings(scene, static _ => default);
        var wood = FretboardArtist.Wood(layout, _bass);
        FretboardBoardPainter.DrawBehindCapo(d, ink, layout, wood);
        if (_covered.Count > 0)
        {
            if (_multiple)
                FretboardBoardPainter.DrawCapos(d, ink, layout, _look, _bass, _assets,
                    _selected < 0 ? ShownFret : _fullFret, _partials, _selected, _preview, null, -1, NumberScale, Font, showBadge: false);
            else if (_isFull)
                FretboardBoardPainter.DrawCapos(d, ink, layout, _look, _bass, _assets, ShownFret, null, -1, null,
                    _drag is not null ? _drag.CurrentFret : null, -1, NumberScale, Font, showBadge: false);
            else
                FretboardBoardPainter.DrawCapos(d, ink, layout, _look, _bass, _assets, 0, _single, 0, _preview,
                    _drag is not null ? _drag.CurrentFret : null, 0, NumberScale, Font, showBadge: false);
        }
        if (_ghostFret is { } ghost && _drag is null)
        {
            var x = PlacementBounds(layout, ghost);
            d.Rect(ink("Accent").WithOpacity(DialogMetrics.TrackCapoOffOpacity), new UiStroke(ink("Accent"), DialogMetrics.TrackCapoSelectionThickness),
                x.X, x.Y, x.Width, x.Height, DialogMetrics.TrackCapoBarRadius);
        }
        if (_multiple && ShownFret > 0)
        {
            var bar = SelectionBounds!.Value;
            d.Rect(null, new UiStroke(ink("Accent"), DialogMetrics.TrackCapoSelectionThickness),
                bar.X, bar.Y, bar.Width, bar.Height, DialogMetrics.TrackCapoBarRadius);
        }
    }

    internal UiRect BarBounds(in FretboardGeometryRules.Layout layout, UiRect wood, int fret) => FretboardBoardPainter.BarBounds(layout, wood, fret);

    private UiRect PlacementBounds(in FretboardGeometryRules.Layout layout, int fret)
    {
        var bar = BarBounds(layout, FretboardArtist.Wood(layout, _bass), fret);
        var band = _placementMask is { } mask ? FretboardCapoRules.PartialBand(layout, mask) : null;
        return new UiRect(bar.X, band?.Top ?? bar.Y, bar.Width, band is { } b ? b.Bottom - b.Top : bar.Height);
    }

    internal UiRect? SelectionBounds
    {
        get
        {
            if (!_multiple || ShownFret <= 0) return null;
            var layout = Board(Bounds.Width, Bounds.Height).Layout;
            var bar = BarBounds(layout, FretboardArtist.Wood(layout, _bass), ShownFret);
            var band = _selected >= 0 ? FretboardCapoRules.PartialBand(layout, _mask) : null;
            return new UiRect(bar.X, band?.Top ?? bar.Y, bar.Width, band is { } b ? b.Bottom - b.Top : bar.Height);
        }
    }

    private void DrawFretNumbers(IDrawContext d, Func<string, UiColor> ink, in FretboardGeometryRules.Layout layout)
    {
        var wood = FretboardArtist.Wood(layout, _bass);
        for (var fret = layout.FirstFret; fret <= layout.LastFret; fret++)
        {
            var number = d.Text(fret.ToString(), Font, DialogMetrics.TrackCapoNumberFontSize, ink("FretLabel"), d.PixelsPerDip);
            d.DrawText(number, FretboardGeometryRules.FretX(layout, fret) - number.Width / 2, wood.Bottom + DialogMetrics.TrackCapoNumberTop);
        }
    }

    private (int Full, IReadOnlyList<PartialCapo>? Partials) ShownState() =>
        _multiple ? (_selected < 0 ? ShownFret : _fullFret, PreviewPartials()) : _isFull ? (ShownFret, null) : (0, _single);

    /// <summary>Grabs the bar when <paramref name="point"/> is on it; false leaves the press alone.</summary>
    internal bool BeginDrag(Point point)
    {
        var layout = Board(Bounds.Width, Bounds.Height).Layout;
        var (full, partials) = ShownState();
        if (FretboardBoardPainter.CapoAt(layout, point.X, point.Y, full, partials) is not { } hit) return false;
        if (_multiple) SelectionRequested?.Invoke(hit);
        _drag = new FretboardCapoDrag(_fret,
            preview: fret => { _preview = fret; RefreshSingle(); InvalidateVisual(); },
            commit: fret => CommitRequested?.Invoke(fret) == true);
        _ghostFret = null;
        return true;
    }

    /// <summary>Moves the grabbed bar to the fret under <paramref name="point"/>: shows it, writes nothing.</summary>
    internal void Drag(Point point)
    {
        if (_drag is null) return;
        var layout = Board(Bounds.Width, Bounds.Height).Layout;
        _drag.Move(FretboardCapoRules.FretAtX(layout, point.X));
    }

    /// <summary>Releases the bar: one commit when the fret changed, then the display follows the window's working copy again.</summary>
    internal void EndDrag()
    {
        var drag = _drag;
        if (drag is null) return;
        _drag = null;
        drag.End();
        _preview = null;
        RefreshSingle();
        InvalidateVisual();
    }

    private int? ClickFret(Point point)
    {
        var layout = Board(Bounds.Width, Bounds.Height).Layout;
        if (point.Y < layout.Board.Y || point.Y > layout.Board.Bottom || point.X > layout.Board.Right) return null;
        return FretboardCapoRules.FretAtX(layout, point.X);
    }

    private void UpdateHover(Point point)
    {
        var layout = Board(Bounds.Width, Bounds.Height).Layout;
        var (full, partials) = ShownState();
        var hover = FretboardBoardPainter.CapoAt(layout, point.X, point.Y, full, partials);
        int? ghost = null;
        if (hover is null)
        {
            var clicked = ClickFret(point);
            if (clicked is { } fret && fret >= layout.FirstFret && fret <= layout.LastFret) ghost = fret;
        }
        if (hover == _hoverCapo && ghost == _ghostFret) return;
        _hoverCapo = hover;
        _ghostFret = ghost;
        Cursor = hover is not null ? _handCursor ??= new Cursor(StandardCursorType.Hand) : null;
        ToolTip.SetTip(this, hover is { } index
            ? $"{(index < 0 ? "Full capo" : "Partial capo")} fret {(index < 0 ? full : partials![index].Fret)}, drag to move"
            : ghost is { } ghostFret ? $"Place {(_placementMask is null ? "full" : "partial")} capo at fret {ghostFret}" : null);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var at = e.GetPosition(this);
        _pressArmed = true;
        _pressAt = at;
        e.Pointer.Capture(this);
        if (BeginDrag(at)) _pressFret = null;
        else _pressFret = ClickFret(at);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var at = e.GetPosition(this);
        if (_drag is not null) { Drag(at); return; }
        if (_pressArmed)
        {
            if (Math.Abs(at.X - _pressAt.X) > FretboardCapoRules.GrabRadius || Math.Abs(at.Y - _pressAt.Y) > FretboardCapoRules.GrabRadius)
                _pressFret = null;
            return;
        }
        UpdateHover(at);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag is not null)
        {
            EndDrag();
            _pressArmed = false;
            _pressFret = null;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }
        if (_pressArmed && _pressFret is { } fret)
        {
            if (_multiple) PlacementRequested?.Invoke(fret);
            else CommitRequested?.Invoke(fret);
            _ghostFret = null;
        }
        _pressArmed = false;
        _pressFret = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoverCapo is null && _ghostFret is null) return;
        _hoverCapo = null;
        _ghostFret = null;
        Cursor = null;
        ToolTip.SetTip(this, null);
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndDrag();
        _pressArmed = false;
        _pressFret = null;
    }
}
