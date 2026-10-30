using TabForge.Avalonia.Features.Recording.View;
using TabForge.Avalonia.Views;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The track header column of the arrangement: one row per timeline row with the track-row cells in the default order and widths
/// (<see cref="TrackRowCells"/>): cogwheel, the owner's instrument icon, record arm, number, name, split FX button, M, S, volume and pan sliders,
/// and the meter column (drawn by Views/Meters/TrackMeterColumn), under a header strip with the column labels. Rows sit exactly where the <see cref="TimelineView"/> puts them (its Core row geometry), scroll
/// with it and follow its row-drag offsets, so the two stay aligned while a row drags. It only draws and answers hit tests; <see cref="TimelineInputController"/>
/// handles the pointer and shows a slider drag through <see cref="SliderPreview"/> before the edit lands.
/// Owns: the column's drawing, the cell hit test (track rows and the master row's cells, which report <see cref="MasterRow"/>), the cell tooltips,
/// the hover and pressed states of the cells, the slider preview value and the input levels the armed sub-rows' meters show.
/// Does not own: the row geometry (TimelineView), the cell geometry and art (TrackRowCells, MasterLaneRowPainter), the edits, the pointer gestures, the
/// row-height grip, group headers and the "Add track" lane of the column.
/// Tests: TabForge.Avalonia.Tests.ArrangementViewTests, TabForge.Avalonia.Tests.TrackRowControlsTests, TrackRowSelectionTests, MasterLaneRowTests.
/// </summary>
public sealed class TrackHeaderColumn : Control
{
    public double ColumnWidth => TrackRowCells.TotalWidthFor(NameColumnWidth);

    /// <summary>The name column width the list is laid out with (the timeline holds it, the splitter sets it).</summary>
    public double NameColumnWidth => _timeline.TrackListNameWidth;
    private const double IconSize = 22;
    private const string Font = "Segoe UI";
    private const int SelectedNameWeight = 700, NameWeight = 400;

    /// <summary>The row index the master row's cells report in hit tests and requests: no track row.</summary>
    public const int MasterRow = -1;

    /// <summary>The master row's cells that take the pointer, in hit-test order (its mute, solo, cog and arm cells are not drawn).</summary>
    private static readonly TrackRowCells.Cell[] MasterCells = { TrackRowCells.Cell.Fx, TrackRowCells.Cell.FxPower, TrackRowCells.Cell.Volume, TrackRowCells.Cell.Pan };

    /// <summary>Every cell in hit-test order, taken from the enum once so a pointer move allocates no array.</summary>
    internal static readonly TrackRowCells.Cell[] HitTestCells = Enum.GetValues<TrackRowCells.Cell>();

    /// <summary>Whether the track name of <paramref name="row"/> is drawn bold: every selected track's name is, the active track and each multi-selected row (the same set that tints the rows).</summary>
    internal bool IsNameBold(int row) => row == _timeline.SelectedRow || _timeline.IsRowPicked(row);

    /// <summary>The tooltip text of a cell of <paramref name="row"/>, in the wording (the master row's own texts).</summary>
    public static string TooltipOf(int row, TrackRowCells.Cell cell) => row == MasterRow ? MasterLaneRowPainter.TooltipOf(cell) : cell switch
    {
        TrackRowCells.Cell.Settings => "Track properties (instrument, tuning, mixer, details)",
        TrackRowCells.Cell.Arm => "Arm the track for recording",
        TrackRowCells.Cell.Fx => "Open the FX chain",
        TrackRowCells.Cell.FxPower => "Switch between the FX chain and Windows MIDI",
        TrackRowCells.Cell.Mute => "Mute track",
        TrackRowCells.Cell.Solo => "Solo track",
        TrackRowCells.Cell.Volume => "Volume",
        TrackRowCells.Cell.Meter => Views.Meters.TrackMeterColumn.Tip(),
        TrackRowCells.Cell.Kind => "Change instrument",
        _ => "Pan",
    };

