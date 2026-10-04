using TabForge.Audio.Contracts;
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
internal enum ClipGesture { Move, TrimStart, TrimEnd, FadeIn, FadeOut }

// TrackTimeline: the clip lanes under a track (fixed lanes) and their audio and MIDI clips:
// waveform / note drawing, greyed takes on lanes that do not play, drag to move (also to another lane or
// track, or a MIDI clip onto a notation row), edge trims, the right-click menu, file drops, and the live take
// being recorded (drawn on the overlay layer, so recording never repaints the whole timeline).
internal sealed partial class TrackTimeline : IClipGestureHost
{

    public AudioClip? SelectedClip { get; set; }

    /// <summary>Snap settings (shared with Settings); null = no snapping.</summary>
    public SnapSettings? Snap { get; set; }
    /// <summary>Playhead in song seconds (a snap target).</summary>
    public Func<double>? PlayheadSec { get; set; }

    /// <summary>
    /// Snaps a song time: to the grid (nearest grid line of the song's bars), to the edges of other clips, and to the playhead,
    /// whichever is nearest within the snap distance (the grid at any distance when that is on). Alt turns snapping off while held.
    /// </summary>
    public double SnapSec(double sec, AudioClip? except, out double distancePx, bool? altHeld = null)
    {
        distancePx = double.PositiveInfinity;
        var snap = Snap;
        if (snap is not { Enabled: true } || Project is null || (altHeld ?? Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))) return sec;
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


    /// <summary>Takes being recorded right now (drawn live on the overlay).</summary>
    public List<LiveTake> LiveTakes { get; } = new();


    public TrackTimeline()
    {
        AllowDrop = true;
        Focusable = true;
        // A waveform finished reading in the background: redraw once, if that file is on this song. Attached while the timeline is in a window
        // (Loaded) and detached when it leaves it (Unloaded: the window closed or the panel was removed), so the static cache holds no
        // handler of a closed window; weak as well, so a timeline that never loaded is not kept alive either.
        Loaded += (_, _) => _waveformSubscription ??= WaveformCache.SubscribeWeak(this, static (t, file) => t.OnWaveformReady(file));
        Unloaded += (_, _) =>
        {
            _waveformSubscription?.Dispose();
            _waveformSubscription = null;
            WaveformCache.Cancel(Project?.Tracks.SelectMany(t => t.AudioClips).Where(c => !c.IsMidi).Select(c => c.File).ToList() ?? new List<string>(), Media);   // what this song asked for stops decoding once its timeline is gone
        };
    }

    private IDisposable? _waveformSubscription;

    /// <summary>The bound song's media context (set with the song by the panel): where its clips' relative paths resolve and what is approved for it. Waveform reads and drop measuring use it, never a "current" document.</summary>
    internal MediaContext Media { get; set; } = MediaContext.Anonymous;

    private void OnWaveformReady(string file) => Dispatcher.BeginInvoke(() =>
    {
        if (!IsLoaded) return;   // the timeline left its window while this was queued: nothing to redraw
        if (Project?.Tracks.Any(t => t.AudioClips.Any(c => string.Equals(c.File, file, StringComparison.OrdinalIgnoreCase))) == true)
            InvalidateVisual();
    });

    private double RowTop(int track) =>
        ArrangementPanel.RulerHeight + ArrangementPanel.SectionHeight + ArrangementPanel.RowTopOf(Project, track) - VerticalScrollOffset;

    public double LaneTop(int track, int lane) => RowTop(track) + ArrangementPanel.NotationHeightOf(Project, Project?.Tracks.ElementAtOrDefault(track)) + lane * ArrangementPanel.AudioLaneHeight;

