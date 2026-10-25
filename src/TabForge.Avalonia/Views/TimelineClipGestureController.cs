using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Features.Recording.Model;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the drag of an audio clip on the arrangement grid. A press on a clip's body moves it in time and to the lane or track under the
//       pointer (only a track of the same kind); a press within the edge grip trims that edge; a press on a fade handle sets that fade; a move into another track's lane sets TimelineView.ClipDropLane. The drag previews the time and trim on the
//       clip, snapped to the targets of TimelineClipSnap unless Alt is held; release puts the clip back where the press found it and applies
//       the final state as one DocumentEdits edit (moving it to its track and lane too), so one undo step returns to the state before the press.
// Does not own: the press and the selection click (TimelineInputController), the snap targets (TimelineClipSnap), the drawing of the drop lane (TimelineAudioClips),
//       the song growing to hold a trimmed end (WPF ClipGestureController and ClipEditController), the move itself (ClipRelocation),
//       and the clip drawing (TimelineAudioClips).
// Tests: TabForge.Avalonia.Tests.TimelineAudioClipTests.
internal sealed class TimelineClipGestureController
{
    /// <summary>How close to a clip's edge, in DIPs, a press trims rather than moves it (the WPF clip edge grip).</summary>
    private const double EdgeGripDip = 6;

    private readonly TimelineView _view;
    private readonly Func<double, double, TimelineClipSelection, RepeatPosition>? _resolvePosition;
    private TimelineClipSelection? _pressed;
    private ClipGesture _gesture;
    private (double Start, double Offset, double Length) _origin;
    private double _originEnd;
    private (double In, double Out) _originFades;
    private int _originLane;
    private (TrackModel Track, int Lane) _target;
    private double _pressSec;
    private bool _changed;
    private readonly List<(AudioClip Clip, double Start)> _others = new();   // the other selected clips a move carries along, with their starts at the press

    public TimelineClipGestureController(TimelineView view, Func<double, double, TimelineClipSelection, RepeatPosition>? resolvePosition = null)
    {
        _view = view;
        _resolvePosition = resolvePosition;
    }

    /// <summary>Remembers the clip under a press on the grid, the lane edit cursor it leaves, and the gesture its position starts; nothing is changed yet.</summary>
    public void Press(bool onGrid, Point point)
    {
        _pressed = onGrid ? TimelineAudioClips.ClipAt(_view, point) : null;
        if (_pressed is not { } pressed) return;
        // A pass lives in its own sub-lane: its edges and fade band are its drawn box, not the main lane.
        var box = TimelineAudioClips.ClipBounds(_view, pressed.Track, pressed.Clip);
        var left = box.X;
        var right = box.Right;
        _pressSec = _view.SecondsOfX(point.X);
        // The pass under the press (a folded clip's slice), so Split uses the performed time the click showed, not the bar's first play.
        var position = _resolvePosition?.Invoke(point.X, point.Y, pressed);
        _view.LaneCursor = new TimelineLaneCursor(pressed.Track, pressed.Clip.Lane, _pressSec, position);
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
        _origin = (clip.StartSec, clip.OffsetSec, clip.SourceLengthSec);
        _originEnd = clip.EndSec;
        _originFades = (clip.FadeInSec, clip.FadeOutSec);
        _originLane = clip.Lane;
        _target = (pressed.Track, clip.Lane);
        _changed = false;
        // A move keeps the fold it starts with for the whole drag (re-folded once on release), so the preview cannot flicker at a repeat edge.
        _view.DragFold = _gesture == ClipGesture.Move ? (clip, clip.StartSec, TimelineAudioClips.FoldOf(_view, clip)) : null;
        _view.PastEnd = _gesture is ClipGesture.Move or ClipGesture.TrimEnd;
        // Pressing a clip of a multi-selection moves them all together (sideways, each keeping its track and lane); any other press selects the pressed clip alone.
        _others.Clear();
        if (_gesture == ClipGesture.Move && _view.IsClipSelected(clip) && !TakeGroups.IsGrouped(clip))
            foreach (var selected in _view.SelectedClips)
                if (!ReferenceEquals(selected.Clip, clip) && !TakeGroups.IsGrouped(selected.Clip)) _others.Add((selected.Clip, selected.Clip.StartSec));
        if (_others.Count == 0) _view.SelectedClip = pressed;
        return true;
    }

    /// <summary>Moves the clip under the drag to the pointer: the preview, applied to the clip until release. Alt held: no snapping.</summary>
    public void Update(Point point, bool altHeld)
    {
        if (_pressed is not { } pressed) return;
        var clip = pressed.Clip;
        var deltaSec = _view.SecondsOfX(point.X) - _pressSec;
        switch (_gesture)
        {
            case ClipGesture.Move:
                // A pass clip moves horizontally only: its lane and its group stay exactly where they were.
                clip.StartSec = SnappedMoveStart(clip, ClipGesturePresenter.MovedStart(_origin.Start, deltaSec), altHeld);
                if (_others.Count > 0) MoveOthers(clip);
                else if (!TakeGroups.IsGrouped(clip)) AimAt(point, pressed.Track);
                break;
            case ClipGesture.TrimStart:
                ClipTrim.TrimStart(clip, _origin, SnapSecond(_origin.Start + deltaSec, clip, altHeld));
                break;
            case ClipGesture.TrimEnd:
                ClipTrim.TrimEnd(clip, _origin.Start, SnapSecond(_originEnd + deltaSec, clip, altHeld));
                break;
            case ClipGesture.FadeIn:
                ClipSplitGlue.SetFades(clip, _view.SecondsOfX(point.X) - clip.StartSec, null);
                break;
            case ClipGesture.FadeOut:
                ClipSplitGlue.SetFades(clip, null, clip.EndSec - _view.SecondsOfX(point.X));
                break;
            default:
                return;
        }
        _changed = true;
        _view.GhostBars = GhostBarsFor(clip);
        _view.InvalidateVisual();
    }

