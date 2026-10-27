using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;
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
        if (TryDropCloseGap()) return true;
        var from = _slots[_dragSlot].MarkerIndex;
        var split = false;
        // Audio or MIDI clips crossing the section's edges: the setting decides, or the prompt asks (the lifted column stays until it is answered).
        if (SectionAudioCrossings.WouldMove(_document.Project, from, _dropBefore) && SectionAudioCrossings.Find(_document.Project, from) is { } crossing)
        {
            var remembered = SectionAudioPromptPresenter.Remembered(SectionAudioSetting?.Invoke());
            if (remembered is null && SectionAudioPrompt is { } ask)
            {
                UiTasks.Forget(AskThenDropSection(from, _dropBefore, crossing, ask), "Moving the section", _ => ClearDrag());
                return true;
            }
            split = remembered?.Split == true;
        }
        return CommitSectionDrop(from, _dropBefore, split);
    }

    /// <summary>The nearest bar line to x, kept on the song's bars.</summary>
    private int NearestBarLine(double x)
    {
        var bar = Math.Clamp(_view.BarAtX(x), 0, Math.Max(0, _view.BarCount - 1));
        return x - _view.BarX(bar) > (_view.BarX(bar + 1) - _view.BarX(bar)) / 2 ? bar + 1 : bar;
    }

    /// <summary>The first bar a section dragged left into the empty bars before it would start at (snapping to the previous section's end when it reaches it), with the gap's first bar; null when it did not move left into a gap.</summary>
    internal (int Target, int Section, int GapFirst)? GapDropTarget()
    {
        if (_dropBefore != _dragSlot && _dropBefore != _dragSlot + 1) return null;   // a reorder, not a slide into a gap
        if (_view.SectionMarkerAt(_slots[_dragSlot].MarkerIndex) is not { } marker || SectionGapEdits.Find(_document.Project, marker.MeasureIndex - 1) is not var (first, _)) return null;
        var target = Math.Clamp(NearestBarLine(_view.DragSectionX), first, marker.MeasureIndex);
        return target < marker.MeasureIndex ? (target, marker.MeasureIndex, first) : null;
    }

    // A section slid left into the empty bars before it closes the dragged-over bars with the Close gap operation (the full gap when it reaches the previous section).
    private bool TryDropCloseGap()
    {
        if (GapDropTarget() is not var (target, section, _)) return false;
        UiTasks.Forget(CloseGapOnDrop(_view.SectionMarkerAt(_slots[_dragSlot].MarkerIndex)!, target, section - 1), "Closing the gap", _ => ClearDrag());
        return true;
    }

    private async Task CloseGapOnDrop(MarkerModel marker, int first, int last)
    {
        var document = _document;
        var changed = await MainWindowTimelineSections.CloseGapAsync(document, first, last, GapClosePrompt, _ => { });
        if (!ReferenceEquals(document, _document)) return;
        if (changed) AfterEdit();
        _view.SectionPreviewX = null;
        _view.SectionRestingX = null;
        var slots = _view.SectionSlots();
        var landing = slots.FirstOrDefault(slot => ReferenceEquals(_view.SectionMarkerAt(slot.MarkerIndex), marker));
        if (landing.Width <= 0) { ClearDrag(); return; }
        _view.DragSection = landing.MarkerIndex;
        if (!SectionAnimator.BeginSettle(_view.DragSectionX, landing.Left, ClearDrag)) ClearDrag();
    }

    private async Task AskThenDropSection(int from, int dropBefore, SectionAudioCrossing crossing, Func<SectionAudioCrossing, Task<SectionAudioAnswer?>> ask)
    {
        var document = _document;
        var answer = await ask(crossing);
        if (!ReferenceEquals(document, _document)) return;   // the tab changed under the prompt: the drag was cancelled with it
        if (answer is not { } picked)
        {
            _view.SectionPreviewX = null;   // Cancel move: the column slides back to where it was
            _view.SectionRestingX = null;
            _view.DragSection = _slots[_dragSlot].MarkerIndex;
            if (!SectionAnimator.BeginSettle(_view.DragSectionX, _slots[_dragSlot].Left, ClearDrag)) ClearDrag();
            return;
        }
        if (picked.Remember) SectionAudioRemember?.Invoke(SectionAudioPromptPresenter.Store(picked));
        if (!CommitSectionDrop(from, dropBefore, picked.Split)) ClearDrag();
    }

    private bool CommitSectionDrop(int from, int dropBefore, bool splitCrossingAudio)
    {
        var landing = _slots[_dragSlot];
        var starts = new List<(AudioClip Clip, double Start)>();   // every clip before the move: the ones that moved or were cut off afterwards are the edited ones
        var edit = DocumentEdits.Run<int[]>(_document, song =>
            {
                TabForge.Avalonia.Platform.SectionDisplayColours.FreezeDisplayedColours(song);
                starts.AddRange(song.Tracks.SelectMany(track => track.AudioClips).Select(clip => (clip, clip.StartSec)));
                return SectionAudioCrossings.Move(song, from, dropBefore, splitCrossingAudio);   // splits + bar move: one undo step
            }, label: "Move section");
        if (edit.Changed)
        {
            AfterEdit();
            var moved = _document.Project.Tracks.SelectMany(track => track.AudioClips)
                .Where(clip => starts.FindIndex(s => ReferenceEquals(s.Clip, clip)) is var i && (i < 0 || Math.Abs(starts[i].Start - clip.StartSec) > 1e-9)).ToList();
            var now = _view.SectionSlots();
            var target = Math.Clamp(dropBefore > _dragSlot ? dropBefore - 1 : dropBefore, 0, now.Length - 1);
            landing = now[target];
            // The playing song takes the new order from the next bar (no restart): the host re-syncs the moved clips and refreshes the engine live.
            if (edit.Value is { } mapping) SectionMoved?.Invoke(_document, mapping);
            _view.ClipOverlapsAfter(_document, moved);   // a carried clip may now cover another on its lane
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
