using System;
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

/// <summary>
/// The pointer handling of the Avalonia <see cref="TimelineView"/>: hover of the bar and row under the pointer, click on a tab cell to highlight it (the grey bar highlight, see TimelineInputController.Highlight.cs),
/// shift-click to extend it, click elsewhere to move the playhead, a box drag on the lanes to select the clips it touches (white, TimelineInputController.Box.cs), a drag
/// on the bar-number ruler to make, resize or move the loop region (TimelineInputController.Loop.cs), dragging a section to reorder it (preview from <see cref="SectionDragPresenter"/>), and dragging a
/// row up or down to reorder tracks (preview from <see cref="TrackReorderPresenter"/>; the drag starts on the track header, not the grid), and the
/// header cells (<see cref="TrackRowCells"/>): mute, solo and record arm toggle, the volume and pan sliders drag (previewed on the header, edited on
/// release), and the cogwheel, instrument icon and FX cells raise <see cref="TrackCellRequested"/> (the icon only on instrument tracks: an audio
/// track's icon keeps the row press). The master row's sliders raise <see cref="MasterSliderReleased"/> instead. A drop, toggle or slider release changes the song only through
/// <see cref="DocumentEdits"/>.Run, so it is one undo step.
/// Owns: the gesture state and the translation from pointer positions to the view's overlay state and to the two edits, and the click that
/// selects an audio clip (a click on the grid outside any clip clears the selection).
/// Does not own: the hit-query composition (TimelineHitTester), drawing and hit geometry (TimelineView), the reorder rules (the Core presenters and services), the audio clip drag (TimelineClipGestureController), audio lanes and
/// the loop band, the clips that travel with a moved section, and the animated settle of the timeline (a drop lands at once). The score cursor
/// has no hook in the preview, so a click raises <see cref="SelectedBarChanged"/> for the host to use.
/// Tests: TabForge.Avalonia.Tests.TimelineInputTests, TabForge.Avalonia.Tests.TrackRowControlsTests.
/// </summary>
public sealed partial class TimelineInputController
{
    private const double DragThreshold = TabDragRules.DragThreshold;

    private enum Gesture { None, Press, SectionDrag, RowDrag, Slider, Clip, Box, Loop, SectionResize }

    private readonly TimelineView _view;
    private IEditableDocument _document;
    private readonly TrackHeaderColumn? _header;
    private readonly TimelineClipGestureController _clips;
    private readonly TimelineHitTester _hitTests;
    private readonly TimelineRepeatPickerController _picker;
    private bool _pressOnGrid;
    private bool _pressOnLanes;   // a press on a track row (not the ruler, sections or the gaps), where a drag box-selects clips
    private bool _pressOnRuler;   // a press on the ruler bar numbers: a drag makes or edits the loop region, a click seeks
    private Gesture _gesture;
    private Point _pressPoint;
    private int _pressRow = -1;
    private int _pressBar;
    private bool _pressShift;
    private TimelineView.SectionSlot[] _slots = Array.Empty<TimelineView.SectionSlot>();
    private int _dragSlot = -1;
    private double _grabOffset;
    private int _dropBefore;
    private int _dropRow;
    private TrackRowCells.Cell _sliderCell;
    private double _sliderGrab;
    private double[] _layoutScratch = Array.Empty<double>();
    private SelectionModel _selection;
    private BarHighlight _highlight;
    private bool _collapseRowsOnRelease;   // a plain press on a selected track of a group: the release (no drag) makes it the only one
    private Key? _heldArrow;   // the arrow key whose press started the highlight move that auto-repeat now continues (one undo step)

    /// <summary>Eases the other sections aside while one drags and settles the dropped column; its Enabled flag is the section drag animation setting.</summary>
    internal SectionDragAnimator SectionAnimator { get; }

    private void OnSectionFrame()
    {
        if (SectionAnimator.Settling) _view.DragSectionX = SectionAnimator.SettleX;
        Repaint();
    }

