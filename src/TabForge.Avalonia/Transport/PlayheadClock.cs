using System;
using System.Diagnostics;
using TabForge.Playback;

namespace TabForge.Avalonia.Transport;

// Owns: the one position the views draw (the score line, the timeline line, the fretboard and score follow): the engine's audible position,
//     carried forward by the wall time between frames but never more than MaxLeadMs past it, never back except on an explicit jump, and held
//     at a start (or a jump back) until the engine reports progress past it; mapped to a bar, beat and fraction through the timeline.
// Does not own: the frame source that calls Update, the engine position (PlaybackTransport, PlaybackEngine.AudiblePlayhead), the drawing.
// Tests: TabForge.Avalonia.Tests.PlayheadClockTests, TabForge.Avalonia.Tests.PlayheadStartAuthorityTests.
// Threads: UI only. Allocates nothing per frame: Position is one reused object.
public sealed class PlayheadClock
{
    /// <summary>The farthest the drawn position runs ahead of the engine's last audible position, in ms: one 60 Hz frame.</summary>
    public const double MaxLeadMs = 1000.0 / 60;

    /// <summary>An engine position this many ms below the last one is an explicit jump back (a seek, a loop wrap, a count-in, a restart).</summary>
    public const double JumpBackMs = 1.0;

    /// <summary>Timeline milliseconds that pass per wall millisecond. The timeline is compiled at the playback speed, so its time already includes it.</summary>
    public const double TimelineMsPerWallMs = 1.0;

    private readonly Func<double> _wallMs;
    private double _drawnMs;
    private double _lastEngineMs;
    private double _lastWallMs;
    private bool _anchored;
    private bool _holding;

    public PlayheadClock(Func<double>? wallMs = null)
    {
        var watch = Stopwatch.StartNew();
        _wallMs = wallMs ?? (() => watch.Elapsed.TotalMilliseconds);
    }

    /// <summary>The position to draw now (bar, beat, fraction, ms); the same object every frame.</summary>
    public PlaybackPosition Position { get; } = new() { Bar = -1 };

    /// <summary>Computes this frame's position from the transport's newest engine position. Call once per frame, after the transport applied its frame.</summary>
    public void Update(PlaybackTransport transport)
    {
        var timeline = transport.Timeline;
        // While a start compiles, the transport's timeline is the previous run's: the engine's start bar and cell are drawn as they are.
        if (!transport.IsPlaying || transport.IsPaused || transport.StartPending || timeline is null || timeline.Bars.Count == 0 || transport.PlayheadBar < 0)
        {
            _anchored = false;
            CopyEngine(transport);
            return;
        }
        var now = _wallMs();
        var engineMs = transport.PlayheadMs;
        if (!_anchored || engineMs < _lastEngineMs - JumpBackMs)
        {
            // A start or an explicit jump back: the position holds on the engine's until the engine moves past it. A revised timeline keeps
            //     the song time, so it changes nothing here.
            _anchored = true;
            _holding = true;
            _drawnMs = engineMs;
        }
        else if (_holding)
        {
            if (engineMs > _drawnMs)
            {
                _holding = false;
                _drawnMs = engineMs;
            }
        }
        else
        {
            // Between engine readings the position runs on with the wall clock, at most MaxLeadMs past the engine; a latency rise that
            // holds the engine is absorbed (the position waits), and an engine ahead of it is caught up at once.
            var predicted = _drawnMs + Math.Max(0, now - _lastWallMs) * TimelineMsPerWallMs;
            _drawnMs = Math.Max(_drawnMs, Math.Max(engineMs, Math.Min(predicted, engineMs + MaxLeadMs)));
        }
        _lastEngineMs = engineMs;
        _lastWallMs = now;
        var ms = Math.Min(_drawnMs, Math.Max(timeline.TotalMs, engineMs));
        PlayheadMapper.MapInto(Position, timeline, ms, transport.PlayheadBar, transport.PlayheadCell);
    }

    private void CopyEngine(PlaybackTransport transport)
    {
        Position.Bar = transport.PlayheadBar;
        Position.Cell = transport.PlayheadCell;
        Position.BarFraction = transport.PlayheadFraction;
        Position.ElapsedMs = transport.PlayheadMs;
    }
}
