using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using RenderDraw = TabForge.Visualization.Draw;

namespace TabForge.Views;

// TabEditorControl: mouse input and hit testing.
public sealed partial class TabEditorControl
{
    // ---------- mouse ----------

    private (int measure, int cell, int stringIndex) HitTest(Point p)
    {
        var track = Track;
        if (track is null) return (0, 0, 0);
        var system = Math.Max(0, (int)((p.Y - HeaderHeight) / SystemHeight));
        var layout = GetScoreLayout(track);
        system = Math.Clamp(system, 0, layout.SystemCount - 1);
        var row = layout.Systems[system];
        var measurePosition = row.Measures.FirstOrDefault(item => p.X >= item.X && p.X < item.X + item.Width);
        if (measurePosition.Width <= 0)
            measurePosition = p.X < row.X ? row.Measures[0] : row.Measures[^1];
        var measure = measurePosition.MeasureIndex;
        var localX = Math.Clamp(p.X - measurePosition.X, 0, Math.Max(0, measurePosition.Width - 0.001));
        var hitSlot = WarpFor(track, measure).SlotAt(localX / Math.Max(1e-6, measurePosition.Width));
        var gridCell = Math.Clamp((int)hitSlot, 0, SlotsFor(measure) - 1);
        var measureModel = track.Measures[measure];
        var cell = ResolveBeatHitCell(measureModel, hitSlot, gridCell,
            CellsFor(measureModel, create: _activeVoiceIndex == 1));
        var tabTop = TabTop(system);
        var stringIndex = Math.Clamp((int)Math.Round((p.Y - tabTop) / StringGap), 0, Math.Max(0, track.StringTunings.Count - 1));
        return (measure, cell, stringIndex);
    }

    /// <summary>
    /// Resolve clicks on a sustained note to its actual beat cell instead of the empty sixteenth-grid
    /// slot underneath it. Notes are drawn at their rhythmic onset, while the old hit test used only
    /// the integer slot index; imported fractional onsets and any note longer than one slot could
    /// therefore leave a selectable cursor position in the middle of the visible note.
    /// </summary>
    internal static int ResolveBeatHitCell(MeasureModel measure, double slotPosition, int fallbackCell,
        IReadOnlyList<TabCell>? voiceCells = null)
    {
        if (!double.IsFinite(slotPosition)) return fallbackCell;
        var hit = (voiceCells ?? measure.Cells).Select((cell, index) =>
            {
                var start = BeatStart(cell, index);
                var distanceFromHead = Math.Abs(slotPosition - (start + 0.5));
                var occupiesPosition = slotPosition >= start - 0.001 &&
                                       slotPosition < start + MusicTime.CellSlots(cell) - 0.001;
                var nearHead = cell.Notes.Count > 0 && distanceFromHead <= 0.55;
                return (cell, index, start, distanceFromHead, IsHit: occupiesPosition || nearHead,
                    HasBeat: cell.Notes.Count > 0 || cell.IsRest || cell.HasAnnotation);
            })
            .Where(candidate => candidate.HasBeat && candidate.IsHit)
            .OrderBy(candidate => candidate.distanceFromHead)
            .ThenByDescending(candidate => candidate.start)
            .Select(candidate => candidate.index)
            .FirstOrDefault(-1);
        return hit >= 0 ? hit : fallbackCell;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _leftMouseDownPending = false;
        Focus();
        if (Track is null) return;
        var pointer = e.GetPosition(this);
        var p = ToPagePoint(pointer);
        if (p.Y < HeaderHeight) { ClearSelection(); return; }
        var (measure, cell, stringIndex) = HitTest(p);
        ClearSelection(notify: false);
        SelectedMeasure = measure; SelectedCell = cell; SelectedString = stringIndex;
        _leftMouseDownPoint = pointer;
        _leftMouseDownTicks = Environment.TickCount64;
        _leftMouseDownPending = true;
        SelectionChangedNow();
    }

