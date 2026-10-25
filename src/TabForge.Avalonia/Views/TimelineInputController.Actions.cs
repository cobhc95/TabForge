using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using TabForge.Avalonia;
using TabForge.Avalonia.Features.Recording.Presenter;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Features.Recording.Rules;
using TabForge.Menus;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Shell;

namespace TabForge.Avalonia.Views;

// Owns: the controller's track toggles, selection clicks and post-edit helpers.
// Does not own: the rest of the class (see the main file).
// Tests: no dedicated test class.
public sealed partial class TimelineInputController
{
    /// <summary>Mute, solo or record arm of the row, or of every selected track when the row is one of them: all take the clicked track's new state. Disarming trims the takes as the WPF RecordingController does. Record arm is its own undo step
    /// ("Arm track"/"Disarm track"), separate from the recording landing; a track being recorded keeps its lane until the global stop.</summary>
    private void ToggleTrack(int row, TrackRowCells.Cell kind)
    {
        var flag = kind == TrackRowCells.Cell.Mute ? TrackFlag.Mute : kind == TrackRowCells.Cell.Solo ? TrackFlag.Solo : TrackFlag.Arm;
        var rows = _selection.Tracks.Targets(_document.Project.Tracks, row);
        var on = !TrackGroupEdits.Get(_document.Project.Tracks[row], flag);
        if (flag == TrackFlag.Arm)
        {
            var flipped = rows.Where(r => TrackGroupEdits.Get(_document.Project.Tracks[r], flag) != on).ToList();
            DocumentEdits.Run(_document, song =>
            {
                TrackGroupEdits.SetFlag(song, rows, flag, on);
                if (!on) foreach (var track in flipped.Select(r => song.Tracks[r]).Where(t => _view.LiveTakes.Of(t) is null)) ClipLanes.Trim(track);   // a recording lane stays until the stop
                return true;
            }, label: on ? "Arm track" : "Disarm track");
            Repaint();
            TrackArmToggled?.Invoke(row);
            return;
        }
        var changed = DocumentEdits.Run(_document, song => TrackGroupEdits.SetFlag(song, rows, flag, on),
            invalidatesTimeline: false, label: flag == TrackFlag.Mute ? "Mute track" : "Solo track").Changed;
        if (!changed) return;
        Repaint();
        TrackMuteSoloToggled?.Invoke();
    }

    private void Repaint()
    {
        _view.InvalidateVisual();
        _header?.InvalidateVisual();
    }

    /// <summary>Ends any gesture without an edit (pointer capture lost or a tab switch).</summary>
    private void Cancel()
    {
        if (_gesture == Gesture.Loop) CancelLoopDrag();
        _gesture = Gesture.None;
        EndBoxSelect();
        _clips.Cancel();
        _picker.Cancel();
        if (_header is { SliderPreview: not null }) { _header.SliderPreview = null; _header.SliderGroup = null; }
        ClearDrag();
    }

    /// <summary>Clears the drag previews and repaints; the caller keeps the model as it is.</summary>
    private void ClearDrag()
    {
        SectionAnimator.Stop();
        _view.DragSection = _view.DragRow = -1;
        _view.SectionPreviewX = null;
        _view.SectionRestingX = null;
        _view.RowOffsets = null;
        Repaint();
    }

    private void ApplyHover(Point p)
    {
        var hover = _hitTests.HoverAt(p);
        SetHover(hover.Bar, hover.Row);
        _view.SetHoveredSection(hover.Section);
        ApplyLoopCursor(p);
    }

    // A click (no drag) on the grid: Ctrl+click toggles a clip in or out of the white, Shift+click adds it, a plain click selects it alone (the score goes to
    // its track and start bar); a click on a tab cell highlights it (Shift extends); a plain click on empty lane space clears the white. A ruler click clears the
    // white and the loop (and the caller moves the playhead there).
    private void ClickSelect(TimelineClipSelection? clip, int clipBar, bool ctrl)
    {
        if (_pressOnRuler) { ClearAllOnRulerClick(); MarkBar(_pressBar); return; }
        if (clip is not null)
        {
            if (ctrl) _view.ToggleClip(clip);
            else if (_pressShift) { if (!_view.IsClipSelected(clip.Clip)) _view.ToggleClip(clip); }
            else
            {
                _view.SelectedClip = clip;
                MarkBar(clipBar);
                if (_pressRow >= 0 && clipBar >= 0 && clipBar < _view.BarCount) CellClicked?.Invoke(_pressRow, clipBar);
            }
            return;
        }
        var cell = TabCellAt(_pressPoint);   // a click on a tab cell highlights it and leaves the clips and the loop as they are
        if (_pressShift) { if (cell is { } shifted) HighlightCell(shifted.Row, shifted.Bar, extend: true); return; }
        if (ctrl)
        {
            if (cell is { } toggled) ToggleCell(toggled.Row, toggled.Bar);
            return;
        }
        _view.SelectedClip = null;
        MarkBar(_pressBar);
        if (_pressRow >= 0 && _pressBar >= 0 && _pressBar < _view.BarCount) CellClicked?.Invoke(_pressRow, _pressBar);
        if (cell is { } plain) HighlightCell(plain.Row, plain.Bar, extend: false);
        else ClearHighlight();
    }

    // A plain ruler click (no drag): the white selection and the loop go (the loop clear with Loop off is one undo step); the caller moves the playhead.
    private void ClearAllOnRulerClick()
    {
        // 25 Oct: a plain ruler click clears only the loop region (the white selection stays; the Loop button is untouched)
        DocumentEdits.EditLoop(_document, model => model.ClearLoop(SelectionOrigin.Timeline), "Clear loop");
    }

    // The clicked bar becomes the selected bar (the host follows it).
    private void MarkBar(int bar)
    {
        if (_view.BarCount == 0 || bar < 0) return;
        SelectedBar = bar;
        Repaint();
        SelectedBarChanged?.Invoke(bar);
    }

    private void SetHover(int bar, int row)
    {
        if (_view.HoverBar == bar && _view.HoverRow == row) return;
        _view.HoverBar = bar;
        _view.HoverRow = row;
        Repaint();
    }

    private void AfterEdit()
    {
        _selection.Clear(SelectionOrigin.Document);
        SelectedBar = -1;
        _view.Reload(_document.Project);
        _header?.Reload(_document.Project);
        SongEdited?.Invoke();
    }
}
