using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: where a clip move would land vertically: the track and lane under the dragged clip centre as it follows the pointer (a lane below the last one opens a new lane),
//       the whole selection shift from there, whether every target track can take its clip, the Ctrl copy and Shift axis-lock choices, and the preview
//       the timeline draws (TimelineView.ClipDragPreview). Nothing here edits the song: the drop (Commit) does.
// Does not own: the time of the move (TimelineClipGestureController.Update), the move itself (ClipRelocation) or the drawing (TimelineAudioClips.DragPreview).
// Tests: TabForge.Avalonia.Tests.ClipVerticalDragTests.
internal sealed partial class TimelineClipGestureController
{
    /// <summary>A pull sideways or up and down smaller than this, in DIPs, is not yet a direction: a mostly vertical drag keeps the time of the clip until it is.</summary>
    private const double SidewaysDeadZoneDip = 4;

    /// <summary>Where in the last lane (0-1 of its height) the dragged clip centre opens a new lane below it.</summary>
    private const double NewLaneZone = 0.85;

    private enum Axis { None, Horizontal, Vertical }

    /// <summary>A selected clip a move carries along: its start, track row and lane at the press.</summary>
    private readonly record struct Carried(AudioClip Clip, double Start, TrackModel Track, int Row, int Lane);

    private Point _pressPoint;
    private double _bandCenterY;           // y of the middle of the pressed clip lane band at the press: the dragged clip centre follows the pointer from here
    private int _originRow, _trackCount;   // the row of the pressed clip track and how many tracks the song had at the press: an edit under the drag that changes them ends its vertical move
    private Axis _axis;
    private bool _movedX;
    private bool _copy;                    // Ctrl: the clips stay and copies land
    private int _rowShift, _laneShift;     // how far the whole selection moves, from the dragged clip target
    private bool _allowed = true;
    private bool _newTrack;                // the pointer is under the last track (the Add track strip): the drop makes a new track for the clips
    private readonly Dictionary<AudioClip, AudioClip> _aliases = new();   // a copy drag stand-ins, one per clip, for the preview
    private double _moveStart, _shiftSec;  // the start the move chose for the dragged clip, and the shift it gives every carried clip

    /// <summary>Sets up the vertical state at the start of a drag: the pressed clip row and lane band.</summary>
    private void BeginAim(TimelineClipSelection pressed)
    {
        _originRow = _view.Song.Tracks.IndexOf(pressed.Track);
        _trackCount = _view.Song.Tracks.Count;
        _bandCenterY = _view.LaneBandTopOf(_originRow, _originLane) + ClipLanes.AudioLaneHeight / 2;
        (_axis, _movedX, _copy, _rowShift, _laneShift, _allowed, _newTrack) = (Axis.None, false, false, 0, 0, true, false);
        _aliases.Clear();
        _moveStart = _origin.Start;
        _shiftSec = 0;
    }

    /// <summary>The pointer choice for a move: Ctrl copies, Shift locks the first direction it moves in, and the landing under the dragged clip is previewed.</summary>
    private void SteerMove(Point point, AudioClip clip, double deltaSec, bool altHeld, KeyModifiers keys)
    {
        _copy = keys.HasPrimary() && !TakeGroups.IsGrouped(clip);
        if (!keys.HasFlag(KeyModifiers.Shift)) _axis = Axis.None;
        else if (_axis == Axis.None) _axis = Math.Abs(point.X - _pressPoint.X) >= Math.Abs(point.Y - _pressPoint.Y) ? Axis.Horizontal : Axis.Vertical;
        _movedX |= Math.Abs(point.X - _pressPoint.X) >= SidewaysDeadZoneDip;
        var sideways = _axis != Axis.Vertical && (_movedX || Math.Abs(point.Y - _pressPoint.Y) < SidewaysDeadZoneDip);
        var start = sideways ? SnappedMoveStart(clip, ClipGesturePresenter.MovedStart(_origin.Start, deltaSec), altHeld) : _origin.Start;
        // The other selected clips keep their distance from the moved one; none goes before the song start (the whole group stops there).
        _moveStart = _others.Count == 0 ? start : _origin.Start + Math.Max(start - _origin.Start, -_others.Min(other => other.Start));
        _shiftSec = _moveStart - _origin.Start;
        // A copy leaves the clips where they were; the preview shows the copies.
        clip.StartSec = _copy ? _origin.Start : _moveStart;
        foreach (var other in _others) other.Clip.StartSec = _copy ? other.Start : Math.Max(0, other.Start + _shiftSec);
        AimAt(point, clip);
    }