    /// <summary>The tooltip at the pointer: the cell's text, or null for an audio track's icon (its press keeps the row behaviour).</summary>
    private string? TipOf(int row, TrackRowCells.Cell cell) =>
        cell == TrackRowCells.Cell.Kind && row >= 0 && row < _song.Tracks.Count && _song.Tracks[row].IsAudio ? null : TooltipOf(row, cell);

    /// <summary>The range a slider of <paramref name="row"/> runs over: the master volume is 0-100 %, a track's volume 0-127.</summary>
    public static (int Minimum, int Maximum) SliderRangeOf(int row, TrackRowCells.Cell cell) =>
        row == MasterRow && cell == TrackRowCells.Cell.Volume ? (MasterLaneRowPainter.VolumeMinimum, MasterLaneRowPainter.VolumeMaximum) : TrackRowCells.SliderRange(cell);

    /// <summary>The value a double-click resets a slider of <paramref name="row"/> to: the master volume its own default, a track's the cell's.</summary>
    public static int SliderDefaultOf(int row, TrackRowCells.Cell cell) =>
        row == MasterRow && cell == TrackRowCells.Cell.Volume ? MixerStripPresenter.MasterVolumeDefault : TrackRowCells.SliderDefault(cell);

    /// <summary>The top of the grid, where the master lane starts under the ruler and the section strip.</summary>
    private double GridTop => TimelineView.RulerHeight + TimelineView.SectionHeight;

    private readonly TimelineView _timeline;
    private readonly AvaloniaDrawContext _draw = new();
    private SongProject _song;
    private (int Row, TrackRowCells.Cell Cell, int Value)? _sliderPreview;

    public TrackHeaderColumn(TimelineView timeline, SongProject song)
    {
        _timeline = timeline;
        _song = song;
        ContextMenuBuilder.Attach(this, timeline.ContextMenus, at => ContextMenuRows.TrackRowAt(_timeline, at));
        timeline.HoverRowChanged += InvalidateVisual;
        timeline.RowScrollChanged += InvalidateVisual;
        timeline.PickedRowsChanged += InvalidateVisual;
        // The header measures to the timeline's Height (see MeasureOverride), so it measures again whenever that Height changes: a pane resize,
        // a row reload or a new row height. Without this the column keeps the height of an older, smaller or larger pane.
        timeline.PropertyChanged += (_, e) => { if (e.Property == HeightProperty) InvalidateMeasure(); };
        PointerWheelChanged += (_, e) => _timeline.ScrollRowsByWheel(e);
        PointerMoved += (_, e) =>
        {
            var tip = CellAt(e.GetPosition(this)) is { } hit ? TipOf(hit.Row, hit.Cell) : null;
            if (!Equals(ToolTip.GetTip(this), tip)) ToolTip.SetTip(this, tip);
            _timeline.HoverRowAtY(e.GetPosition(this).Y);
            UpdateCellHover(e.GetPosition(this));
        };
        PointerExited += (_, _) =>
        {
            _timeline.HoverRowAtY(-1);
            SetHoverCell(null);
            SetPressedCell(null);
        };
        PointerPressed += (_, e) =>
        {
            var point = e.GetPosition(this);
            var properties = e.GetCurrentPoint(this).Properties;
            if (CellAt(point) is { Cell: TrackRowCells.Cell.Meter } meter)
            {
                if (properties.IsRightButtonPressed)
                {
                    e.Handled = true;
                    MeterMenuRequested?.Invoke();
                    return;
                }
                if (properties.IsLeftButtonPressed && meter.Row >= 0) MeterResetRequested?.Invoke(meter.Row);
            }
            if (!properties.IsLeftButtonPressed) return;
            UpdateCellHover(point);   // the cell under this press, even when the rows reloaded since the last move
            SetPressedCell(HoverCell);
        };
        PointerReleased += (_, _) => SetPressedCell(null);
        PointerCaptureLost += (_, _) => SetPressedCell(null);   // a dialog the press opened takes the release
        DoubleTapped += (_, e) =>
        {
            if (CellAt(e.GetPosition(this)) is { Cell: TrackRowCells.Cell.Meter } meter && meter.Row >= 0) MeterMixerRequested?.Invoke(meter.Row);
        };
    }

