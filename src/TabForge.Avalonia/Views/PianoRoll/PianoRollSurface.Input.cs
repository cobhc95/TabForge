using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: the piano roll surface's pointer gestures: per tool (Select: click, Shift-click adds, Ctrl-click toggles, box select (Shift adds, Ctrl
//       toggles), move, Ctrl+drag copy, Alt-drag velocity, edge resize, double-click to add; Draw: click adds a note of the snap length, drag sets
//       its length; Erase: click deletes one note, a box deletes the notes in it (Shift adds the selection, Ctrl toggles) as one step; Split: click cuts at the snap point; Mute:
//       click toggles), the ruler (click moves the playhead, drag makes a loop), the keys (click plays), the velocity lane and its divider, and the
//       wheel (zooms time around the pointer, Ctrl zooms the key height, Shift scrolls the keys, or with notes selected moves the shown lane's values (PianoRollSurface.LaneWheel), Alt or a tilt scrolls time). A drag keeps a preview; the edit commits on release.
// Does not own: the edits and the commit (PianoRollPresenter, PianoRollEdits), the drawing, the velocity lane gestures (PianoRollSurface.Velocity).
// Tests: TabForge.Avalonia.Tests.PianoRollViewTests.
public sealed partial class PianoRollSurface
{
    private enum Gesture { None, Pan, PlayheadDrag, Move, ResizeEnd, ResizeStart, Marquee, Lasso, Draw, Erase, SplitLine, GluePaint, MutePaint, Velocity, VelocityTool, Ruler, LoopEdge, LoopMove, Divider, Key, Controller }

    private const double DragThreshold = 3;
    private const double WheelRows = 3;
    private const double WheelBeats = 2;
    private const int DefaultVelocity = 100;

    private Gesture _gesture;
    private KeyModifiers _pressModifiers;
    private KeyModifiers _dragModifiers;
    private bool _pending;   // a commit waits for the tab prompt: the preview stays and new gestures wait
    private bool _moved;
    private bool _copy;
    private bool _alt;
    private Point _press;
    private Point _now;
    private double _pressBeat;
    private double _dragBeat;
    private int _pressPitch;
    private int _pressNote = -1;
    private double _dBeats;
    private int _dPitch;
    private double _drawEnd;
    private int _keyDown = -1;
    private int _keyHover = -1;   // the piano key under the pointer (hover tint)
    private bool _laneHeaderHover;   // the pointer is over the lane header (hover fill, as the toolbar buttons)
    private int _lastAuditioned = -1;
    private readonly HashSet<int> _erased = new();
    private readonly Dictionary<int, int> _velocityPreview = new();
    private (double Beat, int Pitch, int Index)? _hover;
    private double _hoverBeat = double.NaN;   // the pointer's own beat over the grid (the readout's position), NaN off the grid

    /// <summary>True while a gesture or a commit is in progress (the host holds its reloads until it ends).</summary>
    internal bool Busy => _gesture != Gesture.None || _pending;

    private void EndGesture()
    {
        _rightBox = false;
        _gesture = Gesture.None;
        _moved = false;
        _copy = false;
        _pressNote = -1;
        _dBeats = 0;
        _dPitch = 0;
        _keyDown = -1;
        _lastAuditioned = -1;
        _keyAudition = false;
        _erased.Clear();
        UpdatePreview();   // a finished gesture may have set the anchor or the selection: the modifier preview follows
        ClearSelectState();
        _velocityPreview.Clear();
        _items = null;
    }

    private void Sound(int pitch)
    {
        if (AuditionOn) Audition?.Invoke(pitch);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        FlushLaneWheel();   // a press ends a Shift+wheel burst: its values commit first
        var point = e.GetCurrentPoint(this);
        if (_pending) return;
        if (point.Properties.IsRightButtonPressed)
        {
            PrepareMenu(point.Position);
            if (PressRight(point.Position, e.KeyModifiers)) e.Pointer.Capture(this);   // a right drag is a box selection; a right click is the menu
            return;
        }
        var middle = point.Properties.IsMiddleButtonPressed;
        if (!middle && !point.Properties.IsLeftButtonPressed) return;
        if (Press(point.Position, e.KeyModifiers, e.ClickCount, middle)) { e.Pointer.Capture(this); e.Handled = true; }
    }

