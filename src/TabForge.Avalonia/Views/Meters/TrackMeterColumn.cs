using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Audio.Metering;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;

namespace TabForge.Avalonia.Views.Meters;

// Owns: the per-track stereo meters in the track rows: one control over the meter column, the models of the visible rows, their tick and their drawing.
//   A row's meter reads the same MasterMeterModel as the master meter and paints through MeterBarPainter; only this control repaints on a tick.
// Does not own: the numbers (MasterMeterModel, fed by AudioEngineClient.ReadTrackMeter), the settings (TrackMeterSettings) or the row geometry (TimelineView).
// Tests: TabForge.Avalonia.Tests.TrackMeterTests, TrackMeterFeedTests, TrackMeterAppWindowTests.
// Thread: UI thread. Rows outside the visible band are neither fed nor drawn; a tick invalidates this control only.
public sealed class TrackMeterColumn : Control
{
    private readonly TimelineView _timeline;
    private readonly AvaloniaDrawContext _draw = new();
    private readonly Dictionary<TrackModel, MasterMeterModel> _models = new();
    private readonly TrackMeterSettings _fallback = new();
    private readonly SharedMeterTick? _tick;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private SongProject _song;
    private Func<TrackMeterSettings> _settings;
    private Action? _onTick;
    private bool _subscribed;
    private (string Mode, bool ShowNumber) _tipFor;
    private int _hoverMeterRow = -1;
    private int _pressedMeterRow = -1;

    public TrackMeterColumn(TimelineView timeline, SongProject song, SharedMeterTick? tick = null)
    {
        _timeline = timeline;
        _song = song;
        _tick = tick;
        _settings = () => _fallback;
        Width = ChromeMetrics.TrackMeterColumnWidth;
        HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left;
        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top;
        IsHitTestVisible = false;
        SyncLayout();
        AttachedToVisualTree += (_, _) => Subscribe(true);
        DetachedFromVisualTree += (_, _) => Subscribe(false);
        ShowTip();
    }

    /// <summary>The settings every track meter reads (one setting drives them all).</summary>
    public Func<TrackMeterSettings> Settings
    {
        get => _settings;
        set { _settings = value; ShowTip(); InvalidateVisual(); }
    }

    /// <summary>The engine reading of one track at the chosen tap; null: no engine.</summary>
    public Func<TrackModel, MasterMeterReading?>? Source { get; set; }

    /// <summary>Runs after the menu changed a choice: the window saves and refreshes.</summary>
    public Action? Changed { get; set; }

    /// <summary>Opens the mixer (a double click on a row's meter); null: the window has no mixer command.</summary>
    public Action? OpenMixer { get; set; }

    /// <summary>The paint state of a row's meter cell: pressed while its press is held, hovered under the pointer, else idle.</summary>
    public TrackRowCells.CellPaint PaintOf(int row) =>
        _pressedMeterRow == row ? TrackRowCells.CellPaint.Pressed : _hoverMeterRow == row ? TrackRowCells.CellPaint.Hover : TrackRowCells.CellPaint.Idle;

    /// <summary>True while the row's meter shows its clip light.</summary>
    public bool ClipLitOf(int row) => row >= 0 && row < _song.Tracks.Count && _models.TryGetValue(_song.Tracks[row], out var model) && model.ClipLit;

    /// <summary>Takes the header's hovered and pressed cells; repaints only when the meter cell under the pointer changed.</summary>
    public void UpdatePointer((int Row, TrackRowCells.Cell Cell)? hover, (int Row, TrackRowCells.Cell Cell)? pressed)
    {
        var hoverRow = hover is { Cell: TrackRowCells.Cell.Meter } h ? h.Row : -1;
        var pressedRow = pressed is { Cell: TrackRowCells.Cell.Meter } p ? p.Row : -1;
        if (hoverRow == _hoverMeterRow && pressedRow == _pressedMeterRow) return;
        _hoverMeterRow = hoverRow;
        _pressedMeterRow = pressedRow;
        InvalidateVisual();
    }

    /// <summary>Resets the clip light of one row's meter only.</summary>
    public void ResetClip(int row)
    {
        if (row < 0 || row >= _song.Tracks.Count) return;
        if (_models.TryGetValue(_song.Tracks[row], out var model) && model.ResetClip()) InvalidateVisual();
    }

    /// <summary>True while the meter menu is open (the header's right-click opened it).</summary>
    public bool MenuOpen { get; private set; }

    /// <summary>Shows the meters of <paramref name="song"/>; models of tracks that left are dropped.</summary>
    public void Reload(SongProject song)
    {
        _song = song;
        foreach (var gone in new List<TrackModel>(_models.Keys))
            if (!song.Tracks.Contains(gone)) _models.Remove(gone);
        SyncLayout();
        InvalidateVisual();
    }

    /// <summary>Repaints after a menu or settings change.</summary>
    public void Refresh() { ShowTip(); InvalidateVisual(); }

    /// <summary>Opens the meter menu (the header forwards a right-click on the meter here).</summary>
    public void OpenMenu()
    {
        var settings = _settings();
        var frame = TransportPopups.MasterMeterPopup.CreateTrack(settings, () => { Refresh(); Changed?.Invoke(); }, () =>
        {
            foreach (var model in _models.Values) model.ResetClip();
            InvalidateVisual();
        });
        MenuOpen = true;
        frame.Closed += () => MenuOpen = false;
        frame.OpenAbove(this);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        SyncLayout();
        return new Size(ChromeMetrics.TrackMeterColumnWidth, _timeline.Height);
    }

