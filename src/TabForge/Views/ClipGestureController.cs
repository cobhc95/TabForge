using System.Windows;
using System.Windows.Input;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>What the clip gestures need from the timeline that hosts them (implemented by <see cref="TrackTimeline"/>).</summary>
internal interface IClipGestureHost
{
    SongProject? Project { get; }
    AudioClip? SelectedClip { get; set; }
    double VerticalScrollOffset { get; }
    double MeasureWidth { get; }
    Cursor Cursor { get; set; }
    bool IsMouseCaptured { get; }
    DropPreview? CurrentDropPreview { get; }
    bool Focus();
    bool CaptureMouse();
    void ReleaseMouseCapture();
    void InvalidateVisual();
    double SecOfX(double x);
    double XOfSec(double sec);
    double ClipEndX(double startSec, double endSec);
    double ClipSecOfX(double startSec, double x);
    int BarAt(double x);
    int TrackAt(double y);
    double LaneTop(int track, int lane);
    double SnapSec(double sec, AudioClip? except, out double distancePx, bool? altHeld = null);
    SongQuarterMap QuarterMap();
    DropPreview DropGeometry(MediaDropPlan plan, Point p, IReadOnlyList<DropItem> items);
    DropPreview NotationDropGeometry(MediaDropPlan plan, int trackIndex, double startSec, double endSec);
    void SetDropPreview(DropPreview? preview);
    /// <summary>A click on a lane seeks: bar clicked, then track clicked.</summary>
    void RaiseSeek(int bar, int track);
    void RaisePlainClicked();
}

// Owns: the clip gesture state (dragged clip, origin, press point, pending selection, move plan) and the clip events.
// Does not own: drawing (TrackTimeline.Clips.cs reads DropTarget, FromTrack, IsMovingOriginal), the drop ghost and snapping (host),
// the clip model edits after release (ClipEditController via the events).
// Tests: TestClipDragPress, TestClipMoves, TestClipMoveGhost, TestMidiClipMoves, TestClipSplitGlueFades.
/// <summary>
/// Clip gestures on the timeline's clip lanes: press, move (to another lane, track or notation row), edge trims, fade handles,
/// the right-click hit, Esc / lost-capture cancel, and the simulated move used by tests and the render diagnostic.
/// </summary>
internal sealed class ClipGestureController
{
    internal const double ClipEdgeGrip = 6;
    internal const double FadeHandleSize = 7;
    private static readonly IReadOnlyList<DropItem> MoveGhostItems = new[] { new DropItem { Path = "", Name = "" } };

    private readonly IClipGestureHost _host;
    private AudioClip? _clipDrag;
    private ClipGesture _clipGesture;
    private (double Start, double Offset, double Length) _clipOrigin;
    private (double In, double Out) _fadeOrigin;
    private double _clipPressSec;
    private Point _clipPressPoint;
    /// <summary>From the press point down to the dragged clip's lane centre: the move targets the lane under the clip, not under the pointer.</summary>
    private double _grabToCentre;

    private double GrabToCentre(int track, AudioClip clip, Point press) =>
        _host.LaneTop(track, clip.Lane) + ArrangementPanel.AudioLaneHeight / 2 - press.Y;

    internal double SnapClipSec(double anchorStartSec, double sec, AudioClip? except, out double distancePx, bool? altHeld = null)
    {
        var x = _host.ClipEndX(anchorStartSec, sec);
        var axisSec = _host.SecOfX(x);
        var snappedAxisSec = _host.SnapSec(axisSec, except, out distancePx, altHeld);
        return _host.ClipSecOfX(anchorStartSec, _host.XOfSec(snappedAxisSec));
    }

    private bool _clipChanged;
    private double _clipLastX = double.NaN;
    private int _clipFromTrack = -1;
    private (int Track, int Lane, bool NotationRow)? _clipDropTarget;
    // The move gesture: the clip stays where it is (drawn faint) while the shared drop ghost shows where it would land.
    private bool _clipMoveActive;
    private bool _moveCopy;
    private MediaDropPlan? _movePlan;
    private (int Track, int Lane, bool Notation, int Zone, double Start, double Scroll, double Zoom, int Revision, bool Copy)? _moveKey;
    private (int Track, int Lane, bool Midi)? _clipPendingSelect;
    private int _clipPendingBar;
    private bool _clipPendingCtrl;

    public ClipGestureController(IClipGestureHost host) => _host = host;

