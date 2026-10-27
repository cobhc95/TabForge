using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Features.Recording.Model;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the drag of an audio or MIDI clip on the arrangement grid. A press on a clip's body moves it in time and to the lane or track under the
//       pointer (any lane of any track but a bus, a new lane under the last one; the whole selection together, Ctrl copies, Shift locks to one direction);
//       a press within the edge grip trims that edge; a press on a fade handle sets that fade. The drag previews the time and trim on the
//       clip, snapped to the targets of TimelineClipSnap unless Alt is held, and the landing lane as ghosts (TimelineView.ClipDragPreview); release puts the clip back
//       where the press found it and applies the final state as one DocumentEdits edit (moving or copying it to its track and lane too), so one undo step returns to the state before the press.
// Does not own: the press and the selection click (TimelineInputController), the snap targets (TimelineClipSnap), the landing choice (TimelineClipGestureController.Aim),
// the drawing of the landing (TimelineAudioClips.DragPreview), the song growing to hold a trimmed end, the move itself (ClipRelocation), and the clip drawing (TimelineAudioClips).
// Tests: TabForge.Avalonia.Tests.TimelineAudioClipTests, ClipVerticalDragTests.
internal sealed partial class TimelineClipGestureController
{
    /// <summary>How close to a clip's edge, in DIPs, a press trims rather than moves it.</summary>
    private const double EdgeGripDip = 6;

    private readonly TimelineView _view;
    private readonly Func<double, double, TimelineClipSelection, RepeatPosition>? _resolvePosition;
    private TimelineClipSelection? _pressed;
    private ClipGesture _gesture;
    private (double Start, double Offset, double Length) _origin;
    private double _originEnd;
    private (double In, double Out) _originFades;
    private int _originLane;
    private double _pressSec;
    private TimelineLaneCursor? _markerBeforePress;
    private bool _changed;
    private readonly List<Carried> _others = new();   // the other selected clips a move carries along, with their starts, tracks and lanes at the press

    public TimelineClipGestureController(TimelineView view, Func<double, double, TimelineClipSelection, RepeatPosition>? resolvePosition = null)
    {
        _view = view;
        _resolvePosition = resolvePosition;
    }

    /// <summary>Remembers the clip under a press on the grid, the lane edit cursor it leaves, and the gesture its position starts; nothing is changed yet.</summary>
    public void Press(bool onGrid, Point point, bool moveMarker = true)
    {
        if (_pressed is not null && _changed) Cancel();   // a press while a drag is still previewing (a missed release) puts that clip back first, so it never stays where the preview left it
        _pressed = onGrid ? TimelineAudioClips.ClipAt(_view, point) : null;
        _pressPoint = point;
        _markerBeforePress = _view.LaneCursor;
        if (_pressed is not { } pressed) return;
        // A pass lives in its own sub-lane: its edges and fade band are its drawn box, not the main lane.
        var box = TimelineAudioClips.ClipBounds(_view, pressed.Track, pressed.Clip);
        var left = box.X;
        var right = box.Right;
        _pressSec = TimelineAudioClips.GrabSeconds(_view, pressed.Track, pressed.Clip, point);   // read in the grabbed pass: a repeat draws all its passes on the same bars
        // The pass under the press (a folded clip's slice), so Split uses the performed time the click showed, not the bar's first play.
        // The split marker sits where the magnet snaps the click (the exact pointer time with snapping off); Ctrl+click leaves it where it was.
        if (moveMarker)
        {
            var markerX = TimelineClipSnap.SnapX(_view, point.X, pressed.Clip, false, out var snapDistance);
            var markerSec = double.IsFinite(snapDistance) ? _view.SecondsOfX(markerX) : _pressSec;
            var position = _resolvePosition?.Invoke(markerX, point.Y, pressed);
            _view.LaneCursor = new TimelineLaneCursor(pressed.Track, pressed.Clip.Lane, markerSec, position);
        }
        // The top band owns the fade handles: a press there near one starts that fade, and only elsewhere in the band (or on an edge below it)
        // is the press a trim or a move. A hovered clip shows its handles, so the first press can start a fade.
        _gesture = ClipGesturePresenter.BodyGesture(point.X, left, right, EdgeGripDip);
        var row = _view.Song.Tracks.IndexOf(pressed.Track);
        var index = pressed.Track.AudioClips.IndexOf(pressed.Clip);
        if (ClipGesturePresenter.InFadeBand(point.Y, box.Y, TimelineAudioClips.FadeHandleSize) &&
            TimelineAudioClips.ShowsFadeHandles(_view, pressed.Clip, row, index))
        {
            var (inX, outX) = TimelineAudioClips.FadeHandleXs(_view, pressed.Clip);
            if (TimelineAudioClips.FoldOf(_view, pressed.Clip) is not null)
            {
                // A folded clip draws its handles clamped to its box (its fade ends can alias onto an earlier bar): hit what is shown.
                inX = Math.Clamp(inX, box.X, box.Right);
                outX = Math.Clamp(outX, box.X, box.Right);
            }
            if (ClipGesturePresenter.FadeHandleAt(point.X, inX, outX, TimelineAudioClips.FadeHandleSize) is { } fade) _gesture = fade;
        }
    }