    // ---------- drawing ----------
    private void DrawAudioLane(DrawingContext dc, TrackModel track, double rowTop, double width, Color trackColor)
    {
        var lanes = ArrangementPanel.LaneCountOf(track);
        if (lanes == 0) return;
        using var slowTrace = SlowTrace.Measure("audio lane draw", 2);
        var clipColour = Readable(trackColor);
        var trackIndex = Project?.Tracks.IndexOf(track) ?? -1;
        var notation = ArrangementPanel.NotationHeightOf(Project, track);
        for (var lane = 0; lane < lanes; lane++)
        {
            var laneRect = new Rect(0, rowTop + notation + lane * ArrangementPanel.AudioLaneHeight, width, ArrangementPanel.AudioLaneHeight);
            dc.DrawRectangle(Draw.Solid(_theme.Board, 0.55), null, laneRect);
            dc.DrawRectangle(Draw.Solid(trackColor, ClipLanes.Plays(track, lane) ? 0.08 : 0.03), null, laneRect);
            if (ClipGestures.DropTarget is { NotationRow: false } target && target.Track == trackIndex && target.Lane == lane && ClipGestures.FromTrack != trackIndex)
                dc.DrawRectangle(Draw.Solid(_theme.Accent, 0.16), Draw.Pen(_theme.Accent, 1, 0.8), laneRect);
            dc.DrawLine(Draw.Pen(_theme.BoardEdge, 0.6, 0.7), new Point(0, laneRect.Bottom - 0.5), new Point(width, laneRect.Bottom - 0.5));
        }
        if (track.RecordArm && track.AudioClips.Count == 0 && !LiveTakes.Any(t => ReferenceEquals(t.Track, track)))
            Draw.At(dc, AudioInputs.IsMidi(track.AudioInput) ? "Armed (MIDI): press Record to record here" : "Armed: press Record to record here, or drop audio files",
                8, rowTop + notation + 15, 11, Draw.Solid(_theme.Muted));
        if (ClipGestures.DropTarget is { NotationRow: true } row && row.Track == trackIndex)
            dc.DrawRectangle(Draw.Solid(_theme.Accent, 0.18), Draw.Pen(_theme.Accent, 1.4), new Rect(0, rowTop, width, ArrangementPanel.RowHeightFor(Project)));
        foreach (var clip in track.AudioClips)
        {
            var x1 = XOfSec(clip.StartSec);
            var x2 = XOfSec(clip.EndSec);
            if (x2 < 0 || x1 > width) continue;
            var laneTop = rowTop + notation + clip.Lane * ArrangementPanel.AudioLaneHeight;
            var box = new Rect(x1, laneTop + 3, Math.Max(3, x2 - x1), ArrangementPanel.AudioLaneHeight - 6);
            // Greyed: muted, or an audio take on a lane that is not playing.
            var heard = ClipLanes.Audible(track, clip);
            var colour = heard ? clipColour : Blend(clipColour, _theme.Muted, 0.7);
            var alpha = heard ? 1.0 : 0.45;
            if (ClipGestures.IsMovingOriginal(clip)) alpha *= 0.4;   // the ghost shows where it is going
            var selected = ReferenceEquals(clip, SelectedClip);
            dc.DrawRoundedRectangle(Draw.Solid(colour, 0.34 * alpha), Draw.Pen(selected ? _theme.Text : colour, selected ? 1.8 : 1, 0.9 * alpha), box, 3, 3);
            // A clip wider than the view (a long take zoomed in) clips its contents with a plain rectangle: a rounded clip region tens of
            // thousands of pixels wide is rendered through a mask and is costly on every zoom or scroll step. The corners are off-screen.
            dc.PushClip(LongerThanView(box) ? new RectangleGeometry(box) : new RectangleGeometry(box, 3, 3));
            if (clip.IsMidi) DrawMidiNotes(dc, clip, box, colour, alpha, width);
            else DrawWaveform(dc, clip, box, colour, alpha, width);
            var label = clip.Muted ? $"{clip.Name} (muted)" : clip.Name;
            if (!clip.IsMidi && WaveformCache.StatusOf(clip.File, Media) is { State: WaveState.NeedsApproval or WaveState.Failed } problem)
                label = $"{label}: {problem.Message}";
            if (box.Width > 30) Draw.At(dc, label, box.X + 5, box.Y + 1, 10, Draw.Solid(_theme.Text, 0.85 * alpha));
            DrawFadeShades(dc, clip, box);
            dc.Pop();
            if (selected || clip.FadeInSec > 0 || clip.FadeOutSec > 0) DrawFadeHandles(dc, clip, box, alpha);
        }
    }

    /// <summary>True when the box is wider than the visible timeline (no viewport known: wider than 4000 px).</summary>
    private bool LongerThanView(Rect box) => box.Width > (_viewWidth > 0 ? _viewWidth : 4000);

    private const double FadeHandleSize = ClipGestureController.FadeHandleSize;

    /// <summary>The silenced part of each fade: a triangle above the gain ramp, with the ramp as a line.</summary>
    private void DrawFadeShades(DrawingContext dc, AudioClip clip, Rect box)
    {
        void Shade(double edgeX, double rampX)
        {
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(new Point(edgeX, box.Y), true, true);
                g.LineTo(new Point(rampX, box.Y), true, false);
                g.LineTo(new Point(edgeX, box.Bottom), true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(Draw.Solid(_theme.Background, 0.55), Draw.Pen(_theme.Text, 1, 0.8), geometry);
        }
        if (clip.FadeInSec > 0) Shade(box.X, XOfSec(clip.StartSec + clip.FadeInSec));
        if (clip.FadeOutSec > 0) Shade(XOfSec(clip.EndSec), XOfSec(clip.EndSec - clip.FadeOutSec));
    }

    /// <summary>The small square handles at the top corners (drag to set the fade lengths).</summary>
    private void DrawFadeHandles(DrawingContext dc, AudioClip clip, Rect box, double alpha)
    {
        var half = FadeHandleSize / 2;
        foreach (var x in new[] { XOfSec(clip.StartSec + clip.FadeInSec), XOfSec(clip.EndSec - clip.FadeOutSec) })
            dc.DrawRectangle(Draw.Solid(_theme.Text, 0.9 * alpha), Draw.Pen(_theme.Background, 1, 0.9 * alpha), new Rect(x - half, box.Y - 1, FadeHandleSize, FadeHandleSize));
    }

    private static Color Blend(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    /// <summary>A track colour too close to the lane background is lifted toward the text colour.</summary>
    private Color Readable(Color c)
    {
        static double Luma(Color x) => 0.2126 * x.R + 0.7152 * x.G + 0.0722 * x.B;
        return Math.Abs(Luma(c) - Luma(_theme.Background)) >= 60 ? c : Blend(c, _theme.Text, 0.5);
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

    // ---------- gestures: ClipGestureController (press, move, trims, fades, right-click, cancel) ----------
    internal ClipGestureController ClipGestures => _clipGestures ??= new ClipGestureController(this);
    private ClipGestureController? _clipGestures;
    void IClipGestureHost.RaiseSeek(int bar, int track) { BarClicked?.Invoke(this, bar); TrackClicked?.Invoke(this, track); }
    double IClipGestureHost.VerticalScrollOffset => VerticalScrollOffset;
    void IClipGestureHost.RaisePlainClicked() => PlainClicked?.Invoke(this, EventArgs.Empty);

    // File drops (audio and MIDI from Windows or a plug-in): TrackTimeline.MediaDrop.cs, driven by the panel's scroll viewer
    // so the area below the last track takes drops too.
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
