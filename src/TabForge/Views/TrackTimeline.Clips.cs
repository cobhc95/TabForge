using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Audio;
using TabForge.Models;
using TabForge.Services;
using TabForge.Visualization;

namespace TabForge.Views;

/// <summary>What a clip edit gesture is doing.</summary>
internal enum ClipGesture { Move, TrimStart, TrimEnd }

// TrackTimeline: the clip lanes under a track (fixed lanes) and their audio and MIDI clips:
// waveform / note drawing, greyed takes on lanes that do not play, drag to move (also to another lane or
// track, or a MIDI clip onto a notation row), edge trims, the right-click menu, file drops, and the live take
// being recorded (drawn on the overlay layer, so recording never repaints the whole timeline).
internal sealed partial class TrackTimeline
{
    private const double ClipEdgeGrip = 6;

    public AudioClip? SelectedClip { get; set; }

    /// <summary>Snap settings (shared with Settings); null = no snapping.</summary>
    public SnapSettings? Snap { get; set; }
    /// <summary>Playhead in song seconds (a snap target).</summary>
    public Func<double>? PlayheadSec { get; set; }
    /// <summary>A take (clip) was clicked: track, lane, is it MIDI, Ctrl held. The host makes that lane the playing one.</summary>
    public event Action<int, int, bool, bool>? ClipLaneSelected;

