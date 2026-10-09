using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Visualization;

namespace TabForge.Views;

/// <summary>
/// Hosts an instrument visualisation (fretboard / drum kit / keyboard).
/// <para>
/// The panel renders exactly the state the playback timeline produced and adds no presentation
/// animation: an earlier eased "travelling marker" duplicated the sounding note and lagged it by up
/// to 130 ms, which made the highlight disagree with what was audible.
/// </para>
/// </summary>
public sealed partial class InstrumentPanel : FrameworkElement
{
    private IInstrumentRenderer _renderer = new FretboardRenderer();
    private InstrumentKind _rendererKind = InstrumentKind.Guitar;
    private InstrumentVisualState? _state;
    private InstrumentVisualState? _settingsState;
    private InstrumentVisualState? _editingSelection;
    private InstrumentVisualState? _playbackState;
    private readonly VisualTheme _theme = new();
    private FretboardHorizontalPosition _horizontalPosition = FretboardHorizontalPosition.Centre;
    private FretboardHorizontalPosition? _snapPreview;
    private double _horizontalDragOffset;

    private (int String, int Fret)? _hover;

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        (int, int)? next = CanRepositionFretboard && System.Windows.Input.Mouse.LeftButton != System.Windows.Input.MouseButtonState.Pressed &&
            TryHitFret(e.GetPosition(this), out var s, out var f) ? (s, f) : null;
        if (next != _hover) { _hover = next; InvalidateVisual(); }
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover is null) return;
        _hover = null;
        InvalidateVisual();
    }

    public InstrumentPanel()
    {
        SnapsToDevicePixels = true;
        // A Tab stop only: Tab can reach the panel, but a mouse click (note entry) never takes focus away from the score.
        Focusable = true;
        FocusVisualStyle = null;   // the focus outline is drawn by the panel itself
        GotKeyboardFocus += (_, _) => InvalidateVisual();
        LostKeyboardFocus += (_, _) => InvalidateVisual();
    }

    /// <summary>Shift+F10 or the Menu key while the panel has keyboard focus: open its context menu.</summary>
    public event EventHandler? ContextMenuKeyPressed;

    protected override void OnPreviewGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnPreviewGotKeyboardFocus(e);
        if (e.NewFocus == this && Mouse.LeftButton == MouseButtonState.Pressed) e.Handled = true;   // not by mouse click
    }

    internal bool TryHandleContextMenuKey(Key key, ModifierKeys mods)
    {
        if (key == Key.System) return false;
        if (!((key == Key.Apps && mods == ModifierKeys.None) || (key == Key.F10 && mods == ModifierKeys.Shift))) return false;
        ContextMenuKeyPressed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // F10 arrives as a "system" key.
        if (TryHandleContextMenuKey(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers)) e.Handled = true;
    }

    public string Title { get; set; } = "Instrument";

    public FretboardHorizontalPosition HorizontalPosition
    {
        get => _horizontalPosition;
        set
        {
            if (_horizontalPosition == value && Math.Abs(_horizontalDragOffset) < 0.01 && _snapPreview is null) return;
            _horizontalPosition = value;
            _horizontalDragOffset = 0;
            _snapPreview = null;
            InvalidateVisual();
        }
    }

    public bool ShowsKeyboard => _state?.Kind == InstrumentKind.Keyboard;

    public bool CanRepositionFretboard => _state?.Kind is InstrumentKind.Guitar or InstrumentKind.Bass;

    public void UpdatePlacementDrag(double horizontalOffset, FretboardHorizontalPosition snapPreview)
    {
        _horizontalDragOffset = double.IsFinite(horizontalOffset) ? horizontalOffset : 0;
        _snapPreview = snapPreview;
        InvalidateVisual();
    }

    public void CommitPlacementDrag(FretboardHorizontalPosition position)
    {
        _horizontalPosition = position;
        _horizontalDragOffset = 0;
        _snapPreview = null;
        InvalidateVisual();
    }

    public void CancelPlacementDrag()
    {
        _horizontalDragOffset = 0;
        _snapPreview = null;
        InvalidateVisual();
    }

    public FretboardHorizontalPosition NearestPositionForDrag(double horizontalOffset)
    {
        var state = _state;
        if (state is null || state.Kind is not (InstrumentKind.Guitar or InstrumentKind.Bass))
            return _horizontalPosition;

        var s = DrawScale;
        var width = (ActualWidth > 0 ? ActualWidth : 900) / s;
        var height = (ActualHeight > 0 ? ActualHeight : 168) / s;
        horizontalOffset /= s;
        var content = FretboardContent(state, width, height);
        var draggedLeft = FretboardGeometry.Compute(state, content, _horizontalPosition, horizontalOffset, width).Board.Left;
        var nearest = FretboardHorizontalPosition.Left;
        var nearestDistance = double.PositiveInfinity;
        foreach (var position in Enum.GetValues<FretboardHorizontalPosition>())
        {
            var anchorLeft = FretboardGeometry.Compute(state, content, position, 0, width).Board.Left;
            var distance = Math.Abs(anchorLeft - draggedLeft);
            if (distance >= nearestDistance) continue;
            nearest = position;
            nearestDistance = distance;
        }
        return nearest;
    }

    /// <summary>The fretboard layout in the drawing's virtual (scaled) space, exactly as the renderer computes it; null when no fretboard is shown.</summary>
    internal (FretboardGeometry.Layout Layout, double Scale)? CurrentFretboardLayout()
    {
        var state = _state;
        if (state is null || state.Kind is not (InstrumentKind.Guitar or InstrumentKind.Bass)) return null;
        var s = DrawScale;
        var w = (ActualWidth <= 0 ? 900 : ActualWidth) / s;
        var h = (ActualHeight <= 0 ? 168 : ActualHeight) / s;
        return (FretboardGeometry.Compute(state, FretboardContent(state, w, h), _horizontalPosition, 0, w), s);
    }

    /// <summary>Hit test in panel coordinates, using the same geometry as the renderer.</summary>
    public bool TryHitFret(Point point, out int stringIndex, out int fret)
    {
        stringIndex = 0; fret = 0;
        var state = _state;
        if (state is null) return false;
        var s = DrawScale;   // the drawing is scaled: hit test in the same virtual space
        var width = (ActualWidth <= 0 ? 900 : ActualWidth) / s;
        var content = FretboardContent(state, width, (ActualHeight <= 0 ? 168 : ActualHeight) / s, s);   // same scale as OnRender: the keyboard's legend strip depends on it
        return FretboardGeometry.HitTest(state, content, new Point(point.X / s, point.Y / s), out stringIndex, out fret,
            _horizontalPosition, _horizontalDragOffset / s, width);
    }

    public void SetState(InstrumentVisualState state)
    {
        _settingsState = state;
        _playbackState = state.IsPlaying || state.IsPaused ? state : null;
        ApplyActiveState();
    }

    /// <summary>Updates the non-expiring cursor highlight independently from real-time playback.</summary>
    public void SetEditingSelection(InstrumentVisualState state)
    {
        _editingSelection = state;
        ApplyActiveState();
    }

    private bool _audioTrack;

    /// <summary>True while an audio track is selected: the panel shows no instrument, only a short note.</summary>
    public bool AudioTrack
    {
        get => _audioTrack;
        set
        {
            if (_audioTrack == value) return;
            _audioTrack = value;
            ApplyActiveState();
            InvalidateVisual();
        }
    }

    private void ApplyActiveState()
    {
        _state = _audioTrack ? null : _playbackState ?? _editingSelection ?? _settingsState;
        if (_state is null) return;
        var host = Parent as FrameworkElement;
        var tip = _state.Kind == InstrumentKind.Drums
            ? "Click a drum sound to write it at the cursor. Sounds light up as they are played."
            : "Click a fret to enter a note. Drag horizontally to snap the fretboard left, centre or right.";
        if (host is not null && host.ToolTip is string current && current != tip) host.ToolTip = tip;
        if (_rendererKind != _state.Kind)
        {
            _rendererKind = _state.Kind;
            _renderer = _state.Kind switch
            {
                InstrumentKind.Drums => new DrumRenderer(),
                InstrumentKind.Keyboard => new KeyboardRenderer(),
                _ => new FretboardRenderer()
            };
        }
        UpdateRequiredHeight();
        InvalidateVisual();
    }

    // ---------- the height the instrument needs to draw completely (the dock's hard minimum) ----------

    /// <summary>Smallest spacing between fretboard strings that still leaves each string's markers and label readable.</summary>
    public const double MinStringGap = FretboardGeometry.MinStringGap;
    /// <summary>Extra room under the drawn extent so rounding / DPI never cuts the last row.</summary>
    private const double SafetyMargin = 6;
    /// <summary>Sounding marker below the lowest string: radius 16 + halo 4; fret numbers end at bottom + 5 + ~14.</summary>
    private const double BelowLowestString = 16 + 4 + 2;
    /// <summary>Smallest row of the drum key map that fits its 9 pt label.</summary>
    private const double MinPercussionRow = 14;
    /// <summary>Colour legend (4 rows from y 8-10) and the Scales button placed under it (24 px + margin).</summary>
    private const double LegendAndScalesButton = 10 + 4 * 18 + 6 + 24 + 6;
    /// <summary>Highest the fretboard legend may sit (when the grid is too short to bottom-align the whole block).</summary>
    private const double LegendTop = 10;

    /// <summary>Smallest drawing scale (markers still readable); the pane's minimum height is the natural height at this scale.</summary>
    public const double MinScale = 0.7;
    /// <summary>Largest drawing scale; a taller pane beyond this spreads the strings instead.</summary>
    public const double MaxScale = 2.0;
    /// <summary>Width (DIPs) the fretboard drawing is laid out for at scale 1; a narrower pane scales it down instead of squeezing it.</summary>
    public const double NaturalWidth = 900;

    /// <summary>Height at which the instrument is drawn at scale 1 (full natural layout).</summary>
    public double NaturalHeight { get; private set; } = RequiredHeightFor(null, 900);

    /// <summary>Current drawing scale: pane height / natural height, clamped to [MinScale, MaxScale]
    /// (drum map: always 1, it lays itself out in rows). Cheap; derived from the size, not per-frame state.</summary>
    public double DrawScale
    {
        get
        {
            if (_state is null || _state.Kind is InstrumentKind.Drums or InstrumentKind.Keyboard || NaturalHeight <= 0) return 1;
            var h = ActualHeight > 0 ? ActualHeight : NaturalHeight;
            var scale = h / NaturalHeight;
            // Fretboard: a narrow pane must not blow the drawing up by its height alone (that squeezed the frets
            // and stretched the strings); the virtual width never drops below NaturalWidth unless MinScale forces it.
            if (_state.Kind is InstrumentKind.Guitar or InstrumentKind.Bass && ActualWidth > 0)
                scale = Math.Min(scale, ActualWidth / NaturalWidth);
            return Math.Clamp(Math.Min(scale, ScaleCap), MinScale, MaxScale);
        }
    }

    /// <summary>The pane height (DIPs) below which the instrument could not be drawn completely even at
    /// <see cref="MinScale"/>. Changes only with the instrument kind, string count or (drum map) column count.</summary>
    public double RequiredHeight { get; private set; } = MinimumPaneHeight(null, RequiredHeightFor(null, 900));

    private static double MinimumPaneHeight(InstrumentVisualState? state, double natural) =>
        state?.Kind == InstrumentKind.Keyboard ? KeyboardPaneSizing.MinKeyHeight
        : state?.Kind == InstrumentKind.Drums
            ? natural
            // Scaled drawing, plus the unscaled Scales button (24 px) under the scaled legend.
            : Math.Ceiling(Math.Max(natural * MinScale, (10 + 4 * 18 + 8) * MinScale + 24 + 6));

    /// <summary>Drawing scale per unit of score text scale: fret numbers (13.5 px at scale 1) stay about 1.1x the score's tab digits
    /// (11 px x zoom x tab spacing), so the board, its labels and legend read in the same family of sizes as the score.</summary>
    public const double ScoreTextRatio = 0.9;
    private double _contentScale = 1;
    /// <summary>The score's text scale (zoom x tab spacing; 1 in Band rows). The drawing never grows past it times
    /// <see cref="ScoreTextRatio"/>, so a tall or wide pane spreads the strings instead of blowing the text up.</summary>
    public double ContentScale
    {
        get => _contentScale;
        set
        {
            if (!double.IsFinite(value) || Math.Abs(value - _contentScale) < 0.001) return;
            _contentScale = value;
            UpdateRequiredHeight();
            InvalidateVisual();
        }
    }
    /// <summary>Largest drawing scale at the current <see cref="ContentScale"/>.</summary>
    public double ScaleCap => Math.Clamp(_contentScale * ScoreTextRatio, MinScale, MaxScale);

    /// <summary>Default ("medium") drawing scale of a fresh profile: about 1.1x, i.e. string spacing near 29 px at a wide window.</summary>
    public const double MediumScale = 1.1;

    /// <summary>The tallest pane height (DIPs) the fretboard is useful at: the board at its maximum stretch plus its
    /// labels and legend. A taller pane would only leave empty space, so the dock clamps to it. +Infinity for
    /// the drum map and keyboard, which fill any height.</summary>
    public double MaximumHeight { get; private set; } = double.PositiveInfinity;

    /// <summary>Raised when <see cref="MaximumHeight"/> changes (width, string count or spacing).</summary>
    public event Action<double>? MaximumHeightChanged;

    /// <summary>Pane height of a fresh profile: natural height at <see cref="MediumScale"/> or <see cref="ScaleCap"/> if smaller (limited by the width like <see cref="DrawScale"/>),
    /// never below <see cref="RequiredHeight"/> nor above <see cref="MaximumHeight"/>.</summary>
    public double MediumHeight()
    {
        var scale = Math.Min(MediumScale, ScaleCap);
        if (_state?.Kind is InstrumentKind.Guitar or InstrumentKind.Bass && ActualWidth > 0)
            scale = Math.Min(scale, ActualWidth / NaturalWidth);
        scale = Math.Max(scale, MinScale);
        return Math.Min(Math.Max(Math.Ceiling(NaturalHeight * scale), RequiredHeight), Math.Max(MaximumHeight, RequiredHeight));
    }

    /// <summary>Pane height at which the fretboard stops growing: the drawing is at its largest scale and each string gap
    /// at its widest (natural gap times the spacing factor); see <see cref="FretboardGeometry.Compute"/>.</summary>
    public static double MaximumPaneHeight(InstrumentVisualState? state, double width, double natural, double cap = MaxScale)
    {
        if (state?.Kind == InstrumentKind.Keyboard) return KeyboardPaneSizing.MaxKeyHeight;
        if (state is not null && state.Kind is not (InstrumentKind.Guitar or InstrumentKind.Bass)) return double.PositiveInfinity;
        var w = width > 0 ? width : NaturalWidth;
        var scale = Math.Clamp(Math.Min(cap, w / NaturalWidth), MinScale, MaxScale);
        var strings = state is null ? 6 : Math.Max(1, state.Tuning.Count);
        var frets = state is null ? 24 : (state.DisplayFrets is 12 or 24 ? state.DisplayFrets : 24);
        if (state is not null) frets = Math.Max(12, Math.Min(frets, Math.Max(12, state.FretCount)));
        var boardWidth = Math.Max(80, Math.Min(Math.Max(80, w / scale - 28), 1180) - FretboardGeometry.LeftGutter);
        var fretWidth = boardWidth / frets;
        var spacing = state is not null && double.IsFinite(state.StringSpacing)
            ? Math.Clamp(state.StringSpacing, 0.75, FretboardGeometry.MaxSpacingFactor) : 1.0;
        var gap = Math.Max(MinStringGap, FretboardGeometry.MaxGapToFretWidth * fretWidth) * spacing;
        if (state is not null)
        {
            // The renderer's own layout at an unlimited height: the board's widest stretch, with the legend clearance applied.
            var content = FretboardContent(state, w / scale, 100000, scale);
            gap = FretboardGeometry.Compute(state, content).StringGap;
        }
        var virtualHeight = FretboardGeometry.TopPad + (strings - 1) * gap + Math.Max(FretboardGeometry.BottomPad, BelowLowestString) + SafetyMargin;
        var max = Math.Ceiling(Math.Max(virtualHeight, natural) * scale);
        return Math.Max(max, MinimumPaneHeight(state, natural));
    }

    /// <summary>Raised when <see cref="RequiredHeight"/> changes (track / tuning / view mode switch).</summary>
    public event Action<double>? RequiredHeightChanged;

    public static double RequiredHeightFor(InstrumentVisualState? state, double width)
    {
        switch (state?.Kind)
        {
            case InstrumentKind.Drums:
            {
                var columns = Math.Clamp((int)((width > 0 ? width : 900) / 190), 4, 13);
                var rows = (int)Math.Ceiling(PercussionNames.Length / (double)columns);
                // Grid starts at y 5 and PercussionGrid reserves 10 px; the Scales button sits top-right.
                return Math.Ceiling(Math.Max(10 + rows * MinPercussionRow + 2, 8 + 24 + 4));
            }
            case InstrumentKind.Keyboard:
                // Natural key height from the key width (60-120 px, see KeyboardPaneSizing).
                return Math.Ceiling(KeyboardPaneSizing.HeightFor(width, KeyboardPaneSizing.WhiteKeysFor(state.KeyboardKeys)));
            default:
            {
                // Fretboard (also the placeholder before a track is chosen: six strings, so the pane does
                // not jump when the first song loads): top pad, string gaps, and the bottom pad that holds
                // the lowest string's markers and the fret-number row.
                var strings = state is null ? 6 : Math.Max(1, state.Tuning.Count);
                // Drawn extent from the renderer's own layout: board top, string rows, then the larger of the
                // bottom pad (fret numbers) and the lowest string's marker + halo.
                // "Wide" string spacing needs proportionally more room (the drawing scales down instead of clipping).
                var wide = state is not null && double.IsFinite(state.StringSpacing)
                    ? Math.Clamp(state.StringSpacing, 1.0, FretboardGeometry.MaxSpacingFactor) : 1.0;
                var board = FretboardGeometry.TopPad + (strings - 1) * MinStringGap * wide
                            + Math.Max(FretboardGeometry.BottomPad, BelowLowestString);
                return Math.Ceiling(Math.Max(board, LegendAndScalesButton) + SafetyMargin);
            }
        }
    }

    private void UpdateRequiredHeight()
    {
        NaturalHeight = RequiredHeightFor(_state, ActualWidth);
        var maximum = MaximumPaneHeight(_state, ActualWidth, NaturalHeight, ScaleCap);
        if (!(Math.Abs(maximum - MaximumHeight) < 0.5) && !(double.IsPositiveInfinity(maximum) && double.IsPositiveInfinity(MaximumHeight)))
        {
            MaximumHeight = maximum;
            MaximumHeightChanged?.Invoke(maximum);
        }
        var required = MinimumPaneHeight(_state, NaturalHeight);
        if (Math.Abs(required - RequiredHeight) < 0.5) return;
        RequiredHeight = required;
        RequiredHeightChanged?.Invoke(required);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        // Only the drum key map's row count depends on the width.
        // The fretboard's maximum height also follows the width.
        if (sizeInfo.WidthChanged && _state?.Kind is InstrumentKind.Drums or InstrumentKind.Guitar or InstrumentKind.Bass or InstrumentKind.Keyboard) UpdateRequiredHeight();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var w = double.IsInfinity(availableSize.Width) ? 900 : availableSize.Width;
        // No minimum desired height: WPF arranges a child at max(pane, desired), so a 168 here made the panel
        // taller than a small pane (the host then clipped the bottom strings and fret numbers). The dock's
        // minimum (RequiredHeight) is what keeps the pane large enough; the panel just fills it.
        return new Size(w, 0);
    }

    protected override void OnRender(DrawingContext dc)
    {
        try
        {
            RenderGuard.Inject("InstrumentPanel"); RenderCore(dc);
            // Visible keyboard focus: an accent outline just inside the panel.
            if (IsKeyboardFocused && ActualWidth > 6 && ActualHeight > 6)
                dc.DrawRectangle(null, Draw.Pen(_theme.Accent, 2), new Rect(1, 1, ActualWidth - 2, ActualHeight - 2));
        }
        catch (Exception ex) when (RenderGuard.Contain(ex, "InstrumentPanel", dc, ActualWidth, ActualHeight)) { }
    }

    private void RenderCore(DrawingContext dc)
    {
        _legendDrawnThisFrame = false;
        using var dpiScope = Draw.UseDpi(this);   // A-03: text shaped for this window's monitor
        try { RenderPanel(dc); }
        finally { if (!_legendDrawnThisFrame) SetLegendAnchor(null); }
    }

    private void RenderPanel(DrawingContext dc)
    {
        var w = ActualWidth <= 0 ? 900 : ActualWidth;
        var h = ActualHeight <= 0 ? 168 : ActualHeight;
        dc.DrawRectangle(Draw.Solid(_theme.Background), null, new Rect(0, 0, w, h));
        dc.DrawLine(Draw.Pen(_theme.BoardEdge, 1), new Point(0, h - 0.5), new Point(w, h - 0.5));

        var state = _state;
        if (state is null)
        {
            Draw.Centered(dc, _audioTrack ? "Audio track — no instrument view" : "Select a track to see the instrument",
                w / 2, h / 2 - 8, 13, Draw.Solid(_theme.Muted));
            return;
        }

        // Drum tracks: the GM percussion key map (27-87) in columns; sounds light up as they are hit.
        IsAnimating = false;
        if (state.Kind == InstrumentKind.Drums) { DrawPercussionMap(dc, state, w, h); return; }

        // Fretboard / keyboard: drawn at their natural layout in a virtual (w/s x h/s) space and scaled by s,
        // so every string, fret, marker, label and the legend grow and shrink with the pane and always fit.
        var s = DrawScale;
        w /= s; h /= s;
        var offset = _horizontalDragOffset / s;
        dc.PushTransform(new ScaleTransform(s, s));
        try
        {
        var content = FretboardContent(state, w, h, s);
        _renderer.Render(dc, state, content, _theme,
            new InstrumentRenderPlacement(_horizontalPosition, offset, w, _snapPreview));
        if (state.Kind == InstrumentKind.Keyboard && h >= LegendAndScalesButton)
        {
            // Same legend as the fretboard, at the same (unscaled) size, on a backing plate in the strip the keys leave free
            // at the right (see FretboardContent); the hide (X) button's corner strip is also kept clear. Real pixels, so a tall
            // pane (which scales the keys up) does not blow the legend up.
            dc.PushTransform(new ScaleTransform(1 / s, 1 / s));
            var plateWidth = FretboardGeometry.LegendWidth - FretboardGeometry.CornerReserve;
            var lx = Math.Max(4, w * s - plateWidth - 2 - FretboardGeometry.CornerReserve);
            dc.DrawRoundedRectangle(Draw.Solid(_theme.Background, 0.85), Draw.Pen(_theme.BoardEdge, 1),
                new Rect(lx - 4, 4, plateWidth, 4 * 18 + 8), 4, 4);
            Legend(dc, 8, lx + 2, _theme);
            dc.Pop();
            SetLegendAnchor(new Point(lx, 8 + 4 * 18 + 8));
        }
        if (state.Kind is InstrumentKind.Guitar or InstrumentKind.Bass)
        {
            var geometry = FretboardGeometry.Compute(state, content, _horizontalPosition, offset, w);
            // The legend with the Scales button under it (24 real px) is bottom-aligned with the grid's lower edge, so it rides
            // with the board when a tall pane centres it and never sits above the grid's top.
            var legendY = Math.Max(LegendTop, geometry.Board.Bottom - (4 * 18 + 6 + 24 / s));
            Legend(dc, legendY, geometry.Board.Right + 14, _theme);
            SetLegendAnchor(new Point((geometry.Board.Right + 12) * s, (legendY + 4 * 18 + 6) * s));
            // Hover preview: a faded, semi-transparent note where a click would write one.
            if (_hover is { } hover)
            {
                var p = FretboardGeometry.PositionOf(geometry, hover.String, hover.Fret);
                var r = Math.Clamp(geometry.StringGap * 0.42, 6, 12);
                dc.DrawEllipse(Draw.Solid(_theme.Accent, 0.28), Draw.Pen(_theme.Accent, 1.4, 0.55), p, r, r);
                if (hover.Fret >= 0)
                    Draw.Centered(dc, hover.Fret.ToString(), p.X, p.Y - 6.5, 10, Draw.Solid(_theme.Text, 0.75), true);
            }
        }
        }
        finally { dc.Pop(); }
    }

    private static Rect FretboardContent(InstrumentVisualState state, double width, double height, double scale = 1)
    {
        var content = new Rect(0, 0, width, height);
        if (state.Kind == InstrumentKind.Keyboard)
        {
            // The keys stop short of the legend plate (real pixels: the plate is not scaled with the drawing).
            var reserve = (FretboardGeometry.LegendWidth - FretboardGeometry.CornerReserve + 4 + FretboardGeometry.CornerReserve) / Math.Max(0.1, scale);
            return KeyboardPaneSizing.KeysRect(width, height, state.KeyboardKeys, reserve);
        }
        if (state.Kind is not (InstrumentKind.Guitar or InstrumentKind.Bass)) return content;

        var board = FretboardGeometry.Compute(state, content).Board;
        var overflow = board.Right + FretboardGeometry.LegendWidth - content.Right;
        if (overflow > 0)
        {
            // Only narrow the fretboard once the side legend would overlap it, and only by the
            // amount needed to keep the legend clear.
            content.Width = Math.Max(80, content.Width - overflow);
        }
        return content;
    }

    /// <summary>Where the space under the colour legend starts (null when no legend is drawn); the main
    /// window places its Scales button there. Raised only when it moves, never per frame.</summary>
    public event Action<Point?>? LegendAnchorChanged;
    private Point? _legendAnchor;
    private bool _legendDrawnThisFrame;

    private void SetLegendAnchor(Point? anchor)
    {
        _legendDrawnThisFrame = anchor is not null;
        if (Nullable.Equals(_legendAnchor, anchor)) return;
        _legendAnchor = anchor;
        LegendAnchorChanged?.Invoke(anchor);
    }

    private static void Legend(DrawingContext dc, double y, double x, VisualTheme theme)
    {
        var items = new (Color c, string t)[]
        {
            (theme.Current, "now"),
            (theme.Next, "next"),
            (theme.Accent, "upcoming"),
            (theme.Past, "recent")
        };
        for (var i = 0; i < items.Length; i++)
        {
            var (color, text) = items[i];
            var lineY = y + i * 18;
            dc.DrawEllipse(Draw.Solid(color), Draw.Pen(theme.Wood, 1.5), new Point(x + 4, lineY + 7), 4.5, 4.5);   // ringed in the board colour, as the markers sit on it
            Draw.At(dc, text, x + 14, lineY, 13.5, Draw.Solid(theme.Legible));
        }
    }
}