    /// <summary>A take (clip) was clicked: track, lane, is it MIDI, Ctrl held. The host makes that lane the playing one.</summary>
    public event Action<int, int, bool, bool>? ClipLaneSelected;
    /// <summary>A clip drag begins (the host captures undo).</summary>
    public event EventHandler? ClipEditStarting;
    /// <summary>A clip drag finished and changed the clip.</summary>
    public event EventHandler<AudioClip>? ClipEdited;
    /// <summary>Right-click on a lane: track, clip (null on empty lane space), song seconds.</summary>
    public event Action<int, AudioClip?, double>? ClipContextRequested;
    /// <summary>Double-click on a clip.</summary>
    public event Action<int, AudioClip>? ClipPropertiesRequested;
    /// <summary>A clip was dragged to a lane (any track, a new lane, or below the last track): clip, from track, where it lands, Ctrl held (copy).</summary>
    public event Action<AudioClip, int, MediaDropPlan, bool>? ClipMoveRequested;
    /// <summary>A MIDI clip was dropped on a track's notation row: clip, from track, to track.</summary>
    public event Action<AudioClip, int, int>? MidiClipToNotation;
    /// <summary>A lane was clicked: track, lane, song seconds (the clip edit cursor, where Paste goes).</summary>
    public event Action<int, int, double>? LaneClicked;

    internal bool DragActive => _clipDrag is not null;
    internal (int Track, int Lane, bool NotationRow)? DropTarget => _clipDropTarget;
    internal int FromTrack => _clipFromTrack;
    /// <summary>The clip is the original of a move (not a copy) in progress: drawn faint while the ghost shows where it goes.</summary>
    internal bool IsMovingOriginal(AudioClip clip) => _clipMoveActive && !_moveCopy && ReferenceEquals(clip, _clipDrag);
    /// <summary>The plan the simulated drag would drop (null when none is valid).</summary>
    internal MediaDropPlan? SimulatedMovePlan => _movePlan;

    // ---------- hit testing ----------
    internal (int Track, int Lane, AudioClip? Clip, ClipGesture Gesture)? LaneHitAt(Point p)
    {
        var project = _host.Project;
        if (project is null) return null;
        var track = _host.TrackAt(p.Y);
        if (track < 0 || !ArrangementPanel.HasAudioLane(project.Tracks[track])) return null;
        var first = _host.LaneTop(track, 0);
        if (p.Y < first) return null;
        var lane = (int)((p.Y - first) / ArrangementPanel.AudioLaneHeight);
        if (lane >= ArrangementPanel.LaneCountOf(project.Tracks[track])) return null;
        var clips = project.Tracks[track].AudioClips;
        for (var i = clips.Count - 1; i >= 0; i--)   // topmost (last drawn) first
        {
            if (clips[i].Lane != lane) continue;
            var x1 = _host.XOfSec(clips[i].StartSec);
            var x2 = Math.Max(x1 + 3, _host.ClipEndX(clips[i].StartSec, clips[i].EndSec));
            if (p.X < x1 - 2 || p.X > x2 + 2) continue;
            var top = _host.LaneTop(track, lane) + 3;
            if (p.Y >= top - 2 && p.Y <= top + FadeHandleSize + 3 && (ReferenceEquals(clips[i], _host.SelectedClip) || clips[i].FadeInSec > 0 || clips[i].FadeOutSec > 0))
            {
                var inX = _host.ClipEndX(clips[i].StartSec, clips[i].StartSec + clips[i].FadeInSec);
                var outX = _host.ClipEndX(clips[i].StartSec, clips[i].EndSec - clips[i].FadeOutSec);
                var nearIn = Math.Abs(p.X - inX); var nearOut = Math.Abs(p.X - outX);
                if (Math.Min(nearIn, nearOut) <= FadeHandleSize) return (track, lane, clips[i], nearIn <= nearOut ? ClipGesture.FadeIn : ClipGesture.FadeOut);
            }
            var grip = Math.Min(ClipEdgeGrip, (x2 - x1) / 3);
            var gesture = p.X <= x1 + grip ? ClipGesture.TrimStart : p.X >= x2 - grip ? ClipGesture.TrimEnd : ClipGesture.Move;
            return (track, lane, clips[i], gesture);
        }
        return (track, lane, null, ClipGesture.Move);
    }