    /// <summary>Chooses the track and lane under the dragged clip for a move and publishes the preview. A pass clip (take group) moves sideways only.</summary>
    private void AimAt(Point point, AudioClip clip)
    {
        (_rowShift, _laneShift, _allowed, _newTrack) = (0, 0, true, false);
        if (TracksChanged(_pressed)) { _view.ClipDragPreview = null; return; }
        if (!TakeGroups.IsGrouped(clip) && _axis != Axis.Horizontal && Landing(point) is { } hit)
        {
            var tracks = _view.Song.Tracks;
            var rows = _others.Select(other => other.Row).Append(_originRow).ToList();
            var lowestLane = _others.Select(other => other.Lane).Append(_originLane).Min();
            if (hit.Track is null)
            {
                // Under the last track (the Add track strip): the whole selection lands on a new track, the topmost lane of it on the new track first lane.
                (_newTrack, _laneShift) = (true, -lowestLane);
            }
            else
            {
                // The selection keeps its places relative to the dragged clip; nothing goes above the first track or lane.
                _rowShift = Math.Clamp(tracks.IndexOf(hit.Track) - _originRow, -rows.Min(), tracks.Count - 1 - rows.Max());
                _laneShift = Math.Max(hit.Lane - _originLane, -lowestLane);
                var track = tracks[_originRow + _rowShift];
                // A lone clip dragged to a new lane of its own track would only come back to its lane when that one closes up.
                if (_rowShift == 0 && _others.Count == 0 && _originLane + _laneShift >= _view.LaidOutLaneCountOf(track)
                    && !track.AudioClips.Any(other => !ReferenceEquals(other, clip) && other.Lane == _originLane)) _laneShift = 0;
                _allowed = rows.All(row => ClipRelocation.CanHost(tracks[row + _rowShift]));
            }
        }
        _view.ClipDragPreview = _rowShift != 0 || _laneShift != 0 || _copy || _newTrack ? GhostsFor(clip) : null;
    }

    /// <summary>True when an edit under the drag added, removed or reordered tracks since the press: the rows the gesture remembers no longer match the song.</summary>
    private bool TracksChanged(TimelineClipSelection? pressed) =>
        pressed is null || _view.Song.Tracks.Count != _trackCount || _originRow < 0 || _view.Song.Tracks.IndexOf(pressed.Track) != _originRow;

    private ClipDragPreview GhostsFor(AudioClip clip)
    {
        var tracks = _view.Song.Tracks;
        TrackModel? TargetOf(int row) => _newTrack ? null : tracks[row + _rowShift];
        var ghosts = new List<ClipDragGhost>(_others.Count + 1)
        {
            new(tracks[_originRow], clip, TargetOf(_originRow), Math.Max(0, _originLane + _laneShift), Shown(clip, _moveStart)),
        };
        foreach (var other in _others)
            ghosts.Add(new(other.Track, other.Clip, TargetOf(other.Row), Math.Max(0, other.Lane + _laneShift), Shown(other.Clip, Math.Max(0, other.Start + _shiftSec))));
        return new ClipDragPreview(ghosts, _allowed, _copy, _newTrack);
    }

    /// <summary>The clip the preview draws for <paramref name="clip"/>: the clip itself for a move (it is already at its new start); for a copy a stand-in at the copy start, kept between steps so its waveform outline is built once.</summary>
    private AudioClip Shown(AudioClip clip, double start)
    {
        if (!_copy) return clip;
        if (!_aliases.TryGetValue(clip, out var alias))
            _aliases[clip] = alias = new AudioClip { File = clip.File, Name = clip.Name, OffsetSec = clip.OffsetSec, SourceLengthSec = clip.SourceLengthSec, FileLengthSec = clip.FileLengthSec,
                GainDb = clip.GainDb, Speed = clip.Speed, FadeInSec = clip.FadeInSec, FadeOutSec = clip.FadeOutSec, Notes = clip.Notes };   // the notes are shared, never edited here
        alias.StartSec = start;
        return alias;
    }

    /// <summary>The track and lane the dragged clip centre is over (the pointer own when the centre is off the rows); a lane under the last one is a new lane; a null track is the new track under the last one (the Add track strip). Null over no row.</summary>
    private (TrackModel? Track, int Lane)? Landing(Point point)
    {
        if (_view.NewTrackDropAvailable && point.Y >= _view.NewTrackSlotTop && _view.Song.Tracks.Count > 0) return (null, 0);
        var centre = _bandCenterY + (point.Y - _pressPoint.Y);
        var row = _view.RowAtY(centre);
        if (row < 0 || row >= _view.RowCount || row >= _view.Song.Tracks.Count) { row = _view.RowAtY(point.Y); centre = point.Y; }
        if (row < 0 || row >= _view.RowCount || row >= _view.Song.Tracks.Count) return null;
        var track = _view.Song.Tracks[row];
        var lanes = _view.LaidOutLaneCountOf(track);
        if (lanes == 0) return (track, 0);   // a track with no clips: its first lane
        var height = ClipLanes.AudioLaneHeight;
        var index = (int)Math.Floor((centre - _view.LaneBandTopOf(row, 0)) / height);
        if (index < 0) return (track, 0);   // the notation band is above the lanes
        if (index >= lanes) return (track, lanes);   // under the last lane (an armed strip): a new lane
        var inside = centre - _view.LaneBandTopOf(row, index);
        return index == lanes - 1 && inside >= height * NewLaneZone ? (track, lanes) : (track, index);
    }
}
