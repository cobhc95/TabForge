using System;
using System.Collections.Generic;
using TabForge.Avalonia.Transport;
using TabForge.Models;
using TabForge.Playback;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the start contract of the drawn playhead (PlayheadClock), frame by frame: it holds at the start until the engine's audible position moves
///     past it, never runs more than PlayheadClock.MaxLeadMs ahead of that position, and never moves back except on an explicit jump; with the
///     output latency rising a moment into the start, with the start's compile delay (the previous run's timeline still on the transport), and
///     with a count-in before the first bar.
/// Does not own: the engine's own audible position (PlaybackEngine; AudiblePlayheadLatencyRiseTests, PlaybackStartHoldTests).
/// Tests: this class.
/// </summary>
public class PlayheadStartAuthorityTests
{
    private const double FrameMs = 1000.0 / 120;
    private const int Frames = 120;

    private static ScoreTimeline Timeline(double firstBarMs = 0)
    {
        var timeline = new ScoreTimeline { TotalMs = firstBarMs + 8000, CountInMs = firstBarMs, PlayFromMs = firstBarMs };
        for (var bar = 0; bar < 4; bar++) timeline.Bars.Add(new ScoreBar(bar, firstBarMs + bar * 2000, firstBarMs + (bar + 1) * 2000, 8, 120));
        return timeline;
    }

    /// <summary>Runs <see cref="Frames"/> frames; <paramref name="engineAt"/> gives the engine's audible ms at a wall ms. Returns (engine, drawn, bar) per frame.</summary>
    private static List<(double Engine, double Drawn, int Bar)> Run(AvaloniaPlaybackSessionTests.FakeEngine engine, PlaybackTransport transport, Func<double, double> engineAt)
    {
        var wall = new double[1];
        var clock = new PlayheadClock(() => wall[0]);
        var frames = new List<(double, double, int)>();
        for (var frame = 0; frame < Frames; frame++)
        {
            wall[0] = frame * FrameMs;
            var ms = engineAt(wall[0]);
            engine.Report(PlayheadMapper.Map(transport.Timeline!, ms, 0, 0));
            transport.ApplyFrame();
            clock.Update(transport);
            frames.Add((ms, clock.Position.ElapsedMs, clock.Position.Bar));
        }
        return frames;
    }

    private static void AssertMonotonicAndNeverAhead(List<(double Engine, double Drawn, int Bar)> frames, double startMs)
    {
        for (var i = 0; i < frames.Count; i++)
        {
            var (engineMs, drawn, _) = frames[i];
            Assert.True(drawn <= engineMs + PlayheadClock.MaxLeadMs + 1e-9, $"frame {i}: drawn {drawn:0.0} ahead of the engine's {engineMs:0.0}");
            if (i > 0) Assert.True(drawn >= frames[i - 1].Drawn, $"frame {i}: drawn moved back from {frames[i - 1].Drawn:0.0} to {drawn:0.0}");
            if (engineMs <= startMs) Assert.Equal(startMs, drawn, 6);
        }
    }

    private static (AvaloniaPlaybackSessionTests.FakeEngine, PlaybackTransport) Started(ScoreTimeline timeline)
    {
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        var transport = new PlaybackTransport(engine);
        transport.Play(new SongProject());
        engine.RaiseTimelineChanged(timeline);
        return (engine, transport);
    }

    [Fact]
    public void ALatencyRiseAMomentIntoTheStartIsAbsorbedNotShownAsAJump()
    {
        const double settleMs = 120, riseAtMs = 250, riseMs = 90;
        var (engine, transport) = Started(Timeline());
        // The engine holds its start, then plays; at riseAtMs the output latency rises and the engine's audible position waits for the sound.
        var held = 0.0;
        var frames = Run(engine, transport, wall =>
        {
            var audible = Math.Max(0, wall - settleMs - (wall >= riseAtMs ? riseMs : 0));
            held = Math.Max(held, audible);
            return held;
        });
        AssertMonotonicAndNeverAhead(frames, 0);
        Assert.True(frames[^1].Drawn > 300, $"the playhead did not play on: {frames[^1].Drawn:0}");
    }

    [Fact]
    public void WhileTheStartCompilesThePreviousRunsTimelineNeverMovesTheLine()
    {
        var engine = new AvaloniaPlaybackSessionTests.FakeEngine();
        var transport = new PlaybackTransport(engine);
        var previous = Timeline();
        transport.Play(new SongProject());
        engine.RaiseTimelineChanged(previous);
        transport.Stop();

        // A start at bar 3: while it compiles the engine shows that bar at 0 ms; the transport still holds the previous timeline.
        transport.Seek(new SongProject(), 2, 0);
        transport.Play(new SongProject());
        Assert.True(transport.StartPending);
        var wall = new double[1];
        var clock = new PlayheadClock(() => wall[0]);
        for (var frame = 0; frame < 30; frame++)
        {
            wall[0] = frame * FrameMs;
            engine.Report(new PlaybackPosition { Bar = 2, Cell = 0, ElapsedMs = 0 });
            transport.ApplyFrame();
            clock.Update(transport);
            Assert.Equal(2, clock.Position.Bar);
            Assert.Equal(0, clock.Position.BarFraction, 6);
        }

        // The compile is ready: the new timeline starts at bar 3 (4000 ms), held there until the engine plays past it.
        engine.RaiseTimelineChanged(previous);
        Assert.False(transport.StartPending);
        var frames = new List<(double Engine, double Drawn, int Bar)>();
        for (var frame = 0; frame < Frames; frame++)
        {
            wall[0] = (30 + frame) * FrameMs;
            var ms = 4000 + Math.Max(0, frame * FrameMs - 200);
            engine.Report(PlayheadMapper.Map(previous, ms, 0, 0));
            transport.ApplyFrame();
            clock.Update(transport);
            frames.Add((ms, clock.Position.ElapsedMs, clock.Position.Bar));
            Assert.True(clock.Position.Bar >= 2, $"frame {frame}: bar {clock.Position.Bar}");
        }
        AssertMonotonicAndNeverAhead(frames, 4000);
    }

    [Fact]
    public void ACountInHoldsTheLineAtTheFirstBarAndThenOnlyMovesForward()
    {
        const double countInMs = 2000;
        var (engine, transport) = Started(Timeline(countInMs));
        // The engine runs through the count-in clicks before the first bar.
        var frames = Run(engine, transport, wall => Math.Max(0, wall - 50) * 3);
        AssertMonotonicAndNeverAhead(frames, 0);
        for (var i = 0; i < frames.Count; i++)
            if (frames[i].Engine <= countInMs) Assert.Equal(0, frames[i].Bar);
    }

    [Fact]
    public void ASeekBackIsTheOnlyWayTheLineMovesBack()
    {
        var (engine, transport) = Started(Timeline());
        var frames = Run(engine, transport, wall => wall < 500 ? wall : 100 + (wall - 500));
        var backSteps = 0;
        for (var i = 1; i < frames.Count; i++) if (frames[i].Drawn < frames[i - 1].Drawn) backSteps++;
        Assert.Equal(1, backSteps);
        var jump = frames.FindIndex(1, f => f.Engine < 450);
        Assert.True(jump > 0 && frames[jump].Drawn == frames[jump].Engine, "the seek did not land on the engine's position");
    }
}