    /// <summary>Starts the drag once the pointer has passed the drag threshold; false when the press was not on a clip.</summary>
    public bool Begin()
    {
        if (_pressed is not { } pressed) return false;
        var clip = pressed.Clip;
        _view.LaneCursor = _markerBeforePress;   // a drag does not set the split marker
        _origin = (clip.StartSec, clip.OffsetSec, clip.SourceLengthSec);
        _originEnd = clip.EndSec;
        _originFades = (clip.FadeInSec, clip.FadeOutSec);
        _originLane = clip.Lane;
        _changed = false;
        BeginAim(pressed);
        _view.PastEnd = _gesture is ClipGesture.Move or ClipGesture.TrimEnd;
        // Pressing a clip of a multi-selection moves them all together (sideways and up and down, each keeping its place relative to the pressed clip); any other press selects the pressed clip alone.
        _others.Clear();
        if (_gesture == ClipGesture.Move && _view.IsClipSelected(clip) && !TakeGroups.IsGrouped(clip))
            foreach (var selected in _view.SelectedClips)
                if (!ReferenceEquals(selected.Clip, clip) && !TakeGroups.IsGrouped(selected.Clip) && _view.Song.Tracks.IndexOf(selected.Track) is var row and >= 0)
                    _others.Add(new Carried(selected.Clip, selected.Clip.StartSec, selected.Track, row, selected.Clip.Lane));
        if (_others.Count == 0) _view.SelectedClip = pressed;
        return true;
    }

    /// <summary>
    /// Moves the clip under the drag to the pointer: the preview, applied to the clip until release. Alt held: no snapping. For a move, Ctrl held copies
    /// (the clips stay, ghosts show the copies) and Shift locks the drag to the direction it first moved in (sideways or up and down).
    /// </summary>
    public void Update(Point point, bool altHeld, KeyModifiers keys = KeyModifiers.None)
    {
        if (_pressed is not { } pressed) return;
        var clip = pressed.Clip;
        var deltaSec = _view.SecondsOfXAlong(point.X, _pressSec) - _pressSec;   // the performed-time delta, read along the pass grabbed at the press
        switch (_gesture)
        {
            case ClipGesture.Move:
                SteerMove(point, clip, deltaSec, altHeld, keys);   // a pass clip moves horizontally only: its lane and its group stay exactly where they were
                break;
            case ClipGesture.TrimStart:
                ClipTrim.TrimStart(clip, _origin, SnapSecond(_origin.Start + deltaSec, clip, altHeld));
                break;
            case ClipGesture.TrimEnd:
                ClipTrim.TrimEnd(clip, _origin.Start, SnapSecond(_originEnd + deltaSec, clip, altHeld));
                break;
            case ClipGesture.FadeIn:
                ClipSplitGlue.SetFades(clip, _view.SecondsOfXNear(point.X, clip.StartSec) - clip.StartSec, null);
                break;
            case ClipGesture.FadeOut:
                ClipSplitGlue.SetFades(clip, null, clip.EndSec - _view.SecondsOfXNear(point.X, clip.EndSec));
                break;
            default:
                return;
        }
        _changed = true;
        _view.LaneCursor = null;   // changing the clip (move, trim, fade) ends the split marker; the next click sets it again
        _view.GhostBars = GhostBarsFor(clip);
        _view.InvalidateVisual();
    }