    /// <summary>A left press at <paramref name="p"/> (the pointer handler and tests): remembers where it went down and starts the gesture there.</summary>
    internal bool Press(Point p, KeyModifiers modifiers, int clicks = 1, bool middle = false)
    {
        if (_pending) return false;
        FlushLaneWheel();
        _press = _now = p;
        _alt = modifiers.HasFlag(KeyModifiers.Alt);
        _dragModifiers = modifiers;
        _pressBeat = _dragBeat = BeatAt(p.X);
        _pressPitch = PitchAt(p.Y);
        _lastInserted = clicks >= 2 ? _lastInserted : null;
        var started = middle ? InRuler(p) && PressRuler(p, middle: true) || StartPan() : PressAt(p, modifiers, clicks);
        InvalidateVisual();
        return started;
    }

    /// <summary>A right press in the grid (the pointer handler and tests): a drag from here draws the selection box (Shift adds, Ctrl toggles); a release
    /// without a drag leaves the selection alone and the menu opens.</summary>
    internal bool PressRight(Point p, KeyModifiers modifiers)
    {
        if (_pending) return false;
        if (InKeys(p)) return PressKeyRight(p, modifiers);   // the key strip: selects only (no box, no menu)
        if (!InGrid(p)) return false;
        _press = _now = p;
        _dragModifiers = modifiers;
        _pressBeat = _dragBeat = BeatAt(p.X);
        _gesture = Gesture.Marquee;
        if (modifiers.HasFlag(KeyModifiers.Alt)) { _gesture = Gesture.Lasso; _lasso.Clear(); _lasso.Add(p); }   // Alt: a freehand lasso
        _rightBox = true;
        SuppressMenu = false;
        return true;
    }

    /// <summary>True when the last right press became a box drag: the menu that the release asks for is not opened.</summary>
    internal bool SuppressMenu { get; set; }

    private bool _rightBox;   // the box selection in progress was started with the right button

