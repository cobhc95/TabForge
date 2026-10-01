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

    private void ApplyActiveState()
    {
        _state = _playbackState ?? _editingSelection ?? _settingsState;
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
            if (_state is null || _state.Kind == InstrumentKind.Drums || NaturalHeight <= 0) return 1;
            var h = ActualHeight > 0 ? ActualHeight : NaturalHeight;
            var scale = h / NaturalHeight;
            // Fretboard: a narrow pane must not blow the drawing up by its height alone (that squeezed the frets
            // and stretched the strings); the virtual width never drops below NaturalWidth unless MinScale forces it.
            if (_state.Kind is InstrumentKind.Guitar or InstrumentKind.Bass && ActualWidth > 0)
                scale = Math.Min(scale, ActualWidth / NaturalWidth);
            return Math.Clamp(scale, MinScale, MaxScale);
        }
    }

    /// <summary>The pane height (DIPs) below which the instrument could not be drawn completely even at
    /// <see cref="MinScale"/>. Changes only with the instrument kind, string count or (drum map) column count.</summary>
    public double RequiredHeight { get; private set; } = MinimumPaneHeight(null, RequiredHeightFor(null, 900));

    private static double MinimumPaneHeight(InstrumentVisualState? state, double natural) =>
        state?.Kind == InstrumentKind.Drums
            ? natural
            // Scaled drawing, plus the unscaled Scales button (24 px) under the scaled legend.
            : Math.Ceiling(Math.Max(natural * MinScale, (10 + 4 * 18 + 8) * MinScale + 24 + 6));

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
                // Legend plate over the keys plus the Scales button; the keys themselves need the white-key
                // labels (bottom - 16) and scale marks (bottom - 30) below the black keys (60 % of the height).
                return Math.Ceiling(Math.Max(LegendAndScalesButton, 100));
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
        var required = MinimumPaneHeight(_state, NaturalHeight);
        if (Math.Abs(required - RequiredHeight) < 0.5) return;
        RequiredHeight = required;
        RequiredHeightChanged?.Invoke(required);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        // Only the drum key map's row count depends on the width.
        if (sizeInfo.WidthChanged && _state?.Kind == InstrumentKind.Drums) UpdateRequiredHeight();
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
            Draw.Centered(dc, "Select a track to see the instrument", w / 2, h / 2 - 8, 13, Draw.Solid(_theme.Muted));
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
        if (state.Kind == InstrumentKind.Keyboard)
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

    public static readonly string[] PercussionNames =
    {
        "High Q", "Slap", "Scratch Push", "Scratch Pull", "Sticks", "Square Click", "Metronome Click", "Metronome Bell",
        "Acoustic Bass Drum", "Bass Drum 1", "Side Stick", "Acoustic Snare", "Hand Clap", "Electric Snare", "Low Floor Tom",
        "Closed Hi-Hat", "High Floor Tom", "Pedal Hi-Hat", "Low Tom", "Open Hi-Hat", "Low-Mid Tom", "Hi-Mid Tom",
        "Crash Cymbal 1", "High Tom", "Ride Cymbal 1", "Chinese Cymbal", "Ride Bell", "Tambourine", "Splash Cymbal",
        "Cowbell", "Crash Cymbal 2", "Vibraslap", "Ride Cymbal 2", "High Bongo", "Low Bongo", "Mute Hi Conga",
        "Open Hi Conga", "Low Conga", "High Timbale", "Low Timbale", "High Agogo", "Low Agogo", "Cabasa", "Maracas",
        "Short Whistle", "Long Whistle", "Short Guiro", "Long Guiro", "Claves", "Hi Wood Block", "Low Wood Block",
        "Mute Cuica", "Open Cuica", "Mute Triangle", "Open Triangle", "Shaker", "Jingle Bell", "Bell Tree",
        "Castinets", "Mute Surdo", "Open Surdo",
    };
    private const int FirstPercussion = 27;
    /// <summary>Drum tracks: the label the track's drum preset writes on the TAB for a sound (shown in the key map).</summary>
    public Func<int, string>? DrumLabel { get; set; }

    private (int Columns, int Rows, double CellW, double CellH) PercussionGrid(double w, double h)
    {
        var count = PercussionNames.Length;
        var columns = Math.Clamp((int)(w / 190), 4, 13);
        var rows = (int)Math.Ceiling(count / (double)columns);
        return (columns, rows, (w - 16) / columns, (h - 10) / rows);
    }

    /// <summary>GM percussion note under a point of the key map (drum tracks), for click-to-write.</summary>
    public bool TryHitPercussion(Point point, out int midi)
    {
        midi = 0;
        if (_state?.Kind != InstrumentKind.Drums) return false;
        var (columns, rows, cw, ch) = PercussionGrid(ActualWidth <= 0 ? 900 : ActualWidth, ActualHeight <= 0 ? 168 : ActualHeight);
        var col = (int)((point.X - 8) / cw); var row = (int)((point.Y - 5) / ch);
        if (col < 0 || col >= columns || row < 0 || row >= rows) return false;
        var index = col * rows + row;
        if (index >= PercussionNames.Length) return false;
        midi = FirstPercussion + index;
        return true;
    }

    /// <summary>True while a drum hit is still fading, so the host redraws per frame only then.</summary>
    public bool IsAnimating { get; private set; }

    private void DrawPercussionMap(DrawingContext dc, InstrumentVisualState state, double w, double h)
    {
        var (columns, rows, cw, ch) = PercussionGrid(w, h);
        // Hit recently (within 260 ms of its onset) -> glow that fades; sounding -> steady highlight.
        var glow = new Dictionary<int, double>();
        foreach (var note in state.Notes)
        {
            var age = state.NowMs - note.OnsetMs;
            if (age < -5 || age > 260) continue;
            var strength = 1 - Math.Clamp(age / 260, 0, 1);
            glow[note.Midi] = Math.Max(glow.GetValueOrDefault(note.Midi), strength);
        }
        IsAnimating = glow.Count > 0;
        var fontSize = Math.Clamp(ch * 0.62, 9, 13);
        for (var i = 0; i < PercussionNames.Length; i++)
        {
            var col = i / rows; var row = i % rows;
            var rect = new Rect(8 + col * cw, 5 + row * ch, cw - 4, ch - 1);
            var midi = FirstPercussion + i;
            if (glow.TryGetValue(midi, out var g) && g > 0)
                dc.DrawRoundedRectangle(Draw.Solid(_theme.Current, 0.25 + 0.55 * g), Draw.Pen(_theme.Current, 1, 0.9), rect, 3, 3);
            var mapped = DrumLabel?.Invoke(midi);
            var text = string.IsNullOrEmpty(mapped) || mapped == midi.ToString() ? $"{midi} - {PercussionNames[i]}" : $"{midi} - {PercussionNames[i]}  [{mapped}]";
            Draw.At(dc, text, rect.X + 4, rect.Y + (ch - fontSize * 1.35) / 2, fontSize,
                Draw.Solid(g > 0 ? _theme.Text : _theme.Muted), g > 0.3);
        }
    }

    private static Rect FretboardContent(InstrumentVisualState state, double width, double height, double scale = 1)
    {
        var content = new Rect(0, 0, width, height);
        if (state.Kind == InstrumentKind.Keyboard)
        {
            // The keys stop short of the legend plate (real pixels: the plate is not scaled with the drawing).
            var reserve = (FretboardGeometry.LegendWidth - FretboardGeometry.CornerReserve + 4 + FretboardGeometry.CornerReserve) / Math.Max(0.1, scale);
            content.Width = Math.Max(120, width - reserve);
            return content;
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
            dc.DrawEllipse(Draw.Solid(color), null, new Point(x + 4, lineY + 7), 4, 4);
            Draw.At(dc, text, x + 14, lineY + 1, 10, Draw.Solid(theme.Muted));
        }
    }
}