    private void OnRightDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        if (Track is null) return;
        var p = ToPagePoint(e.GetPosition(this));
        // Right-click only opens a menu: it must not move the cursor or seek running playback.
        var (measure, cell, stringIndex) = HitTest(p);
        var onNote = false;
        var overBeat = p.Y >= HeaderHeight && measure >= 0 && measure < Track.Measures.Count;
        if (overBeat)
        {
            var cells = CellsFor(Track.Measures[measure], create: false);
            onNote = cell >= 0 && cell < cells.Count && cells[cell].Notes.Any(n => n.StringIndex == stringIndex);
        }
        ContextMenuRequested?.Invoke(this, new ContextMenuEventArgs(new Point(p.X, p.Y))
        {
            Measure = measure, Cell = cell, StringIndex = stringIndex, OnNote = onNote, OverBeat = overBeat,
            InsideSelection = overBeat && IsInSelection(measure, cell)
        });
        e.Handled = true;
    }

    /// <summary>True when the beat (measure, cell) lies inside the current score selection (false without a selection).</summary>
    public bool IsInSelection(int measure, int cell)
    {
        if (!HasSelection) return false;
        var (m1, c1, m2, c2) = SelectionCellRange;
        if (measure < m1 || measure > m2) return false;
        if (measure == m1 && cell < c1) return false;
        if (measure == m2 && cell > c2) return false;
        return true;
    }

    /// <summary>Selects a beat/string for editing without seeking playback (used by the note menu).</summary>
    public void SelectForEdit(int measure, int cell, int stringIndex)
    {
        ClearSelection(notify: false);
        SelectedMeasure = measure; SelectedCell = cell; SelectedString = stringIndex;
        SelectionChangedNow(seekPlayback: false);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (Track is null) return;
        var p = ToPagePoint(e.GetPosition(this));
        if (e.LeftButton != MouseButtonState.Pressed || !_leftMouseDownPending)
        {
            // Hover highlight only (no editing, no position change).
            if (p.Y < HeaderHeight)
            {
                if (_hoverMeasure != -1) { _hoverMeasure = -1; _hoverCell = -1; InvalidateVisual(); }
                return;
            }
            var (hm, hc, _) = HitTest(p);
            if (hm != _hoverMeasure || hc != _hoverCell) { _hoverMeasure = hm; _hoverCell = hc; InvalidateVisual(); }
            return;
        }
        var pointer = e.GetPosition(this);
        var horizontalDrag = Math.Abs(pointer.X - _leftMouseDownPoint.X);
        // A normal click can move a few pixels while the button is down. Do not turn that
        // pointer jitter into a score range; time-range selection requires an intentional drag.
        if (horizontalDrag < Math.Max(10, SystemParameters.MinimumHorizontalDragDistance * 2)) return;
        if (!_selecting && Environment.TickCount64 - _leftMouseDownTicks < RangeSelectHoldMs) return;
        if (!_selecting) BeginSelection();
        if (p.Y < HeaderHeight) return;
        var (measure, cell, _) = HitTest(p);
        // Pointer moves inside the same cell change nothing: no repaint, no status/fretboard refresh.
        if (measure == _selectionEndMeasure && cell == _selectionEndCell) return;
        _selectionEndMeasure = measure; _selectionEndCell = cell;
        SelectionChangedNow(seekPlayback: false);
    }

    /// <summary>True while the user drags a score range (the window defers side-panel refreshes).</summary>
    public bool IsDragSelecting => _selecting && _leftMouseDownPending && Mouse.LeftButton == MouseButtonState.Pressed;

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) _leftMouseDownPending = false;
        if (_selecting) InvalidateVisual();
    }

    public event EventHandler<ContextMenuEventArgs>? ContextMenuRequested;

    private Point ToPagePoint(Point point) => new(point.X / _zoom, point.Y / _zoom);
}
