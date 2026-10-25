using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Avalonia.Views;

// Owns: the timeline input bar highlight (grey cells): a click or shift-click on a tab cell (never a drag: a lane drag is the box select), a right-click on a cell,
//   the arrow keys, Esc and the click on empty space, and recording each move as one undo step (DocumentEdits.RecordHighlight). The highlight never changes the selection, the loop or the playhead.
// Does not own: the highlight rules (BarHighlight), its drawing (TimelineView.Highlight) or what edits do with it (MainWindowHighlightEdit); the click's other work (the playhead, the score) stays in TimelineInputController.
// Tests: TabForge.Avalonia.Tests.TimelineBarHighlightInputTests.
public sealed partial class TimelineInputController
{
    // Follows the shown document's highlight from now on (a tab switch): it draws, and its changes repaint.
    private void UseHighlight(BarHighlight highlight)
    {
        _highlight.Changed -= Repaint;
        _highlight = highlight;
        _view.Highlight = highlight;
        _highlight.Changed += Repaint;
    }

    private (int Row, int Bar)? TabCellAt(Point point) => _view.TabCellAt(point.X, point.Y);

    // One highlight change = one undo step (nothing is stored when it changed nothing).
    private void RecordHighlight(Action change, string label = "Highlight bars")
    {
        var before = _highlight.Snapshot();
        change();
        DocumentEdits.RecordHighlight(_document, before, label);
    }

    // A click on a tab cell highlights it (shift-click: the block from the anchor) and puts the edit keys on the highlight.
    private void HighlightCell(int row, int bar, bool extend)
    {
        RecordHighlight(() => { if (extend) _highlight.ExtendTo(row, bar); else _highlight.Set(row, bar); });
        _selection.SetFocus(FocusTarget.BarHighlight);
        SelectedBar = bar;
        Repaint();
    }

    // Ctrl+click on a tab cell: it joins the highlight or leaves it (one undo step); the playhead, the score and the clips stay as they are.
    private void ToggleCell(int row, int bar)
    {
        RecordHighlight(() => _highlight.Toggle(row, bar));
        _selection.SetFocus(_highlight.Has ? FocusTarget.BarHighlight : FocusTarget.None);
        Repaint();
    }

    // Esc: the white selection goes together (the selected clips and the grey highlight); the loop region stays for a later Esc (SelectionLoopLink).
    private bool ClearWhite()
    {
        var hadClips = _selection.ClipCount > 0;
        if (hadClips) _view.SelectedClip = null;
        return ClearHighlight() || hadClips;
    }

    // Esc, or a click on empty timeline space.
    private bool ClearHighlight()
    {
        if (!_highlight.Has) return false;
        RecordHighlight(() => _highlight.Clear(), "Clear highlight");
        if (_selection.Focus == FocusTarget.BarHighlight) _selection.SetFocus(FocusTarget.None);
        return true;
    }

    // A right-click on a tab cell outside the highlight highlights that cell; the context menu then shows the highlight's rows.
    private void HighlightForMenu(Point point)
    {
        _view.Focus();
        if (_hitTests.ClipAt(point) is not null || TabCellAt(point) is not { } cell) return;
        if (!_highlight.Contains(cell.Row, cell.Bar)) RecordHighlight(() => _highlight.Set(cell.Row, cell.Bar));
        _selection.SetFocus(FocusTarget.BarHighlight);
    }

    // Esc clears the highlight; the arrow keys move it and Shift+arrows extend it while the last click was on it. True when the key was used.
    private bool HighlightKey(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None) return ClearWhite();
        if (!_highlight.Has || _selection.Focus != FocusTarget.BarHighlight || e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift)) return false;
        var (trackStep, barStep) = e.Key switch { Key.Left => (0, -1), Key.Right => (0, 1), Key.Up => (-1, 0), Key.Down => (1, 0), _ => (0, 0) };
        if (trackStep == 0 && barStep == 0) return false;
        var tracks = _document.Project.Tracks;
        var repeat = _heldArrow == e.Key;   // auto-repeat of the held key joins the step its first press recorded; a new press is a step of its own
        _heldArrow = e.Key;
        void Move() => _highlight.Move(trackStep, barStep, e.KeyModifiers == KeyModifiers.Shift, tracks.Count, _view.BarCount, i => tracks[i].HasNotation);
        if (repeat) Move(); else RecordHighlight(Move);
        _view.EnsureRowVisible(_highlight.ActiveTrack);
        var cell = new Rect(_view.BarX(_highlight.ActiveBar), _view.RowTop(_highlight.ActiveTrack), _view.BarX(_highlight.ActiveBar + 1) - _view.BarX(_highlight.ActiveBar), _view.RowHeight);
        _view.RaiseEvent(new RequestBringIntoViewEventArgs { RoutedEvent = Control.RequestBringIntoViewEvent, TargetObject = _view, TargetRect = cell });   // the scroll viewer brings the cell into view
        return true;
    }
}