    /// <param name="sectionClock">The clock the section slide reads each frame; null uses real time (see <see cref="SectionDragAnimator"/>).</param>
    /// <param name="sectionTimer">The section slide's frame timer; null uses a dispatcher timer. Tests pass one they fire themselves.</param>
    public TimelineInputController(TimelineView view, IEditableDocument document, TrackHeaderColumn? header = null, Func<TimeSpan>? sectionClock = null, IUiTimer? sectionTimer = null)
    {
        _view = view;
        _document = document;
        _header = header;
        _selection = document.Selection;
        _highlight = document.Highlight;
        view.Highlight = _highlight;
        _highlight.Changed += Repaint;
        _clips = new TimelineClipGestureController(view, ResolveClickPosition);
        _hitTests = new TimelineHitTester(view, header);
        _picker = new TimelineRepeatPickerController(view);
        SectionAnimator = new SectionDragAnimator(OnSectionFrame, sectionClock, sectionTimer);
        _selection.Changed += OnSelectionChanged;
        view.FocusModel = _selection;
        view.PointerMoved += (_, e) => Moved(e, view);
        view.PointerPressed += (_, e) => Pressed(e, view);
        view.PointerReleased += (_, e) => Released(e, view);
        view.PointerExited += (_, _) => { if (_gesture == Gesture.None) { SetHover(-1, -1); view.HoverSectionEdge = null; } _picker.OnPointerExited(); };
        view.PointerCaptureLost += (_, _) => Cancel();
        view.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && _picker.OnEsc()) e.Handled = true;
            else if (HighlightKey(e)) e.Handled = true;
        };
        view.KeyUp += (_, e) => { if (e.Key == _heldArrow) _heldArrow = null; };
        view.LostFocus += (_, _) => _heldArrow = null;
        if (header is null) return;
        header.PointerMoved += (_, e) => Moved(e, header);
        header.PointerPressed += (_, e) => Pressed(e, header);
        header.PointerReleased += (_, e) => Released(e, header);
        header.PointerCaptureLost += (_, _) => Cancel();
    }

    /// <summary>Edits and shows <paramref name="document"/> from now on (a tab switch): a gesture in progress ends and the timeline shows that document's own selection.</summary>
    public void Show(IEditableDocument document)
    {
        Cancel();
        _document = document;
        SelectedBar = -1;
        UseSelection(document.Selection);
        UseHighlight(document.Highlight);
        _view.SetPlayhead(_view.PlayheadBar, _view.PlayheadFraction);
        _view.Reload(document.Project);
        _header?.Reload(document.Project);
    }

    /// <summary>Follows the shown document's selection again (the view attached); following it twice changes nothing.</summary>
    public void Watch()
    {
        Unwatch();
        _selection.Changed += OnSelectionChanged;
        _highlight.Changed += Repaint;
    }

    /// <summary>Stops following the selection (the view detached), so a closed view no longer redraws for a document that lives on elsewhere.</summary>
    public void Unwatch()
    {
        _selection.Changed -= OnSelectionChanged;
        _highlight.Changed -= Repaint;
        SectionAnimator.Stop();   // a slide still running would keep repainting the detached view
    }

    /// <summary>The selection this timeline reads and writes: the shown document's.</summary>
    public SelectionModel Selection => _selection;

    // Reads and writes the shown document's selection from now on and shows its white clips and loop at once.
    private void UseSelection(SelectionModel model)
    {
        _selection.Changed -= OnSelectionChanged;
        _selection = model;
        _view.FocusModel = model;
        _selection.Changed += OnSelectionChanged;
        OnSelectionChanged(model, SelectionOrigin.Document);
    }

    // Any writer of the shared selection or loop (this timeline, the score, a command) repaints the white clips and the loop marks.
    private void OnSelectionChanged(object? sender, SelectionOrigin origin) => Repaint();

    /// <summary>The bar last clicked, or -1.</summary>
    public int SelectedBar { get; private set; } = -1;

    /// <summary>Raised with the bar index when a click selects a bar.</summary>
    public event Action<int>? SelectedBarChanged;

    /// <summary>Raised with the row after a record arm toggle changes the song, so the host can sync the engine's armed inputs.</summary>
    public event Action<int>? TrackArmToggled;

    /// <summary>Raised after a track's mute or solo toggle changes the song, so the host can push the live mix to the engine.</summary>
    public event Action? TrackMuteSoloToggled;

    /// <summary>Raised with the row and cell when the cogwheel, instrument icon, FX or FX power cell is pressed; the host opens the window or switches the chain. The master row's cells report <see cref="TrackHeaderColumn.MasterRow"/>.</summary>
    public event Action<int, TrackRowCells.Cell>? TrackCellRequested;

    /// <summary>Raised with the cell and the value when a master row slider (volume or pan) is released; the host applies the value (the master knob, or the song's master pan).</summary>
    public event Action<TrackRowCells.Cell, int>? MasterSliderReleased;

    /// <summary>Raised after a track's volume or pan slider changed the song (a release or a double-click reset), so the running playback takes the level at once.</summary>
    public event Action? TrackLevelChanged;

    /// <summary>Raised after a drag drop changed the song (the view has been reloaded).</summary>
    public event Action? SongEdited;

    /// <summary>Raised with the document and the old-to-new bar mapping when a section drop commits (for the engine's live order refresh).</summary>
    public event Action<IEditableDocument, int[]>? SectionMoved;

    /// <summary>Raised when a press on the ruler, a section or a lane lands on a bar and no clip or section drag takes it; the arrangement seeks playback to that bar.</summary>
    public event Action<int>? BarSeekRequested;
    /// <summary>Raised with the pass-aware position when a press on a folded clip lands inside a repeat with a pass above 1 (RV12); the arrangement seeks that pass, else the old <see cref="BarSeekRequested"/> bar path.</summary>
    public event Action<RepeatPosition>? PositionSeekRequested;
    /// <summary>Raised when a section strip is clicked without dragging: the sorted index of the section and whether Ctrl was held. The arrangement jumps to the section.</summary>
    public event Action<int, bool>? SectionClicked;

    /// <summary>Raised when a press on a track row (its list or its lane) is released without a drag: the track of that row is selected.</summary>
    public event Action<int>? TrackRowClicked;

    /// <summary>Raised after a plain click on a lane cell (no drag, no Shift, no clip) with its track row and bar: the score goes to that track's staff and bar.</summary>
    public event Action<int, int>? CellClicked;

    /// <summary>Maps a clip-click point (+ lane slice for RV12 repeat passes) to the pass-aware seek position: inside a repeat region with a folded clip, N&lt;=3 takes the slice under the pointer (folded clips only) and N&gt;=4 takes the shown pass; ruler, empty-space and notation clicks stay pass 1.</summary>
    internal RepeatPosition ResolveClickPosition(double x, double y, TimelineClipSelection? clip)
    {
        var bar = _view.BarAtX(x);
        var fraction = FractionAtX(bar, x);
        if (clip is null) return new RepeatPosition(bar, fraction, 1);
        var row = _view.Song.Tracks.IndexOf(clip.Track);
        if (row < 0 || TimelineAudioClips.FoldOf(_view, clip.Clip) is not { } slices) return new RepeatPosition(bar, fraction, 1);
        RepeatRegion? containing = null;
        foreach (var region in _view.RepeatRegionsNow())
            if (bar >= region.StartBar && bar <= region.EndBar) { containing = region; break; }
        if (containing is not { } hit) return new RepeatPosition(bar, fraction, 1);
        var offsets = _view.RowOffsets;
        var laneTop = _view.LaneBandTopOf(row) + (offsets is { } o && row < o.Length ? o[row] : 0) + Math.Max(0, clip.Clip.Lane) * ClipLanes.AudioLaneHeight;
        var point = new Point(x, y);
        // An unfolded one-pass stretch is its own pass (no slice, no shown pass).
        foreach (var slice in slices)
            if (!slice.IsPart && TimelineAudioClips.SliceRect(_view, slice, laneTop).Contains(point))
                return new RepeatPosition(bar, fraction, Math.Max(1, slice.TagPass));
        if (hit.PassCount > TimelineRepeatPicker.StackedMaxPasses)
            return new RepeatPosition(bar, fraction, _view.RepeatShown.ShownPass(hit));
        foreach (var slice in slices)
            if (TimelineAudioClips.SliceRect(_view, slice, laneTop).Contains(point))
                return slice.IsPart ? new RepeatPosition(bar, fraction, slice.PartIndex + 1) : new RepeatPosition(bar, fraction, 1);
        return new RepeatPosition(bar, fraction, 1);
    }

    private double FractionAtX(int bar, double x)
    {
        if (bar < 0 || bar >= _view.BarCount) return 0;
        var left = _view.BarX(bar);
        var width = _view.BarX(bar + 1) - left;
        return width <= 0 ? 0 : Math.Clamp((x - left) / width, 0, 1);
    }

    private RepeatRegion? RegionAtBar(int bar)
    {
        foreach (var region in _view.RepeatRegionsNow())
            if (region.PassCount >= 2 && bar >= region.StartBar && bar <= region.EndBar)
                return region;
        return null;
    }

    private void Moved(PointerEventArgs e, Control source)
    {
        if (_picker.HasPressed) return;
        var p = e.GetPosition(source);
        switch (_gesture)
        {
            case Gesture.None:
                if (source == _view) { _picker.OnHover(p); ApplyHover(p); }
                break;
            case Gesture.Slider:
                PreviewSlider(p);
                break;
            case Gesture.Press:
                if (Math.Abs(p.X - _pressPoint.X) < DragThreshold && Math.Abs(p.Y - _pressPoint.Y) < DragThreshold) break;
                if (_dragSlot >= 0) BeginSectionDrag();
                else if (_clips.Begin()) { _gesture = Gesture.Clip; ClearRows(); _clips.Update(p, e.KeyModifiers.HasFlag(KeyModifiers.Alt)); break; }
                else if (_pressOnRuler) { BeginLoopDrag(p); break; }
                else if (_pressOnLanes)
                {
                    _gesture = Gesture.Box;
                    ClearRows();
                    BeginBoxSelect(p, e.KeyModifiers.HasFlag(KeyModifiers.Control));
                    break;
                }
                else if (source == _header && _pressRow >= 0 && Math.Abs(p.Y - _pressPoint.Y) > Math.Abs(p.X - _pressPoint.X)) BeginRowDrag();
                else break;
                UpdateDrag(p);
                break;
            case Gesture.Clip:
                _clips.Update(p, e.KeyModifiers.HasFlag(KeyModifiers.Alt));
                break;
            case Gesture.Box:
                UpdateBoxSelect(p);
                break;
            case Gesture.Loop:
                UpdateLoopDrag(p);
                break;
            case Gesture.SectionResize:
                UpdateSectionResize(p);
                break;
            default:
                UpdateDrag(p);
                break;
        }
    }

    private void Pressed(PointerPressedEventArgs e, Control source)
    {
        if (e.GetCurrentPoint(source).Properties.IsRightButtonPressed && ReferenceEquals(source, _view)) HighlightForMenu(e.GetPosition(source));
        if (!e.GetCurrentPoint(source).Properties.IsLeftButtonPressed) return;
        // A press in the arrangement takes the keyboard context: Delete/arrows/Range keys act on the timeline while it has focus.
        if (source == _view || source == _header) _view.Focus();
        _pressPoint = e.GetPosition(source);
        if (ReferenceEquals(source, _view) && _view.TryRequestApproval(_pressPoint)) { e.Handled = true; return; }   // the lock on a clip: click to allow its folder
        var hit = _hitTests.Hit(_pressPoint, source);
        if (hit.HeaderCell is { } cell)
        {
            _pressRow = cell.Row;   // not left over from an earlier press
            _view.TrackRowFocused = true;   // a track row cell click puts the TrackRow.* keys on that row
            _view.ContextMenus.ClickedTrack = cell.Row;
            if (cell.Cell is TrackRowCells.Cell.Volume or TrackRowCells.Cell.Pan)
            {
                if (e.ClickCount == 2) ResetSlider(cell.Row, cell.Cell);   // the second press of a double-click resets; it starts no drag
                else BeginSlider(cell.Row, cell.Cell, e);
            }
            else if (cell.Cell is TrackRowCells.Cell.Mute or TrackRowCells.Cell.Solo or TrackRowCells.Cell.Arm) ToggleTrack(cell.Row, cell.Cell);
            else TrackCellRequested?.Invoke(cell.Row, cell.Cell);
            return;
        }
        // The picker wins before any clip/box/loop/section gesture: a press on a picker rect never seeks, selects or moves a clip.
        if (ReferenceEquals(source, _view) && _picker.OnPress(_pressPoint))
        {
            _pressRow = -1;
            _view.TrackRowFocused = false;
            _pressBar = hit.Bar;
            _pressShift = false;
            _slots = hit.Slots;
            _dragSlot = -1;
            _pressOnGrid = false;
            _pressOnLanes = false;
            _pressOnRuler = false;
            _gesture = Gesture.Press;
            e.Pointer.Capture(source);
            return;
        }
        if (ReferenceEquals(source, _view) && TryBeginSectionResize(_pressPoint, e.Pointer)) { e.Handled = true; return; }
        _pressRow = hit.Row;
        TakeFocus(source, hit.OnGrid && hit.SectionSlot < 0, e.KeyModifiers);
        _pressBar = hit.Bar;
        _pressShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        _slots = hit.Slots;
        _dragSlot = hit.SectionSlot;
        if (_dragSlot >= 0) _view.SectionButtons.Press(hit.Section!);
        _pressOnGrid = hit.OnGrid;
        _pressOnLanes = hit.OnLanes;
        _pressOnRuler = _pressOnGrid && _dragSlot < 0 && _pressPoint.Y < TimelineView.RulerHeight;
        if (_pressOnRuler) PressRuler();
        _clips.Press(_pressOnGrid && _dragSlot < 0, _pressPoint, !e.KeyModifiers.HasFlag(KeyModifiers.Control));
        _gesture = Gesture.Press;
        e.Pointer.Capture(source);
    }

    // The last press sets what the edit keys act on (R1): a track header -> TrackRow (a score selection never follows to it, S6); a clip -> Clips;
    // the ruler -> Loop; a press on an empty lane leaves the focus (its click clears the white). A press elsewhere (a section strip, a gap) only ends the track row focus.
    private void TakeFocus(Control source, bool onTimelineGrid, KeyModifiers modifiers)
    {
        _collapseRowsOnRelease = false;
        if (source == _header)
        {
            _view.TrackRowFocused = _pressRow >= 0;
            if (_pressRow >= 0) SelectPressedRow(modifiers);   // a press on the row itself selects it (the Escape order clears it last); a press on its buttons does not
            if (_pressRow >= 0 && _selection is { HasRange: true, Scope: not SelectionScope.AllTracks }) _selection.Clear(SelectionOrigin.Track);
            return;
        }
        if (!onTimelineGrid) { _view.TrackRowFocused = false; return; }
        if (_hitTests.ClipAt(_pressPoint) is not null) _selection.SetFocus(FocusTarget.Clips);
        else if (_pressPoint.Y < TimelineView.RulerHeight) _selection.SetFocus(FocusTarget.Loop);
    }

    // A box or clip gesture leaves the track rows; a plain press on the grid keeps them.
    private void ClearRows() => _selection.SetRows(Array.Empty<Guid>());

    // Plain press = only this track, Shift = every track from the anchor, Ctrl = add or remove this one. A plain press on a member of a multi-selection waits for the release,
    // so a drag from it moves the whole group.
    private void SelectPressedRow(KeyModifiers modifiers)
    {
        var tracks = _document.Project.Tracks;
        var pick = modifiers.HasFlag(KeyModifiers.Shift) ? TrackPick.Range : modifiers.HasFlag(KeyModifiers.Control) ? TrackPick.Toggle : TrackPick.Plain;
        _collapseRowsOnRelease = pick == TrackPick.Plain && _selection.TrackRowCount > 1 && _selection.Tracks.Contains(tracks, _pressRow);
        if (!_collapseRowsOnRelease) _selection.Tracks.Click(tracks, _pressRow, pick);
    }

    private void Released(PointerReleasedEventArgs e, Control source)
    {
        var gesture = _gesture;
        _gesture = Gesture.None;
        EndBoxSelect(commit: gesture == Gesture.Box);
        if (gesture == Gesture.SectionResize)
        {
            CommitSectionResize();   // before the capture is released: that raises PointerCaptureLost
            e.Pointer.Capture(null);
            ClearDrag();
            if (source == _view) ApplyHover(e.GetPosition(_view));
            return;
        }
        if (gesture == Gesture.Loop) EndLoopDrag();
        if (gesture == Gesture.Slider) CommitSlider();
        // The clip drop commits before the capture is released: releasing it raises PointerCaptureLost, which cancels the drag.
        else if (gesture == Gesture.Clip) _clips.Commit(_document);
        // The picker release must run before the capture is released too: releasing it raises PointerCaptureLost,
        // which cancels the picker press (HasPressed) before OnRelease can apply it.
        else if (_picker.HasPressed)
        {
            var releasePos = e.GetPosition(_view);
            _picker.OnRelease(releasePos);
            e.Pointer.Capture(null);
            _view.SectionButtons.Release();
            ClearDrag();
            if (source == _view) ApplyHover(releasePos);
            return;
        }
        e.Pointer.Capture(null);
        _view.SectionButtons.Release();
        if (gesture == Gesture.Slider) return;
        if (gesture == Gesture.Press && _pressRow >= 0)
        {
            if (_collapseRowsOnRelease) _selection.Tracks.Click(_document.Project.Tracks, _pressRow, TrackPick.Plain);
            TrackRowClicked?.Invoke(_pressRow);
        }
        if (gesture == Gesture.Press && _pressOnGrid)
        {
            var clip = _dragSlot < 0 ? _hitTests.ClipAt(_pressPoint) : null;
            var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
            var clipBar = clip is null ? -1 : AudioClipTimelineRules.BarAtSeconds(_document.Project, clip.Clip.StartSec).Bar;
            // Alt+click on the ruler inside a repeat cycles the playhead pass (k -> k+1, wrapping); outside a repeat it is a no-op (no seek).
            if (_dragSlot >= 0) SectionClicked?.Invoke(_slots[_dragSlot].MarkerIndex, e.KeyModifiers.HasFlag(KeyModifiers.Control));
            else if (_pressOnRuler && alt)
            {
                if (RegionAtBar(_pressBar) is { } altRegion)
                {
                    var current = _view.PlayheadBar == _pressBar ? _view.PlayheadPass : 1;
                    var next = current >= altRegion.PassCount ? 1 : current + 1;
                    PositionSeekRequested?.Invoke(new RepeatPosition(_pressBar, FractionAtX(_pressBar, _pressPoint.X), next));
                }
            }
            // A plain click on a clip selects it and moves the playhead to its start (the score scrolls there); Ctrl or Shift on a clip only changes the white.
            // Any other press on the ruler, a section or a lane moves the playhead to its bar (pass 1 on the old bar path). The loop region always stays.
            else if (clip is not null)
            {
                var seekBar = _pressBar >= 0 && _pressBar < _view.BarCount ? _pressBar : clipBar;   // the bar under the pointer, not the clip start
                if (!ctrl && !_pressShift && seekBar >= 0 && seekBar < _view.BarCount) BarSeekRequested?.Invoke(seekBar);
            }
            else if (ctrl && TabCellAt(_pressPoint) is not null) { }   // Ctrl+click on a tab cell only toggles it: the playhead and the score cursor stay
            else if (_pressBar >= 0 && _pressBar < _view.BarCount) BarSeekRequested?.Invoke(_pressBar);
            if (_dragSlot < 0 && !(_pressOnRuler && alt)) _selection.Batch(() => ClickSelect(clip, clipBar, ctrl));   // the click's changes reach the views as one
        }
        else if (gesture == Gesture.SectionDrag && DropSection()) return;
        else if (gesture == Gesture.RowDrag) DropRow();
        ClearDrag();
        if (source == _view) ApplyHover(e.GetPosition(_view));
    }

}
