using System.Linq;
using System.Windows;
using System.Windows.Input;
using TabForge.Models;

namespace TabForge.Views.Score;

/// <summary>What the mouse input of the score reads from the editor that owns it, and the notifications it raises through it.</summary>
internal interface IEditorInputHost : IScorePageHost
{
    bool PlaybackActive { get; }
    double HeaderHeight { get; }
    double SystemHeight { get; }
    int SelectedCell { get; }
    int SelectedString { get; }
    int ActiveVoiceIndex { get; }
    List<TabCell> CellsFor(MeasureModel measure, bool create);
    bool IsInSelection(int measure, int cell);
    void BeginSelection();
    void ClearSelection(bool notify);

    /// <summary>Moves the cursor without notifying.</summary>
    void SetCursor(int measure, int cell, int stringIndex);

    /// <summary>Raises the selection-changed notification (and repaints); <paramref name="seekPlayback"/> says whether playback may follow.</summary>
    void SelectionChangedNow(bool seekPlayback);

    void RaiseContextMenuRequested(ContextMenuEventArgs args);
    bool Focus();
}

// Owns: the score editor's mouse input: hit testing, click, shift-click and drag selection, hover and context-menu requests.
// Does not own: the selection state (SelectionModel) and the editor drawing.
// Tests: TestTabEditorInputScript, TestSelectionModel.
/// <summary>
/// Mouse input of the score editor: hit testing, click, shift-click and drag selection, hover, and the context-menu requests.
/// Selection state lives in <see cref="EditorSelectionState"/>; every change reaches the owner through <see cref="IEditorInputHost"/>
/// so the shared selection model stays the one source of truth.
/// </summary>
internal sealed class EditorInputController
{
    /// <summary>A score range needs the button held this long; a click made mid-movement stays a click.</summary>
    private const int RangeSelectHoldMs = 140;

    private readonly IEditorInputHost _host;
    private readonly EditorSelectionState _sel;
    private Point _leftMouseDownPoint;
    private long _leftMouseDownTicks;
    private bool _leftMouseDownPending;

    public EditorInputController(IEditorInputHost host, EditorSelectionState sel)
    {
        _host = host;
        _sel = sel;
        sel.CellsOf = measure => host.Track is { } track && measure >= 0 && measure < track.Measures.Count ? host.CellsFor(track.Measures[measure], create: false) : null;
    }

    private Point ToPagePoint(Point point) => new(point.X / _host.Zoom, point.Y / _host.Zoom);

    /// <summary>The beat and string under a page point.</summary>
    public (int measure, int cell, int stringIndex) HitTest(Point p)
    {
        var track = _host.Track;
        if (track is null) return (0, 0, 0);
        var system = Math.Max(0, (int)((p.Y - _host.HeaderHeight) / _host.SystemHeight));
        var layout = _host.Layout.GetLayout(track);
        system = Math.Clamp(system, 0, layout.SystemCount - 1);
        var row = layout.Systems[system];
        var measurePosition = row.Measures.FirstOrDefault(item => p.X >= item.X && p.X < item.X + item.Width);
        if (measurePosition.Width <= 0)
            measurePosition = p.X < row.Measures[0].X ? row.Measures[0] : row.Measures[^1]; // the clef / key / time signature area before the first beat belongs to the first bar
        var measure = measurePosition.MeasureIndex;
        var localX = Math.Clamp(p.X - measurePosition.X, 0, Math.Max(0, measurePosition.Width - 0.001));
        var hitSlot = _host.Layout.WarpFor(track, measure).SlotAt(localX / Math.Max(1e-6, measurePosition.Width));
        var gridCell = Math.Clamp((int)hitSlot, 0, _host.SlotsFor(measure) - 1);
        var measureModel = track.Measures[measure];
        var cell = TabEditorControl.ResolveBeatHitCell(measureModel, hitSlot, gridCell,
            _host.CellsFor(measureModel, create: _host.ActiveVoiceIndex == 1));
        var stringIndex = Math.Clamp((int)Math.Round((p.Y - _host.TabTop(system)) / _host.StringGap), 0, Math.Max(0, track.StringTunings.Count - 1));
        return (measure, cell, stringIndex);
    }

