using System;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the timeline input's section and row drags and the master slider gesture (begin, update, drop, commit), split from TimelineInputController.cs.
// Does not own: the press/move/release dispatch that starts them (TimelineInputController).
public sealed partial class TimelineInputController
{
    private void BeginSlider(int row, TrackRowCells.Cell cell, PointerPressedEventArgs e)
    {
        _gesture = Gesture.Slider;
        _header!.SliderGroup = row == TrackHeaderColumn.MasterRow ? null : _selection.Tracks.Targets(_document.Project.Tracks, row);   // the selected tracks follow this slider
        _pressRow = row;
        _sliderCell = cell;
        e.Pointer.Capture(_header);
        _sliderGrab = GrabOffsetOnThumb(row, cell);
        PreviewSlider(_pressPoint);
    }

    /// <summary>A press on the track slider's own thumb grabs it where it is: the pointer's offset from the thumb centre is kept, so a click that does not move it changes nothing (and records no undo step).</summary>
    private double GrabOffsetOnThumb(int row, TrackRowCells.Cell cell)
    {
        if (row < 0 || row >= _document.Project.Tracks.Count) return 0;
        return TrackRowCells.SliderGrabOffset(_header!.CellRect(row, cell), TrackRowCells.SliderValue(_document.Project.Tracks[row], cell), TrackHeaderColumn.SliderRangeOf(row, cell), _pressPoint.X);
    }

    /// <summary>A double-click: the slider goes to its declared default through the same commit as a drag's release (one undo step).</summary>
    internal void ResetSlider(int row, TrackRowCells.Cell cell)
    {
        _header!.SliderPreview = (row, cell, TrackHeaderColumn.SliderDefaultOf(row, cell));
        CommitSlider(reset: true);
    }

    private void PreviewSlider(Point p) =>
        _header!.SliderPreview = (_pressRow, _sliderCell, TrackRowCells.SliderValueAt(_sliderCell, _header.CellRect(_pressRow, _sliderCell), p.X + _sliderGrab,
            TrackHeaderColumn.SliderRangeOf(_pressRow, _sliderCell)));

    /// <summary>One edit for the whole drag, with the value under the pointer at release; the master row's sliders are applied by their host. Every selected track moves by the dragged
    /// track's change (each clamped), or all go to the default on a reset.</summary>
    private void CommitSlider(bool reset = false)
    {
        if (_header!.SliderPreview is not { } preview) return;
        _header.SliderPreview = null;
        _header.SliderGroup = null;
        if (preview.Row == TrackHeaderColumn.MasterRow)
        {
            MasterSliderReleased?.Invoke(preview.Cell, preview.Value);
            Repaint();
            return;
        }
        var volume = preview.Cell == TrackRowCells.Cell.Volume;
        var level = volume ? TrackLevel.Volume : TrackLevel.Pan;
        var rows = _selection.Tracks.Targets(_document.Project.Tracks, preview.Row);
        var changed = DocumentEdits.Run(_document, song => reset
            ? TrackGroupEdits.ResetLevel(song, rows, level, volume ? preview.Value : preview.Value + 64)
            : TrackGroupEdits.ShiftLevel(song, rows, level, preview.Value - TrackRowCells.SliderValue(song.Tracks[preview.Row], preview.Cell)),
            invalidatesTimeline: false, label: volume ? "Track volume" : "Track pan").Changed;
        Repaint();
        if (changed) TrackLevelChanged?.Invoke();
    }

    private void BeginSectionDrag()
    {
        // A drag is not a click: the press eases back here, so the release jumps nowhere and flashes nothing.
        _view.SectionButtons.Release();
        _gesture = Gesture.SectionDrag;
        _grabOffset = _pressPoint.X - _slots[_dragSlot].Left;
        _view.DragSection = _slots[_dragSlot].MarkerIndex;
        _view.DragSectionX = _slots[_dragSlot].Left;
        SectionAnimator.Stop();
        _layoutScratch = new double[_slots.Length];
        SectionAnimator.Begin(Array.ConvertAll(_slots, slot => slot.Left));
        _view.SectionPreviewX = SectionAnimator.Shown;
        _view.SectionRestingX = SectionAnimator.Targets;
        _view.SectionDropX = _slots[_dragSlot].Left - SectionDragPresenter.PackedGap / 2;
        SetHover(-1, -1);
    }

    private void BeginRowDrag()
    {
        _gesture = Gesture.RowDrag;
        _dropRow = _pressRow;
        _view.DragRow = _pressRow;
        _view.RowOffsets = new double[_view.RowCount];
        SetHover(-1, -1);
    }

    private void UpdateDrag(Point p)
    {
        if (_gesture == Gesture.SectionDrag)
        {
            _dropBefore = SectionDragPresenter.InsertBefore(_slots, p.X);
            SectionDragPresenter.FillPreviewLayout(_slots, _dragSlot, _dropBefore, _layoutScratch);
            SectionAnimator.SetTargets(_layoutScratch);
            _view.DragSectionX = p.X - _grabOffset;
            _view.SectionDropX = _layoutScratch[_dragSlot] - SectionDragPresenter.PackedGap / 2;
        }
        else
        {
            var row = _view.RowAtY(p.Y);
            _dropRow = Math.Clamp(row >= 0 ? row : p.Y < _view.RowTop(0) ? 0 : _view.RowCount - 1, 0, _view.RowCount - 1);
            _view.EnsureRowVisible(_dropRow);
            var delta = p.Y - _pressPoint.Y;
            var group = _selection.Tracks.Targets(_document.Project.Tracks, _pressRow);   // the selected tracks travel with the dragged one
            for (var i = 0; i < _view.RowCount; i++)
                _view.RowOffsets![i] = group.Contains(i) ? delta : TrackReorderPresenter.TargetOffset(i, _pressRow, _dropRow, delta, _view.RowHeight);
        }
        Repaint();
    }

    /// <summary>Applies the reorder; true when the dropped column is now settling into its slot (the drag visuals clear when it lands).</summary>
    private bool DropSection()
    {
        var from = _slots[_dragSlot].MarkerIndex;
        var landing = _slots[_dragSlot];
        var edit = DocumentEdits.Run<int[]>(_document, song =>
            {
                TabForge.Avalonia.Platform.SectionDisplayColours.FreezeDisplayedColours(song);
                return SectionReorderService.MoveWithClips(song, from, _dropBefore);
            }, label: "Move section");
        if (edit.Changed)
        {
            AfterEdit();
            var now = _view.SectionSlots();
            var target = Math.Clamp(_dropBefore > _dragSlot ? _dropBefore - 1 : _dropBefore, 0, now.Length - 1);
            landing = now[target];
            // The playing song takes the new order from the next bar (no restart): the host re-syncs the moved clips and refreshes the engine live.
            if (edit.Value is { } mapping) SectionMoved?.Invoke(_document, mapping);
        }
        _view.DragSection = landing.MarkerIndex;
        _view.SectionPreviewX = null;
        _view.SectionRestingX = null;
        return SectionAnimator.BeginSettle(_view.DragSectionX, landing.Left, ClearDrag);
    }

    private void DropRow()
    {
        var group = _selection.Tracks.Targets(_document.Project.Tracks, _pressRow);
        if (_dropRow != _pressRow && DocumentEdits.Run(_document, song => TrackGroupEdits.MoveBlock(song, group, _pressRow, _dropRow), label: group.Count > 1 ? "Move tracks" : "Move track").Changed) AfterEdit();
    }
}