    /// <summary>
    /// Snaps a song time: to the grid (nearest grid line of the song's bars), to the edges of other clips, and to the playhead,
    /// whichever is nearest within the snap distance (the grid at any distance when that is on). Alt turns snapping off while held.
    /// </summary>
    private double SnapSec(double sec, AudioClip? except, out double distancePx)
    {
        distancePx = double.PositiveInfinity;
        var snap = Snap;
        if (snap is not { Enabled: true } || Project is null || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return sec;
        var best = sec; var bestDistance = double.PositiveInfinity;
        void Consider(double candidate, double limitPx)
        {
            var d = Math.Abs(XOfSec(candidate) - XOfSec(sec));
            if (d <= limitPx && d < bestDistance) { best = candidate; bestDistance = d; }
        }
        if (snap.ToItems)
            foreach (var track in Project.Tracks)
                foreach (var other in track.AudioClips)
                {
                    if (ReferenceEquals(other, except)) continue;
                    Consider(other.StartSec, snap.DistancePx); Consider(other.EndSec, snap.DistancePx);
                }
        if (snap.ToPlayhead && PlayheadSec is { } playhead) Consider(playhead(), snap.DistancePx);
        if (snap.ToGrid) Consider(GridSec(sec, snap.Grid), snap.GridAtAnyDistance ? double.PositiveInfinity : snap.DistancePx);
        distancePx = bestDistance;
        return best;
    }

    /// <summary>The grid line nearest to a song time, on the song's own bars (each bar's length and time signature).</summary>
    private double GridSec(double sec, string grid)
    {
        var (bar, fraction) = BarOfSec is null ? (0, 0.0) : BarOfSec(sec);
        var slots = Math.Max(1, TabForge.Services.MusicTime.BarSlots(Project!, Math.Clamp(bar, 0, Math.Max(0, BarCount - 1))));
        var step = grid switch { "Bar" => slots, "1/2" => 8.0, "1/4" => 4.0, "1/8" => 2.0, "1/16" => 1.0, "1/32" => 0.5, _ => 4.0 };
        var snapped = Math.Round(fraction * slots / step) * step;
        var barStart = SecOfBar(bar);
        return barStart + snapped / slots * BarLengthSec(bar);
    }

    /// <summary>A clip drag begins (the host captures undo).</summary>
    public event EventHandler? ClipEditStarting;
    /// <summary>A clip drag finished and changed the clip.</summary>
    public event EventHandler<AudioClip>? ClipEdited;
    /// <summary>Right-click on a lane: track, clip (null on empty lane space), song seconds.</summary>
    public event Action<int, AudioClip?, double>? ClipContextRequested;
    /// <summary>Double-click on a clip.</summary>
    public event Action<int, AudioClip>? ClipPropertiesRequested;
    /// <summary>Audio files dropped on a track: track, song seconds, files.</summary>
    public event Action<int, double, string[]>? AudioFilesDropped;
    /// <summary>A clip was dropped on another track's lane: clip, from track, to track, lane.</summary>
    public event Action<AudioClip, int, int, int>? ClipMovedToTrack;
    /// <summary>A MIDI clip was dropped on a track's notation row: clip, from track, to track.</summary>
    public event Action<AudioClip, int, int>? MidiClipToNotation;
    /// <summary>A lane was clicked: track, lane, song seconds (the clip edit cursor, where Paste goes).</summary>
    public event Action<int, int, double>? LaneClicked;

    /// <summary>Takes being recorded right now (drawn live on the overlay).</summary>
    public List<LiveTake> LiveTakes { get; } = new();

    private AudioClip? _clipDrag;
    private ClipGesture _clipGesture;
    private (double Start, double Offset, double Length) _clipOrigin;
    private double _clipPressSec;
    private Point _clipPressPoint;
    private bool _clipChanged;
    private double _clipLastX = double.NaN;
    private int _clipFromTrack = -1;
    private (int Track, int Lane, bool NotationRow)? _clipDropTarget;

    public TrackTimeline()
    {
        AllowDrop = true;
        Focusable = true;
        // A waveform finished reading in the background: redraw once, if that file is on this song.
        WaveformCache.Ready += file => Dispatcher.BeginInvoke(() =>
        {
            if (Project?.Tracks.Any(t => t.AudioClips.Any(c => string.Equals(c.File, file, StringComparison.OrdinalIgnoreCase))) == true)
                InvalidateVisual();
        });
    }

    private double RowTop(int track) =>
        ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight + ArrangementPanel.RowTopOf(Project, track) - VerticalScrollOffset;

    private double LaneTop(int track, int lane) => RowTop(track) + ArrangementPanel.TrackRowHeight + lane * ArrangementPanel.AudioLaneHeight;

    // ---------- drawing ----------
    private void DrawAudioLane(DrawingContext dc, TrackModel track, double rowTop, double width, Color trackColor)
    {
        var lanes = ArrangementPanel.LaneCountOf(track);
        if (lanes == 0) return;
        var clipColour = Readable(trackColor);
        var trackIndex = Project?.Tracks.IndexOf(track) ?? -1;
        for (var lane = 0; lane < lanes; lane++)
        {
            var laneRect = new Rect(0, rowTop + ArrangementPanel.TrackRowHeight + lane * ArrangementPanel.AudioLaneHeight, width, ArrangementPanel.AudioLaneHeight);
            dc.DrawRectangle(Draw.Solid(_theme.Board, 0.55), null, laneRect);
            dc.DrawRectangle(Draw.Solid(trackColor, ClipLanes.Plays(track, lane) ? 0.08 : 0.03), null, laneRect);
            if (_clipDropTarget is { NotationRow: false } target && target.Track == trackIndex && target.Lane == lane && _clipFromTrack != trackIndex)
                dc.DrawRectangle(Draw.Solid(_theme.Accent, 0.16), Draw.Pen(_theme.Accent, 1, 0.8), laneRect);
            dc.DrawLine(Draw.Pen(_theme.BoardEdge, 0.6, 0.7), new Point(0, laneRect.Bottom - 0.5), new Point(width, laneRect.Bottom - 0.5));
        }
        if (track.RecordArm && track.AudioClips.Count == 0 && !LiveTakes.Any(t => ReferenceEquals(t.Track, track)))
            Draw.At(dc, AudioInputs.IsMidi(track.AudioInput) ? "Armed (MIDI): press Record to record here" : "Armed: press Record to record here, or drop audio files",
                8, rowTop + ArrangementPanel.TrackRowHeight + 15, 11, Draw.Solid(_theme.Muted));
        if (_clipDropTarget is { NotationRow: true } row && row.Track == trackIndex)
            dc.DrawRectangle(Draw.Solid(_theme.Accent, 0.18), Draw.Pen(_theme.Accent, 1.4), new Rect(0, rowTop, width, ArrangementPanel.TrackRowHeight));
        foreach (var clip in track.AudioClips)
        {
            var x1 = XOfSec(clip.StartSec);
            var x2 = XOfSec(clip.EndSec);
            if (x2 < 0 || x1 > width) continue;
            var laneTop = rowTop + ArrangementPanel.TrackRowHeight + clip.Lane * ArrangementPanel.AudioLaneHeight;
            var box = new Rect(x1, laneTop + 3, Math.Max(3, x2 - x1), ArrangementPanel.AudioLaneHeight - 6);
            // Greyed: muted, or an audio take on a lane that is not playing.
            var heard = ClipLanes.Audible(track, clip);
            var colour = heard ? clipColour : Blend(clipColour, _theme.Muted, 0.7);
            var alpha = heard ? 1.0 : 0.45;
            var selected = ReferenceEquals(clip, SelectedClip);
            dc.DrawRoundedRectangle(Draw.Solid(colour, 0.34 * alpha), Draw.Pen(selected ? _theme.Text : colour, selected ? 1.8 : 1, 0.9 * alpha), box, 3, 3);
            dc.PushClip(new RectangleGeometry(box, 3, 3));
            if (clip.IsMidi) DrawMidiNotes(dc, clip, box, colour, alpha, width);
            else DrawWaveform(dc, clip, box, colour, alpha, width);
            var label = clip.Muted ? $"{clip.Name} (muted)" : clip.Name;
            if (box.Width > 30) Draw.At(dc, label, box.X + 5, box.Y + 1, 10, Draw.Solid(_theme.Text, 0.85 * alpha));
            dc.Pop();
        }
    }

    private static Color Blend(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    /// <summary>A track colour too close to the lane background is lifted toward the text colour.</summary>
    private Color Readable(Color c)
    {
        static double Luma(Color x) => 0.2126 * x.R + 0.7152 * x.G + 0.0722 * x.B;
        return Math.Abs(Luma(c) - Luma(_theme.Background)) >= 60 ? c : Blend(c, _theme.Text, 0.5);
    }

    private void DrawWaveform(DrawingContext dc, AudioClip clip, Rect box, Color colour, double alpha, double width)
    {
        var peaks = WaveformCache.Get(clip.File);
        if (peaks is null || peaks.Length == 0 || clip.SourceLengthSec <= 0) return;
        var gain = Math.Pow(10, clip.GainDb / 20);
        DrawPeaks(dc, box, colour, alpha, width, x =>
        {
            var fileFrom = clip.OffsetSec + (x - box.X) / box.Width * clip.SourceLengthSec;
            var fileTo = clip.OffsetSec + (x + 1 - box.X) / box.Width * clip.SourceLengthSec;
            var i0 = Math.Max(0, (int)(fileFrom / WaveformCache.SecondsPerPeak));
            var i1 = Math.Min(peaks.Length, Math.Max(i0 + 1, (int)Math.Ceiling(fileTo / WaveformCache.SecondsPerPeak)));
            var peak = 0f;
            for (var i = i0; i < i1; i++) peak = Math.Max(peak, peaks[i]);
            return peak * gain;
        });
    }

    /// <summary>One vertical line per visible pixel column, square-root scaled (quiet takes stay readable), as one geometry.</summary>
    private static void DrawPeaks(DrawingContext dc, Rect box, Color colour, double alpha, double width, Func<double, double> peakAt)
    {
        var mid = box.Y + box.Height / 2 + 4;
        var half = box.Height / 2 - 6;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            var right = Math.Min(box.Right, width);
            for (var x = Math.Floor(Math.Max(box.X, 0)); x < right; x += 1)
            {
                var h = Math.Sqrt(Math.Clamp(peakAt(x), 0, 1)) * half;
                if (h < 0.5) continue;
                g.BeginFigure(new Point(x + 0.5, mid - h), false, false);
                g.LineTo(new Point(x + 0.5, mid + h), true, false);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, Draw.Pen(colour, 1, 0.9 * alpha), geometry);
    }

    /// <summary>MIDI clip: its notes as short bars, pitch spread over the clip's own range.</summary>
    private void DrawMidiNotes(DrawingContext dc, AudioClip clip, Rect box, Color colour, double alpha, double width)
    {
        var notes = clip.Notes!;
        if (notes.Count == 0) return;
        var low = notes.Min(n => n.Pitch);
        var high = Math.Max(low + 11, notes.Max(n => n.Pitch));
        var top = box.Y + 13; var h = box.Height - 16;
        var speed = Math.Clamp(clip.Speed, 0.25, 4);
        var brush = Draw.Solid(colour, 0.95 * alpha);
        var end = clip.OffsetSec + clip.SourceLengthSec;
        foreach (var n in notes)
        {
            if (n.StartSec + n.LengthSec <= clip.OffsetSec || n.StartSec >= end) continue;
            var x1 = XOfSec(clip.StartSec + (Math.Max(n.StartSec, clip.OffsetSec) - clip.OffsetSec) / speed);
            var x2 = XOfSec(clip.StartSec + (Math.Min(n.StartSec + n.LengthSec, end) - clip.OffsetSec) / speed);
            if (x2 < 0 || x1 > width) continue;
            var y = top + (1 - (n.Pitch - low) / (double)(high - low)) * (h - 2);
            dc.DrawRectangle(brush, null, new Rect(x1, y, Math.Max(1.5, x2 - x1), 2));
        }
    }

    /// <summary>The takes being recorded (overlay pass): a red box growing with the playhead, with what came in.</summary>
    private void DrawLiveTakes(DrawingContext dc, double width)
    {
        if (LiveTakes.Count == 0 || Project is null) return;
        var red = (TryFindResource("DangerBrush") as SolidColorBrush)?.Color ?? _theme.Accent;
        foreach (var take in LiveTakes)
        {
            var t = Project.Tracks.IndexOf(take.Track);
            if (t < 0) continue;
            var x1 = XOfSec(take.StartSec);
            var x2 = XOfSec(Math.Max(take.StartSec, take.EndSec));
            if (x2 < 0 || x1 > width) continue;
            var box = new Rect(x1, LaneTop(t, take.Lane) + 3, Math.Max(2, x2 - x1), ArrangementPanel.AudioLaneHeight - 6);
            var colour = Readable(Parse(take.Track.ColorHex, _theme.Accent));
            dc.DrawRoundedRectangle(Draw.Solid(red, 0.22), Draw.Pen(red, 1.2), box, 3, 3);
            dc.PushClip(new RectangleGeometry(box, 3, 3));
            if (take.Midi)
            {
                var clip = new AudioClip { StartSec = take.StartSec, SourceLengthSec = Math.Max(0.01, take.EndSec - take.StartSec), Notes = take.Notes };
                DrawMidiNotes(dc, clip, box, colour, 1, width);
            }
            else if (take.Peaks.Count > 0)
            {
                var peaks = take.Peaks;
                DrawPeaks(dc, box, colour, 1, width, x =>
                {
                    var i = (int)((x - box.X) / box.Width * peaks.Count);
                    return i >= 0 && i < peaks.Count ? peaks[i] : 0;
                });
            }
            if (box.Width > 40) Draw.At(dc, "● REC", box.X + 5, box.Y + 1, 10, Draw.Solid(red));
            dc.Pop();
        }
    }

    // ---------- hit testing ----------
    private (int Track, int Lane, AudioClip? Clip, ClipGesture Gesture)? LaneHitAt(Point p)
    {
        var project = Project;
        if (project is null) return null;
        var track = TrackAt(p.Y);
        if (track < 0 || !ArrangementPanel.HasAudioLane(project.Tracks[track])) return null;
        var first = LaneTop(track, 0);
        if (p.Y < first) return null;
        var lane = (int)((p.Y - first) / ArrangementPanel.AudioLaneHeight);
        if (lane >= ArrangementPanel.LaneCountOf(project.Tracks[track])) return null;
        var clips = project.Tracks[track].AudioClips;
        for (var i = clips.Count - 1; i >= 0; i--)   // topmost (last drawn) first
        {
            if (clips[i].Lane != lane) continue;
            var x1 = XOfSec(clips[i].StartSec);
            var x2 = Math.Max(x1 + 3, XOfSec(clips[i].EndSec));
            if (p.X < x1 - 2 || p.X > x2 + 2) continue;
            var grip = Math.Min(ClipEdgeGrip, (x2 - x1) / 3);
            var gesture = p.X <= x1 + grip ? ClipGesture.TrimStart : p.X >= x2 - grip ? ClipGesture.TrimEnd : ClipGesture.Move;
            return (track, lane, clips[i], gesture);
        }
        return (track, lane, null, ClipGesture.Move);
    }

    /// <summary>Where a dragged clip would land: a lane of some track, or a track's notation row.</summary>
    private (int Track, int Lane, bool NotationRow)? DropTargetAt(Point p)
    {
        if (Project is null) return null;
        var track = TrackAt(p.Y);
        if (track < 0) return null;
        var offset = p.Y - LaneTop(track, 0);
        if (offset < 0) return (track, 0, true);
        var lane = (int)(offset / ArrangementPanel.AudioLaneHeight);
        return lane < ArrangementPanel.LaneCountOf(Project.Tracks[track]) ? (track, lane, false) : null;
    }

    // ---------- gestures (called first by the timeline's mouse handlers; true = handled) ----------
    private bool ClipMouseDown(MouseButtonEventArgs e, Point p)
    {
        if (LaneHitAt(p) is not { } hit) return false;
        Focus();
        SelectedClip = hit.Clip;
        LaneClicked?.Invoke(hit.Track, hit.Lane, SecOfX(p.X));
        // Clip lanes consume mouse-down before the bar grid sees it. Seek here as well so
        // empty lanes and takes behave like every other row of the timeline.
        BarClicked?.Invoke(this, BarAt(p.X));
        TrackClicked?.Invoke(this, hit.Track);
        if (hit.Clip is not { } clip)
        {
            // Empty lane space: deselect; the spot becomes the paste position (the edit cursor).
            // It is empty timeline space, so the selected bar range is cleared too (no drag starts here).
            PlainClicked?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
            return true;
        }
        if (e.ClickCount > 1)
        {
            ClipPropertiesRequested?.Invoke(hit.Track, clip);
            e.Handled = true;
            return true;
        }
        ClipLaneSelected?.Invoke(hit.Track, hit.Lane, clip.IsMidi, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        _clipDrag = clip;
        _clipGesture = hit.Gesture;
        _clipOrigin = (clip.StartSec, clip.OffsetSec, clip.SourceLengthSec);
        _clipPressSec = SecOfX(p.X);
        _clipPressPoint = p;
        _clipChanged = false;
        _clipLastX = p.X;
        _clipFromTrack = hit.Track;
        _clipDropTarget = null;
        ClipEditStarting?.Invoke(this, EventArgs.Empty);
        CaptureMouse();
        InvalidateVisual();
        e.Handled = true;
        return true;
    }

    private bool ClipMouseMove(MouseEventArgs e, Point p)
    {
        if (_clipDrag is not { } clip)
        {
            // Hover: resize cursor on clip edges.
            if (e.LeftButton == MouseButtonState.Released && LaneHitAt(p) is { Clip: not null } hover)
            {
                Cursor = hover.Gesture == ClipGesture.Move ? Cursors.Arrow : Cursors.SizeWE;   // arrow over the body, resize only at the edges
                return true;
            }
            if (Cursor == Cursors.SizeWE || Cursor == Cursors.SizeAll) Cursor = null;   // left the clip edge
            return false;
        }
        // A plain click (even with a little hand jitter) only seeks: the gesture starts past the drag threshold.
        if (!_clipChanged &&
            Math.Abs(p.X - _clipPressPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - _clipPressPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return true;
        var target = _clipGesture == ClipGesture.Move && Math.Abs(p.Y - _clipPressPoint.Y) > 8 ? DropTargetAt(p) : null;
        var targetChanged = target != _clipDropTarget;
        if (Math.Abs(p.X - _clipLastX) < 1 && !targetChanged) return true;   // repaint only when something visible changed
        _clipLastX = p.X;
        _clipDropTarget = target;
        // Within its own track the clip follows the pointer between lanes right away.
        if (target is { NotationRow: false } same && same.Track == _clipFromTrack && same.Lane != clip.Lane) { clip.Lane = same.Lane; _clipChanged = true; }
        var delta = SecOfX(p.X) - _clipPressSec;
        var speed = Math.Clamp(clip.Speed, 0.25, 4);
        var fileLength = clip.FileLengthSec > 0 ? clip.FileLengthSec : _clipOrigin.Offset + _clipOrigin.Length;
        switch (_clipGesture)
        {
            case ClipGesture.Move:
            {
                var start = Math.Max(0, _clipOrigin.Start + delta);
                var length = clip.LengthSec;
                var byStart = SnapSec(start, clip, out var startDistance);
                var byEnd = SnapSec(start + length, clip, out var endDistance) - length;
                clip.StartSec = Math.Max(0, endDistance < startDistance ? byEnd : byStart);
                break;
            }
            case ClipGesture.TrimStart:
            {
                var snappedStart = SnapSec(_clipOrigin.Start + delta, clip, out _);
                var fileDelta = Math.Clamp((snappedStart - _clipOrigin.Start) * speed, -_clipOrigin.Offset, _clipOrigin.Length - 0.05);
                if (_clipOrigin.Start + fileDelta / speed < 0) fileDelta = -_clipOrigin.Start * speed;
                clip.OffsetSec = _clipOrigin.Offset + fileDelta;
                clip.SourceLengthSec = _clipOrigin.Length - fileDelta;
                clip.StartSec = _clipOrigin.Start + fileDelta / speed;
                break;
            }
            case ClipGesture.TrimEnd:
                var endSec = SnapSec(_clipOrigin.Start + (_clipOrigin.Length + delta * speed) / speed, clip, out _);
                clip.SourceLengthSec = Math.Clamp((endSec - _clipOrigin.Start) * speed, 0.05,
                    clip.IsMidi ? 86_400 : Math.Max(0.05, fileLength - _clipOrigin.Offset));
                break;
        }
        _clipChanged = true;
        InvalidateVisual();
        return true;
    }

    private bool ClipMouseUp()
    {
        if (_clipDrag is not { } clip) return false;
        _clipDrag = null;
        var target = _clipDropTarget;
        _clipDropTarget = null;
        ReleaseMouseCapture();
        if (target is { NotationRow: true } row && clip.IsMidi)
        {
            MidiClipToNotation?.Invoke(clip, _clipFromTrack, row.Track);
            return true;
        }
        if (target is { NotationRow: false } lane && lane.Track != _clipFromTrack)
        {
            ClipMovedToTrack?.Invoke(clip, _clipFromTrack, lane.Track, lane.Lane);
            return true;
        }
        if (_clipChanged) ClipEdited?.Invoke(this, clip);
        else
        {
            // A plain click selected the clip (above); like clicking a note in the score it drops the bar range.
            PlainClicked?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
        return true;
    }

    private bool ClipRightClick(Point p)
    {
        if (LaneHitAt(p) is not { } hit) return false;
        SelectedClip = hit.Clip;
        InvalidateVisual();
        LaneClicked?.Invoke(hit.Track, hit.Lane, SecOfX(p.X));
        ClipContextRequested?.Invoke(hit.Track, hit.Clip, SecOfX(p.X));
        return true;
    }

    // ---------- file drops ----------
    private static string[] AudioFilesIn(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] files
            ? files.Where(f => WaveformCache.Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToArray()
            : Array.Empty<string>();

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        if (AudioFilesIn(e).Length == 0 || TrackAt(e.GetPosition(this).Y) < 0) return;
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        var files = AudioFilesIn(e);
        var p = e.GetPosition(this);
        var track = TrackAt(p.Y);
        if (files.Length == 0 || track < 0) return;
        e.Handled = true;
        AudioFilesDropped?.Invoke(track, SecOfX(p.X), files);
    }
}

/// <summary>A take being recorded, drawn live: where it is, and what has come in so far.</summary>
public sealed class LiveTake
{
    public required TrackModel Track { get; init; }
    public int Lane { get; set; }
    public double StartSec { get; init; }
    public double EndSec { get; set; }
    public bool Midi { get; init; }
    /// <summary>Audio: input peaks sampled each frame across the take (the real waveform replaces it at the end).</summary>
    public List<float> Peaks { get; } = new();
    /// <summary>MIDI: notes so far (times from the take start; held notes grow until released).</summary>
    public List<ClipNote> Notes { get; } = new();
}