    public void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _leftMouseDownPending = false;
        _host.Focus();
        if (_host.Track is null) return;
        var pointer = e.GetPosition((IInputElement)sender);
        var p = ToPagePoint(pointer);
        if (p.Y < _host.HeaderHeight) { _host.ClearSelection(true); return; }
        var (measure, cell, stringIndex) = HitTest(p);
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && ShiftClickExtend(measure, cell, stringIndex)) return;
        _host.ClearSelection(false);
        _host.SetCursor(measure, cell, stringIndex);
        _leftMouseDownPoint = pointer;
        _leftMouseDownTicks = Environment.TickCount64;
        _leftMouseDownPending = true;
        _host.SelectionChangedNow(true);
    }

    /// <summary>
    /// Shift+click: extends the selection from the cursor (or the existing anchor) to the clicked beat, like Shift+arrows.
    /// Returns false when there is no cursor to extend from (the click then behaves as a plain click).
    /// </summary>
    public bool ShiftClickExtend(int measure, int cell, int stringIndex)
    {
        var track = _host.Track;
        if (track is null || measure < 0 || measure >= track.Measures.Count) return false;
        if (!_sel.Selecting) _host.BeginSelection();
        _sel.SetEnd(measure, cell);
        _host.SetCursor(measure, cell, stringIndex);
        _host.SelectionChangedNow(true);
        return true;
    }

    public void OnRightDown(object sender, MouseButtonEventArgs e)
    {
        _host.Focus();
        var track = _host.Track;
        if (track is null) return;
        var p = ToPagePoint(e.GetPosition((IInputElement)sender));
        // Right-click only opens a menu: it must not move the cursor or seek running playback.
        var (measure, cell, stringIndex) = HitTest(p);
        var onNote = false;
        var overBeat = p.Y >= _host.HeaderHeight && measure >= 0 && measure < track.Measures.Count;
        if (overBeat)
        {
            var cells = _host.CellsFor(track.Measures[measure], create: false);
            onNote = cell >= 0 && cell < cells.Count && cells[cell].Notes.Any(n => n.StringIndex == stringIndex);
        }
        _host.RaiseContextMenuRequested(new ContextMenuEventArgs(new Point(p.X, p.Y))
        {
            Measure = measure, Cell = cell, StringIndex = stringIndex, OnNote = onNote, OverBeat = overBeat,
            InsideSelection = overBeat && _host.IsInSelection(measure, cell)
        });
        e.Handled = true;
    }

    /// <summary>
    /// Shift+F10 / the Menu key: the same menu as a right-click, for the caret: OnNote and InsideSelection come from the caret and
    /// selection like the mouse path, and the menu opens at the bottom-left of the caret cell.
    /// </summary>
    public bool RequestContextMenuAtCaret()
    {
        var track = _host.Track;
        if (track is null || track.Measures.Count == 0) return false;
        var measure = Math.Clamp(_host.SelectedMeasure, 0, track.Measures.Count - 1);
        var layout = _host.Layout.GetLayout(track);
        var system = -1;
        var placement = default((int MeasureIndex, double X, double Width));
        for (var s = 0; s < layout.SystemCount && system < 0; s++)
            foreach (var item in layout.Systems[s].Measures)
                if (item.MeasureIndex == measure) { system = s; placement = (item.MeasureIndex, item.X, item.Width); break; }
        if (system < 0) return false;
        var cells = _host.CellsFor(track.Measures[measure], create: false);
        var cell = Math.Clamp(_host.SelectedCell, 0, Math.Max(0, _host.SlotsFor(measure) - 1));
        var x = placement.X + _host.Layout.WarpFor(track, measure).Fraction(ScoreRenderer.CellStartSlots(track.Measures[measure], cell, cells)) * placement.Width;
        var stringIndex = Math.Clamp(_host.SelectedString, 0, Math.Max(0, track.StringTunings.Count - 1));
        var y = _host.TabTop(system) + stringIndex * _host.StringGap + _host.StringGap / 2;
        var onNote = cell < cells.Count && cells[cell].Notes.Any(n => n.StringIndex == stringIndex);
        var zoom = _host.Zoom;
        _host.RaiseContextMenuRequested(new ContextMenuEventArgs(new Point(x, y))
        {
            Measure = measure, Cell = cell, StringIndex = stringIndex, OnNote = onNote, OverBeat = true,
            InsideSelection = _host.IsInSelection(measure, cell), FromKeyboard = true, Anchor = new Point(x * zoom, y * zoom)
        });
        return true;
    }

    public void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_host.Track is null) return;
        var p = ToPagePoint(e.GetPosition((IInputElement)sender));
        if (e.LeftButton != MouseButtonState.Pressed || !_leftMouseDownPending)
        {
            // Hover highlight only (no editing, no position change).
            if (p.Y < _host.HeaderHeight)
            {
                if (_sel.ClearHover()) RepaintHover();
                return;
            }
            var (hm, hc, _) = HitTest(p);
            if (hm != _sel.HoverMeasure || hc != _sel.HoverCell) { _sel.HoverMeasure = hm; _sel.HoverCell = hc; RepaintHover(); }
            return;
        }
        var pointer = e.GetPosition((IInputElement)sender);
        var horizontalDrag = Math.Abs(pointer.X - _leftMouseDownPoint.X);
        // A normal click can move a few pixels while the button is down. Do not turn that
        // pointer jitter into a score range; time-range selection requires an intentional drag.
        if (horizontalDrag < Math.Max(10, SystemParameters.MinimumHorizontalDragDistance * 2)) return;
        if (!_sel.Selecting && Environment.TickCount64 - _leftMouseDownTicks < RangeSelectHoldMs) return;
        if (!_sel.Selecting) _host.BeginSelection();
        if (p.Y < _host.HeaderHeight) return;
        var (measure, cell, _) = HitTest(p);
        // Pointer moves inside the same cell change nothing: no repaint, no status/fretboard refresh.
        if (measure == _sel.EndMeasure && cell == _sel.EndCell) return;
        _sel.SetEnd(measure, cell);
        _host.SelectionChangedNow(false);
    }

    /// <summary>True while the user drags a score range (the window defers side-panel refreshes).</summary>
    public bool IsDragSelecting => _sel.Selecting && _leftMouseDownPending && Mouse.LeftButton == MouseButtonState.Pressed;

    public void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) _leftMouseDownPending = false;
        if (_sel.Selecting) _host.RepaintAll();
    }

    public void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_sel.ClearHover()) RepaintHover();
    }

    /// <summary>The hover highlight is not drawn while a song plays, so a hover change then repaints nothing (the score is not redrawn under the playhead).</summary>
    private void RepaintHover()
    {
        if (!_host.PlaybackActive) _host.RepaintAll();
    }
}