    /// <summary>The track row and cell under the pointer, or null when the pointer is on no drawn cell.</summary>
    public (int Row, TrackRowCells.Cell Cell)? HoverCell { get; private set; }

    /// <summary>Runs on a right-click on a track's meter cell; the arrangement opens the meter menu.</summary>
    public Action? MeterMenuRequested { get; set; }

    /// <summary>Runs with the row on a left-click on its meter cell; the arrangement resets that row's clip light.</summary>
    public Action<int>? MeterResetRequested { get; set; }

    /// <summary>Runs with the row on a double-click on its meter cell; the arrangement opens the mixer.</summary>
    public Action<int>? MeterMixerRequested { get; set; }

    /// <summary>Raised when the hovered or pressed cell changes, so the meter column can repaint just its own cell.</summary>
    public event Action? CellPaintChanged;

    /// <summary>The cell a left press is holding, drawn darker until the release; null when none.</summary>
    public (int Row, TrackRowCells.Cell Cell)? PressedCell => _pressedCell;
    private (int Row, TrackRowCells.Cell Cell)? _pressedCell;

    /// <summary>Tracks the drawn cell under <paramref name="p"/>; invalidates the column only when the cell changes.</summary>
    public void UpdateCellHover(Point p) => SetHoverCell(CellAt(p));

    private void SetHoverCell((int Row, TrackRowCells.Cell Cell)? cell)
    {
        if (cell == HoverCell) return;
        var repaint = PaintsCellState(HoverCell) || PaintsCellState(cell);
        HoverCell = cell;
        if (repaint) InvalidateVisual();
        CellPaintChanged?.Invoke();
    }

    private void SetPressedCell((int Row, TrackRowCells.Cell Cell)? cell)
    {
        if (cell == _pressedCell) return;
        var repaint = PaintsCellState(_pressedCell) || PaintsCellState(cell);
        _pressedCell = cell;
        if (repaint) InvalidateVisual();
        CellPaintChanged?.Invoke();
    }

    // The meter cell is drawn by TrackMeterColumn over this column, so a meter-only state change repaints that control instead.
    private static bool PaintsCellState((int Row, TrackRowCells.Cell Cell)? cell) => cell is { } hit && hit.Cell != TrackRowCells.Cell.Meter;

    /// <summary>The state a cell is drawn in: pressed while its press is held, hovered under the pointer, else idle.</summary>
    internal TrackRowCells.CellPaint PaintOf(int row, TrackRowCells.Cell cell) =>
        _pressedCell == (row, cell) ? TrackRowCells.CellPaint.Pressed : HoverCell == (row, cell) ? TrackRowCells.CellPaint.Hover : TrackRowCells.CellPaint.Idle;

    /// <summary>Which half of the split FX button the pointer is on in this row: 0 the FX label, 1 the power symbol, -1 neither.</summary>
    private int FxPartOf(int row) => HoverCell is { } hover && hover.Row == row
        ? hover.Cell switch { TrackRowCells.Cell.Fx => 0, TrackRowCells.Cell.FxPower => 1, _ => -1 }
        : -1;

    /// <summary>The cell's name as a lower-case id, the form the tests and the automation use ("mute" for the M box).</summary>
    public static string CellId(TrackRowCells.Cell cell) => cell.ToString().ToLowerInvariant();


    /// <summary>The song the rows are drawn from now.</summary>
    internal SongProject Song => _song;

    /// <summary>Shows the tracks of <paramref name="song"/> after an edit changed them.</summary>
    public void Reload(SongProject song)
    {
        SetPressedCell(null);   // a press whose release never arrived (a dialog opened over it) must not keep the cell dark
        SetHoverCell(null);     // rows may have changed under the pointer: the next move finds the cell again
        _song = song;
        foreach (var gone in _inputLevels.Keys.Where(track => !song.Tracks.Contains(track)).ToList()) { _inputLevels.Remove(gone); _inputHolds.Remove(gone); }
        InvalidateMeasure();
        InvalidateVisual();
    }