    /// <summary>Starts the gesture of the area and tool under <paramref name="p"/>; false when nothing starts.</summary>
    private bool PressAt(Point p, KeyModifiers modifiers, int clicks = 1)
    {
        if (OnDivider(p)) { _gesture = Gesture.Divider; return true; }
        if (InLaneHeader(p))
        {
            LaneShown = !LaneShown;
            LaneChanged?.Invoke();
            InvalidateVisual();
            return false;
        }
        if (InLane(p)) return LaneId == PianoRollControllers.Velocity ? PressLane(p) : PressController(p);
        if (InRuler(p)) return PressRuler(p, middle: false);
        if (InKeys(p)) return PressKeyLeft(p);   // a left press auditions only (a right press selects: PressKeyRight)
        if (!InGrid(p)) return false;
        var (index, part) = HitNote(p);
        var tool = Presenter.Tool;   // the Edit tool (PianoRollTool.Select) draws on empty grid and edits notes; the others are optional tools
        switch (tool)
        {
            case PianoRollTool.Erase:
                _gesture = Gesture.Erase;   // a click erases the note under it; a drag erases the notes in its box (Shift adds the selection, Ctrl toggles)
                _pressNote = index;
                return true;
            case PianoRollTool.Split:
                return StartSplit(p, index);
            case PianoRollTool.Mute:
                return StartMute(p, index);
            case PianoRollTool.Glue:
                _pressNote = index;
                return StartGlue(p, index);
            // Retired 26 Oct (tombstone): the Velocity tool (key 7). An Alt-drag on a note does its job in the Select tool (below), and the velocity
            // lane's bars are dragged directly.
            case PianoRollTool.Draw or PianoRollTool.Select when index < 0:   // the Write tool (the pencil; Select) writes: press, hold and drag sets the note's length and pitch
                return StartDraw();
        }
        // Retired 26 Oct (tombstone): a left drag on empty grid drew the selection box (now the right button's); a double-click drew a note.
        if (index < 0 && tool == PianoRollTool.Pointer)   // the Select tool: drag a box (Alt = lasso); a click clears; a double-click adds a note; never a drag-write
            return clicks >= 2 ? StartDraw() : StartSelectBox(p, modifiers);
        if (index < 0) return StartDraw();
        if (clicks >= 2 && _lastInserted is var (insertedAt, insertedPitch) && Presenter.Notes[index].Pitch == insertedPitch
            && Math.Abs(Presenter.Notes[index].Start - insertedAt) < 1e-6) return false;   // a double-click on empty grid: its first click made this note
        if (clicks >= 2 && part == NotePart.Body)   // a double-click on a note deletes it (the DAW convention)
        {
            Run(Presenter.CommitAsync(PianoRollEdits.Delete(Presenter.Notes, new[] { index }), PianoRollEditKind.Notes, "Delete note"));
            return false;
        }
        _pressModifiers = modifiers;
        if (modifiers.HasFlag(KeyModifiers.Alt) || part == NotePart.Top)   // Alt-drag or the top edge: its velocity (the selection's, when it is selected)
        {
            if (!Presenter.Selected.Contains(index)) Presenter.Select(new[] { index });
            SelectionChanged?.Invoke();
            _gesture = Gesture.VelocityTool;
            _pressNote = index;
            return true;
        }
        if (modifiers.HasFlag(KeyModifiers.Shift)) Presenter.Select(new[] { index }, add: true);   // Shift-click adds
        else if (!modifiers.HasPrimary() && !Presenter.Selected.Contains(index)) Presenter.Select(new[] { index });   // Ctrl: toggled on release
        SelectionChanged?.Invoke();
        _pressNote = index;
        _copy = modifiers.HasPrimary() && part == NotePart.Body;
        _gesture = part switch { NotePart.End => Gesture.ResizeEnd, NotePart.Start => Gesture.ResizeStart, _ => Gesture.Move };
        Sound(Presenter.Notes[index].Pitch);
        _lastAuditioned = Presenter.Notes[index].Pitch;
        return true;
    }

    private (double Start, int Pitch)? _lastInserted;   // the note the last click inserted (a double-click on empty grid keeps it)
    private (double X, double Y) _panFrom;

    /// <summary>The length of the last drawn note (the Length dropdown's "Last used").</summary>
    internal double LastLength { get; private set; }

    /// <summary>The Length dropdown's choice for new notes (PianoRollSnap.Lengths).</summary>
    internal string NoteLengthChoice { get; set; } = "Grid";

    internal double NewLength(double beat) => PianoRollSnap.NewLength(Presenter.Map, Snap(), beat, NoteLengthChoice, LastLength);

    /// <summary>A middle drag outside the ruler's loop pans the view (the timeline follows sideways).</summary>
    private bool StartPan()
    {
        _gesture = Gesture.Pan;
        _panFrom = (ScrollX, ScrollY);
        return true;
    }

    private double SplitBeat(double beat) => SnapBeat(beat, Array.Empty<int>(), _alt);