    /// <summary>The empty bars to draw after the song's last one while the dragged clip runs past it (one more than it needs, so the drag can go on); 0 when it fits.</summary>
    private int GhostBarsFor(AudioClip clip)
    {
        var (songEnd, barSec) = SongExtent.Measure(_view.Song);
        var over = (_gesture == ClipGesture.Move ? _moveStart + clip.LengthSec : clip.EndSec) - songEnd;   // a copy drag leaves the clip itself in place
        if (over <= 1e-6 || barSec <= 0) return 0;
        return Math.Clamp((int)Math.Ceiling(over / barSec) + 1, 0, Math.Max(0, InputLimits.MaxMeasuresPerTrack - _view.BarCount));
    }

    /// <summary>Ends the drag on release. The clip goes back to the press and the final state is applied as one edit; true when the song changed.</summary>
    public bool Commit(IEditableDocument document)
    {
        _view.ClipDropLane = null;
        _view.ClipDragPreview = null;
        _view.GhostBars = 0;
        _view.PastEnd = false;
        var pressed = _pressed;
        var hadDrag = _changed;
        _pressed = null;
        _aliases.Clear();
        if (pressed is null || !hadDrag) { _others.Clear(); return false; }
        var clip = pressed.Clip;
        var moving = _gesture == ClipGesture.Move;
        (double Start, double Offset, double Length) final = (moving ? _moveStart : clip.StartSec, clip.OffsetSec, clip.SourceLengthSec);
        (double In, double Out) finalFades = (clip.FadeInSec, clip.FadeOutSec);
        var shiftSec = _shiftSec;
        var (rowShift, laneShift, copy) = moving ? (_rowShift, _laneShift, _copy) : (0, 0, false);
        // A grouped clip never leaves its lane or track, however far the pointer travels.
        var newTrack = moving && _newTrack && !TakeGroups.IsGrouped(clip);   // dropped under the last track (the Add track strip): the clips land on a new track
        var relocate = !TakeGroups.IsGrouped(clip) && (rowShift != 0 || laneShift != 0 || newTrack);
        var carried = _others.ToList();
        Restore(clip);
        foreach (var other in carried) other.Clip.StartSec = other.Start;
        _others.Clear();
        if (moving && (!_allowed || TracksChanged(pressed))) { _view.LaneCursor = null; _view.InvalidateVisual(); return false; }   // a target track that cannot take the clips, or tracks that changed under the drag: they snap back
        var tracks = _view.Song.Tracks;
        var fresh = newTrack ? NewTrackFor(document.Project, clip) : null;   // made here, added to the song inside the edit below
        TrackModel TargetOf(int row) => fresh ?? tracks[row + rowShift];
        // Every moved clip with its start, the track it is on and the track and lane it lands on; the dragged clip first.
        var moves = new List<(TrackModel Source, AudioClip Clip, double Start, TrackModel Target, int Lane)> { (pressed.Track, clip, final.Start, TargetOf(_originRow), Math.Max(0, _originLane + laneShift)) };
        foreach (var other in carried)
            moves.Add((other.Track, other.Clip, Math.Max(0, other.Start + shiftSec), TargetOf(other.Row), Math.Max(0, other.Lane + laneShift)));
        var dodge = _view.ClipOverlap is null || newTrack;   // with the overlap prompt wired a clip stays on its lane and the check below asks (clips landing together on a new track never pile up on one lane)
        var landed = new List<TimelineClipSelection>();
        var fit = default(AutoFitResult);
        var changed = DocumentEdits.Run(document, project =>
        {
            if (final == _origin && finalFades == _originFades && !relocate && !copy) return false;
            if (fresh is not null) project.Tracks.Add(fresh);
            if (copy)
            {
                foreach (var move in moves)
                {
                    var made = move.Clip.Clone();
                    ClipEditService.PlaceCopy(move.Target, made, move.Start, move.Lane, dodge);
                    landed.Add(new TimelineClipSelection(move.Target, made));
                }
            }
            else
            {
                foreach (var move in moves) move.Clip.StartSec = move.Start;
                clip.OffsetSec = final.Offset;
                clip.SourceLengthSec = final.Length;
                ClipSplitGlue.SetFades(clip, finalFades.In, finalFades.Out);
                if (relocate) ClipRelocation.MoveMany(moves.Select(move => (move.Source, move.Clip, move.Target, move.Lane)).ToList(), dodge);
                landed.AddRange(moves.Select(move => new TimelineClipSelection(relocate ? move.Target : move.Source, move.Clip)));
            }
            fit = ClipAutoBars.Fit(project);   // the song grows (or gives its auto-added bars back) in the same undo step as the move
            return true;
        }).Changed;
        if (fit.EndsInJump) _view.ClipNotice?.Invoke(ClipAutoBars.JumpNotice);
        if (changed && (relocate || copy)) _view.SelectClips(landed);
        // A move or trim can land a clip over another on its lane: the overlap check follows the drop (never the drag), then the refresh.
        var moveOrTrim = _gesture is ClipGesture.Move or ClipGesture.TrimStart or ClipGesture.TrimEnd;
        if (changed) _view.ClipEditCommitted(document, moveOrTrim ? landed.Select(selection => selection.Clip).ToList() : null);
        if (hadDrag) _view.LaneCursor = null;
        _view.InvalidateVisual();
        return changed;
    }