    // The input peak (0..1) of each armed track, as the armed sub-row's meter shows it. Key: the song's track; ceiling: the song's tracks (pruned on
    // Reload); thread: UI only.
    private readonly Dictionary<TrackModel, double> _inputLevels = new();
    // The peak-hold tick of a MIDI-armed track's meter; same key, ceiling and thread as the levels (absent: no tick).
    private readonly Dictionary<TrackModel, double> _inputHolds = new();

    /// <summary>The input peak of a track (0..1) the sub-row's meter shows; 0 when none was shown.</summary>
    public double InputLevelOf(TrackModel track) => _inputLevels.TryGetValue(track, out var level) ? level : 0;

    /// <summary>The peak-hold tick (0..1) the sub-row's meter shows; 0 when none.</summary>
    public double InputHoldOf(TrackModel track) => _inputHolds.TryGetValue(track, out var hold) ? hold : 0;

    /// <summary>Shows the input peak of an armed track in its sub-row's meter, with the peak-hold tick of a MIDI input; repaints the column only when either changed.</summary>
    public void ShowInputLevel(TrackModel track, double level, double hold = 0)
    {
        if (InputLevelOf(track) == level && InputHoldOf(track) == hold) return;
        _inputLevels[track] = level;
        _inputHolds[track] = hold;
        InvalidateVisual();
    }

    /// <summary>A slider value shown in place of the track's while a drag is in progress, or null.</summary>
    public (int Row, TrackRowCells.Cell Cell, int Value)? SliderPreview
    {
        get => _sliderPreview;
        set { _sliderPreview = value; InvalidateVisual(); }
    }

    /// <summary>The rows that follow the dragged slider by the same change (the selected tracks), or null when only the dragged row moves.</summary>
    internal IReadOnlyList<int>? SliderGroup { get; set; }

    /// <summary>The master row's volume in percent, read at each paint: the arrangement gives the transport's master knob here. Null shows 0.</summary>
    public Func<int>? MasterVolumePercent { get; set; }

    /// <summary>The row whose header holds <paramref name="y"/>, or -1.</summary>
    public int RowAtY(double y) => _timeline.RowAtY(y);

    /// <summary>The control cell under <paramref name="p"/> and its row (<see cref="MasterRow"/> for the master row's cells), or null.</summary>
    public (int Row, TrackRowCells.Cell Cell)? CellAt(Point p)
    {
        if (IsInMasterLane(p))
        {
            foreach (var masterCell in MasterCells)
                if (CellRect(MasterRow, masterCell).Contains(p)) return (MasterRow, masterCell);
            return null;
        }
        var row = _timeline.RowAtY(p.Y);
        if (row < 0 || row >= _song.Tracks.Count) return null;
        var audio = _song.Tracks[row].IsAudio;
        foreach (var cell in HitTestCells)
        {
            // An audio track has no instrument: its icon keeps the row press (select/drag), as before the icon cell.
            if (audio && cell == TrackRowCells.Cell.Kind) continue;
            if (CellRect(row, cell).Contains(p)) return (row, cell);
        }
        return null;
    }

    private bool IsInMasterLane(Point p) => _timeline.MasterLaneHeight > 0 && p.Y >= GridTop && p.Y < GridTop + _timeline.MasterLaneHeight;

    /// <summary>The cell's box in column coordinates (not offset by a row drag); the master row's cells sit in the master lane's height.</summary>
    public Rect CellRect(int row, TrackRowCells.Cell cell) => row == MasterRow
        ? TrackRowCells.Box(cell, GridTop, _timeline.MasterLaneHeight, NameColumnWidth)
        : TrackRowCells.Box(cell, _timeline.RowTop(row), _timeline.HeaderLineHeightOf(row), NameColumnWidth);