    private bool StartDraw()
    {
        _gesture = Gesture.Draw;
        _pressPitch = LockedPitch(_pressPitch);
        _pressBeat = PianoRollSnap.Floor(Presenter.Map, Snap(), _pressBeat, _alt);
        _drawEnd = _pressBeat + NewLength(_pressBeat);
        Sound(_pressPitch);
        return true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_gesture == Gesture.None) { HoverAt(p, e.KeyModifiers); return; }
        _alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt) && _gesture != Gesture.VelocityTool;
        _dragModifiers = e.KeyModifiers;
        DragTo(p);
    }

    /// <summary>True while the pointer is over the lane header (a test reads it).</summary>
    internal bool LaneHeaderHovered => _laneHeaderHover;

    /// <summary>The pointer moved with no button: the split line or draw ghost follows it, and the resize cursor shows over a note edge or the divider.</summary>
    internal void HoverAt(Point p, KeyModifiers modifiers = default)
    {
        (double, int, int)? hover = null;
        _hoverAlt = modifiers.HasFlag(KeyModifiers.Alt);
        UpdateKeyHover(p, modifiers);   // the key strip's hover and modifier preview (repaints only on change)
        var header = InLaneHeader(p);   // repaints only when the header hover changes, never per frame
        if (header != _laneHeaderHover) { _laneHeaderHover = header; InvalidateVisual(); }
        if (InGrid(p))
        {
            var (index, part) = HitNote(p);
            var beat = Presenter.Tool == PianoRollTool.Split ? SnapBeat(BeatAt(p.X), Array.Empty<int>(), false) : PianoRollSnap.Floor(Presenter.Map, Snap(), BeatAt(p.X), false);
            hover = (beat, PitchAt(p.Y), index);
            var editing = Presenter.Tool is PianoRollTool.Select or PianoRollTool.Pointer;
            Cursor = index >= 0 && editing && part is NotePart.Start or NotePart.End ? ResizeX
                : index >= 0 && editing && part == NotePart.Top || OnDivider(p) ? ResizeY : null;
        }
        else Cursor = OnDivider(p) ? ResizeY : null;
        var rawBeat = InGrid(p) ? BeatAt(p.X) : double.NaN;
        var beatMoved = !double.IsNaN(rawBeat) && Math.Abs(rawBeat - _hoverBeat) * PixelsPerBeat >= 1;
        if (beatMoved || double.IsNaN(rawBeat) != double.IsNaN(_hoverBeat)) { _hoverBeat = rawBeat; InvalidateVisual(); }   // the readout's position
        UpdateOverlays(p, hover is var (_, _, over) && over >= 0);
        if (hover == _hover) return;
        var otherNote = hover?.Item3 != _hover?.Index || hover?.Item2 != _hover?.Pitch;   // the readout follows the note and the pitch
        _hover = hover;
        if (otherNote || Presenter.Tool is PianoRollTool.Split or PianoRollTool.Draw) InvalidateVisual();
    }

    private static readonly Cursor ResizeX = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor ResizeY = new(StandardCursorType.SizeNorthSouth);

    /// <summary>A drag to <paramref name="p"/>: moves the gesture's preview only (no song change until release).</summary>
    internal void DragTo(Point p)
    {
        _now = p;
        if (!_moved && Math.Abs(p.X - _press.X) < DragThreshold && Math.Abs(p.Y - _press.Y) < DragThreshold) return;
        _moved = true;
        _dragBeat = BeatAt(p.X);
        switch (_gesture)
        {
            case Gesture.Move: DragMove(p); break;
            case Gesture.ResizeEnd:
            case Gesture.ResizeStart: DragResize(); break;
            case Gesture.Draw:
                _drawEnd = Math.Max(_pressBeat + PianoRollEdits.MinLength, SnapBeat(_dragBeat, Array.Empty<int>(), _alt));
                var drawPitch = LockedPitch(PitchAt(p.Y));   // up and down changes the drawn note's pitch
                if (drawPitch != _pressPitch) { _pressPitch = drawPitch; Sound(drawPitch); }
                break;
            case Gesture.Marquee when _rightBox: SuppressMenu = true; break;
            case Gesture.Marquee or Gesture.Lasso: if (_rightBox) SuppressMenu = true; DragSelectBox(p); break;
            case Gesture.SplitLine: DragSplit(p); break;
            case Gesture.GluePaint: DragGlue(p); break;
            case Gesture.MutePaint: DragMute(p); break;
            case Gesture.Erase: EraseBox(_dragModifiers); break;
            case Gesture.Velocity: DragLane(p); break;
            case Gesture.Pan:
                ScrollTo(_panFrom.X - (p.X - _press.X), _panFrom.Y - (p.Y - _press.Y));
                UserScrolled?.Invoke();
                break;
            case Gesture.VelocityTool: DragVelocityTool(p); break;
            case Gesture.Controller: _dragBeat = BeatAt(p.X); break;
            case Gesture.Ruler or Gesture.LoopEdge or Gesture.LoopMove: DragRuler(p); break;
            case Gesture.PlayheadDrag: DragPlayhead(p); return;   // only the playhead layer repaints
            case Gesture.Divider: Lane = Math.Clamp(Bounds.Height - p.Y - LaneHeaderSize, MinLane, Math.Max(MinLane, Bounds.Height * MaxLaneShare)); break;
            case Gesture.Key when _keyAudition:   // press-and-hold sounds while held (a right press only selects)
                var pitch = PitchAt(p.Y);
                if (pitch != _keyDown) { _keyDown = pitch; Sound(pitch); }
                break;
        }
        InvalidateVisual();
    }

    /// <summary>The notes an Erase drag would delete: those in its box, with the selection added (Shift) or toggled against the box (Ctrl).</summary>
    private int EraseBox(KeyModifiers modifiers)
    {
        var box = MarqueeRect();
        _erased.Clear();
        foreach (var i in Combine(PianoRollEdits.InBox(Presenter.Notes, BeatAt(box.Left), BeatAt(box.Right), PitchAt(box.Bottom), PitchAt(box.Top)), modifiers)) _erased.Add(i);
        return _erased.Count;
    }

    /// <summary>Explorer-style: the box alone, the selection plus the box (Shift), or the selection with the box's notes toggled (Ctrl).</summary>
    private IEnumerable<int> Combine(List<int> inBox, KeyModifiers modifiers)
    {
        if (modifiers.HasPrimary()) return Presenter.Selected.Except(inBox).Concat(inBox.Except(Presenter.Selected)).ToList();
        if (modifiers.HasFlag(KeyModifiers.Shift)) return Presenter.Selected.Union(inBox).ToList();
        return inBox;
    }

    private void DragMove(Point p)
    {
        if (_copy && !Presenter.Selected.Contains(_pressNote)) { Presenter.Select(new[] { _pressNote }, add: true); SelectionChanged?.Invoke(); }
        var note = Presenter.Notes[_pressNote];
        var start = SnapBeat(note.Start + (_dragBeat - _pressBeat), Presenter.Selected, _alt);
        _dBeats = start - note.Start;
        _dPitch = PitchAt(p.Y) - _pressPitch;
        var sounding = PianoRollEdits.ClampPitch(note.Pitch + _dPitch);
        if (sounding != _lastAuditioned) { _lastAuditioned = sounding; Sound(sounding); }
    }

    private void DragResize()
    {
        if (!Presenter.Selected.Contains(_pressNote)) { Presenter.Select(new[] { _pressNote }, add: true); SelectionChanged?.Invoke(); }
        var note = Presenter.Notes[_pressNote];
        var edge = _gesture == Gesture.ResizeEnd ? note.End : note.Start;
        _dBeats = SnapBeat(edge + (_dragBeat - _pressBeat), Presenter.Selected, _alt) - edge;
    }

    /// <summary>The selected notes where the drag would leave them (the move or the resize applied).</summary>
    private IEnumerable<PianoRollNote> DragPreview()
    {
        var result = PreviewResult();
        return result.Selected.Select(i => result.Notes[i]);
    }

    private PianoRollEditResult PreviewResult() => _gesture switch
    {
        Gesture.Move => PianoRollEdits.Move(Presenter.Notes, Presenter.Selected, _dBeats, _dPitch, _copy),
        Gesture.ResizeEnd => PianoRollEdits.Resize(Presenter.Notes, Presenter.Selected, _dBeats),
        Gesture.ResizeStart => PianoRollEdits.Resize(Presenter.Notes, Presenter.Selected, _dBeats, start: true),
        _ => new PianoRollEditResult(Presenter.Notes.ToList(), new List<int>()),
    };

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_gesture == Gesture.None) { e.Pointer.Capture(null); return; }   // an Escape-cancelled gesture: just let go
        // The gesture ends (and commits) before the pointer is released: releasing the capture first raised CaptureLost, which dropped the
        // gesture, so a drawn note, a box selection or a move vanished on button up in the app (tests that call Release never saw it).
        Release(e.KeyModifiers);
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_gesture != Gesture.None && !_pending) { EndGesture(); InvalidateVisual(); }
    }

    /// <summary>The button went up: the gesture's one edit is committed (one undo step), or the click's selection, seek or loop applies.</summary>
    internal void Release(KeyModifiers modifiers)
    {
        var gesture = _gesture;
        switch (gesture)
        {
            case Gesture.Move when _moved && (_dBeats != 0 || _dPitch != 0 || _copy):
                Run(Presenter.CommitAsync(PreviewResult(), PianoRollEditKind.Notes, _copy ? "Copy notes" : "Move notes"));
                return;
            case Gesture.ResizeEnd or Gesture.ResizeStart when _moved && _dBeats != 0:
                Run(Presenter.CommitAsync(PreviewResult(), PianoRollEditKind.Notes, "Resize notes"));
                return;
            case Gesture.Draw:
                var drawn = new PianoRollNote(_pressBeat, _drawEnd - _pressBeat, _pressPitch, DefaultVelocity, Voice: DrawVoice);
                LastLength = drawn.Length;
                _lastInserted = _moved ? null : (drawn.Start, drawn.Pitch);   // a click's note: the double-click's second click keeps it
                Run(Presenter.CommitAsync(PianoRollEdits.AddMany(Presenter.Notes, PianoRollEdits.Chord(drawn, Chord)), PianoRollEditKind.Notes, Chord == "Off" ? "Add note" : "Add chord"));
                return;
            case Gesture.Controller:
                Run(CommitController());
                return;
            case Gesture.Erase when _moved && EraseBox(modifiers) > 0:   // the keys held at release decide (Shift adds, Ctrl toggles)
                Run(Presenter.CommitAsync(PianoRollEdits.Delete(Presenter.Notes, _erased.ToList()), PianoRollEditKind.Notes, "Erase notes"));
                return;
            case Gesture.Erase when !_moved && _pressNote >= 0:   // a plain click erases the one note under it
                Run(Presenter.CommitAsync(PianoRollEdits.Delete(Presenter.Notes, new[] { _pressNote }), PianoRollEditKind.Notes, "Erase note"));
                return;
            case Gesture.Move or Gesture.ResizeEnd or Gesture.ResizeStart when !_moved:   // a click: Ctrl toggles the note, a plain click keeps only it
                if (_pressModifiers.HasPrimary()) Presenter.Toggle(_pressNote);
                else if (!_pressModifiers.HasFlag(KeyModifiers.Shift)) Presenter.Select(new[] { _pressNote });
                SelectionChanged?.Invoke();
                break;
            case Gesture.Velocity or Gesture.VelocityTool when _velocityPreview.Count > 0:
                Run(Presenter.CommitAsync(VelocityResult(), PianoRollEditKind.Expression, "Set velocity"));
                return;
            case Gesture.Marquee when _moved:
                var box = MarqueeRect();
                var inBox = PianoRollEdits.InBox(Presenter.Notes, BeatAt(box.Left), BeatAt(box.Right), PitchAt(box.Bottom), PitchAt(box.Top));
                Presenter.Select(Combine(inBox, modifiers | _dragModifiers));   // Shift adds the box, Ctrl toggles it (Explorer-style)
                SelectionChanged?.Invoke();
                // Retired 26 Oct (tombstone): a box selection set the loop to its span. In the piano roll a selection never touches the loop;
                // loops come only from the rulers (the score's drag selection still sets it).
                break;
            case Gesture.Lasso when _moved:
                ReleaseSelectBox(modifiers);
                break;
            case Gesture.Marquee or Gesture.Lasso when !_moved && !_rightBox && Presenter.Tool == PianoRollTool.Pointer:   // the Select tool: a click on empty grid writes nothing and clears the selection
                if (!modifiers.HasFlag(KeyModifiers.Shift) && !modifiers.HasPrimary() && Presenter.Selected.Count > 0) { Presenter.ClearSelection(); SelectionChanged?.Invoke(); }
                Seek?.Invoke(SnapBeat(_pressBeat, Array.Empty<int>(), _alt));   // and the playhead goes there, as a click on the timeline does
                break;
            case Gesture.SplitLine:
                ReleaseSplit();
                return;
            case Gesture.GluePaint:
                ReleaseGlue();
                return;
            case Gesture.MutePaint:
                ReleaseMute();
                return;
            case Gesture.PlayheadDrag:
                ReleasePlayhead();
                break;
            case Gesture.Ruler or Gesture.LoopEdge or Gesture.LoopMove:
                ReleaseRuler();
                break;
            case Gesture.Divider:
                LaneChanged?.Invoke();
                break;
        }
        EndGesture();
        InvalidateVisual();
    }

    private Rect MarqueeRect() => new(new Point(Math.Min(_press.X, _now.X), Math.Min(_press.Y, _now.Y)), new Point(Math.Max(_press.X, _now.X), Math.Max(_press.Y, _now.Y)));

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var p = e.GetPosition(this);
        var ctrl = e.KeyModifiers.HasPrimary();
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        Wheel(delta, p, ctrl, shift, alt || e.Delta.X != 0 && e.Delta.Y == 0);   // a sideways tilt or touchpad swipe scrolls sideways
        e.Handled = true;
    }

    /// <summary>The wheel over the piano roll: plain zooms time around the pointer, Ctrl zooms the key height around the pointer, Shift (or the plain wheel over the key strip) scrolls the
    /// keys up and down, Alt (or a sideways tilt) scrolls sideways (the timeline follows). Each step only sets the view and invalidates (one paint per frame).</summary>
    internal void Wheel(double delta, Point p, bool ctrl, bool shift, bool alt)
    {
        if (delta == 0) return;
        if (shift && !alt && LaneWheel(delta, ctrl)) return;   // notes selected: Shift moves the shown lane's values (PianoRollSurface.LaneWheel.cs); else it scrolls
        if (p.X < GridLeft && !ctrl && !alt) shift = true;   // over the key strip (and its corner readout) the plain wheel scrolls up and down
        if (shift) ScrollTo(ScrollX, ScrollY - delta * WheelRows * RowPx);
        else if (alt) ScrollTo(ScrollX - delta * WheelBeats * PixelsPerBeat, ScrollY);
        else if (ctrl) ZoomPitch(delta > 0 ? ZoomStep : 1 / ZoomStep, p.Y);
        else ZoomTime(delta > 0 ? ZoomStep : 1 / ZoomStep, p.X);
        if (!shift && !ctrl) UserScrolled?.Invoke();   // the left edge's time changed: the timeline follows
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ClearKeyHover();
        if (_laneHeaderHover) { _laneHeaderHover = false; InvalidateVisual(); }
        _hoverBeat = double.NaN;   // off the grid: the split key falls back to the playhead
        _pointerIn = false;
        _ghostOn = false;
        RepaintIfOverlayMoved(_overlayBounds);
        if (_hover is null) return;
        _hover = null;
        InvalidateVisual();
    }
}