    // ---------- gestures (called first by the timeline's mouse handlers; true = handled) ----------
    internal bool MouseDown(MouseButtonEventArgs e, Point p)
    {
        if (LaneHitAt(p) is not { } hit) return false;
        _host.Focus();
        _host.SelectedClip = hit.Clip;
        LaneClicked?.Invoke(hit.Track, hit.Lane, _host.SecOfX(p.X));
        // Clip lanes consume mouse-down before the bar grid sees it. Seek here as well so
        // empty lanes and takes behave like every other row of the timeline.
        if (hit.Clip is not { } clip)
        {
            _host.RaiseSeek(_host.BarAt(p.X), hit.Track);
            // Empty lane space: deselect; the spot becomes the paste position (the edit cursor).
            // It is empty timeline space, so the selected bar range is cleared too (no drag starts here).
            _host.RaisePlainClicked();
            _host.InvalidateVisual();
            e.Handled = true;
            return true;
        }
        if (e.ClickCount > 1)
        {
            ClipPropertiesRequested?.Invoke(hit.Track, clip);
            e.Handled = true;
            return true;
        }
        // Ctrl on a clip toggles its lane's play state, but Ctrl+drag copies: the toggle waits for the release (a click, not a drag).
        // Seek, track switch and take selection wait for the release without a drag: a press on a clip
        // starts the gesture at once (no playback reposition or window-wide refresh before the first frame).
        _clipPendingSelect = (hit.Track, hit.Lane, clip.IsMidi);
        _clipPendingCtrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        _clipPendingBar = _host.BarAt(p.X);
        _clipDrag = clip;
        _clipGesture = hit.Gesture;
        _clipOrigin = (clip.StartSec, clip.OffsetSec, clip.SourceLengthSec);
        _fadeOrigin = (clip.FadeInSec, clip.FadeOutSec);
        _clipPressSec = _host.SecOfX(p.X);
        _clipPressPoint = p;
        _grabToCentre = GrabToCentre(hit.Track, clip, p);
        _clipChanged = false;
        _clipLastX = p.X;
        _clipFromTrack = hit.Track;
        _clipDropTarget = null;
        _clipMoveActive = false; _movePlan = null; _moveKey = null;
        if (hit.Gesture != ClipGesture.Move) ClipEditStarting?.Invoke(_host, EventArgs.Empty);   // a move captures undo when it lands
        _host.CaptureMouse();
        _host.InvalidateVisual();
        e.Handled = true;
        return true;
    }