    // The other selected clips keep their distance from the moved one; none goes before the song start (the whole group stops there).
    private void MoveOthers(AudioClip moved)
    {
        var floor = -_others.Min(other => other.Start);
        var shift = Math.Max(moved.StartSec - _origin.Start, floor);
        moved.StartSec = _origin.Start + shift;
        foreach (var (other, start) in _others) other.StartSec = Math.Max(0, start + shift);
    }

    /// <summary>The empty bars to draw after the song's last one while the dragged clip runs past it (one more than it needs, so the drag can go on); 0 when it fits.</summary>
    private int GhostBarsFor(AudioClip clip)
    {
        var (songEnd, barSec) = SongExtent.Measure(_view.Song);
        var over = clip.EndSec - songEnd;
        if (over <= 1e-6 || barSec <= 0) return 0;
        return Math.Clamp((int)Math.Ceiling(over / barSec) + 1, 0, Math.Max(0, InputLimits.MaxMeasuresPerTrack - _view.BarCount));
    }

    /// <summary>Ends the drag on release. The clip goes back to the press and the final state is applied as one edit; true when the song changed.</summary>
    public bool Commit(IEditableDocument document)
    {
        _view.ClipDropLane = null;
        _view.DragFold = null;
        _view.GhostBars = 0;
        _view.PastEnd = false;
        var pressed = _pressed;
        var hadDrag = _changed;
        _pressed = null;
        var othersFinal = _others.Select(other => (other.Clip, other.Clip.StartSec)).ToList();
        if (pressed is null || !hadDrag) { _others.Clear(); return false; }
        var clip = pressed.Clip;
        (double Start, double Offset, double Length) final = (clip.StartSec, clip.OffsetSec, clip.SourceLengthSec);
        (double In, double Out) finalFades = (clip.FadeInSec, clip.FadeOutSec);
        var (targetTrack, targetLane) = _target;
        // A grouped clip never leaves its lane or track, however far the pointer travels.
        var relocate = !TakeGroups.IsGrouped(clip) && (targetTrack != pressed.Track || targetLane != _originLane);
        Restore(clip);
        foreach (var (other, start) in _others) other.StartSec = start;
        _others.Clear();
        var fit = default(AutoFitResult);
        var changed = DocumentEdits.Run(document, project =>
        {
            if (final == _origin && finalFades == _originFades && !relocate) return false;
            clip.StartSec = final.Start;
            foreach (var (other, start) in othersFinal) other.StartSec = start;
            clip.OffsetSec = final.Offset;
            clip.SourceLengthSec = final.Length;
            ClipSplitGlue.SetFades(clip, finalFades.In, finalFades.Out);
            if (relocate) ClipRelocation.Move(pressed.Track, targetTrack, clip, targetLane);
            fit = ClipAutoBars.Fit(project);   // the song grows (or gives its auto-added bars back) in the same undo step as the move
            return true;
        }).Changed;
        if (fit.EndsInJump) _view.ClipNotice?.Invoke(ClipAutoBars.JumpNotice);
        if (changed && relocate) _view.SelectedClip = new TimelineClipSelection(targetTrack, clip);
        if (changed) _view.ClipEdited?.Invoke(document);
        if (hadDrag) ClearStalePass();
        _view.InvalidateVisual();
        return changed;
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
        return double.IsFinite(distancePx) ? _view.SecondsOfX(snappedX) : sec;
    }

    /// <summary>Chooses the track and lane under the pointer for a move. A row of another kind, or no row, keeps the clip's track and lane.</summary>
    private void AimAt(Point point, TrackModel source)
    {
        _target = (source, _originLane);
        _view.ClipDropLane = null;
        var row = _view.RowAtY(point.Y);
        if (row < 0 || row >= _view.RowCount) return;
        var track = _view.Song.Tracks[row];
        if (!ClipRelocation.CanMove(source, track)) return;
        var lanes = ClipLanes.Count(track);
        // Read the lane bands through the view: they carry the landed pass stacks, so lane N is not simply N * AudioLaneHeight.
        var lane = -1;
        for (var index = 0; index < lanes; index++)
        {
            var bandTop = _view.LaneBandTopOf(row, index);
            if (point.Y >= bandTop && point.Y < bandTop + ClipLanes.AudioLaneHeight) { lane = index; break; }
        }
        if (lane < 0) return;   // above the lanes (the notation row is not a lane): the clip keeps its track and lane, not lane 0
        _target = (track, lane);
        if (track != source) _view.ClipDropLane = (track, lane);
    }

    /// <summary>Ends the drag without an edit (the pointer capture was lost, or the tab changed): the clip goes back to the press.</summary>
    public void Cancel()
    {
        _view.ClipDropLane = null;
        _view.DragFold = null;
        _view.GhostBars = 0;
        _view.PastEnd = false;
        if (_pressed is { } pressed && _changed)
        {
            Restore(pressed.Clip);
            foreach (var (other, start) in _others) other.StartSec = start;
            _view.InvalidateVisual();
        }
        _pressed = null;
        _others.Clear();
        _changed = false;
        ClearStalePass();
    }

    private void Restore(AudioClip clip)
    {
        ClipGesturePresenter.RestoreOrigin(clip, _origin.Start, _origin.Offset, _origin.Length, _originFades.In, _originFades.Out);
    }
}