    /// <summary>The row whose name cell holds <paramref name="p"/> (header coordinates), or -1.</summary>
    public int NameRowAt(Point p)
    {
        var (left, width) = TrackRowCells.Column("name", NameColumnWidth);
        if (p.X < left || p.X > left + width) return -1;
        var row = RowAtY(p.Y);
        if (row < 0 || row >= _song.Tracks.Count) return -1;
        return row;
    }

    protected override Size MeasureOverride(Size availableSize) => new(ColumnWidth, _timeline.Height);

    public override void Render(DrawingContext context)
    {
        var light = ActualThemeVariant == ThemeVariant.Light;
        PaintCells(_draw.Use(context), key => AvaloniaPalette.Get(key, light), light);
    }

    /// <summary>Draws the whole column: the header strip, the master cell, the track rows (clipped under the master lane and above the pinned strip) and the strip.</summary>
    internal void PaintCells(IDrawContext d, Func<string, UiColor> ink, bool light)
    {
        var edge = new UiStroke(ink("TimelineLine"), 1);
        var gridTop = GridTop;
        d.Rect(ink("TimelineRow"), null, 0, 0, ColumnWidth, Bounds.Height);
        d.Line(edge, 0, TimelineView.RulerHeight - 0.5, ColumnWidth, TimelineView.RulerHeight - 0.5);
        TrackRowCells.DrawHeader(d, ink, TimelineView.RulerHeight, TimelineView.SectionHeight, NameColumnWidth);
        d.Line(edge, 0, gridTop - 0.5, ColumnWidth, gridTop - 0.5);
        d.PushClip(0, gridTop, ColumnWidth, Math.Max(0, Bounds.Height - gridTop));
        DrawMasterCell(d, ink, edge, gridTop);
        d.PushClip(0, _timeline.TrackRowsTop, ColumnWidth, Math.Max(0, _timeline.TrackRowsBottom - _timeline.TrackRowsTop));
        for (var row = 0; row < _timeline.RowCount; row++) if (row != _timeline.DragRow && RowInBand(row)) DrawRow(d, ink, edge, row, light);
        if (_timeline.DragRow >= 0 && _timeline.DragRow < _timeline.RowCount) DrawRow(d, ink, edge, _timeline.DragRow, light);
        d.Pop();
        d.Pop();
        d.Line(edge, ColumnWidth - 0.5, 0, ColumnWidth - 0.5, Bounds.Height);
    }

    /// <summary>
    /// Whether a row's lane overlaps the band the track rows are drawn in. The arrow is the row's whole height (<see cref="TimelineView.RowHeightOf"/>),
    /// so an armed track whose base band is just above the band still draws its sub-row, as it did before the cull.
    /// </summary>
    private bool RowInBand(int row)
    {
        var top = _timeline.RowTop(row) + (_timeline.RowOffsets is { } offsets ? offsets[row] : 0);
        return top + _timeline.RowHeightOf(row) > _timeline.TrackRowsTop && top < _timeline.TrackRowsBottom;
    }

    private void DrawMasterCell(IDrawContext d, Func<string, UiColor> ink, UiStroke edge, double top)
    {
        var height = _timeline.MasterLaneHeight;
        if (height <= 0) return;
        d.Rect(ink("TimelineRow"), null, 0, top, ColumnWidth, height);
        var volume = ShownSlider(MasterRow, TrackRowCells.Cell.Volume, MasterVolumePercent?.Invoke() ?? 0);
        var pan = ShownSlider(MasterRow, TrackRowCells.Cell.Pan, _song.Mixer.MasterPan);
        var shown = new MasterLaneRowPainter.Shown(volume, pan, FxPartOf(MasterRow), cell => PaintOf(MasterRow, cell));
        MasterLaneRowPainter.Draw(d, ink, _song, top, height, NameColumnWidth, shown);
        d.Line(edge, 0, top + height - 0.5, ColumnWidth, top + height - 0.5);
    }