    /// <summary>The audio track a clip dropped under the last track lands on, named after the clip (its file name when the clip has none), as a dropped file's track is.</summary>
    private static TrackModel NewTrackFor(SongProject project, AudioClip clip)
    {
        var track = NewTrackFactory.CreateTrack(project, AddTrackLane.DropKind);
        var name = !string.IsNullOrWhiteSpace(clip.Name) ? clip.Name : System.IO.Path.GetFileNameWithoutExtension(clip.File);
        if (!string.IsNullOrWhiteSpace(name)) track.Name = name;
        return track;
    }

    /// <summary>Clears the pass of the lane edit cursor (keeps its track, lane and second): a later split with no fresh press uses the playhead.</summary>
    private void ClearStalePass()
    {
        if (_view.LaneCursor is { } existing)
            _view.LaneCursor = new TimelineLaneCursor(existing.Track, existing.Lane, existing.Sec, null);
    }

    /// <summary>A move lands where its start or its end snaps, whichever target is nearer; with no target in reach it stays where the pointer put it.</summary>
    private double SnappedMoveStart(AudioClip clip, double start, bool altHeld)
    {
        var length = _originEnd - _origin.Start;
        var byStart = SnapSecond(start, clip, altHeld, out var startDistance);
        var byEnd = Math.Max(0, SnapSecond(start + length, clip, altHeld, out var endDistance) - length);
        return endDistance < startDistance ? byEnd : byStart;
    }

    /// <summary>The song time <paramref name="sec"/> snapped for this clip; unchanged when no target is in reach.</summary>
    private double SnapSecond(double sec, AudioClip clip, bool altHeld) => SnapSecond(sec, clip, altHeld, out _);

    private double SnapSecond(double sec, AudioClip clip, bool altHeld, out double distancePx)
    {
        var snappedX = TimelineClipSnap.SnapX(_view, _view.XOfSeconds(sec), clip, altHeld, out distancePx);
        return double.IsFinite(distancePx) ? _view.SecondsOfXAlong(snappedX, sec) : sec;
    }

    /// <summary>Ends the drag without an edit (the pointer capture was lost, or the tab changed): the clip goes back to the press.</summary>
    public void Cancel()
    {
        _view.ClipDropLane = null;
        _view.ClipDragPreview = null;
        _view.GhostBars = 0;
        _view.PastEnd = false;
        if (_pressed is { } pressed && _changed)
        {
            Restore(pressed.Clip);
            foreach (var other in _others) other.Clip.StartSec = other.Start;
            _view.InvalidateVisual();
        }
        _pressed = null;
        _others.Clear();
        _aliases.Clear();
        _newTrack = false;
        _changed = false;
        ClearStalePass();
    }

    private void Restore(AudioClip clip)
    {
        ClipGesturePresenter.RestoreOrigin(clip, _origin.Start, _origin.Offset, _origin.Length, _originFades.In, _originFades.Out);
    }
}
