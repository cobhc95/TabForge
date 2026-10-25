using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Avalonia.Views;

// Owns: the box drag on the lanes (Explorer style): the marquee from the press to the pointer (TimelineView.Marquee, drawn as an overlay) and the white
//   selection of exactly the clips it touches, live while dragging; the release hides the marquee and keeps the white. It never makes a time range or loop.
// Does not own: the clip hit geometry (TimelineAudioClips.ClipsIn), the clip move (TimelineClipGestureController) or the loop region (TimelineInputController.Loop).
// Tests: TabForge.Avalonia.Tests.TimelineBoxSelectionTests.
public sealed partial class TimelineInputController
{
    private readonly List<(int Row, int Index)> _boxNow = new();
    private readonly List<(int Row, int Index)> _boxShown = new();
    private BarHighlightSnapshot? _boxBefore;
    private bool _boxAdds;   // Ctrl held at the start: the box adds its cells to the highlight

    // A new box drops the earlier white selection (clips, the grey cells and the score notes), so a box touching nothing leaves nothing white.
    private void BeginBoxSelect(Point pointer, bool add)
    {
        _boxAdds = add;
        _boxShown.Clear();
        _view.SelectedClip = null;
        _boxBefore = _highlight.Snapshot();   // the grey block goes now, quietly; the release records the clear and the new block as one undo step
        if (!add && _highlight.Clear() && _selection.Focus == FocusTarget.BarHighlight) _selection.SetFocus(FocusTarget.None);
        if (_selection.HasRange) _selection.Clear(SelectionOrigin.Timeline);
        UpdateBoxSelect(pointer);
    }

    // Each pointer move only moves the marquee (a struct) and walks the clips; the selection changes (and allocates) when the touched set does.
    private void UpdateBoxSelect(Point pointer)
    {
        var box = new Rect(new Point(Math.Min(_pressPoint.X, pointer.X), Math.Min(_pressPoint.Y, pointer.Y)),
            new Point(Math.Max(_pressPoint.X, pointer.X), Math.Max(_pressPoint.Y, pointer.Y)));
        _view.Marquee = box;
        _view.InvalidateVisual();
        _boxNow.Clear();
        TimelineAudioClips.ClipsIn(_view, box, _boxNow);
        if (_boxNow.Count == _boxShown.Count)
        {
            var same = true;
            for (var i = 0; same && i < _boxNow.Count; i++) same = _boxNow[i] == _boxShown[i];
            if (same) return;
        }
        _boxShown.Clear();
        _boxShown.AddRange(_boxNow);
        var tracks = _document.Project.Tracks;
        _selection.SetClips(_boxNow.Select(hit => new TimelineClipSelection(tracks[hit.Row], tracks[hit.Row].AudioClips[hit.Index])));
        if (_boxNow.Count > 0) _selection.SetFocus(FocusTarget.Clips);
    }

    // The release (or a lost capture) hides the marquee; the white stays.
    private void EndBoxSelect(bool commit = false)
    {
        if (_view.Marquee is not { } box) return;
        if (commit) HighlightBox(box);
        if (_boxBefore is { } before) { DocumentEdits.RecordHighlight(_document, before, "Highlight bars"); _boxBefore = null; }
        _view.Marquee = null;
        _view.InvalidateVisual();
    }

    // The release also grey-highlights the tab cells the box touches (one block, as Shift+click extends), so the score follows like it does for a click.
    private void HighlightBox(Rect box)
    {
        int first = -1, last = -1;
        for (var row = 0; row < _view.RowCount; row++)
        {
            if (!_document.Project.Tracks[row].HasNotation) continue;
            var top = _view.RowTop(row);
            if (top >= box.Bottom || top + _view.NotationHeightOf(row) <= box.Y) continue;
            if (top >= _view.TrackRowsBottom || top + _view.NotationHeightOf(row) <= _view.TrackRowsTop) continue;   // scrolled out of view
            if (first < 0) first = row;
            last = row;
        }
        if (first < 0 || _view.BarCount == 0) return;
        var left = Math.Clamp(_view.BarAtX(box.X), 0, _view.BarCount - 1);
        var right = Math.Clamp(_view.BarAtX(box.Right), 0, _view.BarCount - 1);
        if (_boxAdds) _highlight.AddBlock(first, left, last, right, track => _document.Project.Tracks[track].HasNotation);
        else _highlight.SetBlock(first, left, last, right);
        if (_selection.ClipCount == 0) _selection.SetFocus(FocusTarget.BarHighlight);
        if (_boxAdds) return;   // Ctrl+box adds cells and leaves the playhead and the score cursor
        MarkBar(left);
        CellClicked?.Invoke(first, left);   // the score follows, as for a Shift+click
    }
}