    /// <summary>The value a slider shows: the drag's preview while one runs on this slider (the same change on the group's others), else <paramref name="value"/>.</summary>
    private int ShownSlider(int row, TrackRowCells.Cell cell, int value)
    {
        if (_sliderPreview is not { } preview || preview.Cell != cell) return value;
        if (preview.Row == row) return preview.Value;
        if (SliderGroup is not { } group || !group.Contains(row) || preview.Row < 0 || preview.Row >= _song.Tracks.Count) return value;
        var (minimum, maximum) = TrackRowCells.SliderRange(cell);
        return Math.Clamp(value + preview.Value - TrackRowCells.SliderValue(_song.Tracks[preview.Row], cell), minimum, maximum);
    }

    private void DrawRow(IDrawContext d, Func<string, UiColor> ink, UiStroke edge, int row, bool light)
    {
        var track = _song.Tracks[row];
        var dy = _timeline.RowOffsets is { } offsets ? offsets[row] : 0;
        var top = _timeline.RowTop(row) + dy;
        var dimmed = track.Mute && !track.Solo;
        var selected = row == _timeline.SelectedRow;
        var height = _timeline.RowHeightOf(row);
        d.Rect(ink(row % 2 == 0 ? "TimelineRow" : "TimelineRowAlt"), null, 0, top, ColumnWidth, height);
        TrackRowTint.Fill(d, track, light, _timeline.TrackTintPercent, 0, top, ColumnWidth, height);
        TrackRowTint.Stripe(d, track, light, 0, top, height);
        if (row == _timeline.HoverRow || (selected && _timeline.ShadeSelectedTrack) || _timeline.IsRowPicked(row)) d.Rect(HoverShade.RowOverlay(light), null, 0, top, ColumnWidth, height);
        if (row == _timeline.SelectedRow || _timeline.IsRowPicked(row)) TrackRowTint.DrawSelectedStrip(d, track, light, 0, top, height);
        if (_timeline.IsArmed(row)) ArmedRowPainter.Draw(d, ink, track, top + _timeline.HeaderLineHeightOf(row), InputLevelOf(track), InputHoldOf(track));
        var text = ink("TextPrimary");
        if (dimmed) text = text with { A = TrackRowTint.MutedAlpha(_timeline.MutedTrackDimPercent) };
        Rect Box(TrackRowCells.Cell cell) => CellRect(row, cell).Translate(new Vector(0, dy));
        TrackRowCells.DrawCog(d, ink, Box(TrackRowCells.Cell.Settings), PaintOf(row, TrackRowCells.Cell.Settings));
        var kindBox = Box(TrackRowCells.Cell.Kind);
        TrackRowCells.DrawKind(d, ink, kindBox, PaintOf(row, TrackRowCells.Cell.Kind));
        var (kindX, kindWidth) = TrackRowCells.Column("kind", NameColumnWidth);
        var key = track.IsAudio ? TrackIconKeys.AudioKey : TrackIconKeys.KeyFor(track.MidiProgram, track.MidiChannel == 9);
        var iconSize = Math.Min(IconSize * IconSizing.Scale, Math.Min(kindWidth, _timeline.HeaderLineHeightOf(row)));   // Icon size (Appearance) scales the track icon
        TrackIconArt.Draw(d, key, kindX + (kindWidth - iconSize) / 2, top + (_timeline.HeaderLineHeightOf(row) - iconSize) / 2, iconSize, text,
            dimmed ? ink("TrackSilhouetteAccent") with { A = TrackRowTint.MutedAlpha(_timeline.MutedTrackDimPercent) } : ink("TrackSilhouetteAccent"));
        TrackRowCells.DrawArm(d, ink, Box(TrackRowCells.Cell.Arm), track.RecordArm, PaintOf(row, TrackRowCells.Cell.Arm));
        var (numberX, numberWidth) = TrackRowCells.Column("number", NameColumnWidth);
        var number = d.Text((row + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), Font, 11, ink("TextMuted"), d.PixelsPerDip);
        d.DrawText(number, numberX + (numberWidth - number.Width) / 2, top + (_timeline.HeaderLineHeightOf(row) - number.Height) / 2);
        var (nameX, nameWidth) = TrackRowCells.Column("name", NameColumnWidth);
        var nameRight = nameX + nameWidth - 4;
        var name = d.Text(track.Name, Font, 13, text, d.PixelsPerDip, weight: IsNameBold(row) ? SelectedNameWeight : NameWeight);
        // Only a name wider than its cell is clipped: a clip pushed around every name left the later cells drawn at the wrong scale in a render at a scale other than 1 (a capture, a video frame).
        var clipName = name.Width + 1 > nameRight - (nameX + 4);
        if (clipName) d.PushClip(nameX + 4, top, Math.Max(0, nameRight - (nameX + 4)), _timeline.HeaderLineHeightOf(row));
        d.DrawText(name, nameX + 4, top + (_timeline.HeaderLineHeightOf(row) - name.Height) / 2);
        if (clipName) d.Pop();
        TrackRowCells.DrawFx(d, ink, Box(TrackRowCells.Cell.Fx), Box(TrackRowCells.Cell.FxPower), track.SoundSource == SoundSources.Plugins, track.Rig.Plugins.Count, FxPartOf(row));
        DrawToggle(d, ink, Box(TrackRowCells.Cell.Mute), TrackRowCells.Cell.Mute, track.Mute, PaintOf(row, TrackRowCells.Cell.Mute));
        DrawToggle(d, ink, Box(TrackRowCells.Cell.Solo), TrackRowCells.Cell.Solo, track.Solo, PaintOf(row, TrackRowCells.Cell.Solo));
        // The two sliders are drawn one by one (no array per row, so a paint allocates nothing).
        foreach (var slider in (ReadOnlySpan<TrackRowCells.Cell>)[TrackRowCells.Cell.Volume, TrackRowCells.Cell.Pan])
        {
            var value = ShownSlider(row, slider, TrackRowCells.SliderValue(track, slider));
            var (minimum, maximum) = TrackRowCells.SliderRange(slider);
            TrackRowCells.DrawSlider(d, ink, Box(slider), minimum, maximum, value, PaintOf(row, slider));
        }
        if (_timeline.ShowTrackLines) d.Line(edge, 0, top + height - 0.5, ColumnWidth, top + height - 0.5);
    }