    internal bool MouseMove(MouseEventArgs e, Point p)
    {
        if (_clipDrag is not null && e.LeftButton == MouseButtonState.Released) { Cancel(); return true; }   // the release was missed: the drag ends
        if (_clipDrag is not { } clip)
        {
            // Hover: resize cursor on clip edges.
            if (e.LeftButton == MouseButtonState.Released && LaneHitAt(p) is { Clip: not null } hover)
            {
                _host.Cursor = hover.Gesture == ClipGesture.Move ? Cursors.Arrow : Cursors.SizeWE;   // arrow over the body, resize only at the edges
                return true;
            }
            if (_host.Cursor == Cursors.SizeWE || _host.Cursor == Cursors.SizeAll) _host.Cursor = null!;   // left the clip edge
            return false;
        }
        // A plain click (even with a little hand jitter) only seeks: the gesture starts past the drag threshold.
        if (!_clipChanged &&
            Math.Abs(p.X - _clipPressPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - _clipPressPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return true;
        if (_clipGesture == ClipGesture.Move) { UpdateMove(clip, p, Keyboard.Modifiers.HasFlag(ModifierKeys.Alt), Keyboard.Modifiers.HasFlag(ModifierKeys.Control)); return true; }
        if (Math.Abs(p.X - _clipLastX) < 1) return true;   // repaint only when something visible changed
        _clipLastX = p.X;
        var delta = _host.ClipSecOfX(_clipOrigin.Start, p.X) - _host.ClipSecOfX(_clipOrigin.Start, _clipPressPoint.X);
        var speed = Math.Clamp(clip.Speed, 0.25, 4);
        var fileLength = clip.FileLengthSec > 0 ? clip.FileLengthSec : _clipOrigin.Offset + _clipOrigin.Length;
        switch (_clipGesture)
        {
            case ClipGesture.TrimStart:
            {
                var snappedStart = _host.SnapSec(_clipOrigin.Start + delta, clip, out _);
                var fileDelta = Math.Clamp((snappedStart - _clipOrigin.Start) * speed, -_clipOrigin.Offset, _clipOrigin.Length - 0.05);
                if (_clipOrigin.Start + fileDelta / speed < 0) fileDelta = -_clipOrigin.Start * speed;
                clip.OffsetSec = _clipOrigin.Offset + fileDelta;
                clip.SourceLengthSec = _clipOrigin.Length - fileDelta;
                clip.StartSec = _clipOrigin.Start + fileDelta / speed;
                break;
            }
            case ClipGesture.FadeIn: ClipSplitGlue.SetFades(clip, _host.ClipSecOfX(clip.StartSec, p.X) - clip.StartSec, null); break;
            case ClipGesture.FadeOut: ClipSplitGlue.SetFades(clip, null, clip.EndSec - _host.ClipSecOfX(clip.StartSec, p.X)); break;
            case ClipGesture.TrimEnd:
                var endSec = SnapClipSec(_clipOrigin.Start, _clipOrigin.Start + (_clipOrigin.Length + delta * speed) / speed, clip, out _);
                clip.SourceLengthSec = Math.Clamp((endSec - _clipOrigin.Start) * speed, 0.05,
                    clip.IsMidi ? 86_400 : Math.Max(0.05, fileLength - _clipOrigin.Offset));
                break;
        }
        _clipChanged = true;
        _host.InvalidateVisual();
        return true;
    }

    /// <summary>The pointer capture was taken away mid-drag (popup, focus change): the drag ends instead of following the mouse.</summary>
    internal bool CaptureLost() => _clipDrag is not null && Cancel();

    internal bool MouseUp()
    {
        if (_clipDrag is not { } clip) return false;
        _clipDrag = null;
        var target = _clipDropTarget;
        _clipDropTarget = null;
        _host.ReleaseMouseCapture();
        if (_clipMoveActive)
        {
            var plan = _movePlan; var copy = _moveCopy;
            _clipMoveActive = false; _movePlan = null; _moveKey = null;
            _host.SetDropPreview(null);
            if (target is { NotationRow: true } row && clip.IsMidi)
            {
                ClipEditStarting?.Invoke(_host, EventArgs.Empty);
                MidiClipToNotation?.Invoke(clip, _clipFromTrack, row.Track);
            }
            else if (plan is { Valid: true }) ClipMoveRequested?.Invoke(clip, _clipFromTrack, plan, copy);
            else _host.InvalidateVisual();
            return true;
        }
        if (_clipChanged) ClipEdited?.Invoke(_host, clip);
        else
        {
            if (_clipPendingSelect is { } pending)
            {
                _clipPendingSelect = null;
                _host.RaiseSeek(_clipPendingBar, pending.Track);
                ClipLaneSelected?.Invoke(pending.Track, pending.Lane, pending.Midi, _clipPendingCtrl);
            }
            // A plain click selected the clip (above); like clicking a note in the score it drops the bar range.
            _host.RaisePlainClicked();
            _host.InvalidateVisual();
        }
        return true;
    }

    /// <summary>Esc during a clip drag: the clip stays (or goes back) where it was and the ghost goes. False when no clip is being dragged.</summary>
    internal bool Cancel()
    {
        if (_clipDrag is not { } clip) return false;
        if (_clipGesture != ClipGesture.Move)
        {
            clip.StartSec = _clipOrigin.Start;
            clip.OffsetSec = _clipOrigin.Offset;
            clip.SourceLengthSec = _clipOrigin.Length;
            clip.FadeInSec = _fadeOrigin.In;
            clip.FadeOutSec = _fadeOrigin.Out;
        }
        _clipDrag = null; _clipDropTarget = null; _clipMoveActive = false; _movePlan = null; _moveKey = null; _clipChanged = false;
        _host.SetDropPreview(null);
        if (_host.IsMouseCaptured) _host.ReleaseMouseCapture();
        _host.InvalidateVisual();
        return true;
    }

    /// <summary>
    /// The move gesture: where the pointer is (track, lane, snapped start; Alt = free placement, Ctrl = copy) is planned like a file
    /// drop and handed to the shared ghost. Nothing is recomputed or redrawn while the snapped target stays the same.
    /// </summary>
    private void UpdateMove(AudioClip clip, Point p, bool alt, bool copy)
    {
        var project = _host.Project!;
        var start = Math.Max(0, _clipOrigin.Start + _host.SecOfX(p.X) - _clipPressSec);
        var length = clip.LengthSec;
        var byStart = _host.SnapSec(start, clip, out var startDistance, alt);
        var byEnd = SnapClipSec(start, start + length, clip, out var endDistance, alt) - length;
        start = Math.Max(0, endDistance < startDistance ? byEnd : byStart);

        var gridTop = ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight;
        var y = p.Y + _grabToCentre;   // where the clip's centre is: a press near the clip's top edge does not slip into the row above
        var track = _host.TrackAt(y);
        if (track < 0 && y >= gridTop && y + _host.VerticalScrollOffset - gridTop >= ArrangementPanel.RowsHeight(project)) track = project.Tracks.Count;   // below the last track
        int lane = 0; var notation = false;
        if (track >= 0 && track < project.Tracks.Count)
        {
            var lanes = ArrangementPanel.LaneCountOf(project.Tracks[track]);
            var offset = y - _host.LaneTop(track, 0);
            // A MIDI clip on a track's notation row is written into the score. A group header above a track counts as that track's
            // row but is not its notation, and an audio track has no notation to write into.
            if (offset < 0) notation = clip.IsMidi && lanes > 0 && !project.Tracks[track].IsAudio && offset >= -ArrangementPanel.NotationHeightOf(project, project.Tracks[track]);
            else lane = Math.Min((int)(offset / ArrangementPanel.AudioLaneHeight), Math.Max(0, lanes - 1));
        }
        var zone = 0;
        if (track >= 0 && track < project.Tracks.Count)
        {
            var offset = y - _host.LaneTop(track, 0);
            if (offset < 0) zone = -1;
            else zone = Math.Min((int)(offset / ArrangementPanel.AudioLaneHeight), Math.Max(0, ArrangementPanel.LaneCountOf(project.Tracks[track]) - 1));
        }
        var key = (track, lane, notation, zone, start, _host.VerticalScrollOffset, _host.MeasureWidth, project.TimelineRevision, copy);
        if (_clipMoveActive && _moveKey == key) return;
        var redraw = !_clipMoveActive || copy != _moveCopy || notation != (_moveKey?.Notation ?? false);
        _moveKey = key; _moveCopy = copy; _clipMoveActive = true; _clipChanged = true;
        if (notation)
        {
            _movePlan = null;
            _clipDropTarget = (track, 0, true);
            var labelPlan = new MediaDropPlan
            {
                TrackIndex = track, Lane = 0, StartSec = start, EndSec = start + length,
                Clips = new[] { new PlannedClip(MoveGhostItems[0], start, length) },
            };
            _host.SetDropPreview(_host.NotationDropGeometry(labelPlan, track, start, start + length));
        }
        else
        {
            _clipDropTarget = null;
            var sourceKind = _clipFromTrack >= 0 && _clipFromTrack < project.Tracks.Count ? project.Tracks[_clipFromTrack].Kind : TrackKind.Guitar;
            _movePlan = MediaDrop.PlanMove(project, clip, sourceKind, track, lane, start, _host.QuarterMap(), copy);
            _host.SetDropPreview(_host.DropGeometry(_movePlan, new Point(p.X, y), MoveGhostItems));
        }
        if (redraw) _host.InvalidateVisual();
    }

    /// <summary>Test / render hook: the clip is being dragged from <paramref name="press"/> to <paramref name="to"/> (as if the mouse were held there).</summary>
    internal DropPreview? SimulateMove(AudioClip clip, int fromTrack, Point press, Point to, bool copy = false, bool alt = false)
    {
        if (!ReferenceEquals(_clipDrag, clip))
        {
            _clipDrag = clip; _clipGesture = ClipGesture.Move; _clipOrigin = (clip.StartSec, clip.OffsetSec, clip.SourceLengthSec);
            _clipPressSec = _host.SecOfX(press.X); _clipFromTrack = fromTrack; _clipMoveActive = false; _moveKey = null;
            _grabToCentre = GrabToCentre(fromTrack, clip, press);
        }
        UpdateMove(clip, to, alt, copy);
        return _host.CurrentDropPreview;
    }

    internal bool RightClick(Point p)
    {
        if (LaneHitAt(p) is not { } hit) return false;
        _host.SelectedClip = hit.Clip;
        _host.InvalidateVisual();
        LaneClicked?.Invoke(hit.Track, hit.Lane, _host.SecOfX(p.X));
        ClipContextRequested?.Invoke(hit.Track, hit.Clip, _host.SecOfX(p.X));
        return true;
    }
}
