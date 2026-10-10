using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views.Score;

namespace TabForge.Views.Band;

// Owns: one track's strip (tab, notation or both). A TabEditorControl is engraved once into a clipped canvas and slid with a render
//   transform; the playhead line and the sounding-note glow (BandNoteGlow) are further elements on the same canvas, moved by transforms
//   too. Horizontal layout: one line slid sideways. Vertical layout: the systems wrap to the lane's width and stack, and the strip is
//   slid down to the top system. The click maps to a bar and cell.
// Does not own: the song, playback timing, the row, the instrument beside it, the order of the rows, or the scroll position every lane
//   shares (BandScroll hands it in, with a shared bar-width floor and zoom, so one bar has one place in every lane).
// Tests: TestBandLaneCache, TestBandLaneClick, TestBandNoteGlow, TestBandLaneContent, TestBandLanesInSync, TestBandEmptyBarLine, TestBandVerticalLanes.
internal sealed class BandLane : Border
{
    /// <summary>Where the playhead stands in the strip while the strip scrolls (fraction of the lane width).</summary>
    public const double PlayheadAt = 0.35;

    private readonly TabEditorControl _editor = new()
    {
        Notation = NotationMode.TabOnly, HorizontalScroll = true, HideCursor = true, IsHitTestVisible = false, Focusable = false,
        PlaybackMeasure = -1, IgnoreAncestorViewport = true
    };
    private readonly Canvas _canvas = new() { ClipToBounds = true };
    private readonly TranslateTransform _slide = new();
    private readonly TranslateTransform _playheadSlide = new();
    private readonly Rectangle _playhead = new() { Width = 2, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly BandNoteGlow _glow = new();
    private IReadOnlyList<(int Bar, int Cell, int String, string Label, bool Struck)> _sounding = Array.Empty<(int, int, int, string, bool)>();
    private BandAppearance.Key _look;
    private double _offset;
    private double _pos;
    private double _yBase;
    private bool _vertical;
    private double _zoomCap = 4.0;
    private double _userZoom = 1;
    private int _wantBar = -2;
    private double _wantFraction;
    private int _shownBar = -2;
    private double _shownFraction = -1;
    private double _shownPos = double.NaN;

    public BandLane()
    {
        _editor.RenderTransform = _slide;
        _glow.RenderTransform = _slide;
        _playhead.RenderTransform = _playheadSlide;
        _canvas.Children.Add(_editor);
        _canvas.Children.Add(_glow);
        _canvas.Children.Add(_playhead);
        Child = _canvas;
        SnapsToDevicePixels = true;
        ClipToBounds = true;
    }

    /// <summary>How the strip follows the playhead (hold, page jump, glided jump or continuous).</summary>
    internal BandFollow Follow
    {
        get => _follow;
        set
        {
            if (_follow == value) return;
            _follow = value;
            _shownBar = -2;
            if (_wantBar >= -1) Place(_wantBar, _wantFraction, _pos);
        }
    }
    private BandFollow _follow = BandFollow.Continuous;

    /// <summary>Vertical layout: the systems wrap to the lane's width and stack down it. Horizontal: one line slid sideways.</summary>
    internal bool Vertical
    {
        get => _vertical;
        set
        {
            if (_vertical == value) return;
            _vertical = value;
            _editor.HorizontalScroll = !value;
            _editor.InvalidateMeasure();
            _offset = 0; _pos = 0;
            _shownBar = -2;
            Refit();
        }
    }

    /// <summary>The largest zoom the lane may use: the smallest wanted zoom of all lanes, so a bar has the same size in every lane.</summary>
    internal double ZoomCap
    {
        get => _zoomCap;
        set { if (Math.Abs(value - _zoomCap) < 0.001) return; _zoomCap = value; Refit(); }
    }

    /// <summary>The Band's own zoom, a multiplier on the wanted zoom; the strip is engraved again only when it changes.</summary>
    internal double UserZoom
    {
        get => _userZoom;
        set { value = BandChoices.ClampZoom(value); if (Math.Abs(value - _userZoom) < 0.001) return; _userZoom = value; Refit(); }
    }

    /// <summary>The zoom this lane would use alone (the score's text scale, shrunk to fit its height).</summary>
    internal double WantedZoom { get; private set; } = 1;

    /// <summary>Raised when the zoom this lane wants or its width changed: the controller then settles the shared cap and page width.</summary>
    internal event Action? WantedZoomChanged;

    private double _shareWidth, _lastWidth;
    /// <summary>Vertical layout: the page width (the narrowest lane's) every lane wraps its systems at, so each system starts on the same bar in all lanes.</summary>
    internal double SharedWidth
    {
        get => _shareWidth;
        set { if (Math.Abs(value - _shareWidth) < 0.5) return; _shareWidth = value; Refit(); }
    }

    /// <summary>Hands the lane the bar widths all lanes share (the widest of each bar), so the bars line up between lanes.</summary>
    internal void ShareBarWidths(double[]? widths)
    {
        if (ReferenceEquals(_editor.Layout.MinBarWidths, widths)) return;
        _editor.Layout.MinBarWidths = widths;
        _editor.InvalidateMeasure();
        _editor.InvalidateVisual();
        Refit();
    }

    /// <summary>This lane's own bar widths (layout units, before the shared floor), or null for an audio or empty track.</summary>
    internal double[]? NaturalBarWidths()
    {
        var track = _editor.Track;
        if (track is null || track.IsAudio || track.Measures.Count == 0) return null;
        _editor.Layout.GetLayout(track);
        return _editor.Layout.NaturalWidths;
    }

    /// <summary>Tab, Notation or Both (a <see cref="BandChoices"/> name); the strip is laid out and engraved again when it changes.</summary>
    public void SetContent(string content)
    {
        var mode = content switch { BandChoices.Notation => NotationMode.StaffOnly, BandChoices.Both => NotationMode.TabAndStaff, _ => NotationMode.TabOnly };
        if (_editor.Notation == mode) return;
        _editor.Notation = mode;
        _editor.InvalidateScoreLayout();
        _editor.InvalidateMeasure();
        _editor.InvalidateVisual();
        Refit();
    }

    /// <summary>Raised by a left click in the strip: the track's bar and cell under the pointer.</summary>
    public event Action<int, int>? Clicked;

    /// <summary>The strip's editor (tests read its geometry; nothing edits through it).</summary>
    internal TabEditorControl Editor => _editor;

    internal BandNoteGlow Glow => _glow;

    /// <summary>The playhead line's centre in strip pixels from the strip's left edge (the lane x plus the offset).</summary>
    internal double PlayheadStripX => _playheadSlide.X + 1 + _offset;

    internal bool PlayheadVisible => _playhead.Visibility == Visibility.Visible;

    /// <summary>The playhead line's top-left in lane pixels (a self-test reads it).</summary>
    internal Point PlayheadAtLane => new(_playheadSlide.X + 1, double.IsNaN(Canvas.GetTop(_playhead)) ? 0 : Canvas.GetTop(_playhead));

    /// <summary>Slides of the strip since it was built (a self-test counter: a playing song adds slides, never engravings).</summary>
    public int Slides { get; private set; }

    /// <summary>The offset (rendered pixels) of the strip's left edge from the lane's left edge (always 0 in the vertical layout).</summary>
    internal double Offset => _offset;

    /// <summary>The strip's vertical slide (rendered pixels; a self-test reads it).</summary>
    internal double SlideY => _slide.Y;

    /// <summary>Shows <paramref name="trackIndex"/> of <paramref name="project"/> with the look of the main score.</summary>
    public void Bind(SongProject project, int trackIndex, ScoreAppearance look)
    {
        _editor.Project = project;
        _editor.SelectedTrackIndex = trackIndex;
        ApplyLook(look);
        _shownBar = -2;
        _playhead.Visibility = Visibility.Collapsed;
        Refit();
    }

    /// <summary>The look of the main score changed (colours, spacing, labels); a different key engraves the strip again.</summary>
    public void ApplyLook(ScoreAppearance look)
    {
        var key = BandAppearance.KeyOf(look);
        if (key == _look && Background is not null) return;
        _look = key;
        BandAppearance.Copy(look, _editor.Appearance);
        Background = Frozen(BandAppearance.PaperOf(look));
        _playhead.Fill = Frozen(look.PlaybackColor);
        _editor.InvalidateScoreLayout();
        Refit();
    }

    /// <summary>The song's content changed: lay the strip out and engrave it again.</summary>
    public void SongChanged()
    {
        _editor.InvalidateScoreLayout();
        _editor.InvalidateMeasure();
        _editor.InvalidateVisual();
        Refit();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Refit();
    }

    private double _textScale = 1;
    /// <summary>The score's text scale (the main score's "Fit width" zoom for this width): the lane draws its tab at this size,
    /// like the main score, as far as the row's height allows.</summary>
    public double TextScale
    {
        get => _textScale;
        set { if (!double.IsFinite(value) || Math.Abs(value - _textScale) < 0.001) return; _textScale = value; Refit(); }
    }

    /// <summary>How far a system may overflow the lane: its empty top padding is cut before the text shrinks.</summary>
    private const double PaddingOverflow = 1.25;

    /// <summary>Scales the strip to the score's text size (shrunk only when one system cannot fit the lane's height, and never past the
    /// shared cap), centres it, and puts the score header out of view.</summary>
    internal void Refit()
    {
        if (ActualHeight < 20) return;
        _editor.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));   // the system height follows marks found while laying out (tempo, text): read it settled
        var unit = _editor.Zoom > 0 ? _editor.SystemHeightNow / _editor.Zoom : 0;
        if (unit <= 0) return;
        // Only a tab-only strip may overflow (its empty top padding is cut); a strip with notation scales to fit whole.
        var overflow = _editor.Notation == NotationMode.TabOnly ? PaddingOverflow : 1.0;
        var wanted = Math.Clamp(Math.Clamp(Math.Min(_textScale, ActualHeight * 0.98 / unit * overflow), 0.5, 2.0) * _userZoom, 0.25, 4.0);
        var wantedChanged = Math.Abs(wanted - WantedZoom) > 0.001;
        WantedZoom = wanted;
        var zoom = Math.Min(wanted, _zoomCap);
        if (Math.Abs(zoom - _editor.Zoom) > 0.001) _editor.Zoom = zoom;
        if (_vertical) _editor.PageWidthOverride = Math.Max(300, (_shareWidth > 1 ? _shareWidth : ActualWidth) / zoom);
        _editor.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // Centred when it fits; a taller system keeps its tab staff (at the bottom) and loses top padding.
        var spare = ActualHeight - _editor.SystemHeightNow * VisibleSystems;
        // Vertical: whole systems from the top with the spare below, and nothing past them shows. Horizontal: centred.
        _yBase = spare >= 0 ? (_vertical ? 0 : spare / 2) : spare;
        _canvas.Clip = _vertical && spare >= 0 ? new RectangleGeometry(new Rect(0, 0, ActualWidth, _editor.SystemHeightNow * VisibleSystems)) : null;
        _shownBar = -2;   // the strip and the line go back to the last asked position now
        if (_wantBar >= -1) Place(_wantBar, _wantFraction, _pos);
        PlaceGlow();
        var widthChanged = _vertical && Math.Abs(ActualWidth - _lastWidth) > 0.5;
        _lastWidth = ActualWidth;
        if (wantedChanged || widthChanged) WantedZoomChanged?.Invoke();
    }

    /// <summary>How many whole systems the lane shows at once (always 1 for a horizontal strip).</summary>
    internal int VisibleSystems => !_vertical || _editor.SystemHeightNow <= 1 ? 1 : Math.Max(1, (int)Math.Floor(ActualHeight / _editor.SystemHeightNow + 1e-6));

    /// <summary>The number of systems the strip is wrapped into (0 for an audio or empty track).</summary>
    internal int SystemCount => _editor.Track is { IsAudio: false, Measures.Count: > 0 } t ? _editor.Layout.GetLayout(t).SystemCount : 0;

    /// <summary>The system a bar is in, or -1 when the lane has no notation to place it in.</summary>
    internal int SystemOf(int bar) =>
        _editor.Track is { IsAudio: false, Measures.Count: > 0 } t && bar >= 0 ? _editor.Layout.GetLayout(t).SystemForMeasure(Math.Min(bar, t.Measures.Count - 1)) : -1;

    private double SystemTopPx(int system)
    {
        var track = _editor.Track;
        if (track is null || track.Measures.Count == 0) return 0;
        var layout = _editor.Layout.GetLayout(track);
        system = Math.Clamp(system, 0, layout.SystemCount - 1);
        return _editor.SystemTopForMeasure(layout.Systems[system].FirstMeasure);
    }

    /// <summary>Vertical: the strip's offset (rendered pixels) with system position <paramref name="pos"/> at the top.</summary>
    private double TopOffset(double pos)
    {
        var whole = (int)Math.Floor(pos);
        var a = SystemTopPx(whole);
        return pos <= whole ? a : a + (SystemTopPx(whole + 1) - a) * (pos - whole);
    }

    /// <summary>True: the playhead line spans the lane's full height; false: only the engraved staff / tab.</summary>
    internal bool PlayheadFullRow
    {
        get => _playheadFull;
        set { if (_playheadFull == value) return; _playheadFull = value; PlacePlayhead(_playSystem); }
    }
    private bool _playheadFull = true;
    private int _playSystem;

    /// <summary>The playhead line's top and height in lane pixels (a test reads them).</summary>
    internal (double Top, double Height) PlayheadSpan => (Canvas.GetTop(_playhead) is var t && double.IsNaN(t) ? 0 : t, _playhead.Height);

    /// <summary>Full row: the line spans the lane. Tab only: it spans the staff / tab of <paramref name="system"/> (the one the playhead is in).</summary>
    private void PlacePlayhead(int system)
    {
        _playSystem = system;
        var top = 0.0;
        var height = ActualHeight;
        if (!_playheadFull && _editor.Track is { Measures.Count: > 0 } && _editor.Zoom > 0)
        {
            var box = _editor.AuditSystemMetrics(system);
            var z = _editor.Zoom;
            var from = _editor.Notation == NotationMode.TabOnly ? box.TabTop : box.StaffTop;
            var to = _editor.Notation == NotationMode.StaffOnly ? box.StaffBottom : box.TabBottom;
            var pad = box.StringGapPx / 2;
            top = Math.Max(0, _slide.Y + (from - pad) * z);
            height = Math.Max(4, Math.Min(ActualHeight, _slide.Y + (to + pad) * z) - top);
        }
        Canvas.SetTop(_playhead, top);
        _playhead.Height = height;
    }

    /// <summary>
    /// Marks the notes sounding now with the score's own played-note glow. The glow layer repaints only when the set changes; the strip is not engraved again.
    /// </summary>
    public void ShowSounding(IReadOnlyList<(int Bar, int Cell, int String, string Label, bool Struck)> notes)
    {
        _sounding = notes;
        PlaceGlow();
    }

    private void PlaceGlow()
    {
        var track = _editor.Track;
        var zoom = _editor.Zoom;
        if (track is null || track.IsAudio || _sounding.Count == 0 || _editor.Notation == NotationMode.StaffOnly || ActualHeight < 20 || zoom <= 0)
        {
            _glow.Show(Array.Empty<BandNoteGlow.Chip>(), 1, 11, default);
            return;
        }
        var chips = new List<BandNoteGlow.Chip>(_sounding.Count);
        foreach (var (bar, cell, str, label, struck) in _sounding)
        {
            if (bar < 0 || bar >= track.Measures.Count || cell < 0) continue;
            // The centre of the beat's fret number, as the score places it.
            var cells = track.Measures[bar].Cells;
            var start = cell < cells.Count ? cells[cell].RhythmicPosition ?? cell : cell;
            var position = _editor.Layout.GetLayout(track).Measure(bar);
            var metrics = _editor.AuditSystemMetrics(position.SystemIndex);
            var x = position.X + _editor.Layout.WarpFor(track, bar).CenterFraction(Math.Max(0, start)) * position.Width;
            chips.Add(new BandNoteGlow.Chip(x, metrics.TabTop + str * metrics.StringGapPx, label, struck));
        }
        var look = _editor.Appearance;
        _glow.Show(chips, zoom, 11.0 * look.ScoreSpacing, look.PlaybackColor);
    }

    private static SolidColorBrush Frozen(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }

    /// <summary>Forgets the song: the strip's editor drops its project and caches, so a discarded lane pins nothing.</summary>
    public void Release()
    {
        Clicked = null;
        WantedZoomChanged = null;
        _wantBar = -2;
        _sounding = Array.Empty<(int, int, int, string, bool)>();
        _editor.Layout.MinBarWidths = null;
        _editor.ReleaseDocument();
    }

    /// <summary>True when the lane can place <paramref name="bar"/> (it has notation for it).</summary>
    internal bool HasGeometry(int bar) => SystemOf(bar) >= 0;

    /// <summary>Horizontal: where the strip should stand (rendered pixels from its left edge) for the playhead, from <paramref name="current"/>.
    /// A glide eases over several frames, so only a playing song (a call every frame) glides; a seek while stopped lands at once.</summary>
    internal double? NextOffset(int bar, double fraction, bool glide, double current)
    {
        var geometry = bar >= 0 && _editor.Track is { IsAudio: false } ? _editor.PlaybackHorizontalGeometry(bar, fraction) : null;
        if (geometry is null) return null;
        // Continuous: the playhead stands at PlayheadAt of the lane. Jump: the strip stays until the playhead nears the end, then turns half a page.
        var target = _follow.NextOffset(current, ActualWidth, geometry.Value.PlayheadX, geometry.Value.BarWidth, geometry.Value.SystemRight, PlayheadAt);
        var next = glide && _follow.Glide && Math.Abs(target - current) > 0.5 && Math.Abs(target - current) < ActualWidth ? current + (target - current) * 0.25 : target;
        // Whole pixels: the engraved strip and the line then move in step, never a sub-pixel apart.
        return Math.Round(next);
    }

    /// <summary>
    /// Puts the playhead on a bar and a fraction of it (time) and the strip at the shared scroll position <paramref name="pos"/>
    /// (see <see cref="BandScroll"/>). A slide changes two transforms only; the engraving is not touched.
    /// </summary>
    public void Place(int bar, double fraction, double pos, double? systemsAboveBar = null, double? sharedSpaced = null)
    {
        // Vertical: the bar's own system stands the lead lane's number of systems below the top.
        if (_vertical && systemsAboveBar is { } above && SystemOf(bar) is >= 0 and var own) pos = Math.Max(0, own - above);
        _wantBar = bar; _wantFraction = fraction; _pos = pos;
        if (bar == _shownBar && Math.Abs(fraction - _shownFraction) < 1e-6 && pos == _shownPos) return;
        _shownBar = bar; _shownFraction = fraction; _shownPos = pos;
        var geometry = bar >= 0 && _editor.Track is { IsAudio: false } ? _editor.PlaybackHorizontalGeometry(bar, fraction) : null;
        if (_vertical)
        {
            _offset = 0;
            _slide.X = 0;
            _slide.Y = Math.Round(_yBase - TopOffset(pos));
        }
        else
        {
            _offset = pos;
            _slide.X = -pos;
            _slide.Y = _yBase - (_editor.Track is { Measures.Count: > 0 } ? _editor.SystemTopForMeasure(0) : 0);
        }
        if (geometry is null) { _playhead.Visibility = Visibility.Collapsed; return; }
        var playheadX = geometry.Value.PlayheadX;
        // A bar with no notes has no spacing of its own: it takes the position the lanes with notes show, so one line crosses every lane.
        if (sharedSpaced is { } spaced && !BarHasNotes(bar) && _editor.Track is { } t && _editor.Zoom > 0)
        {
            var at = _editor.Layout.GetLayout(t).Measure(bar);
            playheadX = (at.X + spaced * at.Width) * _editor.Zoom;
        }
        _playheadSlide.X = playheadX - _offset - 1;
        PlacePlayhead(geometry.Value.SystemIndex);
        _playhead.Visibility = Visibility.Visible;
        Slides++;
    }

    /// <summary>True when <paramref name="bar"/> holds a note in this lane (either voice).</summary>
    internal bool BarHasNotes(int bar) =>
        _editor.Track is { } t && bar >= 0 && bar < t.Measures.Count && (t.Measures[bar].Cells.Any(c => c.Notes.Count > 0) || t.Measures[bar].Voice2Cells.Any(c => c.Notes.Count > 0));

    /// <summary>The playhead's place in <paramref name="bar"/> as a 0..1 fraction of the bar width, from this lane's own note spacing.</summary>
    internal double? SpacedFraction(int bar, double fraction)
    {
        if (bar < 0 || _editor.Track is not { IsAudio: false } t || _editor.Zoom <= 0 || _editor.PlaybackHorizontalGeometry(bar, fraction) is not { } g) return null;
        var at = _editor.Layout.GetLayout(t).Measure(bar);
        return at.Width > 0 ? (g.PlayheadX / _editor.Zoom - at.X) / at.Width : null;
    }

    /// <summary>A lane on its own: follows the playhead from where it stands (the Band view places every lane through <see cref="BandScroll"/>).</summary>
    public void ShowAt(int bar, double fraction, bool glide = true) =>
        Place(bar, fraction, _vertical ? _pos : NextOffset(bar, fraction, glide, _pos) ?? _pos);

    /// <summary>The bar and cell under a point of the lane (lane coordinates), or null on an audio track or an empty one.</summary>
    internal (int Bar, int Cell)? BarCellAt(double laneX, double laneY = 0)
    {
        var track = _editor.Track;
        if (track is null || track.IsAudio || track.Measures.Count == 0 || _editor.Zoom <= 0) return null;
        var layout = _editor.Layout.GetLayout(track);
        var x = (laneX + _offset) / _editor.Zoom;
        int lo = 0, hi = track.Measures.Count - 1;
        if (_vertical)
        {
            // The system under the point, then the bar within it.
            var page = laneY - _slide.Y;
            var system = 0;
            for (var i = 1; i < layout.SystemCount; i++) if (SystemTopPx(i) <= page) system = i;
            lo = layout.Systems[system].FirstMeasure; hi = layout.Systems[system].LastMeasure;
        }
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (layout.Measure(mid).X <= x) lo = mid; else hi = mid - 1;
        }
        var bar = layout.Measure(lo);
        var spaced = bar.Width > 0 ? Math.Clamp((x - bar.X) / bar.Width, 0, 1) : 0;
        var warp = _editor.Layout.WarpFor(track, lo);
        var cell = Math.Clamp((int)Math.Floor(warp.SlotAt(spaced) + 1e-6), 0, Math.Max(0, (int)warp.Slots - 1));
        return (lo, cell);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var at = e.GetPosition(this);
        if (Click(at.X, at.Y)) e.Handled = true;
    }

    /// <summary>A click at a point of the lane: reports the bar and cell under it; false when there is none.</summary>
    internal bool Click(double laneX, double laneY = 0)
    {
        if (BarCellAt(laneX, laneY) is not { } hit) return false;
        Clicked?.Invoke(hit.Bar, hit.Cell);
        return true;
    }
}