    /// <summary>The boxes use the colours: idle as the row, red when muted, amber when soloed; the muted letter is the higher-contrast ink on the red.</summary>
    internal static (UiColor Fill, UiColor Edge, UiColor Letter) ToggleColours(Func<string, UiColor> ink, TrackRowCells.Cell kind, bool on)
    {
        var mute = kind == TrackRowCells.Cell.Mute;
        if (!on) return (ink("TrackToggleFill"), ink("TrackToggleEdge"), ink("TextPrimary"));
        var fill = ink(mute ? "TrackMuted" : "TrackSolo");
        return (fill, ink(mute ? "TrackMutedEdge" : "TrackSoloActiveBorder"), mute ? ThemeColours.InkOn(fill) : ink("TrackSoloText"));
    }

    internal static void DrawToggle(IDrawContext d, Func<string, UiColor> ink, Rect box, TrackRowCells.Cell kind, bool on, TrackRowCells.CellPaint paint)
    {
        var (fill, edge, letterInk) = ToggleColours(ink, kind, on);
        d.Rect(fill, new UiStroke(edge, 1), box.X, box.Y, box.Width, box.Height, ChromeMetrics.TrackToggleCorner);
        TrackRowCells.DrawCellState(d, ink, box, fill, edge, paint);
        var glyph = d.Text(kind == TrackRowCells.Cell.Mute ? "M" : "S", Font, ChromeMetrics.TrackToggleFontSize, letterInk, d.PixelsPerDip, weight: ChromeMetrics.TrackToggleWeight);
        d.DrawText(glyph, box.X + (box.Width - glyph.Width) / 2, box.Y + (box.Height - glyph.Height) / 2);
    }
}
