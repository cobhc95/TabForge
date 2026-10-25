using System;
using Stopwatch = System.Diagnostics.Stopwatch;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge.Avalonia.Transport;

// Owns: the opt-in playhead trace (TABFORGE_TRACE=playback): what the score, the fretboard and the timeline show while the shown tab plays,
//     one line per performed bar, and one line per frame for the first second after a start or a jump; written to trace-playback.log.
// Does not own: the playhead (AvaloniaPlaybackSession, PlayheadClock), the views, the trace switch and file (Core Trace).
// Tests: TabForge.Avalonia.Tests.PlayheadTraceTests.
// Threads: UI thread only (the session raises its frames there). Off, nothing subscribes and nothing is allocated.
internal sealed class PlayheadTrace
{
    /// <summary>Wall ms after a start or a jump that every frame is traced.</summary>
    public const double FrameWindowMs = 1000;

    /// <summary>A drawn position this many ms away from where the last frame predicts it is a jump (a seek, a wrap, a snap back).</summary>
    public const double JumpMs = 100;

    private readonly MainWindowTabs _tabs;
    private readonly AvaloniaPlaybackSessions _sessions;
    private readonly ScorePreviewView _score;
    private readonly Func<ArrangementView?> _arrangement;
    private readonly Action<string> _write;
    private readonly Stopwatch _wall = Stopwatch.StartNew();
    private AvaloniaPlaybackSession? _session;
    private int _lastPerformed = -1;
    private double _lastDrawnMs = double.NaN;
    private double _lastWallMs;
    private double _framesUntilWallMs = double.NegativeInfinity;

    internal PlayheadTrace(MainWindowTabs tabs, AvaloniaPlaybackSessions sessions, ScorePreviewView score, Func<ArrangementView?> arrangement, Action<string> write)
    {
        _tabs = tabs;
        _sessions = sessions;
        _score = score;
        _arrangement = arrangement;
        _write = write;
        tabs.ActiveChanged += Follow;
        Follow();
    }

    /// <summary>Starts the trace for the window when TABFORGE_TRACE names playback; otherwise does nothing.</summary>
    internal static void AttachIfOn(MainWindowTabs tabs, AvaloniaPlaybackSessions? sessions, ScorePreviewView score, Func<ArrangementView?> arrangement)
    {
        if (sessions is null || !Trace.IsOn(Trace.Playback)) return;
        _ = new PlayheadTrace(tabs, sessions, score, arrangement, line => Trace.Write(Trace.Playback, line));
    }

    private void Follow()
    {
        if (_session is not null) _session.PlayheadFrame -= OnFrame;
        _session = _tabs.Songs.Count == 0 ? null : _sessions.Existing(_tabs.Active);   // the panes make the session; the trace never starts an engine
        if (_session is not null) _session.PlayheadFrame += OnFrame;
        _lastPerformed = -1;
        _lastDrawnMs = double.NaN;
    }

    private void OnFrame()
    {
        var session = _session;
        var timeline = session?.Transport.Timeline;
        if (session is null || timeline is null || !session.IsPlaying || session.Transport.PlayheadBar < 0)
        {
            _lastPerformed = -1;
            _lastDrawnMs = double.NaN;
            return;
        }
        var wallMs = _wall.Elapsed.TotalMilliseconds;
        var drawnMs = session.Playhead.ElapsedMs;
        var jumped = double.IsNaN(_lastDrawnMs) || Math.Abs(drawnMs - (_lastDrawnMs + (wallMs - _lastWallMs))) > JumpMs;
        if (jumped) _framesUntilWallMs = wallMs + FrameWindowMs;
        _lastDrawnMs = drawnMs;
        _lastWallMs = wallMs;
        var performed = timeline.BarIndexAt(drawnMs);
        var barChanged = performed != _lastPerformed;
        _lastPerformed = performed;
        if (wallMs <= _framesUntilWallMs) _write(FrameLine(session, drawnMs, jumped));
        if (barChanged) _write(BarLine(session, timeline, performed, drawnMs));
    }

    private string FrameLine(AvaloniaPlaybackSession session, double drawnMs, bool jumped)
    {
        var engine = _sessions.Engine;
        var latency = session.Routing?.AudibleLatencyMs ?? 0;
        return string.Create(CultureInfo.InvariantCulture,
            $"frame{(jumped ? " jump" : "")} drawnMs {drawnMs:0.0} engineMs {session.Transport.PlayheadMs:0.0} engineBar {session.Transport.PlayheadBar + 1} drawnBar {session.Playhead.Bar + 1} latencyMs {latency:0.0} engineRunning {engine?.IsRunning ?? false} latencyTicks {engine?.LatencyTicks ?? 0}");
    }

    private string BarLine(AvaloniaPlaybackSession session, ScoreTimeline timeline, int performed, double drawnMs)
    {
        var play = _score.Probe.Play;
        var trackIndex = _score.Probe.TrackIndex;
        var notes = timeline.NotesFor(trackIndex);
        var expected = NoteTimeline.SoundingAt(notes, drawnMs).Count;
        var fretboardNow = NoteTimeline.SoundingAt(notes, session.Transport.PlayheadMs).Count;
        var view = _arrangement()?.Timeline;
        var viewer = view?.FindAncestorOfType<ScrollViewer>();
        var follow = view is null || viewer is null ? "timeline none"
            : string.Create(CultureInfo.InvariantCulture,
                $"timelineBar {view.PlayheadBar + 1} timelineShows {view.BarAtX(viewer.Offset.X) + 1}-{view.BarAtX(viewer.Offset.X + viewer.Viewport.Width - 1) + 1} playheadX {view.PlayheadX():0} offset {viewer.Offset.X:0} viewport {viewer.Viewport.Width:0} extent {viewer.Extent.Width:0} timelineBars {view.BarCount}");
        return string.Create(CultureInfo.InvariantCulture,
            $"bar performed {performed + 1}/{timeline.Bars.Count} source {session.Playhead.Bar + 1} engineBar {session.Transport.PlayheadBar + 1} drawnMs {drawnMs:0.0} engineMs {session.Transport.PlayheadMs:0.0} track {trackIndex} scoreBar {play.Measure + 1} scoreActive {play.Active} lit {play.Sounding.Count} expected {expected} fretboardNow {fretboardNow} {follow} lateNotesDropped {session.LateNotesDropped} latencyMs {session.Routing?.AudibleLatencyMs ?? 0:0.0} engineRunning {_sessions.Engine?.IsRunning ?? false}");
    }
}