    /// <summary>Places the control over the meter column of the current track list width.</summary>
    private void SyncLayout()
    {
        var left = TrackRowCells.Column("meter", _timeline.TrackListNameWidth).Left;
        this.WithMargin(left, 0, 0, 0);
    }

    private void OnScroll() { UpdatePointer(null, null); InvalidateVisual(); }
    // The timeline re-reads its song (undo, a reload, a new song in the same pane) before it raises this: follow that song, not the one shown before.
    private void OnRows() { Reload(_timeline.Song); }

    private void Subscribe(bool on)
    {
        if (on == _subscribed) return;
        _subscribed = on;
        if (on)
        {
            _timeline.RowScrollChanged += OnScroll;
            _timeline.RowsChanged += OnRows;
        }
        else
        {
            _timeline.RowScrollChanged -= OnScroll;
            _timeline.RowsChanged -= OnRows;
        }
        if (_tick is null) return;
        _onTick ??= Tick;
        if (on) _tick.Subscribe(_onTick); else _tick.Unsubscribe(_onTick);
    }

    /// <summary>One meter tick: feeds the visible rows' models and repaints this control only when something changed.</summary>
    public void Tick()
    {
        var settings = _settings();
        var changed = false;
        var now = _clock.Elapsed.TotalMilliseconds;
        for (var row = 0; row < _timeline.RowCount && row < _song.Tracks.Count; row++)
        {
            if (!RowInBand(row)) continue;
            var model = ModelOf(_song.Tracks[row]);
            var reading = Source?.Invoke(_song.Tracks[row]);
            changed |= model.Feed(reading, now);
        }
        if (_tipFor != (settings.Mode, settings.ShowNumber)) ShowTip();
        if (changed) InvalidateVisual();
    }

    private MasterMeterModel ModelOf(TrackModel track)
    {
        if (!_models.TryGetValue(track, out var model))
            _models[track] = model = new MasterMeterModel(() => _settings());
        return model;
    }

    private bool RowInBand(int row)
    {
        var top = _timeline.RowTop(row) + (_timeline.RowOffsets is { } offsets ? offsets[row] : 0);
        return top + _timeline.RowHeightOf(row) > _timeline.TrackRowsTop && top < _timeline.TrackRowsBottom;
    }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width < 1 || Bounds.Height < 1) return;
        var light = ActualThemeVariant == ThemeVariant.Light;
        var d = _draw.Use(context);
        Func<string, UiColor> ink = key => AvaloniaPalette.Get(key, light);
        d.PushClip(0, _timeline.TrackRowsTop, Bounds.Width, Math.Max(0, _timeline.TrackRowsBottom - _timeline.TrackRowsTop));
        for (var row = 0; row < _timeline.RowCount && row < _song.Tracks.Count; row++)
        {
            if (!RowInBand(row)) continue;
            var dy = _timeline.RowOffsets is { } offsets ? offsets[row] : 0;
            var box = TrackRowCells.Box(TrackRowCells.Cell.Meter, _timeline.RowTop(row) + dy, _timeline.HeaderLineHeightOf(row), _timeline.TrackListNameWidth);
            DrawMeter(d, ink, light, ModelOf(_song.Tracks[row]), new Rect(0, box.Y, ChromeMetrics.TrackMeterWidth, box.Height), row);
        }
        d.Pop();
    }

    private void DrawMeter(IDrawContext d, Func<string, UiColor> ink, bool light, MasterMeterModel model, Rect box, int row)
    {
        var settings = _settings();
        var inset = ChromeMetrics.TrackMeterInset;
        var barHeight = ChromeMetrics.TrackMeterBarHeight;
        var gap = ChromeMetrics.TrackMeterGap;
        var content = box.Width - inset * 2;
        var barsLength = settings.ShowNumber ? Math.Min(content, ChromeMetrics.TrackMeterBarsWidth) : content;
        var top = box.Y + (box.Height - (barHeight * 2 + gap)) / 2;
        var paint = PaintOf(row);
        if (paint != TrackRowCells.CellPaint.Idle)
            d.Rect(paint == TrackRowCells.CellPaint.Pressed ? MeterBarPainter.PressedFill(light) : MeterBarPainter.HoverFill(light), null,
                box.X, box.Y, box.Width, box.Height, ChromeMetrics.TrackMeterCorner);
        MeterBarPainter.DrawBarsHorizontal(d, ink, light, model, box.X + inset, top, barsLength, barHeight, gap, ChromeMetrics.TrackMeterCorner,
            ChromeMetrics.TrackMeterHoldHeight, ChromeMetrics.TrackMeterClipMark);
        if (settings.ShowNumber)
        {
            var text = d.Text(model.NumberText, ChromeMetrics.UiFontFamily, ChromeMetrics.TrackMeterNumberSize, ink("TextPrimary"), d.PixelsPerDip);
            var free = content - barsLength;
            d.DrawText(text, box.X + inset + barsLength + (free - text.Width) / 2, box.Center.Y - text.Height / 2);
        }
    }

    /// <summary>The tooltip of a track meter without the live settings (the header's cell tip).</summary>
    public static string Tip() => "Track meter: peak level in dBFS, left and right. Click to reset the clip, right-click for options.";

    private void ShowTip()
    {
        var settings = _settings();
        _tipFor = (settings.Mode, settings.ShowNumber);
        ToolTip.SetTip(this, "Track meter: peak level in dBFS, left and right." + (settings.ShowNumber ? " The number is the louder bar." : "")
            + " Click to reset the clip, right-click for options.");
    }
}
